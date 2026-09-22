using AOSharp.Common.GameData;
using AOSharp.Core;
using AOSharp.Core.Inventory;
using AOSharp.Core.UI;
using AOSharp.Pathfinding;
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
        // Incremental test: exercise dungeon exploration, then fall back to idle
        // if the solver is unavailable or cannot produce a target room.
        return FluentBuilder.Create<RkMissionContext>()
            .Selector("Rubi-Ka Mission Explore Test")
                .Subtree(Explore())
                .Subtree(Idle())
            .End()
            .Build();
    }

    private static IBehaviour<RkMissionContext> CompleteObjective()
    {
        return FluentBuilder.Create<RkMissionContext>()
            .Sequence("Mission Objective")
                .Condition(
                    "Active mission",
                    c => c.ActiveMission != null && !c.MissionObjectiveHandled)
                .Do("Complete objective", CompleteObjectiveStep)
            .End()
            .Build();
    }

    private static IBehaviour<RkMissionContext> OpenLockedDoor()
    {
        return FluentBuilder.Create<RkMissionContext>()
            .Sequence("Open Locked Door")
                .Do("Open nearby locked door", OpenLockedDoorStep)
            .End()
            .Build();
    }

    private static IBehaviour<RkMissionContext> Explore()
    {
        return FluentBuilder.Create<RkMissionContext>()
            .Sequence("Explore mission")
                .Condition(
                    "Solver available",
                    c => c.DungeonRunner.Solver != null)
                .Do("Select next room", SelectNextRoom)
                .Do("Move to room", MoveToRoom)
            .End()
            .Build();
    }

    private static IBehaviour<RkMissionContext> Idle()
    {
        return FluentBuilder.Create<RkMissionContext>()
            .Sequence("Idle")
                .Do("Idle", _ => BehaviourStatus.Running)
            .End()
            .Build();
    }

    private static BehaviourStatus CompleteObjectiveStep(RkMissionContext context)
    {
        var mission = context.ActiveMission;

        if (mission == null || context.MissionObjectiveHandled)
            return BehaviourStatus.Failed;

        Vector3 destination = mission.Location.Pos;

        SetDestination(destination, context);

        if (DynelManager.LocalPlayer.Position.DistanceFrom(destination) > 1f)
            return BehaviourStatus.Running;

        context.MissionObjectiveHandled = true;
        return BehaviourStatus.Succeeded;
    }

    private static BehaviourStatus OpenLockedDoorStep(RkMissionContext context)
    {
        var door = Playfield.Doors
            .Where(x =>
                x.IsLocked &&
                x.DistanceFrom(DynelManager.LocalPlayer) < 5f)
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

        var targetRoom = solver.TargetRoom.Room;

        // GetDoorForward returns a walkable point beyond the doorway. The raw
        // GetDoorPosRot coordinate is the shared threshold and can leave the
        // movement controller oscillating inside door geometry.
        var destination = targetRoom.GetDoorForward(solver.TargetRoom.Door);

        SetDestination(destination, context);

        // Wait for the actual room transition rather than treating proximity to
        // the shared doorway coordinate as success.
        if (DynelManager.LocalPlayer.Room.Instance == targetRoom.Instance)
            return BehaviourStatus.Succeeded;

        // If the character reaches the projected doorway point but the room has
        // not changed, force a fresh path request instead of idling there.
        if (DynelManager.LocalPlayer.Position.Distance2DFrom(destination) < 1f)
            context.IsPathStale = true;

        return BehaviourStatus.Running;
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
