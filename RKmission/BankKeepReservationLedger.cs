using System;
using System.Collections.Generic;
using System.Linq;
using AOSharp.Core.Inventory;

namespace RKmission
{
    // Cycle-local authority for Keep items physically staged for automatic bank
    // fallback. AO may recreate Item identities and slots while zoning, so only
    // immutable item details participate in post-zone resolution.
    internal sealed class BankKeepReservationLedger
    {
        private sealed class Reservation
        {
            public int Id, LowId, HighId, QualityLevel;
            public string Name, Source;

            public bool Matches(Item item) => item != null && item.Id == Id &&
                item.LowId == LowId && item.HighId == HighId &&
                item.QualityLevel == QualityLevel &&
                string.Equals(item.Name, Name, StringComparison.Ordinal);

            public bool SameFingerprint(Reservation other) => other != null &&
                Id == other.Id && LowId == other.LowId && HighId == other.HighId &&
                QualityLevel == other.QualityLevel &&
                string.Equals(Name, other.Name, StringComparison.Ordinal);

            public string Describe() => $"'{Name}' (id={Id}, low={LowId}, high={HighId}, QL={QualityLevel})";
        }

        private readonly List<Reservation> _reservations = new List<Reservation>();
        public int Count => _reservations.Count;

        public void Clear() => _reservations.Clear();

        public void Reserve(Item item, string source)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            _reservations.Add(new Reservation
            {
                Id = item.Id,
                LowId = item.LowId,
                HighId = item.HighId,
                QualityLevel = item.QualityLevel,
                Name = item.Name,
                Source = source
            });
        }

        public bool TryResolve(IEnumerable<Item> currentMain, out Item[] resolved, out string error)
        {
            resolved = Array.Empty<Item>();
            error = null;
            Item[] current = (currentMain ?? Enumerable.Empty<Item>()).Where(x => x != null).ToArray();
            var matches = new List<Item>();
            foreach (Reservation representative in _reservations
                .GroupBy(x => new { x.Id, x.LowId, x.HighId, x.QualityLevel, x.Name })
                .Select(group => group.First()))
            {
                int expected = _reservations.Count(representative.SameFingerprint);
                Item[] found = current.Where(representative.Matches).ToArray();
                if (found.Length < expected)
                {
                    error = $"bank Keep reservation missing: expected {expected}, resolved {found.Length} exact match(es) for {representative.Describe()}";
                    return false;
                }
                if (found.Length > expected)
                {
                    error = $"bank Keep reservation ambiguous: {expected} reservation(s) matched {found.Length} current items for {representative.Describe()}";
                    return false;
                }
                matches.AddRange(found);
            }
            resolved = matches.ToArray();
            return resolved.Length == _reservations.Count;
        }

        public bool TrySelectNext(IEnumerable<Item> currentMain, out Item item, out string error)
        {
            item = null;
            if (!TryResolve(currentMain, out Item[] resolved, out error)) return false;
            item = resolved.OrderBy(x => x.Name, StringComparer.Ordinal).ThenBy(x => x.Id)
                .ThenBy(x => x.LowId).ThenBy(x => x.HighId).FirstOrDefault();
            if (item != null) return true;
            error = "no unresolved bank Keep reservation remains";
            return false;
        }

        public bool ConsumeVerified(Item item)
        {
            int index = _reservations.FindIndex(x => x.Matches(item));
            if (index < 0) return false;
            _reservations.RemoveAt(index);
            return true;
        }
    }
}
