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
        ProcessedObjects.Clear();

        base.Reset();
    }
}
