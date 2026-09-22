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

    public DateTime? FightApproachStartUtc { get; set; }
    public Vector3? StallCheckPosition { get; set; }
    public DateTime? StallCheckLastProgressUtc { get; set; }
    public Vector3? LastDestination { get; set; }
    public DateTime? LastHeartbeatUtc { get; set; }
    public DateTime? LastMoveHeartbeatUtc { get; set; }
    public DateTime? LastFightDiagnosticUtc { get; set; }

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
        LastMoveHeartbeatUtc = null;
        LastFightDiagnosticUtc = null;
        ProcessedObjects.Clear();

        base.Reset();
    }
}
