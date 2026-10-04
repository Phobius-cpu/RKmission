using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AOSharp.Common.GameData;
using AOSharp.Core.Inventory;
using Newtonsoft.Json;

namespace RKmission
{
    // Only optional corpse/chest work is suppressed; objective interactions
    // continue through MissionObjective and retain their own free-slot checks.
    internal sealed class InventoryPolicy
    {
        public int MinimumFreeSlots { get; set; } = 3;
        [JsonIgnore] public bool SkipOptionalLoot { get; private set; }
        private HashSet<Identity>? _inventoryAtDungeonEntry;

        public void BeginMissionInventorySnapshot()
        {
            _inventoryAtDungeonEntry = new HashSet<Identity>(Inventory.Items
                .Where(item => item.Slot.Type == IdentityType.Inventory &&
                    item.UniqueIdentity.Type != IdentityType.Container)
                .Select(item => item.UniqueIdentity));
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

        // Called only after the dungeon exit is verified. Classification is
        // informational until bank/vendor interactions have live API evidence.
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
