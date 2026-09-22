using AOSharp.Core;
using AOSharp.Core.UI;
using BehaviourTree;
using Dungeon.Runner;
using Dungeon.Solver;
using System;

namespace RKmission;

/// <summary>
/// AOSharp plugin entry point for the Rubi-Ka mission bot.
/// </summary>
public sealed class RkMissionBot : DungeonRunner<RkMissionContext>
{
    public override SolverMode SolverMode => SolverMode.Clear;

    protected override void Init()
    {
        base.Init();

        Chat.RegisterCommand("rkm", HandleCommand);
        Logger.Information("RKmission loaded.");
    }

    public override void Start()
    {
        _botContext.MissionStartedAt = DateTime.UtcNow;
        _botContext.MissionCompleted = false;
        _botContext.MissionObjectiveHandled = false;
        base.Start();
    }

    public override void Stop()
    {
        SMovementController.Halt();
        base.Stop();
    }

    protected override void EnteredDungeon()
    {
        base.EnteredDungeon();

        if (_botContext.ActiveMission == null &&
            Mission.FindMissionForCurrentDungeon(out Mission mission))
        {
            _botContext.ActiveMission = mission;
        }
    }

    protected override IBehaviour<RkMissionContext> BossRoomTree()
    {
        return RkMissionBehavior.Compile();
    }

    protected override IBehaviour<RkMissionContext> PreDungeonTree()
    {
        return RkMissionBehavior.Compile();
    }

    private void HandleCommand(string command, string[] parameters, ChatWindow chatWindow)
    {
        var subcommand = parameters.Length == 0
            ? "status"
            : parameters[0].ToLowerInvariant();

        switch (subcommand)
        {
            case "start":
                Start();
                break;
            case "stop":
                Stop();
                break;
            case "status":
                Chat.WriteLine(
                    $"RKmission: running={Enabled}, " +
                    $"mission={_botContext.ActiveMission != null}, " +
                    $"rooms={Solver?.VisitedRooms.Count ?? 0}");
                break;
            default:
                Chat.WriteLine("Usage: /rkm start | stop | status");
                break;
        }
    }
}
