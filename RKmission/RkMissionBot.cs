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
        // Do not call DungeonRunner.Start until its runtime state has been
        // initialized. Starting from the login area or outside a recognized
        // mission currently causes the base runner to tick the dungeon tree
        // with no active mission/solver and can crash the client.
        if (DynelManager.LocalPlayer == null)
        {
            Chat.WriteLine("RKmission: player is not ready.");
            return;
        }

        if (ActiveMission == null)
        {
            Chat.WriteLine(
                "RKmission: no active mission detected. Enter the mission first.");
            return;
        }

        if (Solver == null)
        {
            Chat.WriteLine(
                "RKmission: dungeon solver is not ready. Enter the mission and try again.");
            return;
        }

        _botContext.MissionStartedAt = DateTime.UtcNow;
        _botContext.MissionCompleted = false;
        _botContext.MissionObjectiveHandled = false;

        base.Start();
    }

    public override void Stop()
    {
        if (Enabled)
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

    private void HandleCommand(
        string command,
        string[] parameters,
        ChatWindow chatWindow)
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
