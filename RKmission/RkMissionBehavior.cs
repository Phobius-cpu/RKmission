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
        // Fight targets before exploring. DungeonSolver only invalidates rooms
        // after they are clear, so navigation must yield to combat first.
        return FluentBuilder.Create<RkMissionContext>()
            .Selector("Rubi-Ka Mission Explore Test")
                .Subtree(Fight())
                .Subtree(Explore())
                .Subtree(Idle())
            .End()
            .Build();
    }

    private static IBehaviour<RkMissionContext> Fight()
    {
        return FluentBuilder.Create<RkMissionContext>()
            .Do("Fight nearby target", FightStep)
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

    private static BehaviourStatus FightStep(RkMissionContext context)
    {
        if (!context.DungeonRunner.FindFightableTarget(out SimpleChar target))
            return BehaviourStatus.Failed;

        if (!target.IsAlive)
            return BehaviourStatus.Succeeded;

        if (target.IsInLineOfSight && target.IsInAttackRange(true))
        {
            if (!DynelManager.LocalPlayer.IsAttackPending &&
                (!DynelManager.LocalPlayer.IsAttacking ||
                 DynelManager.LocalPlayer.FightingTarget.Identity != target.Identity))
            {
                DynelManager.LocalPlayer.Attack(target);
            }

            return BehaviourStatus.Running;
        }

        SetDestination(target.Position, context);
        return BehaviourStatus.Running;
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
            {
                // Diagnostic: distinguish a genuine dead-end (no unvisited doors
                // anywhere on the room stack) from the main-hall/floor-clear gate
                // that blocks progress until IsFloorClear is true.
                var currentRoom = DynelManager.LocalPlayer.Room;

                context.Logger.Information(
                    $"Progress() returned false. " +
                    $"CurrentRoom: {currentRoom?.Name} ({currentRoom?.Instance}), " +
                    $"RoomStackCount: {solver.RoomStack.Count}, " +
                    $"VisitedRooms: {solver.VisitedRooms.Count}, " +
                    $"IsFloorClear: {solver.IsFloorClear}, " +
                    $"IsOnBossFloor: {solver.IsOnBossFloor}, " +
                    $"Mode: {solver.Mode}, " +
                    $"IsLiftFound: {solver.IsLiftFound}");

                return BehaviourStatus.Failed;
            }
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

        // Use a walkable point beyond the doorway. The raw door threshold can
        // leave the movement controller oscillating inside door geometry.
        var destination = GetDoorForward(targetRoom, solver.TargetRoom.Door);

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

    private static Vector3 GetDoorForward(Room room, int doorIdx)
    {
        room.GetDoorPosRot(doorIdx, out Vector3 position, out Quaternion rotation);

        return position + (rotation * (room.GetDoorConnectZone(doorIdx) == room.Instance
            ? -Vector3.Forward
            : Vector3.Forward));
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
