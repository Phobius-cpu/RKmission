using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AOSharp.Common.GameData;
using AOSharp.Core;
using AOSharp.Core.Inventory;
using Newtonsoft.Json;

namespace RKmission
{
    internal enum InventorySettlement { Moving, Ready, NeedsSpace, Blocked }

    // Only optional corpse/chest work is suppressed; objective interactions
    // continue through MissionObjective and retain their own free-slot checks.
    internal sealed class InventoryPolicy
    {
        private static readonly string[] BuiltInOperationalFragments = {
            "lock pick", "lockpick", "lock-pick",
            "nano recharg", "nano kit", "nano stim", "health laboratory",
            "health kit", "health stim", "treatment laboratory", "treatment kit",
            "first aid", "medical kit", "free movement", "backpack", "back pack",
            "yalmaha", "jetbike", "kodiak", "kodaik", "vehicle", "stiletto"
        };
        public int MinimumFreeSlots { get; set; } = 3;
        // Additive exceptions in RKMissionData/inventory-policy.json. Built-in
        // supply and vehicle protection remains active if these lists are empty.
        public List<int> ProtectedItemIds { get; set; } = new List<int>();
        public List<string> ProtectedNameFragments { get; set; } = new List<string>();
        [JsonIgnore] public bool SkipOptionalLoot { get; private set; }
        private HashSet<Identity>? _inventoryAtDungeonEntry;
        private Identity _pendingItem = Identity.None, _pendingBag = Identity.None;
        private DateTime _moveSent;
        private DateTime _settlementStarted;
        private int _moveAttempts;
        private int _stagedRejects;
        [JsonIgnore] public string? SettlementFailure { get; private set; }

        // RKMission's operational inventory must never become shop input just
        // because a value rule did not list it. ManagerLoot calls this before
        // its Keep/Reject decision, including for items already in RKM Sell.
        public bool IsOperationalItem(Item item)
        {
            if (item == null) return false;
            if (item.UniqueIdentity.Type == IdentityType.Container) return true;
            try { if (item.GetStat(Stat.IsVehicle) > 0) return true; }
            catch { /* Some item views do not expose this stat; names still guard them. */ }
            if (ProtectedItemIds.Contains(item.Id)) return true;
            string name = item.Name?.Trim() ?? string.Empty;
            if (ProtectedNameFragments.Any(x => !string.IsNullOrWhiteSpace(x) &&
                name.IndexOf(x.Trim(), StringComparison.OrdinalIgnoreCase) >= 0)) return true;
            return IsBuiltInOperationalName(name);
        }

        internal static bool IsBuiltInOperationalName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            if (name.StartsWith("Ammo:", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("Ammo ", StringComparison.OrdinalIgnoreCase) ||
                name.IndexOf(" Ammo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Ammunition", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return BuiltInOperationalFragments.Any(x =>
                name.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        public void BeginMissionInventorySnapshot()
        {
            _pendingItem = _pendingBag = Identity.None;
            _moveAttempts = 0;
            _stagedRejects = 0;
            _settlementStarted = DateTime.MinValue;
            SettlementFailure = null;
            _inventoryAtDungeonEntry = new HashSet<Identity>(Inventory.Items
                .Where(item => item.Slot.Type == IdentityType.Inventory &&
                    item.UniqueIdentity.Type != IdentityType.Container)
                .Select(item => item.UniqueIdentity));
        }

        // Reuse Manager.Loot's value decision and the AOSharp item transfer.
        // Only newly collected rejects enter a pre-existing RKM Sell bag. This
        // staging move is checked against both inventories; it is not a sale.
        public InventorySettlement TickAfterVerifiedExit(ManagerLoot.ManagerLoot loot, Action<string> say)
        {
            if (_settlementStarted == DateTime.MinValue)
                _settlementStarted = DateTime.UtcNow;
            if (DateTime.UtcNow - _settlementStarted > TimeSpan.FromMinutes(2))
            {
                SettlementFailure = "Post-exit item staging made no safe completion within two minutes.";
                return InventorySettlement.Blocked;
            }
            if (_pendingItem != Identity.None)
            {
                bool inMain = Inventory.Items.Any(x => x.Slot.Type == IdentityType.Inventory &&
                    x.UniqueIdentity == _pendingItem);
                bool inBag = Inventory.GetContainerItems(_pendingBag).Any(x =>
                    x.UniqueIdentity == _pendingItem);
                if (!inMain && inBag)
                {
                    _pendingItem = _pendingBag = Identity.None;
                    _moveAttempts = 0;
                    _stagedRejects++;
                }
                else if (DateTime.UtcNow - _moveSent < TimeSpan.FromSeconds(5))
                    return InventorySettlement.Moving;
                else if (!inMain || _moveAttempts >= 2)
                {
                    SettlementFailure = $"Move to RKM Sell was not verified for item {_pendingItem}.";
                    return InventorySettlement.Blocked;
                }
            }
            if (_inventoryAtDungeonEntry != null)
            {
                foreach (Item item in Inventory.Items.Where(x =>
                    x.Slot.Type == IdentityType.Inventory &&
                    x.UniqueIdentity.Type != IdentityType.Container &&
                    !_inventoryAtDungeonEntry.Contains(x.UniqueIdentity) &&
                    (_pendingItem == Identity.None || x.UniqueIdentity == _pendingItem)))
                {
                    if (loot.Classify(item, newlyAcquiredDuringMission: true) !=
                        ManagerLoot.ItemClassification.Reject) continue;
                    var bag = Inventory.Backpacks
                        .Where(x => ManagerLoot.ManagedBagFamily.Matches(x.Name,
                            ManagerLoot.ManagedBagFamily.Sell) && x.Items.Count < 21)
                        .OrderBy(x => ManagerLoot.ManagedBagFamily.Order(x.Name,
                            ManagerLoot.ManagedBagFamily.Sell)).FirstOrDefault();
                    if (bag == null) break;
                    if (_pendingItem != item.UniqueIdentity)
                    {
                        _pendingItem = item.UniqueIdentity;
                        _moveAttempts = 0;
                    }
                    _pendingBag = bag.Identity;
                    if (Item.HasPendingUse || Spell.HasPendingCast)
                        return InventorySettlement.Moving;
                    item.MoveToContainer(bag);
                    _moveSent = DateTime.UtcNow;
                    _moveAttempts++;
                    return InventorySettlement.Moving;
                }
            }
            if (_pendingItem != Identity.None)
            {
                SettlementFailure = $"No available RKM Sell bag to verify/retry item {_pendingItem}.";
                return InventorySettlement.Blocked;
            }
            if (_stagedRejects > 0)
                say($"Verified {_stagedRejects} newly collected reject item(s) staged in RKM Sell bag(s); no sale was performed.");
            bool enough = ClassifyAfterVerifiedExit(loot, say);
            return enough ? InventorySettlement.Ready : InventorySettlement.NeedsSpace;
        }

        public static InventoryPolicy Load(string pluginDir, Action<string> say)
        {
            string path = Path.Combine(pluginDir, "RKMissionData", "inventory-policy.json");
            InventoryPolicy policy = new InventoryPolicy();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                if (File.Exists(path))
                    policy = JsonConvert.DeserializeObject<InventoryPolicy>(File.ReadAllText(path)) ?? policy;
                else File.WriteAllText(path, JsonConvert.SerializeObject(policy, Formatting.Indented));
            }
            catch (Exception ex) { say("Inventory policy unavailable; using defaults: " + ex.Message); }
            policy.MinimumFreeSlots = Math.Max(1, Math.Min(20, policy.MinimumFreeSlots));
            policy.ProtectedItemIds = policy.ProtectedItemIds?.Where(x => x > 0).Distinct().ToList() ??
                new List<int>();
            policy.ProtectedNameFragments = policy.ProtectedNameFragments?
                .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>();
            return policy;
        }

        public void Update(Action<string> say)
        {
            bool skip = Inventory.NumFreeSlots < MinimumFreeSlots;
            if (skip == SkipOptionalLoot) return;
            SkipOptionalLoot = skip;
            say(skip
                ? $"Only {Inventory.NumFreeSlots} free inventory slots remain (minimum {MinimumFreeSlots}); optional corpse/chest loot is suspended. Mission objectives and exit continue."
                : $"Inventory recovered to {Inventory.NumFreeSlots} free slots; optional loot resumed.");
        }

        // Called after verified staging, if any. No bank/vendor action occurs.
        public bool ClassifyAfterVerifiedExit(ManagerLoot.ManagerLoot loot, Action<string> say)
        {
            var classes = Inventory.Items
                .Where(item => item.Slot.Type == IdentityType.Inventory &&
                    item.UniqueIdentity.Type != IdentityType.Container)
                .Select(item => loot.Classify(item,
                    newlyAcquiredDuringMission: _inventoryAtDungeonEntry != null &&
                        !_inventoryAtDungeonEntry.Contains(item.UniqueIdentity))).ToList();
            _inventoryAtDungeonEntry = null;
            bool enoughCapacity = Inventory.NumFreeSlots >= MinimumFreeSlots;
            say($"Post-exit inventory classification: protected={classes.Count(x => x == ManagerLoot.ItemClassification.Protected)}, " +
                $"keep={classes.Count(x => x == ManagerLoot.ItemClassification.Keep)}, " +
                $"reject={classes.Count(x => x == ManagerLoot.ItemClassification.Reject)}, " +
                $"unknown={classes.Count(x => x == ManagerLoot.ItemClassification.Unknown)}, " +
                $"free slots={Inventory.NumFreeSlots}. " +
                (enoughCapacity ? "Capacity is sufficient for the next mission." :
                    "LogisticsRequired: capacity is low; no automatic sale, deletion or bank action is available."));
            return enoughCapacity;
        }
    }
}
