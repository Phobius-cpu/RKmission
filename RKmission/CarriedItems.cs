using System;
using System.Collections.Generic;
using System.Linq;
using AOSharp.Common.GameData;
using AOSharp.Core.Inventory;

namespace RKmission
{
    internal static class CarriedItems
    {
        private static readonly string[] LockpickNames = { "Lock Pick", "Personal Lockpick", "Personal Lock Pick" };

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
