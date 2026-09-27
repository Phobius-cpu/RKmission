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
    // Compatible with Manager.Loot's Name/Lql/Hql/Quantity/Exact/OneEach JSON rules.
    internal sealed class LootRule
    {
        public string Name = "";
        public string Lql = "1";
        public string Hql = "500";
        public string Quantity = "999";
        public string Exact = "false";
        public string OneEach = "false";
    }

    internal sealed class LootRules
    {
        private readonly string _path;
        private readonly List<LootRule> _rules;

        public string PathOnDisk => _path;

        public LootRules()
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AOSharp", "RKmission", DynelManager.LocalPlayer.Name);
            Directory.CreateDirectory(folder);
            _path = Path.Combine(folder, "loot-rules.json");
            _rules = File.Exists(_path)
                ? JsonConvert.DeserializeObject<List<LootRule>>(File.ReadAllText(_path)) ?? new List<LootRule>()
                : new List<LootRule>();
            if (!File.Exists(_path))
                Save(); // Empty allowlist: the player explicitly chooses what to take.
        }

        public string Describe() => _rules.Count == 0 ? "No loot items selected." :
            string.Join("; ", _rules.Select((r, i) =>
                $"{i + 1}: {r.Name} (QL {r.Lql}-{r.Hql}, quantity {r.Quantity})"));

        public void Add(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return;
            Add(name, 1, 500, 999, false, false);
        }

        public bool Add(string name, int low, int high, int quantity, bool exact, bool oneEach)
        {
            if (string.IsNullOrWhiteSpace(name) || low < 1 || high > 500 || low > high || quantity < 1 || quantity > 999)
                return false;
            _rules.Add(new LootRule
            {
                Name = name.Trim(), Lql = low.ToString(), Hql = high.ToString(),
                Quantity = quantity.ToString(), Exact = exact.ToString().ToLowerInvariant(),
                OneEach = oneEach.ToString().ToLowerInvariant()
            });
            Save();
            return true;
        }

        public bool Remove(int oneBasedIndex)
        {
            if (oneBasedIndex < 1 || oneBasedIndex > _rules.Count)
                return false;
            _rules.RemoveAt(oneBasedIndex - 1);
            Save();
            return true;
        }

        public LootRule Match(Item item)
        {
            foreach (LootRule rule in _rules)
            {
                if (string.IsNullOrWhiteSpace(rule.Name) ||
                    !int.TryParse(rule.Lql, out int low) || !int.TryParse(rule.Hql, out int high) ||
                    !int.TryParse(rule.Quantity, out int quantity) || quantity < 1 ||
                    item.QualityLevel < low || item.QualityLevel > high)
                    continue;

                if (int.TryParse(rule.Name, out int id))
                {
                    if (item.Id != id) continue;
                }
                else if (string.Equals(rule.Exact, "true", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.Equals(item.Name, rule.Name, StringComparison.OrdinalIgnoreCase)) continue;
                }
                else if (rule.Name.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .Any(word => item.Name.IndexOf(word, StringComparison.OrdinalIgnoreCase) < 0))
                    continue;

                if (string.Equals(rule.OneEach, "true", StringComparison.OrdinalIgnoreCase) &&
                    Inventory.Items.Any(x => x.Slot.Type == IdentityType.Inventory &&
                        string.Equals(x.Name, item.Name, StringComparison.OrdinalIgnoreCase)))
                    continue;
                return rule;
            }
            return null;
        }

        public void RecordLoot(LootRule rule)
        {
            if (rule == null || !int.TryParse(rule.Quantity, out int quantity) || quantity >= 999)
                return;
            rule.Quantity = Math.Max(0, quantity - 1).ToString();
            Save();
        }

        private void Save() => File.WriteAllText(_path, JsonConvert.SerializeObject(_rules, Formatting.Indented));
    }
}
