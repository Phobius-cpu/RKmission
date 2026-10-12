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
        private Identity _source = Identity.None, _destination = Identity.None;
        private DateTime _sent;
        private bool _toMain, _pending, _existingMainReservationsCollected;
        private int _itemId, _itemLowId, _itemHighId, _itemQl, _sourceBefore, _destinationBefore, _maximumBankStages;
        private string _itemName, _sourceLabel;
        private Item _sourceItem;
        private HashSet<Identity> _destinationSlotsBefore = new HashSet<Identity>();
        public bool IsActive { get; private set; }
        public bool Failed { get; private set; }
        public string Failure { get; private set; }
        public int Rescued { get; private set; }
        public int StagedForBank { get; private set; }
        public bool HasPendingTransfer => _pending;
        public BankKeepReservationLedger BankReservations { get; } = new BankKeepReservationLedger();

        public void Start(int maximumBankStages)
        {
            IsActive = true; Failed = false; Failure = null; Rescued = StagedForBank = 0;
            BankReservations.Clear();
            _pending = false; _source = _destination = Identity.None;
            _existingMainReservationsCollected = false;
            _maximumBankStages = Math.Max(1, maximumBankStages);
        }

        public ManagedInventoryNormalizer(ManagerLoot.ManagerLoot loot, InventoryPolicy policy, Action<string> say)
        { _loot = loot; _policy = policy; _say = say; }

        public void Tick()
        {
            if (!IsActive || Failed || Item.HasPendingUse || Spell.HasPendingCast) return;
            if (_pending)
            {
                int sourceNow = Matching(Inventory.GetContainerItems(_source));
                int destinationNow = _toMain ? Matching(MainItems()) :
                    Matching(Inventory.GetContainerItems(_destination));
                if (sourceNow == _sourceBefore - 1 && destinationNow == _destinationBefore + 1)
                {
                    Item destinationItem = DestinationItems().FirstOrDefault(item => MatchesDetails(item) &&
                        (!_destinationSlotsBefore.Contains(item.Slot) ||
                         (Usable(item.UniqueIdentity) && item.UniqueIdentity == _sourceItem.UniqueIdentity)));
                    if (destinationItem == null || !_loot.PreserveVerifiedKeepOwnership(_sourceItem, destinationItem))
                    { Fail($"Keep '{_itemName}' arrived, but exact destination ownership could not be rebound safely"); return; }
                    Rescued++;
                    if (_toMain)
                    {
                        BankReservations.Reserve(destinationItem, _sourceLabel);
                        StagedForBank++;
                    }
                    _say($"Inventory normalization verified Keep '{_itemName}' (id={_itemId}, QL={_itemQl}) moved from {_sourceLabel} " +
                        (_toMain ? $"to main for bank fallback ({StagedForBank}/{_maximumBankStages})." : "to RK Loot."));
                    _say($"Inventory normalization: preserved Keep ownership for '{_itemName}' after verified {_sourceLabel} -> " +
                        (_toMain ? "main" : "RK Loot") + " transfer.");
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
                    if (!ReserveExistingMainKeeps()) return;
                    _existingMainReservationsCollected = true;
                }
                if (StagedForBank >= _maximumBankStages) { IsActive = false; return; }
                var baggedFallback = Inventory.Backpacks
                    .Where(bag => !ManagerLoot.ManagedBagFamily.IsKeep(bag.Name))
                    .SelectMany(bag => Inventory.GetContainerItems(bag.Identity)
                        .Where(item => item != null).Select(item => new { Bag = bag, Item = item }))
                    .FirstOrDefault(entry => _policy.IsBankEligible(entry.Item, _loot));
                if (baggedFallback == null) { IsActive = false; return; }
                if (Inventory.NumFreeSlots < 1) { Fail($"bank-eligible Keep in '{baggedFallback.Bag.Name}' cannot be staged because main inventory is full"); return; }
                _sourceLabel = $"'{baggedFallback.Bag.Name}'";
                Prepare(baggedFallback.Item, baggedFallback.Bag.Identity, Identity.None, true);
                baggedFallback.Item.MoveToInventory();
                _say($"Inventory normalization: staging bank-eligible Keep '{baggedFallback.Item.Name}' from '{baggedFallback.Bag.Name}' to main before deposit.");
                return;
            }

            var destination = Inventory.Backpacks
                .Where(bag => ManagerLoot.ManagedBagFamily.IsKeep(bag.Name) && bag.Items.Count < 21)
                .OrderBy(bag => ManagerLoot.ManagedBagFamily.Order(bag.Name, ManagerLoot.ManagedBagFamily.Keep))
                .ThenBy(bag => bag.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
            _sourceLabel = $"RK Junk '{misplaced.Bag.Name}'";
            if (destination != null)
            {
                string destinationName = destination.Name;
                Prepare(misplaced.Item, misplaced.Bag.Identity, destination.Identity, false);
                misplaced.Item.MoveToContainer(destination);
                _say($"Inventory normalization: moving Keep '{misplaced.Item.Name}' from {misplaced.Bag.Name} to {destinationName}.");
            }
            else
            {
                if (StagedForBank >= _maximumBankStages)
                { Fail($"Keep remains in RK Junk because the bank-stage limit {_maximumBankStages} was reached and RK Loot has no capacity; shop execution is blocked"); return; }
                if (Inventory.NumFreeSlots < 1) { Fail("Keep is stranded in RK Junk, no RK Loot capacity and no free main slot for bank fallback"); return; }
                Prepare(misplaced.Item, misplaced.Bag.Identity, Identity.None, true);
                misplaced.Item.MoveToInventory();
                _say($"Inventory normalization: staging Keep '{misplaced.Item.Name}' from {misplaced.Bag.Name} to main; RK Loot is missing/full, so bank fallback is required.");
            }
        }

        private void Prepare(Item item, Identity source, Identity destination, bool toMain)
        {
            _itemId = item.Id; _itemLowId = item.LowId; _itemHighId = item.HighId;
            _itemQl = item.QualityLevel; _itemName = item.Name; _sourceItem = item;
            _source = source; _destination = destination; _toMain = toMain;
            _sourceBefore = Matching(Inventory.GetContainerItems(source));
            _destinationBefore = toMain ? Matching(MainItems()) :
                Matching(Inventory.GetContainerItems(destination));
            _destinationSlotsBefore = new HashSet<Identity>(DestinationItems().Where(MatchesDetails).Select(x => x.Slot));
            _pending = true; _sent = DateTime.UtcNow;
        }

        private int Matching(System.Collections.Generic.IEnumerable<Item> items) => items.Count(item =>
            MatchesDetails(item));

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

        private bool IsSafeBankEligible(Item item) => item != null &&
            !_policy.IsOperationalItem(item) && _loot.ProtectionReason(item) == null &&
            _policy.IsBankEligible(item, _loot);

        public int MainBankEligibleCount() => MainItems().Count(IsSafeBankEligible);

        private bool MatchesDetails(Item item) => item != null && item.Id == _itemId &&
            item.LowId == _itemLowId && item.HighId == _itemHighId && item.QualityLevel == _itemQl &&
            string.Equals(item.Name, _itemName, StringComparison.Ordinal);
        private IEnumerable<Item> DestinationItems() => _toMain ? MainItems() : Inventory.GetContainerItems(_destination);
        private static bool Usable(Identity identity) => identity != Identity.None &&
            identity.Type != IdentityType.None && identity.Instance != 0;
        private static Item[] MainItems() => Inventory.Items.Where(item => item != null &&
            item.Slot.Type == IdentityType.Inventory && item.UniqueIdentity.Type != IdentityType.Container).ToArray();

        public int BaggedBankEligibleCount() => Inventory.Backpacks
            .Where(bag => !ManagerLoot.ManagedBagFamily.IsKeep(bag.Name))
            .SelectMany(bag => Inventory.GetContainerItems(bag.Identity)).Count(item =>
                item != null && _policy.IsBankEligible(item, _loot));

        public int MisplacedKeepCount() => Inventory.Backpacks
            .Where(bag => ManagerLoot.ManagedBagFamily.IsSell(bag.Name))
            .SelectMany(bag => Inventory.GetContainerItems(bag.Identity)).Count(item =>
                item != null && _loot.Classify(item) == ManagerLoot.ItemClassification.Keep);

        private void Fail(string reason) { Failed = true; IsActive = false; Failure = reason; }
    }
}
