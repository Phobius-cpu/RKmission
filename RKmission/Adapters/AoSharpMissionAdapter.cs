using System;
using RKmission.Models;

namespace RKmission.Adapters;

public sealed class AoSharpInventoryService
{
    public bool HasUsableKeyOrLockpick(MissionObject target)
    {
        // TODO: replace with actual AOSharp inventory and item checks.
        // Examples:
        // - scan inventory for lockpicks or key items
        // - check target.RequiresKey or target.RequiresLockpick
        // - ensure inventory item is usable in the target mission/location
        if (target is null)
            throw new ArgumentNullException(nameof(target));

        return !target.IsLocked || target.RequiresKey || target.RequiresLockpick;
    }
}
