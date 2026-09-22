using AOSharp.Common.GameData;
using AOSharp.Core;
using AOSharp.Core.UI;
using BehaviourTree;
using BehaviourTree.FluentBuilder;
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
    public override float FightDistance => 30f;

    public override bool FindFightableTarget(Room room, out SimpleChar target)
    {
        target = DynelManager.NPCs
            .Where(c => !c.IsPet && c.IsAlive)
            .Where(c => room == null || (c.Room != null && c.Room.Instance == room.Instance))
            .Where(c => c.FightingTarget != null || DynelManager.LocalPlayer.DistanceFrom(c) < FightDistance)
            .OrderBy(c => DynelManager.LocalPlayer.DistanceFrom(c))
            .FirstOrDefault();

        return target != null;
    }

    protected override void Init()
    {
        try
        {
            Chat.WriteLine("RKmission: entering Init.");
            base.Init();
            Chat.WriteLine("RKmission: base Init completed.");

            Chat.RegisterCommand("rkm", HandleCommand);
            Logger.Information("RKmission loaded.");
            Chat.WriteLine("RKmission: Init completed.");
        }
        catch (Exception ex)
        {
            Logger.Error($"RKmission Init failed: {ex}");
            Chat.WriteLine($"RKmission Init EXCEPTION: {ex.Message}");
        }
    }

    public override void Start()
    {
        try
        {
            Chat.WriteLine("RKmission: before base start.");
            base.Start();
            Chat.WriteLine("RKmission: after base start.");
        }
        catch (Exception ex)
        {
            Logger.Error($"RKmission Start failed: {ex}");
            Chat.WriteLine($"RKmission Start EXCEPTION: {ex.Message}");
        }
    }

    public override void Stop()
    {
        try
        {
            Chat.WriteLine("RKmission: stopping.");
            base.Stop();
            Chat.WriteLine("RKmission: stopped.");
        }
        catch (Exception ex)
        {
            Logger.Error($"RKmission Stop failed: {ex}");
            Chat.WriteLine($"RKmission Stop EXCEPTION: {ex.Message}");
        }
    }

    protected override void EnteredDungeon()
    {
        try
        {
            Chat.WriteLine("RKmission: entered dungeon.");
            base.EnteredDungeon();
        }
        catch (Exception ex)
        {
            Logger.Error($"RKmission EnteredDungeon failed: {ex}");
            Chat.WriteLine($"RKmission EnteredDungeon EXCEPTION: {ex.Message}");
        }
    }

    protected override IBehaviour<RkMissionContext> BossRoomTree()
    {
        Chat.WriteLine("RKmission: BossRoomTree() requested.");

        return FluentBuilder.Create<RkMissionContext>()
            .Sequence("RKmission BossRoom Test")
                .Do("Idle", _ => BehaviourStatus.Succeeded)
            .End()
            .Build();
    }

    protected override IBehaviour<RkMissionContext> PreDungeonTree()
    {
        Chat.WriteLine("RKmission: PreDungeonTree() requested.");
        return RkMissionBehavior.Compile();
    }

    private void HandleCommand(
        string command,
        string[] parameters,
        ChatWindow chatWindow)
    {
        try
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
        catch (Exception ex)
        {
            Logger.Error($"RKmission command failed: {ex}");
            Chat.WriteLine($"RKmission command EXCEPTION: {ex.Message}");
        }
    }
}


