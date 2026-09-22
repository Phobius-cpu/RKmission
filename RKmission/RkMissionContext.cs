using AOSharp.Common.GameData;
using Dungeon.Runner;
using System;
using System.Collections.Generic;

namespace RKmission;

/// <summary>
/// Runtime state passed through the Rubi-Ka mission behavior tree.
/// This is the Rubi-Ka equivalent of AIMission.Bot.BTContext.
/// </summary>
public sealed class RkMissionContext : DungeonRunnerContext<RkMissionContext>
{
    public RkMissionBot Bot { get; }

    public Identity? ActiveMissionId { get; set; }
    public bool MissionCompleted { get; set; }
    public bool MissionObjectiveHandled { get; set; }
    public bool LootEnabled { get; set; } = true;
    public DateTime MissionStartedAt { get; set; }

    /// <summary>
    /// Timestamp when the bot started approaching the current fight target.
    /// Used to detect and give up on unreachable targets so combat doesn't
    /// permanently block dungeon exploration.
    /// </summary>
    public DateTime? FightApproachStartUtc { get; set; }

    /// <summary>
    /// Last position sampled while watching for movement stalls (e.g. running
    /// against a wall, or a target blocked by an obstacle).
    /// </summary>
    public Vector3? StallCheckPosition { get; set; }

    /// <summary>
    /// Timestamp of the last time the player was seen making meaningful
    /// progress toward a destination.
    /// </summary>
    public DateTime? StallCheckLastProgressUtc { get; set; }

    /// <summary>
    /// Last destination used for navigation. The stall timer is reset only when
    /// this destination changes, not every tick while navigation is inactive.
    /// </summary>
    public Vector3? LastDestination { get; set; }

    /// <summary>
    /// Timestamp of the last behavior-tree heartbeat log.
    /// </summary>
    public DateTime? LastHeartbeatUtc { get; set; }

    /// <summary>Object identities that have already been processed or skipped.</summary>
    public HashSet<int> ProcessedObjects { get; } = new();

    public RkMissionContext(RkMissionBot bot)
        : base(bot)
    {
        Bot = bot;
    }

    public override void Reset()
    {
        ActiveMissionId = null;
        MissionCompleted = false;
        MissionObjectiveHandled = false;
        LootEnabled = true;
        MissionStartedAt = default;
        FightApproachStartUtc = null;
        StallCheckPosition = null;
        StallCheckLastProgressUtc = null;
        LastDestination = null;
        LastHeartbeatUtc = null;
        ProcessedObjects.Clear();

        base.Reset();
    }
}
