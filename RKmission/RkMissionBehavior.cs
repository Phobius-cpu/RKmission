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
        return FluentBuilder.Create<RkMissionContext>()
            .Selector("Rubi-Ka Mission Explore Test")
                .Do("Heartbeat", Heartbeat)
                .Subtree(Fight())
                .Subtree(Explore())
                .Subtree(Idle())
            .End()
            .Build();
    }

    private static IBehaviour<RkMissionContext> Fight()
    {
        return FluentBuilder.Create<RkMissionContext>()
            .Sequence("Fight")
                .Do("Fight nearby target", c => RunSafely(c, "FightStep", FightStep))
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
                .Do("Complete objective", c => RunSafely(c, "CompleteObjectiveStep", CompleteObjectiveStep))
            .End()
            .Build();
    }

    private static IBehaviour<RkMissionContext> OpenLockedDoor()
    {
        return FluentBuilder.Create<RkMissionContext>()
            .Sequence("Open Locked Door")
                .Do("Open nearby locked door", c => RunSafely(c, "OpenLockedDoorStep", OpenLockedDoorStep))
            .End()
            .Build();
    }

    private static IBehaviour<RkMissionContext> Explore()
    {
        return FluentBuilder.Create<RkMissionContext>()
            .Sequence("Explore mission")
                .Do("Check solver ready", c => RunSafely(c, "CheckSolverReady", CheckSolverReady))
                .Do("Select next room", c => RunSafely(c, "SelectNextRoom", SelectNextRoom))
                .Do("Move to room", c => RunSafely(c, "MoveToRoom", MoveToRoom))
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

    private static BehaviourStatus Heartbeat(RkMissionContext context)
    {
        if (context.LastHeartbeatUtc == null ||
            DateTime.UtcNow - context.LastHeartbeatUtc > TimeSpan.FromSeconds(3))
        {
            context.LastHeartbeatUtc = DateTime.UtcNow;
            context.Logger.Information(
                $"Heartbeat: tree ticking. " +
                $"CurrentRoom: {DynelManager.LocalPlayer.Room?.Name} " +
                $"({DynelManager.LocalPlayer.Room?.Instance}), " +
                $"Solver: {(context.DungeonRunner.Solver == null ? "null" : "present")}.");
        }

        return BehaviourStatus.Failed;
    }

    private static BehaviourStatus RunSafely(
        RkMissionContext context,
        string stepName,
        Func<RkMissionContext, BehaviourStatus> step)
    {
        try
        {
            return step(context);
        }
        catch (Exception ex)
        {
            context.Logger.Error(
                $"Behavior step '{stepName}' threw an exception. " +
                $"CurrentRoom: {DynelManager.LocalPlayer.Room?.Name} " +
                $"({DynelManager.LocalPlayer.Room?.Instance}). Exception: {ex}");

            context.IsPathStale = true;
            context.FightApproachStartUtc = null;
            context.StallCheckPosition = null;
            context.StallCheckLastProgressUtc = null;
            return BehaviourStatus.Failed;
        }
    }

    private static BehaviourStatus CheckSolverReady(RkMissionContext context)
    {
        if (context.DungeonRunner.Solver == null)
        {
            context.Logger.Information(
                $"Explore: solver is null. " +
                $"CurrentRoom: {DynelManager.LocalPlayer.Room?.Name} " +
                $"({DynelManager.LocalPlayer.Room?.Instance}).");
            return BehaviourStatus.Failed;
        }

        return BehaviourStatus.Succeeded;
    }

    private static BehaviourStatus FightStep(RkMissionContext context)
    {
        if (!context.DungeonRunner.FindFightableTarget(out SimpleChar target))
            return BehaviourStatus.Failed;

        if (!target.IsAlive)
            return BehaviourStatus.Succeeded;

        if (target.IsInLineOfSight && target.IsInAttackRange(true))
        {
            context.FightApproachStartUtc = null;

            var fightingTarget = DynelManager.LocalPlayer.FightingTarget;

            if (!DynelManager.LocalPlayer.IsAttackPending &&
                (!DynelManager.LocalPlayer.IsAttacking ||
                 fightingTarget == null ||
                 fightingTarget.Identity != target.Identity))
            {
                DynelManager.LocalPlayer.Attack(target);
            }

            return BehaviourStatus.Running;
        }

        context.FightApproachStartUtc ??= DateTime.UtcNow;

        if (DateTime.UtcNow - context.FightApproachStartUtc > TimeSpan.FromSeconds(5))
        {
            context.Logger.Information($"Giving up on unreachable target {target.Identity}.");
            context.FightApproachStartUtc = null;
            return BehaviourStatus.Failed;
        }

        SetDestination(target.Position, context);

        if (IsMovementStalled(context, target.Position))
        {
            context.Logger.Information(
                $"Fight target appears blocked or unreachable: {target.Identity}. " +
                $"CurrentRoom: {DynelManager.LocalPlayer.Room?.Name} ({DynelManager.LocalPlayer.Room?.Instance}), " +
                $"TargetRoom: {target.Room?.Instance}.");
            context.FightApproachStartUtc = null;
            return BehaviourStatus.Failed;
        }

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
            {
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
        {
            context.Logger.Information(
                $"MoveToRoom: no target room available. " +
                $"Solver: {(solver == null ? "null" : "present")}, " +
                $"CurrentRoom: {DynelManager.LocalPlayer.Room?.Name} " +
                $"({DynelManager.LocalPlayer.Room?.Instance}).");

            return BehaviourStatus.Failed;
        }

        var currentRoom = DynelManager.LocalPlayer.Room;
        var targetRoom = solver.TargetRoom.Room;
        var destination = GetDoorForward(currentRoom, targetRoom, solver.TargetRoom.Door);
        var distanceToDestination = DynelManager.LocalPlayer.Position.Distance2DFrom(destination);

        if (context.LastMoveHeartbeatUtc == null ||
            DateTime.UtcNow - context.LastMoveHeartbeatUtc > TimeSpan.FromSeconds(3))
        {
            context.LastMoveHeartbeatUtc = DateTime.UtcNow;
            context.Logger.Information(
                $"MoveToRoom heartbeat: " +
                $"CurrentRoom={currentRoom?.Name} ({currentRoom?.Instance}), " +
                $"TargetRoom={targetRoom?.Name} ({targetRoom?.Instance}), " +
                $"TargetDoor={solver.TargetRoom.Door}, " +
                $"Destination={destination}, " +
                $"Distance={distanceToDestination:F2}, " +
                $"Navigating={SMovementController.IsNavigating()}, " +
                $"PathStale={context.IsPathStale}.");
        }

        SetDestination(destination, context);

        if (currentRoom.Instance == targetRoom.Instance)
            return BehaviourStatus.Succeeded;

        if (IsMovementStalled(context, destination))
        {
            context.Logger.Information(
                $"MoveToRoom: stalled while approaching room {targetRoom.Instance}. " +
                $"CurrentRoom: {DynelManager.LocalPlayer.Room?.Name} ({DynelManager.LocalPlayer.Room?.Instance}), " +
                $"Destination: {destination}. Forcing a new path request.");
            context.IsPathStale = true;
            return BehaviourStatus.Failed;
        }

        if (DynelManager.LocalPlayer.Position.Distance2DFrom(destination) < 1f)
        {
            context.Logger.Information(
                $"MoveToRoom: reached doorway point but room did not change. " +
                $"CurrentRoom: {DynelManager.LocalPlayer.Room?.Name} ({DynelManager.LocalPlayer.Room?.Instance}), " +
                $"TargetRoom: {targetRoom.Instance}. Forcing path refresh.");
            context.IsPathStale = true;
        }

        return BehaviourStatus.Running;
    }

    private static Vector3 GetDoorForward(Room currentRoom, Room targetRoom, int targetDoorIdx)
    {
        targetRoom.GetDoorPosRot(targetDoorIdx, out Vector3 targetDoorPosition, out Quaternion rotation);

        var sourceDoorIdx = FindConnectedDoor(currentRoom, targetRoom.Instance);
        if (sourceDoorIdx >= 0)
        {
            currentRoom.GetDoorPosRot(sourceDoorIdx, out Vector3 currentDoorPosition, out _);
            var direction = targetDoorPosition - currentDoorPosition;
            var length = Math.Sqrt(
                direction.X * direction.X +
                direction.Y * direction.Y +
                direction.Z * direction.Z);

            if (length > 0.001f)
            {
                var normalizedX = direction.X / length;
                var normalizedY = direction.Y / length;
                var normalizedZ = direction.Z / length;

                return new Vector3(
                    targetDoorPosition.X + normalizedX * 2f,
                    targetDoorPosition.Y + normalizedY * 2f,
                    targetDoorPosition.Z + normalizedZ * 2f);
            }
        }

        return targetDoorPosition + (rotation * Vector3.Forward * 2f);
    }

    private static int FindConnectedDoor(Room room, int connectedRoomInstance)
    {
        for (var i = 0; i < room.NumDoors; i++)
        {
            if (room.GetDoorConnectZone(i) == connectedRoomInstance)
                return i;
        }

        return -1;
    }

    private static bool IsMovementStalled(RkMissionContext context, Vector3 destination)
    {
        var current = DynelManager.LocalPlayer.Position;

        if (context.StallCheckPosition == null || context.StallCheckLastProgressUtc == null)
        {
            context.StallCheckPosition = current;
            context.StallCheckLastProgressUtc = DateTime.UtcNow;
            return false;
        }

        var distanceMoved = current.Distance2DFrom(context.StallCheckPosition.Value);

        if (distanceMoved > 0.5f)
        {
            context.StallCheckPosition = current;
            context.StallCheckLastProgressUtc = DateTime.UtcNow;
            return false;
        }

        if (DateTime.UtcNow - context.StallCheckLastProgressUtc > TimeSpan.FromSeconds(4))
        {
            context.Logger.Information(
                $"Movement stall detected while targeting {destination}. " +
                $"CurrentRoom: {DynelManager.LocalPlayer.Room?.Name} ({DynelManager.LocalPlayer.Room?.Instance}), " +
                $"DistanceMoved: {distanceMoved:F2}.");
            context.StallCheckPosition = current;
            context.StallCheckLastProgressUtc = DateTime.UtcNow;
            return true;
        }

        return false;
    }

    private static void SetDestination(Vector3 destination, RkMissionContext context)
    {
        var destinationChanged =
            context.LastDestination == null ||
            context.LastDestination.Value.Distance2DFrom(destination) > 0.5f;

        if (context.IsPathStale || !SMovementController.IsNavigating())
        {
            SMovementController.SetNavDestination(destination);

            if (SMovementController.IsNavigating())
                context.IsPathStale = false;
        }

        if (destinationChanged)
        {
            context.LastDestination = destination;
            context.StallCheckPosition = DynelManager.LocalPlayer.Position;
            context.StallCheckLastProgressUtc = DateTime.UtcNow;
        }
    }
}
