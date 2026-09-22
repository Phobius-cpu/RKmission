using AOSharp.Core;
using AOSharp.Core.UI;
using BehaviourTree;
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
        // Lifecycle isolation test: do not call DungeonRunner.Start yet.
        // If /rkm start is stable with this implementation, the crash is in
        // DungeonRunner.Start or in behavior-tree initialization/ticking.
        Chat.WriteLine("RKmission: start lifecycle test passed.");
    }

    public override void Stop()
    {
        Chat.WriteLine("RKmission: stop lifecycle test passed.");
    }

    protected override void EnteredDungeon()
    {
        base.EnteredDungeon();
    }

    // Keep the trees disabled during the lifecycle isolation test. These will
    // be restored after the base runner is confirmed safe.
    protected override IBehaviour<RkMissionContext> BossRoomTree()
    {
        return null;
    }

    protected override IBehaviour<RkMissionContext> PreDungeonTree()
    {
        return null;
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
                Chat.WriteLine("RKmission: lifecycle test mode");
                break;
            default:
                Chat.WriteLine("Usage: /rkm start | stop | status");
                break;
        }
    }
}
