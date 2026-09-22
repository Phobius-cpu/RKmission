using System;
using System.Collections.Generic;
using RKmission.Models;

namespace RKmission.Adapters;

public sealed class AoSharpMissionAdapter : IMissionWorld
{
    private readonly AoSharpInventoryService _inventory;

    public AoSharpMissionAdapter(AoSharpInventoryService? inventory = null)
    {
        _inventory = inventory ?? new AoSharpInventoryService();
    }

    public bool IsInMission => throw new NotImplementedException("Bind to the exact AOSharp mission state API used by your checkout.");

    public bool IsAlive => throw new NotImplementedException("Bind to the exact AOSharp character alive/status API.");

    public IReadOnlyCollection<string> VisibleRoomKeys => Array.Empty<string>();

    public IReadOnlyCollection<MissionObject> NearbyObjects => Array.Empty<MissionObject>();

    public bool HasUsableKeyOrLockpick(MissionObject target)
    {
        return _inventory.HasUsableKeyOrLockpick(target);
    }

    public void MoveTo(MissionObject target)
    {
        throw new NotImplementedException("Bind to the AOSharp movement or navmesh pathing API.");
    }

    public void ExploreNextArea()
    {
        throw new NotImplementedException("Bind to the AOSharp next-room or next-waypoint logic.");
    }

    public bool TryInteract(MissionObject target)
    {
        throw new NotImplementedException("Bind to the AOSharp door/chest interaction API and account for lockpicks/keys.");
    }

    public bool TryLoot(MissionObject target)
    {
        throw new NotImplementedException("Bind to the AOSharp container/loot API.");
    }

    public void StopMoving()
    {
        throw new NotImplementedException("Bind to the AOSharp movement stop/cancel API.");
    }

    public void Say(string message)
    {
        throw new NotImplementedException("Bind to the AOSharp chat/log API.");
    }
}
