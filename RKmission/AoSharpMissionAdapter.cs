using System;
using System.Collections.Generic;
using RKmission.Models;

namespace RKmission;

/// <summary>
/// Version boundary for AOSharp.NewBots. Keep all SDK-specific calls in this class.
/// </summary>
public sealed class AoSharpMissionAdapter : IMissionWorld
{
    public bool IsInMission => throw new NotImplementedException("Bind to the AOSharp zone/mission API.");
    public bool IsAlive => throw new NotImplementedException("Bind to the AOSharp character state API.");
    public IReadOnlyCollection<string> VisibleRoomKeys => Array.Empty<string>();
    public IReadOnlyCollection<MissionObject> NearbyObjects => Array.Empty<MissionObject>();

    public bool HasUsableKeyOrLockpick(MissionObject target)
    {
        // Bind inventory lookup here. Do not consume an item until TryInteract succeeds.
        throw new NotImplementedException("Bind to inventory and lockpick/key checks.");
    }

    public void MoveTo(MissionObject target) =>
        throw new NotImplementedException("Bind to the repository's navigation controller.");

    public void ExploreNextArea() =>
        throw new NotImplementedException("Bind to navigation and room/portal discovery.");

    public bool TryInteract(MissionObject target) =>
        throw new NotImplementedException("Bind to door/chest interaction, including key or lockpick use.");

    public bool TryLoot(MissionObject target) =>
        throw new NotImplementedException("Bind to the repository's container/loot API.");

    public void StopMoving() =>
        throw new NotImplementedException("Bind to the navigation stop method.");

    public void Say(string message) =>
        throw new NotImplementedException("Bind to AOSharp chat output.");
}

