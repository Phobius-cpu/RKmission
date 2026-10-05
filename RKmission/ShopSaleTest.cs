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
    // Explicit bounded shop sales. Every item needs its own actor-bound trade
    // completion and inventory/cash proof; uncertain actions are never retried.
    internal sealed class ShopSaleTest : IDisposable
    {
        private enum Phase { Idle, Opening, Selecting, StageMoveSent, AddSent, AcceptSent, BetweenSales, StagingOnlySent }

        private readonly LogisticsRouteNavigator _route;
        private readonly ManagerLoot.ManagerLoot _loot;
        private readonly Action<string> _say;
        private Phase _phase;
        private Identity _actor = Identity.None, _sourceBag = Identity.None, _itemSlot = Identity.None;
        private DateTime _phaseStarted, _nextTick;
        private bool _shopSeen, _openSeen, _addEcho, _completeSeen, _declined;
        private string _itemName;
        private int _itemId, _itemQl, _mainBefore, _bagBefore, _cashBefore;
        private int _requested = 1, _completed;
        private bool _automaticRun;

        public bool IsActive => _phase != Phase.Idle;
        public VerifiedOperationResult Result { get; private set; }
        public string LastFailure { get; private set; }
        public int CompletedCount => _completed;
        public string Status => _phase == Phase.Idle ? "inactive" : _phase == Phase.StagingOnlySent ?
            $"staging '{_itemName}' (id={_itemId}, QL={_itemQl}) from main into RKM Sell; awaiting verification" :
            $"{_phase} at {_actor}; verified sales={_completed}/{_requested}, " +
            $"item='{_itemName ?? "unselected"}' ({_itemId}, QL {_itemQl})";

        public ShopSaleTest(LogisticsRouteNavigator route, ManagerLoot.ManagerLoot loot, Action<string> say)
        {
            _route = route;
            _loot = loot;
            _say = say;
            Game.OnUpdate += OnUpdate;
            Network.N3MessageReceived += OnMessage;
        }

        public void Preview()
        {
            if (Game.IsZoning || DynelManager.LocalPlayer == null)
            { _say("Shop preview unavailable while zoning or before inventory loads; no item moved."); return; }
            Item[] main = MainItems();
            var sellBags = Inventory.Backpacks
                .Where(bag => ManagerLoot.ManagedBagFamily.Matches(bag.Name,
                    ManagerLoot.ManagedBagFamily.Sell))
                .OrderBy(bag => ManagerLoot.ManagedBagFamily.Order(bag.Name,
                    ManagerLoot.ManagedBagFamily.Sell))
                .ToArray();
            Item[] sell = sellBags.SelectMany(bag => Inventory.GetContainerItems(bag.Identity))
                .Where(x => x != null).ToArray();
            Item[] eligible = sell.Where(x => _loot.Classify(x) == ManagerLoot.ItemClassification.Reject &&
                main.All(y => !SameItem(x, y)) && sell.Count(y => SameItem(x, y)) == 1).ToArray();
            var protectedItems = sell.Select(x => new { Item = x, Location = "RKM Sell" })
                .Concat(main.Select(x => new { Item = x, Location = "main" }))
                .Where(x => _loot.Classify(x.Item) == ManagerLoot.ItemClassification.Protected).ToArray();
            int sellRejects = sell.Count(x => _loot.Classify(x) == ManagerLoot.ItemClassification.Reject);
            int sellKeeps = sell.Count(x => _loot.Classify(x) == ManagerLoot.ItemClassification.Keep);
            int sellProtected = sell.Count(x => _loot.Classify(x) == ManagerLoot.ItemClassification.Protected);
            int sellUnknown = sell.Length - sellRejects - sellKeeps - sellProtected;
            int mainRejects = main.Count(x => _loot.Classify(x) == ManagerLoot.ItemClassification.Reject);
            _say($"Shop preview: {eligible.Length} eligible item(s) from {sellBags.Length} RKM Sell bag(s), " +
                $"{sell.Length} observed bag item(s) [Reject={sellRejects}, Keep={sellKeeps}, " +
                $"Protected={sellProtected}, Unknown={sellUnknown}]; " +
                $"main Reject={mainRejects} (excluded), main free slots={Inventory.NumFreeSlots}; no item moved.");
            if (sellBags.Length == 0)
                _say("Shop preview: no RKM Sell bag was found. The shop test will not select from main inventory.");
            else if (sell.Length == 0)
                _say("Shop preview: no items were observed in RKM Sell. If it contains items, open the bag and preview again.");
            else if (sellRejects > eligible.Length)
                _say($"Shop preview: {sellRejects - eligible.Length} Reject item(s) were excluded because their id/QL/name is duplicated in RKM Sell or main inventory.");
            for (int i = 0; i < Math.Min(8, eligible.Length); i++)
            {
                Item item = eligible[i];
                _say($"Shop eligible{(i == 0 ? " (next test item)" : "")}: " +
                    $"'{item.Name}', id={item.Id}, QL={item.QualityLevel}.");
            }
            _say($"Shop preview: {protectedItems.Length} protected item(s) in main/RKM Sell (first 8 below).");
            foreach (var entry in protectedItems.Take(8))
                _say($"Shop protected ({entry.Location}, {_loot.ProtectionReason(entry.Item)}): " +
                    $"'{entry.Item.Name}', id={entry.Item.Id}, QL={entry.Item.QualityLevel}.");
            foreach (Item item in main.Where(x => _loot.Classify(x) == ManagerLoot.ItemClassification.Reject).Take(8))
                _say($"Main Reject (excluded from sale): '{item.Name}', id={item.Id}, QL={item.QualityLevel}; " +
                    $"select with /rkm logistics shop stage {item.Id} only if intended for sale.");
        }

        public void StartStage(int itemId)
        {
            if (IsActive) { _say("Finish or stop the active shop operation before staging an item."); return; }
            try { StageSelected(itemId); }
            catch (Exception ex) { _say("Shop staging held: inventory snapshot unavailable: " + ex.Message + "; no item moved."); }
        }

        private void StageSelected(int itemId)
        {
            if (Game.IsZoning || DynelManager.LocalPlayer == null || Item.HasPendingUse || Spell.HasPendingCast)
            { _say("Shop staging held: wait for zoning and pending actions to finish; no item moved."); return; }
            Item[] matches = MainItems().Where(x => x.Id == itemId).ToArray();
            if (matches.Length != 1)
            { _say($"Shop staging held: item id {itemId} must identify exactly one main-inventory item (found {matches.Length})."); return; }
            Item item = matches[0];
            if (_loot.Classify(item) != ManagerLoot.ItemClassification.Reject)
            { _say($"Shop staging held: '{item.Name}' is {_loot.Classify(item)}, not Reject; no item moved."); return; }
            var bags = Inventory.Backpacks.Where(x => ManagerLoot.ManagedBagFamily.Matches(x.Name,
                ManagerLoot.ManagedBagFamily.Sell)).OrderBy(x => ManagerLoot.ManagedBagFamily.Order(x.Name,
                ManagerLoot.ManagedBagFamily.Sell)).ToArray();
            if (bags.Any(b => Inventory.GetContainerItems(b.Identity).Any(x => x != null && SameItem(item, x))))
            { _say("Shop staging held: the same id/QL/name is already observed in RKM Sell; no item moved."); return; }
            var bag = bags.FirstOrDefault(x => x.Items.Count < 21);
            if (bag == null) { _say("Shop staging held: no RKM Sell bag with space was found; no item moved."); return; }
            Remember(item);
            _sourceBag = bag.Identity;
            _mainBefore = MainItems().Count(Matches);
            _bagBefore = Inventory.GetContainerItems(_sourceBag).Count(Matches);
            _requested = 1;
            _completed = 0;
            _phase = Phase.StagingOnlySent;
            _phaseStarted = DateTime.UtcNow;
            _nextTick = DateTime.MinValue;
            try
            {
                item.MoveToContainer(bag);
                _say($"Shop staging sent once: '{_itemName}', id={_itemId}, QL={_itemQl}, " +
                    $"main -> '{bag.Name}'. Waiting for both inventory changes; no sale requested.");
            }
            catch (Exception ex) { Fail("item staging error: " + ex.Message); }
        }

        public int CountEligibleRejectItems()
        {
            Item[] main = MainItems();
            Item[] sell = Inventory.Backpacks
                .Where(bag => ManagerLoot.ManagedBagFamily.Matches(bag.Name,
                    ManagerLoot.ManagedBagFamily.Sell))
                .SelectMany(bag => Inventory.GetContainerItems(bag.Identity))
                .Where(x => x != null).ToArray();
            return sell.Count(x => _loot.Classify(x) == ManagerLoot.ItemClassification.Reject &&
                main.All(y => !SameItem(x, y)) && sell.Count(y => SameItem(x, y)) == 1);
        }

        public bool Start() => Start(1, false);

        public bool Start(int count, bool automaticRun = false)
        {
            LastFailure = null;
            Result = VerifiedOperationResult.None;
            if (IsActive) { LastFailure = "a shop sale is already active"; _say("A shop sale test is already active."); return false; }
            if (count < 1 || count > 20)
            { LastFailure = "shop sale count is outside 1-20"; Result = VerifiedOperationResult.Failed; _say("Shop sale count must be between 1 and 20."); return false; }
            Dynel actor = _route.FindVerifiedShopActor();
            if (actor == null)
            {
                LastFailure = "no unique exact surveyed shop actor is visible";
                Result = VerifiedOperationResult.Failed;
                _say("Shop sale held: no unique exact surveyed shop actor is visible at the target; no item was moved.");
                return false;
            }
            if (Trade.TradeTarget.HasValue && Trade.TradeTarget.Value != actor.Identity)
            { LastFailure = "another trade target is active"; Result = VerifiedOperationResult.Failed; _say("Shop sale held: another trade target is active; close it before using the surveyed shop."); return false; }
            _actor = actor.Identity;
            _requested = count;
            _automaticRun = automaticRun;
            _completed = 0;
            _sourceBag = _itemSlot = Identity.None;
            _itemName = null;
            _itemId = _itemQl = 0;
            _shopSeen = _openSeen = _addEcho = _completeSeen = _declined = false;
            Result = VerifiedOperationResult.Running;
            _phase = Phase.Opening;
            _phaseStarted = DateTime.UtcNow;
            _nextTick = DateTime.MinValue;
            try
            {
                actor.Use();
                _say($"Opening exact surveyed shop actor {_actor} for up to {_requested} verified sale(s); " +
                    "waiting for shop update and trade open.");
            }
            catch (Exception ex) { Fail("could not use shop actor: " + ex.Message); return false; }
            return true;
        }

        public void Stop()
        {
            if (!IsActive) return;
            Phase old = _phase;
            _phase = Phase.Idle;
            Result = VerifiedOperationResult.Failed;
            LastFailure = old == Phase.AddSent || old == Phase.AcceptSent
                ? "sale stopped with an item possibly in the trade window"
                : "sale stopped before completion";
            _say(old == Phase.AddSent || old == Phase.AcceptSent
                ? $"Shop sale stopped after {_completed} verified sale(s), with an item possibly in the trade window. Inspect the shop and inventory before retrying."
                : old == Phase.StageMoveSent || old == Phase.StagingOnlySent
                    ? $"Shop sale stopped after {_completed} verified sale(s); a bag move may have been sent. Inspect RKM Sell and main inventory."
                    : $"Shop sale stopped after {_completed} verified sale(s); no further item was submitted.");
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
            if (_phase == Phase.StagingOnlySent && Game.IsZoning)
            { Fail("zoning started before staging was verified"); return; }
            if (!IsActive || Game.IsZoning || DateTime.UtcNow < _nextTick) return;
            _nextTick = DateTime.UtcNow.AddMilliseconds(200);
            try { Tick(); }
            catch (Exception ex) { Fail("shop sale error: " + ex.Message); }
        }

        private void Tick()
        {
            if (_phase == Phase.StagingOnlySent)
            {
                var bag = Inventory.Backpacks.FirstOrDefault(x => x.Identity == _sourceBag &&
                    ManagerLoot.ManagedBagFamily.Matches(x.Name, ManagerLoot.ManagedBagFamily.Sell));
                if (bag == null) { Fail("selected RKM Sell bag disappeared or was renamed"); return; }
                int main = MainItems().Count(Matches);
                int stored = Inventory.GetContainerItems(_sourceBag).Count(Matches);
                if (main == _mainBefore - 1 && stored == _bagBefore + 1)
                {
                    _phase = Phase.Idle;
                    _say($"Shop staging verified: '{_itemName}' left main and appeared in '{bag.Name}'. " +
                        "No sale occurred; use /rkm logistics shop preview.");
                }
                else if (TimedOut(8)) Fail($"main-to-RKM Sell move not verified (main={main}, bag={stored})");
                return;
            }
            if (_route.FindVerifiedShopActor(_actor.ToString()) == null)
            { Fail("exact surveyed shop actor, position, or playfield was lost"); return; }
            if (_declined) { Fail("shop trade was declined"); return; }
            if (_phase == Phase.Opening)
            {
                if (_shopSeen && _openSeen)
                {
                    _phase = Phase.Selecting;
                    _phaseStarted = DateTime.UtcNow;
                    _say("Exact shop trade opened and inventory update received; selecting one unprotected Reject from RKM Sell.");
                }
                else if (TimedOut(10)) Fail($"shop did not finish opening (trade={_openSeen}, inventory={_shopSeen})");
                return;
            }
            if (_phase == Phase.BetweenSales)
            {
                if (!TimedOut(1)) return;
                if (Item.HasPendingUse || Spell.HasPendingCast)
                { if (TimedOut(10)) Fail("another item use or spell remained pending between sales"); return; }
                if (Trade.TradeTarget.HasValue && Trade.TradeTarget.Value != _actor)
                { Fail("active trade target changed between verified sales"); return; }
                if (Trade.TradeTarget.HasValue && Trade.TradeTarget.Value == _actor)
                {
                    _phase = Phase.Selecting;
                    _phaseStarted = DateTime.UtcNow;
                    _say($"Verified {_completed}/{_requested} sale(s); shop trade remains open. " +
                        "Selecting the next RKM Sell Reject item.");
                }
                else
                {
                    Dynel actor = _route.FindVerifiedShopActor(_actor.ToString());
                    _shopSeen = _openSeen = false;
                    _phase = Phase.Opening;
                    _phaseStarted = DateTime.UtcNow;
                    actor.Use();
                    _say($"Verified {_completed}/{_requested} sale(s); reopening exact shop actor {_actor} " +
                        "for the next item.");
                }
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
                if (Inventory.NumFreeSlots < 1)
                { Fail("no free main-inventory slot to stage a Reject from RKM Sell"); return; }
                var sellItems = Inventory.Backpacks
                    .Where(bag => ManagerLoot.ManagedBagFamily.Matches(bag.Name,
                        ManagerLoot.ManagedBagFamily.Sell))
                    .OrderBy(bag => ManagerLoot.ManagedBagFamily.Order(bag.Name,
                        ManagerLoot.ManagedBagFamily.Sell))
                    .SelectMany(bag => Inventory.GetContainerItems(bag.Identity)
                        .Where(x => x != null)
                        .Select(x => new { Bag = bag.Identity, Item = x })).ToArray();
                var staged = sellItems
                    .Where(x => _loot.Classify(x.Item) == ManagerLoot.ItemClassification.Reject)
                    .Where(x => main.All(y => !SameItem(x.Item, y)))
                    .Where(x => sellItems.Count(y => SameItem(x.Item, y.Item)) == 1)
                    .FirstOrDefault();
                if (staged == null)
                {
                    if (_completed > 0)
                    {
                        _phase = Phase.Idle;
                        Result = VerifiedOperationResult.Succeeded;
                        LastFailure = null;
                        _say($"Shop selling finished after {_completed} verified sale(s); " +
                            "no further unprotected, unambiguous Reject item was found in RKM Sell.");
                    }
                    else Fail("no unprotected, unambiguous ManagerLoot Reject item in an RKM Sell bag");
                    return;
                }
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
                    _completed++;
                    _say($"Shop sale verified for '{_itemName}': trade complete, item left main inventory, " +
                        $"cash {_cashBefore} -> {cash}; {_completed}/{_requested} sale(s) verified.");
                    if (_completed >= _requested)
                    {
                        _phase = Phase.Idle;
                        Result = VerifiedOperationResult.Succeeded;
                        LastFailure = null;
                        _say($"Shop selling complete: {_completed} item(s) sold and verified.");
                        if (_automaticRun) _say("Automatic shop return will start next.");
                    }
                    else
                    {
                        _phase = Phase.BetweenSales;
                        _phaseStarted = DateTime.UtcNow;
                    }
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
            _addEcho = _completeSeen = false;
            _phase = Phase.AddSent;
            _phaseStarted = DateTime.UtcNow;
            Trade.AddItem(item);
            _say($"Shop sale AddItem sent: '{_itemName}', id={_itemId}, QL={_itemQl}, " +
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
            Result = VerifiedOperationResult.Failed;
            LastFailure = reason;
            _say($"Shop selling stopped after {_completed} verified sale(s): " + reason + ". " +
                (old == Phase.AddSent || old == Phase.AcceptSent
                    ? $"Inspect the trade window and '{_itemName}' before retrying; no trade action was retried."
                    : old == Phase.StagingOnlySent
                        ? $"Check main inventory and RKM Sell for '{_itemName}'; the staging move was not retried."
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
