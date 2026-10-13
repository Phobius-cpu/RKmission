using System;
using System.Collections.Generic;
using System.Linq;
using AOSharp.Common.GameData;
using AOSharp.Core;
using AOSharp.Core.Inventory;

namespace RKmission
{
    // Tracks only bags opened by RKMission. A user-open bag is never closed.
    // Release is delayed and suppressed while any item operation is pending.
    internal static class ManagedBagOpenOwnership
    {
        private sealed class Lease
        {
            public readonly HashSet<string> Owners = new HashSet<string>(StringComparer.Ordinal);
            public bool OpenedByRkMission;
            public DateTime LastUse;
        }

        private static readonly Dictionary<Identity, Lease> Leases = new Dictionary<Identity, Lease>();

        public static void Acquire(string owner, Container bag)
        {
            if (bag == null || string.IsNullOrEmpty(owner)) return;
            if (!Leases.TryGetValue(bag.Identity, out Lease lease))
                Leases[bag.Identity] = lease = new Lease();
            lease.Owners.Add(owner);
        }

        public static void RecordOpenUse(string owner, Container bag)
        {
            if (bag == null) return;
            Acquire(owner, bag);
            Lease lease = Leases[bag.Identity];
            if (!bag.IsOpen) lease.OpenedByRkMission = true;
            lease.LastUse = DateTime.UtcNow;
        }

        public static void Release(string owner, Identity identity)
        {
            if (Leases.TryGetValue(identity, out Lease lease)) lease.Owners.Remove(owner);
        }

        public static void ReleaseAll(string owner)
        {
            foreach (Lease lease in Leases.Values) lease.Owners.Remove(owner);
        }

        public static void Tick()
        {
            if (Game.IsZoning || Item.HasPendingUse || Spell.HasPendingCast) return;
            foreach (var pair in Leases.ToArray())
            {
                Lease lease = pair.Value;
                if (lease.Owners.Count > 0) continue;
                Container bag = Inventory.Backpacks.FirstOrDefault(x => x.Identity == pair.Key);
                if (bag == null || !lease.OpenedByRkMission || !bag.IsOpen)
                {
                    Leases.Remove(pair.Key);
                    continue;
                }
                if (DateTime.UtcNow - lease.LastUse < TimeSpan.FromMilliseconds(750)) continue;
                Item item = Inventory.Items.FirstOrDefault(x => x.UniqueIdentity == pair.Key);
                if (item == null) { Leases.Remove(pair.Key); continue; }
                item.Use();
                Leases.Remove(pair.Key);
                return;
            }
        }
    }
}
