using AOSharp.Common.GameData;
using AOSharp.Core;
using AOSharp.Core.Inventory;
using AOSharp.Core.Movement;
using AOSharp.Core.UI;
using BehaviourTree;
using BehaviourTree.FluentBuilder;
using Dungeon.Runner;
using Dungeon.Solver;
using System;
using System.Linq;

namespace RKmission;

/// <summary>
/// Rubi-Ka mission behavior tree. It deliberately avoids the alien-specific
/// boss and recruiter logic from AIMission.Bot.
/// </summary>
public static class RkMissionBehavior
{
    public static IBehaviour<RkMissionContext> Compile()
    {
        return FluentBuilder.Create<RkMissionContext>()
            .Selector("Rubi-Ka Mission")
                .Subtree(CompleteObjective())
                .Subtree(OpenLockedDoor())
                .Subtree(Explore())
                .Subtree(Idle())
            .End()
            .Build();
    }

    private static IBehaviour<RkMissionContext> CompleteObjective()
    {
        return FluentBuilder.Create<RkMissionContext>()
            .Sequence("Mission Objective")
                .Condition("Active mission", c => c.ActiveMission != null)
                .Do("Complete objective", CompleteObjectiveStep)
            .End()
            .Build();
    }

    private static IBehaviour<RkMissionContext> OpenLockedDoor()
    {
        return FluentBuilder.Create<RkMissionContext>()
            .Do("Open nearby locked door", OpenLockedDoorStep)
            .End()
            .Build();
    }

    private static IBehaviour<RkMissionContext> Explore()
    {
        return FluentBuilder.Create<RkMissionContext>()
            .Sequence("Explore mission")
                .Condition("Solver available", c => c.DungeonRunner.Solver != null)
                .Do("Select next room", SelectNextRoom)
                .Do("Move to room", MoveToRoom)
            .End()
            .Build();
    }

    private static IBehaviour<RkMissionContext> Idle()
    {
        return FluentBuilder.Create<RkMissionContext>()
            .Do("Idle", _ => BehaviourStatus.Running)
            .End()
            .Build();
    }

    private static BehaviourStatus CompleteObjectiveStep(RkMissionContext context)
    {
        if (context.ActiveMission == null)
            return BehaviourStatus.Failed;

        var target = context.ActiveMission.GetMissionTarget();
        if (target == null)
            return BehaviourStatus.Failed;

        SetDestination(target.Position, context);
        if (DynelManager.LocalPlayer.Position.DistanceFrom(target) > 1f)
            return BehaviourStatus.Running;

        // Targeting is the safe common action for FindItem/FindPerson-style
        // objectives. Item-on-item objectives should be added after the exact
        // mission action types used by this checkout are confirmed.
        target.Target();
        context.MissionObjectiveHandled = true;
        return BehaviourStatus.Succeeded;
    }

    private static BehaviourStatus OpenLockedDoorStep(RkMissionContext context)
    {
        var door = Playfield.Doors
            .Where(x => x.IsLocked && x.DistanceFrom(DynelManager.LocalPlayer) < 5f)
            .OrderBy(x => x.DistanceFrom(DynelManager.LocalPlayer))
            .FirstOrDefault();

        if (door == null)
            return BehaviourStatus.Failed;

        if (!Inventory.Find("Lock Pick", out Item lockPick))
        {
            context.Logger.Debug($"No Lock Pick available for door {door.Identity}.");
            context.ProcessedObjects.Add(door.Identity.Instance);
            return BehaviourStatus.Failed;
        }

        lockPick.UseOn(door);
        context.IsPathStale = true;
        return BehaviourStatus.Succeeded;
    }

    private static BehaviourStatus SelectNextRoom(RkMissionContext context)
    {
        var solver = context.DungeonRunner.Solver;
        if (solver == null)
            return BehaviourStatus.Failed;

        if (solver.IsCurrentRoomStale || solver.TargetRoom == null)
        {
            if (!solver.Progress())
                return BehaviourStatus.Failed;
        }

        return BehaviourStatus.Succeeded;
    }

    private static BehaviourStatus MoveToRoom(RkMissionContext context)
    {
        var solver = context.DungeonRunner.Solver;
        if (solver == null || solver.TargetRoom == null)
            return BehaviourStatus.Failed;

        if (solver.IsCurrentRoomStale)
        {
            context.IsPathStale = true;
            return BehaviourStatus.Failed;
        }

        var destination = solver.TargetRoom.Room
            .GetDoorForward(solver.TargetRoom.Door);

        SetDestination(destination, context);
        return DynelManager.LocalPlayer.Position.Distance2DFrom(destination) < 1f
            ? BehaviourStatus.Succeeded
            : BehaviourStatus.Running;
    }

    private static void SetDestination(Vector3 destination, RkMissionContext context)
    {
        if (context.IsPathStale || !SMovementController.IsNavigating())
        {
            SMovementController.SetNavDestination(destination);
            if (SMovementController.IsNavigating())
                context.IsPathStale = false;
        }
    }
}
