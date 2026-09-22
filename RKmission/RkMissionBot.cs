using AOSharp.Core;
using AOSharp.Core.UI;
using BehaviourTree;
using BehaviourTree.FluentBuilder;
using Dungeon.Runner;
using Dungeon.Solver;

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
        Chat.WriteLine("RKmission: before base start.");

        base.Start();

        Chat.WriteLine("RKmission: after base start.");
    }

    public override void Stop()
    {
        Chat.WriteLine("RKmission: stopping.");
        base.Stop();
    }

    protected override void EnteredDungeon()
    {
        base.EnteredDungeon();
    }

    // Keep the boss-room wrapper isolated while testing the real mission tree.
    protected override IBehaviour<RkMissionContext> BossRoomTree()
    {
        return FluentBuilder.Create<RkMissionContext>()
            .Sequence("RKmission BossRoom Test")
                .Do("Idle", _ => BehaviourStatus.Succeeded)
            .End()
            .Build();
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
                Chat.WriteLine("RKmission: DungeonRunner lifecycle test mode");
                break;
            default:
                Chat.WriteLine("Usage: /rkm start | stop | status");
                break;
        }
    }
}
