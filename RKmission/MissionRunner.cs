using System;
using System.Collections.Generic;
using System.Linq;

namespace RKmission;

public interface IMissionWorld
{
    bool IsInMission { get; }
    bool IsAlive { get; }
    IReadOnlyCollection<string> VisibleRoomKeys { get; }
    IReadOnlyCollection<RKmission.Models.MissionObject> NearbyObjects { get; }
    bool HasUsableKeyOrLockpick(RKmission.Models.MissionObject target);
    void MoveTo(RKmission.Models.MissionObject target);
    void ExploreNextArea();
    bool TryInteract(RKmission.Models.MissionObject target);
    bool TryLoot(RKmission.Models.MissionObject target);
    void StopMoving();
    void Say(string message);
}

public sealed class MissionPlanner
{
    public RKmission.Models.MissionObject? SelectNextTarget(IReadOnlyCollection<RKmission.Models.MissionObject> nearby, bool allowLockedWhenToolAvailable)
    {
        if (nearby is null || nearby.Count == 0)
            return null;

        return nearby
            .Where(x => !x.IsOpen)
            .OrderBy(x => x.Distance)
            .FirstOrDefault();
    }
}

public sealed class RoomTracker
{
    private readonly HashSet<string> _visitedRooms = new(StringComparer.OrdinalIgnoreCase);

    public void Clear() => _visitedRooms.Clear();

    public bool MarkVisited(string roomKey)
    {
        if (string.IsNullOrWhiteSpace(roomKey))
            return false;

        return _visitedRooms.Add(roomKey);
    }

    public bool IsVisited(string roomKey) => !string.IsNullOrWhiteSpace(roomKey) && _visitedRooms.Contains(roomKey);

    public int Count => _visitedRooms.Count;
}
