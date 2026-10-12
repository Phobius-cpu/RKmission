using System;
using System.Collections.Generic;
using System.Linq;
using AOSharp.Core.Inventory;

namespace RKmission
{
    // Session-local memory for Keeps deliberately left in main by a bounded
    // logistics cycle. It never uses AO identities or slots as authority.
    internal sealed class DeferredKeepLedger
    {
        internal sealed class Entry
        {
            public int Id, LowId, HighId, QualityLevel, ExpectedCount;
            public string Name, RequestedDestination;

            public bool Matches(Item item) => item != null && item.Id == Id &&
                item.LowId == LowId && item.HighId == HighId &&
                item.QualityLevel == QualityLevel &&
                string.Equals(item.Name, Name, StringComparison.Ordinal);

            public string Describe() => $"'{Name}' (id={Id}, low={LowId}, high={HighId}, QL={QualityLevel})";
        }

        private readonly List<Entry> _entries = new List<Entry>();
        public int Count => _entries.Sum(x => x.ExpectedCount);
        public Entry[] Entries => _entries.ToArray();

        public void Clear() => _entries.Clear();

        public void Replace(IEnumerable<Item> items, Func<Item, string> destination)
        {
            _entries.Clear();
            foreach (var group in (items ?? Enumerable.Empty<Item>()).Where(x => x != null)
                .GroupBy(x => new { x.Id, x.LowId, x.HighId, x.QualityLevel, x.Name }))
            {
                Item item = group.First();
                _entries.Add(new Entry
                {
                    Id = item.Id,
                    LowId = item.LowId,
                    HighId = item.HighId,
                    QualityLevel = item.QualityLevel,
                    Name = item.Name,
                    ExpectedCount = group.Count(),
                    RequestedDestination = destination?.Invoke(item)
                });
            }
        }

        public Item[] Matches(Entry entry, IEnumerable<Item> currentMain) =>
            (currentMain ?? Enumerable.Empty<Item>()).Where(entry.Matches).ToArray();

        public void Remove(Entry entry) => _entries.Remove(entry);

        public void ConsumeOne(Entry entry)
        {
            if (entry == null || !_entries.Contains(entry)) return;
            entry.ExpectedCount--;
            if (entry.ExpectedCount <= 0) _entries.Remove(entry);
        }

        public void RemoveMissing(IEnumerable<Item> currentMain)
        {
            Item[] current = (currentMain ?? Enumerable.Empty<Item>()).ToArray();
            _entries.RemoveAll(entry => Matches(entry, current).Length == 0);
        }
    }
}
