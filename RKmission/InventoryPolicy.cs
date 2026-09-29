using System;
using System.IO;
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
    }
}
