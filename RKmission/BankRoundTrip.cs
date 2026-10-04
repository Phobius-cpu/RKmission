using System;
using System.Linq;
using AOSharp.Common.GameData;
using AOSharp.Core;
using AOSharp.Core.Inventory;

namespace RKmission
{
    // Explicit one-item bank API test. A transfer is never retried after being
    // sent: the server may have accepted it before the inventory view updates.
    internal sealed class BankRoundTrip : IDisposable
    {
        private enum Phase { Idle, Opening, Settling, DepositSent, WithdrawalSent }

        private readonly LogisticsRouteNavigator _route;
        private readonly ManagerLoot.ManagerLoot _loot;
        private readonly Action<string> _say;
        private Phase _phase;
        private DateTime _phaseStarted, _nextTick;
        private int _itemId, _itemQl, _mainBefore, _bankBefore;
        private string _itemName;

        public bool IsActive => _phase != Phase.Idle;
        public string Status => _phase == Phase.Idle ? "inactive" :
            $"{_phase} for '{_itemName ?? "unselected"}' ({_itemId}, QL {_itemQl})";

        public BankRoundTrip(LogisticsRouteNavigator route, ManagerLoot.ManagerLoot loot, Action<string> say)
        {
            _route = route;
            _loot = loot;
            _say = say;
            Game.OnUpdate += OnUpdate;
        }

        public void Start()
        {
            if (IsActive) { _say("Bank round-trip test is already active."); return; }
            SimpleItem terminal = _route.FindVerifiedBankTerminal();
            if (terminal == null)
            {
                _say("Bank test held: finish a surveyed bank route and stand near its exact recorded terminal.");
                return;
            }
            _itemName = null;
            _itemId = _itemQl = 0;
            _phase = Inventory.Bank?.IsOpen == true ? Phase.Settling : Phase.Opening;
            _phaseStarted = DateTime.UtcNow;
            _nextTick = DateTime.MinValue;
            if (_phase == Phase.Opening)
            {
                try
                {
                    terminal.Use();
                    _say($"Opening verified bank terminal {terminal.Identity}; waiting for bank inventory.");
                }
                catch (Exception ex) { Fail("could not use bank terminal: " + ex.Message); }
            }
            else _say("Verified bank is open; waiting for inventory to settle before selecting one Keep item.");
        }

        public void Stop()
        {
            if (!IsActive) return;
            bool transferPending = _phase == Phase.DepositSent || _phase == Phase.WithdrawalSent;
            _phase = Phase.Idle;
            _say(transferPending
                ? "Bank test stopped while an item transfer may be pending. Inspect main inventory and bank before another test."
                : "Bank round-trip test stopped; no item transfer was sent.");
        }

        private void OnUpdate(object sender, float elapsed)
        {
            if (!IsActive || Game.IsZoning || DateTime.UtcNow < _nextTick) return;
            _nextTick = DateTime.UtcNow.AddMilliseconds(200);
            try { Tick(); }
            catch (Exception ex) { Fail("bank API test error: " + ex.Message); }
        }

        private void Tick()
        {
            if (!_route.IsAtBankTarget)
            { Fail("surveyed bank target or playfield was lost"); return; }
            bool bankOpen = Inventory.Bank?.IsOpen == true;
            if (_phase == Phase.Opening)
            {
                if (bankOpen)
                {
                    _phase = Phase.Settling;
                    _phaseStarted = DateTime.UtcNow;
                    _say("Bank opened; waiting for item snapshot to settle.");
                }
                else if (TimedOut(10)) Fail("bank did not open within ten seconds");
                return;
            }
            if (!bankOpen)
            { Fail("bank closed before round-trip verification"); return; }
            if (_phase == Phase.Settling)
            {
                if (Item.HasPendingUse || Spell.HasPendingCast)
                {
                    if (TimedOut(10)) Fail("another item use or spell remained pending");
                    return;
                }
                if (!TimedOut(1)) return;
                Item[] main = MainItems();
                Item[] bank = BankItems();
                Item item = main.Where(x => _loot.Classify(x) == ManagerLoot.ItemClassification.Keep)
                    .Where(x => main.Count(y => SameItem(x, y)) == 1 &&
                        bank.All(y => !SameItem(x, y)))
                    .OrderBy(x => x.Name).FirstOrDefault();
                if (item == null)
                {
                    Fail("no unambiguous main-inventory Keep item absent from the bank was available");
                    return;
                }
                _itemId = item.Id;
                _itemQl = item.QualityLevel;
                _itemName = item.Name;
                _mainBefore = main.Count(Matches);
                _bankBefore = bank.Count(Matches);
                _phase = Phase.DepositSent;
                _phaseStarted = DateTime.UtcNow;
                item.MoveToBank();
                _say($"Bank test deposit sent: '{_itemName}', id={_itemId}, QL={_itemQl}, ManagerLoot=Keep; " +
                    $"main={_mainBefore}, bank={_bankBefore} before transfer. Awaiting both inventory changes.");
                return;
            }
            int mainCount = MainItems().Count(Matches);
            int bankCount = BankItems().Count(Matches);
            if (_phase == Phase.DepositSent)
            {
                if (mainCount == _mainBefore - 1 && bankCount == _bankBefore + 1)
                {
                    Item stored = BankItems().SingleOrDefault(Matches);
                    if (stored == null)
                    { Fail("deposited item could not be identified in bank snapshot"); return; }
                    _say($"Bank deposit verified: '{_itemName}', main={mainCount}, bank={bankCount}. Retrieving that item.");
                    _phase = Phase.WithdrawalSent;
                    _phaseStarted = DateTime.UtcNow;
                    stored.MoveToInventory();
                }
                else if (TimedOut(10))
                    Fail($"deposit not verified (main={mainCount}, bank={bankCount})");
                return;
            }
            if (_phase == Phase.WithdrawalSent)
            {
                if (mainCount == _mainBefore && bankCount == _bankBefore)
                {
                    _phase = Phase.Idle;
                    _say($"Bank round trip verified for '{_itemName}': main={mainCount}, bank={bankCount}; " +
                        "the item returned to main inventory. You can now use /rkm logistics return.");
                }
                else if (TimedOut(10))
                    Fail($"retrieval not verified (main={mainCount}, bank={bankCount})");
            }
        }

        private bool TimedOut(int seconds) => DateTime.UtcNow - _phaseStarted >= TimeSpan.FromSeconds(seconds);
        private bool Matches(Item item) => item != null && item.Id == _itemId &&
            item.QualityLevel == _itemQl && string.Equals(item.Name, _itemName, StringComparison.Ordinal);
        private static bool SameItem(Item left, Item right) => left.Id == right.Id &&
            left.QualityLevel == right.QualityLevel && string.Equals(left.Name, right.Name, StringComparison.Ordinal);
        private static Item[] MainItems() => Inventory.Items.Where(x =>
            x != null && x.Slot.Type == IdentityType.Inventory &&
            x.UniqueIdentity.Type != IdentityType.Container).ToArray();
        private static Item[] BankItems() => Inventory.Bank?.Items?.Where(x => x != null).ToArray() ??
            Array.Empty<Item>();

        private void Fail(string reason)
        {
            bool transferSent = _phase == Phase.DepositSent || _phase == Phase.WithdrawalSent;
            _phase = Phase.Idle;
            _say("Bank round-trip test stopped: " + reason + ". " + (transferSent
                ? $"Check whether '{_itemName}' is in main inventory or bank before retrying; no transfer was retried."
                : "No item transfer was sent."));
        }

        public void Dispose() { Game.OnUpdate -= OnUpdate; Stop(); }
    }
}
