using System;
using System.Linq;
using AOSharp.Common.GameData;
using AOSharp.Core;
using AOSharp.Core.Inventory;
using SmokeLounge.AOtomation.Messaging.GameData;
using SmokeLounge.AOtomation.Messaging.Messages;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;

namespace RKmission
{
    // Explicit one-item shop diagnostic. Trade messages and both inventory
    // changes must be observed; uncertain actions are never retried.
    internal sealed class ShopSaleTest : IDisposable
    {
        private enum Phase { Idle, Opening, Selecting, StageMoveSent, AddSent, AcceptSent }

        private readonly LogisticsRouteNavigator _route;
        private readonly ManagerLoot.ManagerLoot _loot;
        private readonly Action<string> _say;
        private Phase _phase;
        private Identity _actor = Identity.None, _sourceBag = Identity.None, _itemSlot = Identity.None;
        private DateTime _phaseStarted, _nextTick;
        private bool _shopSeen, _openSeen, _addEcho, _completeSeen, _declined;
        private string _itemName;
        private int _itemId, _itemQl, _mainBefore, _bagBefore, _cashBefore;

        public bool IsActive => _phase != Phase.Idle;
        public string Status => _phase == Phase.Idle ? "inactive" :
            $"{_phase} at {_actor}; item='{_itemName ?? "unselected"}' ({_itemId}, QL {_itemQl})";

        public ShopSaleTest(LogisticsRouteNavigator route, ManagerLoot.ManagerLoot loot, Action<string> say)
        {
            _route = route;
            _loot = loot;
            _say = say;
            Game.OnUpdate += OnUpdate;
            Network.N3MessageReceived += OnMessage;
        }

        public void Start()
        {
            if (IsActive) { _say("A shop sale test is already active."); return; }
            Dynel actor = _route.FindVerifiedShopActor();
            if (actor == null)
            {
                _say("Shop sale held: no unique exact surveyed shop actor is visible at the target; no item was moved.");
                return;
            }
            _actor = actor.Identity;
            _sourceBag = _itemSlot = Identity.None;
            _itemName = null;
            _itemId = _itemQl = 0;
            _shopSeen = _openSeen = _addEcho = _completeSeen = _declined = false;
            _phase = Phase.Opening;
            _phaseStarted = DateTime.UtcNow;
            _nextTick = DateTime.MinValue;
            try
            {
                actor.Use();
                _say($"Opening exact surveyed shop actor {_actor}; waiting for shop update and trade open.");
            }
            catch (Exception ex) { Fail("could not use shop actor: " + ex.Message); }
        }

        public void Stop()
        {
            if (!IsActive) return;
            Phase old = _phase;
            _phase = Phase.Idle;
            _say(old == Phase.AddSent || old == Phase.AcceptSent
                ? "Shop sale test stopped with an item possibly in the trade window. Inspect the shop and inventory before retrying."
                : old == Phase.StageMoveSent
                    ? "Shop sale test stopped after a bag move may have been sent. Inspect the RKM Sell bag and main inventory."
                    : "Shop sale test stopped; no item was submitted for sale.");
        }

        private void OnMessage(object sender, N3Message message)
        {
            if (!IsActive || Game.IsZoning || DynelManager.LocalPlayer == null) return;
            if (message is ShopUpdateMessage) _shopSeen = true;
            if (!(message is TradeMessage trade)) return;
            if (trade.Action == TradeAction.Open &&
                trade.Param1 == (int)_actor.Type && trade.Param2 == _actor.Instance)
                _openSeen = true;
            else if (trade.Action == TradeAction.AddItem && _phase == Phase.AddSent &&
                trade.Param1 == (int)DynelManager.LocalPlayer.Identity.Type &&
                trade.Param2 == DynelManager.LocalPlayer.Identity.Instance &&
                trade.Param3 == (int)_itemSlot.Type && trade.Param4 == _itemSlot.Instance)
                _addEcho = true;
            else if (trade.Action == TradeAction.Complete && _phase == Phase.AcceptSent &&
                trade.Param1 == (int)_actor.Type && trade.Param2 == _actor.Instance)
                _completeSeen = true;
            else if (trade.Action == TradeAction.Decline) _declined = true;
        }

        private void OnUpdate(object sender, float elapsed)
        {
            if (!IsActive || Game.IsZoning || DateTime.UtcNow < _nextTick) return;
            _nextTick = DateTime.UtcNow.AddMilliseconds(200);
            try { Tick(); }
            catch (Exception ex) { Fail("shop sale error: " + ex.Message); }
        }

        private void Tick()
        {
            if (_route.FindVerifiedShopActor(_actor.ToString()) == null)
            { Fail("exact surveyed shop actor, position, or playfield was lost"); return; }
            if (_declined) { Fail("shop trade was declined"); return; }
            if (_phase == Phase.Opening)
            {
                if (_shopSeen && _openSeen)
                {
                    _phase = Phase.Selecting;
                    _phaseStarted = DateTime.UtcNow;
                    _say("Exact shop trade opened and inventory update received; selecting one ManagerLoot Reject item.");
                }
                else if (TimedOut(10)) Fail($"shop did not finish opening (trade={_openSeen}, inventory={_shopSeen})");
                return;
            }
            if (_phase != Phase.AcceptSent &&
                (!Trade.TradeTarget.HasValue || Trade.TradeTarget.Value != _actor))
            { Fail($"active trade target changed from exact shop actor {_actor}"); return; }
            if (_phase == Phase.Selecting)
            {
                if (Item.HasPendingUse || Spell.HasPendingCast)
                { if (TimedOut(10)) Fail("another item use or spell remained pending"); return; }
                Item[] main = MainItems();
                Item item = main.Where(x => _loot.Classify(x) == ManagerLoot.ItemClassification.Reject)
                    .Where(x => main.Count(y => SameItem(x, y)) == 1)
                    .OrderBy(x => x.Name).ThenBy(x => x.Id).FirstOrDefault();
                if (item != null) { BeginAdd(item); return; }
                if (Inventory.NumFreeSlots < 1)
                { Fail("no free main-inventory slot to stage a Reject from RKM Sell"); return; }
                var staged = Inventory.Backpacks
                    .Where(bag => ManagerLoot.ManagedBagFamily.Matches(bag.Name,
                        ManagerLoot.ManagedBagFamily.Sell))
                    .OrderBy(bag => ManagerLoot.ManagedBagFamily.Order(bag.Name,
                        ManagerLoot.ManagedBagFamily.Sell))
                    .SelectMany(bag => Inventory.GetContainerItems(bag.Identity)
                        .Where(x => x != null && _loot.Classify(x) == ManagerLoot.ItemClassification.Reject)
                        .Select(x => new { Bag = bag.Identity, Item = x }))
                    .Where(x => main.All(y => !SameItem(x.Item, y)))
                    .FirstOrDefault();
                if (staged == null)
                { Fail("no unambiguous ManagerLoot Reject item in main inventory or an RKM Sell bag"); return; }
                Remember(staged.Item);
                _sourceBag = staged.Bag;
                _mainBefore = main.Count(Matches);
                _bagBefore = Inventory.GetContainerItems(_sourceBag).Count(Matches);
                if (_bagBefore != 1)
                { Fail("selected RKM Sell item is duplicated in its bag"); return; }
                _phase = Phase.StageMoveSent;
                _phaseStarted = DateTime.UtcNow;
                staged.Item.MoveToInventory();
                _say($"Moving one Reject '{_itemName}' from RKM Sell into main inventory before the shop trade.");
                return;
            }
            if (_phase == Phase.StageMoveSent)
            {
                Item[] main = MainItems();
                int bagCount = Inventory.GetContainerItems(_sourceBag).Count(Matches);
                if (main.Count(Matches) == _mainBefore + 1 && bagCount == _bagBefore - 1 &&
                    !Item.HasPendingUse && !Spell.HasPendingCast)
                {
                    Item item = main.SingleOrDefault(Matches);
                    if (item == null) { Fail("staged Reject item could not be identified in main inventory"); return; }
                    _say($"RKM Sell staging verified for '{_itemName}'; adding it to the shop trade.");
                    BeginAdd(item);
                }
                else if (TimedOut(8)) Fail("RKM Sell to main-inventory move was not verified");
                return;
            }
            int currentMain = MainItems().Count(Matches);
            if (_phase == Phase.AddSent)
            {
                if (_addEcho && currentMain == _mainBefore - 1)
                {
                    _phase = Phase.AcceptSent;
                    _phaseStarted = DateTime.UtcNow;
                    Trade.Accept(Identity.None); // Matches the surveyed NPC-shop Accept packet (0/0/0/0).
                    _say($"Shop acknowledged '{_itemName}' in the trade window; sale acceptance sent once.");
                }
                else if (TimedOut(8)) Fail($"shop did not acknowledge item placement (echo={_addEcho}, main={currentMain})");
                return;
            }
            if (_phase == Phase.AcceptSent)
            {
                int cash = DynelManager.LocalPlayer.GetStat(Stat.Cash);
                if (_completeSeen && currentMain == _mainBefore - 1 && cash >= _cashBefore)
                {
                    _phase = Phase.Idle;
                    _say($"Shop sale verified for '{_itemName}': trade complete, item left main inventory, " +
                        $"cash {_cashBefore} -> {cash}.");
                }
                else if (TimedOut(10))
                    Fail($"shop sale not verified (complete={_completeSeen}, main={currentMain}, cash={cash})");
            }
        }

        private void BeginAdd(Item item)
        {
            if (_loot.Classify(item) != ManagerLoot.ItemClassification.Reject)
            { Fail("selected item is no longer classified Reject"); return; }
            Remember(item);
            _mainBefore = MainItems().Count(Matches);
            if (_mainBefore != 1)
            { Fail("selected Reject item is not unique in main inventory"); return; }
            _itemSlot = item.Slot;
            _cashBefore = DynelManager.LocalPlayer.GetStat(Stat.Cash);
            _addEcho = false;
            _phase = Phase.AddSent;
            _phaseStarted = DateTime.UtcNow;
            Trade.AddItem(item);
            _say($"Shop test AddItem sent: '{_itemName}', id={_itemId}, QL={_itemQl}, " +
                $"ManagerLoot=Reject, slot={_itemSlot}, cash={_cashBefore}. Waiting for server echo.");
        }

        private void Remember(Item item)
        { _itemId = item.Id; _itemQl = item.QualityLevel; _itemName = item.Name; }
        private bool TimedOut(int seconds) => DateTime.UtcNow - _phaseStarted >= TimeSpan.FromSeconds(seconds);
        private bool Matches(Item item) => item != null && item.Id == _itemId &&
            item.QualityLevel == _itemQl && string.Equals(item.Name, _itemName, StringComparison.Ordinal);
        private static bool SameItem(Item left, Item right) => left.Id == right.Id &&
            left.QualityLevel == right.QualityLevel && string.Equals(left.Name, right.Name, StringComparison.Ordinal);
        private static Item[] MainItems() => Inventory.Items.Where(x => x != null &&
            x.Slot.Type == IdentityType.Inventory && x.UniqueIdentity.Type != IdentityType.Container).ToArray();

        private void Fail(string reason)
        {
            Phase old = _phase;
            _phase = Phase.Idle;
            _say("Shop sale test stopped: " + reason + ". " +
                (old == Phase.AddSent || old == Phase.AcceptSent
                    ? $"Inspect the trade window and '{_itemName}' before retrying; no trade action was retried."
                    : old == Phase.StageMoveSent
                        ? $"Check whether '{_itemName}' moved from RKM Sell to main inventory."
                        : "No item was submitted for sale."));
        }

        public void Dispose()
        {
            Game.OnUpdate -= OnUpdate;
            Network.N3MessageReceived -= OnMessage;
            Stop();
        }
    }
}
