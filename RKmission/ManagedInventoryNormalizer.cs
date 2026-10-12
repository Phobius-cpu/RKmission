using System;
using System.Linq;
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
        private Identity _item = Identity.None, _source = Identity.None, _destination = Identity.None;
        private DateTime _sent;
        private bool _toMain;
        private string _sourceLabel;
        public bool IsActive { get; private set; }
        public bool Failed { get; private set; }
        public string Failure { get; private set; }
        public int Rescued { get; private set; }
        public int StagedForBank { get; private set; }
        public bool HasPendingTransfer => _item != Identity.None;

        public void Start()
        {
            IsActive = true; Failed = false; Failure = null; Rescued = StagedForBank = 0;
            _item = _source = _destination = Identity.None;
        }

        public ManagedInventoryNormalizer(ManagerLoot.ManagerLoot loot, InventoryPolicy policy, Action<string> say)
        { _loot = loot; _policy = policy; _say = say; }

        public void Tick()
        {
            if (!IsActive || Failed || Item.HasPendingUse || Spell.HasPendingCast) return;
            if (_item != Identity.None)
            {
                bool sourceGone = !Inventory.GetContainerItems(_source).Any(x => x != null && x.UniqueIdentity == _item);
                bool arrived = _toMain
                    ? Inventory.Items.Any(x => x != null && x.Slot.Type == IdentityType.Inventory && x.UniqueIdentity == _item)
                    : Inventory.GetContainerItems(_destination).Any(x => x != null && x.UniqueIdentity == _item);
                if (sourceGone && arrived)
                {
                    Rescued++; if (_toMain) StagedForBank++;
                    _say($"Inventory normalization verified Keep {_item} moved from {_sourceLabel} " +
                        (_toMain ? "to main for bank fallback." : "to RK Loot."));
                    _item = _source = _destination = Identity.None; return;
                }
                if (DateTime.UtcNow - _sent > TimeSpan.FromSeconds(8))
                    Fail($"Keep {_item} move from RK Junk was not verified at source and destination");
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
                var baggedFallback = Inventory.Backpacks
                    .Where(bag => !ManagerLoot.ManagedBagFamily.IsKeep(bag.Name))
                    .SelectMany(bag => Inventory.GetContainerItems(bag.Identity)
                        .Where(item => item != null).Select(item => new { Bag = bag, Item = item }))
                    .FirstOrDefault(entry => _policy.IsBankEligible(entry.Item, _loot));
                if (baggedFallback == null) { IsActive = false; return; }
                if (Inventory.NumFreeSlots < 1) { Fail($"bank-eligible Keep in '{baggedFallback.Bag.Name}' cannot be staged because main inventory is full"); return; }
                _item = baggedFallback.Item.UniqueIdentity; _source = baggedFallback.Bag.Identity;
                _sourceLabel = $"'{baggedFallback.Bag.Name}'";
                _destination = Identity.None; _toMain = true; _sent = DateTime.UtcNow;
                baggedFallback.Item.MoveToInventory();
                _say($"Inventory normalization: staging bank-eligible Keep '{baggedFallback.Item.Name}' from '{baggedFallback.Bag.Name}' to main before deposit.");
                return;
            }

            var destination = Inventory.Backpacks
                .Where(bag => ManagerLoot.ManagedBagFamily.IsKeep(bag.Name) && bag.Items.Count < 21)
                .OrderBy(bag => ManagerLoot.ManagedBagFamily.Order(bag.Name, ManagerLoot.ManagedBagFamily.Keep))
                .ThenBy(bag => bag.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
            _item = misplaced.Item.UniqueIdentity; _source = misplaced.Bag.Identity;
            _sourceLabel = $"RK Junk '{misplaced.Bag.Name}'";
            if (destination != null)
            {
                string destinationName = destination.Name;
                _destination = destination.Identity; _toMain = false;
                misplaced.Item.MoveToContainer(destination);
                _say($"Inventory normalization: moving Keep '{misplaced.Item.Name}' from {misplaced.Bag.Name} to {destinationName}.");
            }
            else
            {
                if (Inventory.NumFreeSlots < 1) { Fail("Keep is stranded in RK Junk, no RK Loot capacity and no free main slot for bank fallback"); return; }
                _toMain = true; _destination = Identity.None;
                misplaced.Item.MoveToInventory();
                _say($"Inventory normalization: staging Keep '{misplaced.Item.Name}' from {misplaced.Bag.Name} to main; RK Loot is missing/full, so bank fallback is required.");
            }
            _sent = DateTime.UtcNow;
        }

        public int MisplacedKeepCount() => Inventory.Backpacks
            .Where(bag => ManagerLoot.ManagedBagFamily.IsSell(bag.Name))
            .SelectMany(bag => Inventory.GetContainerItems(bag.Identity)).Count(item =>
                item != null && _loot.Classify(item) == ManagerLoot.ItemClassification.Keep);

        private void Fail(string reason) { Failed = true; IsActive = false; Failure = reason; }
    }
}
