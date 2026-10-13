using System;
using System.Linq;
using System.Collections.Generic;
using AOSharp.Common.GameData;
using AOSharp.Core;
using AOSharp.Core.Inventory;

namespace RKmission
{
    // Removes Keep items from sale-source bags before any destructive plan is made.
    // Each move is single-shot and must be observed at both source and destination.
    internal sealed class ManagedInventoryNormalizer
    {
        private readonly ManagerLoot.ManagerLoot _loot;
        private readonly InventoryPolicy _policy;
        private readonly Action<string> _say;
        private readonly DeferredKeepLedger _deferredKeeps;
        private Identity _source = Identity.None, _destination = Identity.None;
        private DateTime _sent;
        private bool _toMain, _pending, _preserveOwnership, _existingMainReservationsCollected;
        private bool _drainManagedKeeps;
        private int _itemId, _itemLowId, _itemHighId, _itemQl, _sourceBefore, _destinationBefore, _maximumBankStages;
        private string _itemName, _sourceLabel, _destinationLabel;
        private Item _sourceItem;
        private DeferredKeepLedger.Entry _deferredTransferEntry;
        private HashSet<Identity> _destinationSlotsBefore = new HashSet<Identity>();
        public bool IsActive { get; private set; }
        public bool Failed { get; private set; }
        public string Failure { get; private set; }
        public int Rescued { get; private set; }
        public int StagedForBank { get; private set; }
        public bool HasPendingTransfer => _pending;
        public BankKeepReservationLedger BankReservations { get; } = new BankKeepReservationLedger();

        public void Start(int maximumBankStages, bool drainManagedKeeps = false)
        {
            IsActive = true; Failed = false; Failure = null; Rescued = StagedForBank = 0;
            BankReservations.Clear();
            _pending = false; _source = _destination = Identity.None;
            _deferredTransferEntry = null;
            _existingMainReservationsCollected = false;
            _maximumBankStages = Math.Max(1, maximumBankStages);
            _drainManagedKeeps = drainManagedKeeps;
        }

        public ManagedInventoryNormalizer(ManagerLoot.ManagerLoot loot, InventoryPolicy policy,
            DeferredKeepLedger deferredKeeps, Action<string> say)
        { _loot = loot; _policy = policy; _deferredKeeps = deferredKeeps; _say = say; }

        public void Tick()
        {
            if (!IsActive || Failed || Item.HasPendingUse || Spell.HasPendingCast) return;
            if (_pending)
            {
                int sourceNow = Matching(SourceItems());
                int destinationNow = _toMain ? Matching(MainItems()) :
                    Matching(Inventory.GetContainerItems(_destination));
                if (sourceNow == _sourceBefore - 1 && destinationNow == _destinationBefore + 1)
                {
                    Item destinationItem = DestinationItems().FirstOrDefault(item => MatchesDetails(item) &&
                        (!_destinationSlotsBefore.Contains(item.Slot) ||
                         (Usable(item.UniqueIdentity) && item.UniqueIdentity == _sourceItem.UniqueIdentity)));
                    if (destinationItem == null || (_preserveOwnership && _deferredTransferEntry == null &&
                        !_loot.PreserveVerifiedKeepOwnership(_sourceItem, destinationItem)))
                    { Fail($"Keep '{_itemName}' arrived, but exact destination ownership could not be rebound safely"); return; }
                    bool wasDeferredTransfer = _deferredTransferEntry != null;
                    if (wasDeferredTransfer)
                    {
                        _deferredKeeps.ConsumeOne(_deferredTransferEntry);
                        _deferredTransferEntry = null;
                    }
                    Rescued++;
                    if (_toMain)
                    {
                        BankReservations.Reserve(destinationItem, _sourceLabel);
                        StagedForBank++;
                    }
                    _say($"Inventory normalization verified Keep '{_itemName}' (id={_itemId}, QL={_itemQl}) moved from {_sourceLabel} " +
                        (_toMain ? $"to main for bank fallback ({StagedForBank}/{_maximumBankStages})." : $"to {_destinationLabel}."));
                    if (_preserveOwnership && !wasDeferredTransfer)
                        _say($"Inventory normalization: preserved Keep ownership for '{_itemName}' after verified {_sourceLabel} -> " +
                            (_toMain ? "main" : _destinationLabel) + " transfer.");
                    _pending = false; _source = _destination = Identity.None; return;
                }
                if (DateTime.UtcNow - _sent > TimeSpan.FromSeconds(8))
                    Fail($"Keep '{_itemName}' move was not verified at source and destination " +
                        $"(source {_sourceBefore}->{sourceNow}, destination {_destinationBefore}->{destinationNow})");
                return;
            }

            var misplaced = Inventory.Backpacks
                .Where(bag => ManagerLoot.ManagedBagFamily.IsSell(bag.Name))
                .OrderBy(bag => ManagerLoot.ManagedBagFamily.Order(bag.Name, ManagerLoot.ManagedBagFamily.Sell))
                .SelectMany(bag => Inventory.GetContainerItems(bag.Identity)
                    .Where(item => item != null).Select(item => new { Bag = bag, Item = item }))
                .FirstOrDefault(entry => _loot.Classify(entry.Item) == ManagerLoot.ItemClassification.Keep);
            if (misplaced == null)
            {
                if (!_existingMainReservationsCollected)
                {
                    if (!RestoreDeferredMainKeeps()) return;
                    if (!_drainManagedKeeps && !PlaceExistingMainKeep()) return;
                    if (!ReserveExistingMainKeeps()) return;
                    _existingMainReservationsCollected = true;
                }
                if (StagedForBank >= _maximumBankStages) { IsActive = false; return; }
                var baggedFallback = Inventory.Backpacks
                    .Where(bag => _drainManagedKeeps || !ManagerLoot.ManagedBagFamily.IsKeep(bag.Name))
                    .SelectMany(bag => Inventory.GetContainerItems(bag.Identity)
                        .Where(item => item != null).Select(item => new { Bag = bag, Item = item }))
                    .FirstOrDefault(entry => IsSafeBankEligible(entry.Item));
                if (baggedFallback == null) { IsActive = false; return; }
                if (Inventory.NumFreeSlots < 1)
                {
                    if (BankReservations.Count > 0)
                    {
                        _say($"Inventory normalization: main inventory reached capacity after {BankReservations.Count} verified bank stage(s); closing this bounded round before staging more Keeps.");
                        IsActive = false;
                    }
                    else Fail($"bank-eligible Keep in '{baggedFallback.Bag.Name}' cannot be staged because main inventory is full");
                    return;
                }
                _sourceLabel = $"'{baggedFallback.Bag.Name}'";
                Prepare(baggedFallback.Item, baggedFallback.Bag.Identity, Identity.None, true);
                baggedFallback.Item.MoveToInventory();
                _say($"Inventory normalization: staging bank-eligible Keep '{baggedFallback.Item.Name}' from '{baggedFallback.Bag.Name}' to main before deposit.");
                return;
            }

            string requested = _loot.KeepBagName(misplaced.Item);
            var destination = Inventory.Backpacks
                .Where(bag => !string.IsNullOrWhiteSpace(requested) &&
                    ManagerLoot.ManagedBagFamily.Matches(bag.Name, requested) && bag.Items.Count < 21)
                .OrderBy(bag => ManagerLoot.ManagedBagFamily.Order(bag.Name, requested))
                .ThenBy(bag => bag.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
            _sourceLabel = $"RK Junk '{misplaced.Bag.Name}'";
            if (destination != null && (!_drainManagedKeeps || !IsSafeBankEligible(misplaced.Item)))
            {
                string destinationName = destination.Name;
                Prepare(misplaced.Item, misplaced.Bag.Identity, destination.Identity, false, true, destinationName);
                misplaced.Item.MoveToContainer(destination);
                _say($"Inventory normalization: moving Keep '{misplaced.Item.Name}' from {misplaced.Bag.Name} to {destinationName}.");
            }
            else
            {
                if (_drainManagedKeeps && !IsSafeBankEligible(misplaced.Item))
                { Fail($"protected or operational Keep '{misplaced.Item.Name}' remains in RK Junk and has no safe RK Loot destination; shop execution is blocked"); return; }
                if (StagedForBank >= _maximumBankStages)
                { Fail($"Keep remains in RK Junk because the bank-stage limit {_maximumBankStages} was reached and RK Loot has no capacity; shop execution is blocked"); return; }
                if (Inventory.NumFreeSlots < 1) { Fail("Keep is stranded in RK Junk, no RK Loot capacity and no free main slot for bank fallback"); return; }
                Prepare(misplaced.Item, misplaced.Bag.Identity, Identity.None, true);
                misplaced.Item.MoveToInventory();
                _say($"Inventory normalization: staging Keep '{misplaced.Item.Name}' from {misplaced.Bag.Name} to main for verified bank storage.");
            }
        }

        private void Prepare(Item item, Identity source, Identity destination, bool toMain,
            bool preserveOwnership = true, string destinationLabel = "")
        {
            _itemId = item.Id; _itemLowId = item.LowId; _itemHighId = item.HighId;
            _itemQl = item.QualityLevel; _itemName = item.Name; _sourceItem = item;
            _source = source; _destination = destination; _toMain = toMain;
            _preserveOwnership = preserveOwnership;
            _destinationLabel = string.IsNullOrWhiteSpace(destinationLabel)
                ? (toMain ? "main" : "requested Keep destination") : destinationLabel;
            _sourceBefore = Matching(SourceItems());
            _destinationBefore = toMain ? Matching(MainItems()) :
                Matching(Inventory.GetContainerItems(destination));
            _destinationSlotsBefore = new HashSet<Identity>(DestinationItems().Where(MatchesDetails).Select(x => x.Slot));
            _pending = true; _sent = DateTime.UtcNow;
        }

        private int Matching(System.Collections.Generic.IEnumerable<Item> items) => items.Count(item =>
            MatchesDetails(item));

        private bool PlaceExistingMainKeep()
        {
            foreach (Item item in MainItems().Where(item => item != null &&
                _loot.Classify(item) == ManagerLoot.ItemClassification.Keep &&
                !_policy.IsOperationalItem(item) && _loot.ProtectionReason(item) == null))
            {
                string requested = _loot.KeepBagName(item);
                var destination = Inventory.Backpacks.Where(bag =>
                    !string.IsNullOrWhiteSpace(requested) &&
                    ManagerLoot.ManagedBagFamily.Matches(bag.Name, requested) && bag.Items.Count < 21)
                    .OrderBy(bag => ManagerLoot.ManagedBagFamily.Order(bag.Name, requested))
                    .ThenBy(bag => bag.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
                if (destination == null) continue;

                _sourceLabel = "existing main inventory";
                bool preserveOwnership = !ManagerLoot.ManagedBagFamily.IsKeep(destination.Name);
                Prepare(item, Identity.None, destination.Identity, false, preserveOwnership, destination.Name);
                item.MoveToContainer(destination);
                _say($"Inventory normalization: moving existing main Keep '{item.Name}' to {destination.Name}.");
                return false;
            }
            return true;
        }

        private bool ReserveExistingMainKeeps()
        {
            Item[] main = MainItems();
            var eligibleGroups = main
                .Where(IsSafeBankEligible)
                .GroupBy(item => new { item.Id, item.LowId, item.HighId, item.QualityLevel, item.Name })
                .OrderBy(group => group.Key.Name, StringComparer.Ordinal)
                .ThenBy(group => group.Key.Id).ThenBy(group => group.Key.LowId)
                .ThenBy(group => group.Key.HighId).ThenBy(group => group.Key.QualityLevel);

            foreach (var group in eligibleGroups)
            {
                Item representative = group.First();
                int observed = group.Count();
                int reserved = BankReservations.CountMatching(representative);
                int missing = observed - reserved;
                if (missing <= 0) continue;
                int remaining = _maximumBankStages - BankReservations.Count;
                if (remaining <= 0) break;
                if (missing > remaining)
                {
                    Fail($"exact duplicate bank-eligible Keep '{representative.Name}' has {observed} main-inventory match(es), " +
                        $"but only {remaining} reservation slot(s) remain; the cycle cannot safely reserve a partial indistinguishable fingerprint batch");
                    return false;
                }
                foreach (Item item in group.Take(missing))
                {
                    BankReservations.Reserve(item, "existing main inventory");
                    StagedForBank++;
                    _say($"Inventory normalization: reserved existing main Keep '{item.Name}' for bank fallback " +
                        $"({StagedForBank}/{_maximumBankStages}).");
                }
            }
            return true;
        }

        private bool RestoreDeferredMainKeeps()
        {
            Item[] main = MainItems();
            foreach (DeferredKeepLedger.Entry entry in _deferredKeeps.Entries)
            {
                Item[] matches = _deferredKeeps.Matches(entry, main);
                if (matches.Length == 0)
                {
                    _deferredKeeps.Remove(entry);
                    _say($"Inventory normalization: expired deferred Keep {entry.Describe()}; no exact main-inventory match remains.");
                    continue;
                }
                if (matches.Length != entry.ExpectedCount)
                {
                    Fail($"deferred Keep ambiguity: expected {entry.ExpectedCount}, observed {matches.Length} exact main-inventory match(es) for {entry.Describe()}; no item was reserved");
                    return false;
                }
                if (matches.Any(item => _policy.IsOperationalItem(item) || _loot.ProtectionReason(item) != null))
                {
                    _deferredKeeps.Remove(entry);
                    _say($"Inventory normalization: expired deferred Keep {entry.Describe()} because it is now operational or protected.");
                    continue;
                }
                var destination = Inventory.Backpacks.Where(bag =>
                    !string.IsNullOrWhiteSpace(entry.RequestedDestination) &&
                    ManagerLoot.ManagedBagFamily.Matches(bag.Name, entry.RequestedDestination) && bag.Items.Count < 21)
                    .OrderBy(bag => ManagerLoot.ManagedBagFamily.Order(bag.Name, entry.RequestedDestination))
                    .ThenBy(bag => bag.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
                if (destination != null && !_drainManagedKeeps)
                {
                    Item item = matches.First();
                    Prepare(item, Identity.None, destination.Identity, false, false, destination.Name);
                    _deferredTransferEntry = entry;
                    item.MoveToContainer(destination);
                    _sourceLabel = "deferred main inventory";
                    _say($"Inventory normalization: moving restored deferred Keep '{item.Name}' from main to {destination.Name}; the current RK Loot destination takes precedence over bank fallback.");
                    return false;
                }
                int remaining = _maximumBankStages - BankReservations.Count;
                if (entry.ExpectedCount > remaining)
                {
                    Fail($"deferred Keep {entry.Describe()} has exact multiplicity {entry.ExpectedCount}, but only {remaining} reservation slot(s) remain; the cycle will not split an indistinguishable fingerprint batch");
                    return false;
                }
                foreach (Item item in matches)
                {
                    BankReservations.Reserve(item, "deferred Keep carry-forward");
                    StagedForBank++;
                    _say($"Inventory normalization: restored deferred Keep '{item.Name}' for bank fallback ({StagedForBank}/{_maximumBankStages}).");
                }
            }
            return true;
        }

        private bool IsSafeBankEligible(Item item) => item != null &&
            !_policy.IsOperationalItem(item) && _loot.ProtectionReason(item) == null &&
            (_drainManagedKeeps
                ? _loot.Classify(item) == ManagerLoot.ItemClassification.Keep
                : _policy.IsBankEligible(item, _loot));

        public int MainBankEligibleCount()
        {
            Item[] main = MainItems();
            return main.Count(item => IsSafeBankEligible(item) ||
                _deferredKeeps.Entries.Any(entry => entry.Matches(item) &&
                    !_policy.IsOperationalItem(item) && _loot.ProtectionReason(item) == null));
        }

        public int RetainDeferredMainKeeps()
        {
            Item[] deferred = MainItems().Where(IsSafeBankEligible).ToArray();
            _deferredKeeps.Replace(deferred, item => _loot.KeepBagName(item));
            return _deferredKeeps.Count;
        }

        public void ExpireDepositedDeferredKeeps() => _deferredKeeps.RemoveMissing(MainItems());

        private bool MatchesDetails(Item item) => item != null && item.Id == _itemId &&
            item.LowId == _itemLowId && item.HighId == _itemHighId && item.QualityLevel == _itemQl &&
            string.Equals(item.Name, _itemName, StringComparison.Ordinal);
        private IEnumerable<Item> DestinationItems() => _toMain ? MainItems() : Inventory.GetContainerItems(_destination);
        private IEnumerable<Item> SourceItems() => _source == Identity.None ? MainItems() :
            Inventory.GetContainerItems(_source);
        private static bool Usable(Identity identity) => identity != Identity.None &&
            identity.Type != IdentityType.None && identity.Instance != 0;
        private static Item[] MainItems() => Inventory.Items.Where(item => item != null &&
            item.Slot.Type == IdentityType.Inventory && item.UniqueIdentity.Type != IdentityType.Container).ToArray();

        public int BaggedBankEligibleCount(bool includeManagedKeeps = false) => Inventory.Backpacks
            .Where(bag => includeManagedKeeps || !ManagerLoot.ManagedBagFamily.IsKeep(bag.Name))
            .SelectMany(bag => Inventory.GetContainerItems(bag.Identity)).Count(item =>
                item != null && !_policy.IsOperationalItem(item) && _loot.ProtectionReason(item) == null &&
                (includeManagedKeeps
                    ? _loot.Classify(item) == ManagerLoot.ItemClassification.Keep
                    : _policy.IsBankEligible(item, _loot)));

        public int MisplacedKeepCount() => Inventory.Backpacks
            .Where(bag => ManagerLoot.ManagedBagFamily.IsSell(bag.Name))
            .SelectMany(bag => Inventory.GetContainerItems(bag.Identity)).Count(item =>
                item != null && _loot.Classify(item) == ManagerLoot.ItemClassification.Keep);

        private void Fail(string reason) { Failed = true; IsActive = false; Failure = reason; }
    }
}
