using System;
using System.Linq;
using AOSharp.Common.GameData;
using AOSharp.Core;
using AOSharp.Core.Inventory;

namespace RKmission
{
    // Explicit bank transfers at an exact surveyed terminal. A transfer is
    // never retried after being sent: the server may have accepted it before
    // the inventory view updates.
    internal sealed class BankTransactions : IDisposable
    {
        private enum Operation { RoundTrip, Store }
        private enum Phase { Idle, Opening, Settling, DepositSent, WithdrawalSent }

        private readonly LogisticsRouteNavigator _route;
        private readonly ManagerLoot.ManagerLoot _loot;
        private readonly Action<string> _say;
        private Operation _operation;
        private Phase _phase;
        private DateTime _phaseStarted, _nextTick;
        private int _itemId, _itemQl, _mainBefore, _bankBefore;
        private int _requested, _completed;
        private string _itemName, _terminalIdentity;

        public bool IsActive => _phase != Phase.Idle;
        public string Status => _phase == Phase.Idle ? "inactive" :
            $"{_operation} {_phase}, verified stores={_completed}/{_requested}, " +
            $"item='{_itemName ?? "unselected"}' ({_itemId}, QL {_itemQl})";

        public BankTransactions(LogisticsRouteNavigator route, ManagerLoot.ManagerLoot loot, Action<string> say)
        {
            _route = route;
            _loot = loot;
            _say = say;
            Game.OnUpdate += OnUpdate;
        }

        public void StartRoundTrip() => Start(Operation.RoundTrip, 1);
        public void StartStore(int count) => Start(Operation.Store, count);

        private void Start(Operation operation, int count)
        {
            if (IsActive) { _say("A bank transaction is already active."); return; }
            if (count < 1 || count > 20)
            { _say("Bank storage count must be between 1 and 20."); return; }
            SimpleItem terminal = _route.FindVerifiedBankTerminal();
            if (terminal == null)
            {
                _say("Bank transaction held: " +
                    (_route.IsActive ? "active route target or its exact bank terminal is not verified" :
                        "no unique recorded bank terminal is visible within 8 m") +
                    "; no item was moved. " + _route.BankTargetDiagnostic() + ".");
                return;
            }
            _terminalIdentity = terminal.Identity.ToString();
            _itemName = null;
            _itemId = _itemQl = 0;
            _operation = operation;
            _requested = count;
            _completed = 0;
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
            else _say($"Verified bank is open; waiting for inventory to settle before selecting " +
                (operation == Operation.Store ? $"up to {count} Keep item(s) for storage." :
                    "one Keep item for the round trip."));
        }

        public void Stop()
        {
            if (!IsActive) return;
            bool transferPending = _phase == Phase.DepositSent || _phase == Phase.WithdrawalSent;
            _phase = Phase.Idle;
            _say(transferPending
                ? $"Bank transaction stopped with {_completed} verified store(s) and an item transfer possibly pending. Inspect main inventory and bank before retrying."
                : $"Bank transaction stopped; {_completed} item(s) remain verified in bank from this run.");
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
            if (_route.FindVerifiedBankTerminal(_terminalIdentity) == null)
            { Fail("exact surveyed bank terminal, position, or playfield was lost"); return; }
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
            { Fail("bank closed before transfer verification"); return; }
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
                var candidates = main.Where(x => _loot.Classify(x) == ManagerLoot.ItemClassification.Keep);
                if (_operation == Operation.RoundTrip)
                    candidates = candidates.Where(x => main.Count(y => SameItem(x, y)) == 1 &&
                        bank.All(y => !SameItem(x, y)));
                Item item = candidates.OrderBy(x => x.Name).ThenBy(x => x.Id).FirstOrDefault();
                if (item == null)
                {
                    if (_operation == Operation.Store && _completed > 0)
                    {
                        _phase = Phase.Idle;
                        _say($"Bank storage finished: {_completed} verified item(s); no further ManagerLoot Keep items in main inventory.");
                    }
                    else Fail(_operation == Operation.Store
                        ? "no main-inventory ManagerLoot Keep item was available for storage"
                        : "no unambiguous main-inventory Keep item absent from the bank was available");
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
                _say($"Bank {_operation} deposit sent: '{_itemName}', id={_itemId}, QL={_itemQl}, ManagerLoot=Keep; " +
                    $"main={_mainBefore}, bank={_bankBefore} before transfer. Awaiting both inventory changes.");
                return;
            }
            int mainCount = MainItems().Count(Matches);
            int bankCount = BankItems().Count(Matches);
            if (_phase == Phase.DepositSent)
            {
                if (mainCount == _mainBefore - 1 && bankCount == _bankBefore + 1)
                {
                    if (_operation == Operation.Store)
                    {
                        _completed++;
                        _say($"Bank storage verified: '{_itemName}', main={mainCount}, bank={bankCount}; " +
                            $"{_completed}/{_requested} item(s) stored.");
                        if (_completed >= _requested)
                        {
                            _phase = Phase.Idle;
                            _say($"Bank storage complete: {_completed} item(s) remain in the bank. " +
                                "You can now use /rkm logistics return.");
                        }
                        else
                        {
                            _phase = Phase.Settling;
                            _phaseStarted = DateTime.UtcNow;
                        }
                        return;
                    }
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
            _say("Bank transaction stopped: " + reason + $". Verified stores={_completed}. " + (transferSent
                ? $"Check whether '{_itemName}' is in main inventory or bank before retrying; no transfer was retried."
                : "No further item transfer was sent."));
        }

        public void Dispose() { Game.OnUpdate -= OnUpdate; Stop(); }
    }
}
