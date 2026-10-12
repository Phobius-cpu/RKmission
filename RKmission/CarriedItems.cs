using System;
using System.Collections.Generic;
using System.Linq;
using AOSharp.Common.GameData;
using AOSharp.Core;
using AOSharp.Core.Inventory;

namespace RKmission
{
    internal static class CarriedItems
    {
        private const int MaxBackpackLoadAttempts = 3;
        private const double BackpackResponseSeconds = 2.5;
        private static readonly string[] LockpickNames = { "Lock Pick", "Personal Lockpick", "Personal Lock Pick" };
        private static readonly Dictionary<Identity, BackpackSnapshot> BackpackSnapshots =
            new Dictionary<Identity, BackpackSnapshot>();
        private static Identity _waitingFor = Identity.None;
        private static bool _initialized;

        private sealed class BackpackSnapshot
        {
            public int Attempts;
            public double SentAt;
            public bool Confirmed;
            public bool Exhausted;
        }

        public static bool BackpackSnapshotsReady
        {
            get
            {
                ReconcileBackpacks();
                return BackpackSnapshots.Values.All(snapshot => snapshot.Confirmed || snapshot.Exhausted);
            }
        }

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            Inventory.ContainerOpened += ContainerOpened;
            ResetBackpackSnapshots();
        }

        public static void Teardown()
        {
            if (!_initialized) return;
            Inventory.ContainerOpened -= ContainerOpened;
            BackpackSnapshots.Clear();
            _waitingFor = Identity.None;
            _initialized = false;
        }

        // AO can discard carried-container contents while zoning. Load one bag at
        // a time and wait for positive evidence before advancing. Empty bags need
        // ContainerOpened; non-empty bags can also be confirmed by their snapshot.
        public static void ResetBackpackSnapshots()
        {
            BackpackSnapshots.Clear();
            _waitingFor = Identity.None;
            ReconcileBackpacks();
        }

        public static void UpdateBackpackSnapshots()
        {
            if (!_initialized || Game.IsZoning) return;
            ReconcileBackpacks();
            ConfirmPopulatedSnapshots();

            double now = Time.AONormalTime;
            if (_waitingFor != Identity.None)
            {
                if (!BackpackSnapshots.TryGetValue(_waitingFor, out BackpackSnapshot waiting) ||
                    waiting.Confirmed || waiting.Exhausted)
                {
                    _waitingFor = Identity.None;
                }
                else if (now < waiting.SentAt + BackpackResponseSeconds)
                {
                    return;
                }
                else if (waiting.Attempts >= MaxBackpackLoadAttempts)
                {
                    waiting.Exhausted = true;
                    _waitingFor = Identity.None;
                }
                else
                {
                    _waitingFor = Identity.None;
                }
            }

            if (Item.HasPendingUse) return;
            var next = BackpackSnapshots.FirstOrDefault(pair =>
                !pair.Value.Confirmed && !pair.Value.Exhausted);
            if (next.Equals(default(KeyValuePair<Identity, BackpackSnapshot>))) return;

            Item bagItem = Inventory.Items.FirstOrDefault(item => item != null &&
                item.UniqueIdentity == next.Key);
            if (bagItem == null)
            {
                next.Value.Exhausted = true;
                return;
            }

            bagItem.Use();
            next.Value.Attempts++;
            next.Value.SentAt = now;
            _waitingFor = next.Key;
        }

        private static void ReconcileBackpacks()
        {
            var carried = new HashSet<Identity>(Inventory.Backpacks.Select(bag => bag.Identity));
            foreach (Identity removed in BackpackSnapshots.Keys.Where(id => !carried.Contains(id)).ToList())
                BackpackSnapshots.Remove(removed);
            foreach (Identity identity in carried)
                if (!BackpackSnapshots.ContainsKey(identity))
                    BackpackSnapshots[identity] = new BackpackSnapshot();
            if (_waitingFor != Identity.None && !carried.Contains(_waitingFor))
                _waitingFor = Identity.None;
        }

        private static void ConfirmPopulatedSnapshots()
        {
            foreach (var pair in BackpackSnapshots.Where(pair => !pair.Value.Confirmed &&
                Inventory.GetContainerItems(pair.Key).Any()))
            {
                pair.Value.Confirmed = true;
                if (_waitingFor == pair.Key) _waitingFor = Identity.None;
            }
        }

        private static void ContainerOpened(object sender, Container container)
        {
            if (container == null || !BackpackSnapshots.TryGetValue(container.Identity,
                out BackpackSnapshot snapshot)) return;
            snapshot.Confirmed = true;
            if (_waitingFor == container.Identity) _waitingFor = Identity.None;
        }

        public static IEnumerable<Item> All() => Inventory.Items
            .Where(item => item != null && item.Slot.Type == IdentityType.Inventory &&
                item.UniqueIdentity.Type != IdentityType.Container)
            .Concat(Inventory.Backpacks.SelectMany(bag => Inventory.GetContainerItems(bag.Identity))
                .Where(item => item != null && item.UniqueIdentity.Type != IdentityType.Container));

        public static Item FindExact(params string[] names) => All().FirstOrDefault(item =>
            names.Any(name => string.Equals(item.Name?.Trim(), name, StringComparison.OrdinalIgnoreCase)));
        public static Item FindLockpick() => FindExact(LockpickNames);
        public static bool IsLockpickName(string name) => LockpickNames.Any(candidate =>
            string.Equals(name?.Trim(), candidate, StringComparison.OrdinalIgnoreCase));
    }
}
