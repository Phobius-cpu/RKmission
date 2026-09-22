using AOSharp.Common.GameData;
using AOSharp.Core;
using AOSharp.Core.Inventory;
using AOSharp.Core.Movement;
using AOSharp.Core.UI;
using AOSharp.Pathfinding;
using BehaviourTree;
using BehaviourTree.FluentBuilder;
using BTBotBase.BaseBehavior;
using Dungeon.Solver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Remoting.Contexts;
using System.Runtime.Remoting.Messaging;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Dungeon.Runner.Behaviors
{
    public static class DungeonRunnerBehavior<TContext> where TContext : DungeonRunnerContext<TContext>
    {
        private static int ReadyCheckDuration = 15000;

        public static IBehaviour<TContext> Compile(Func<IBehaviour<TContext>> bossRoomTree)
        {
            return FluentBuilder.Create<TContext>()
                .Sequence("Dungeon Runner")
                    .Condition("Is Dungeon", c => Playfield.IsDungeon)
                    .Condition("Is NavMesh Loaded", c => c.DungeonRunner.Solver.IsNavMeshLoaded)
                    .Do("Ready Check", c => ReadyCheck(c, ReadyCheckDuration))
                    .Subtree(Team.IsLeader ? Leader(bossRoomTree) : NonLeader(bossRoomTree))
                .End()
                .Build();
        }

        public static IBehaviour<TContext> Leader(Func<IBehaviour<TContext>> bossRoomTree)
        {
            return FluentBuilder.Create<TContext>()
                .Selector("Leader")
                    //.Subtree(TryCompleteObjective())
                    .Sequence("Clear room")
                        .Condition("Not Boss Room", c => !c.DungeonRunner.Solver.IsOnBossFloor) // Skip clearing of boss rooms
                        .Condition("Is Clear Mode", c => c.DungeonRunner.Solver.Mode == SolverMode.Clear)
                        .Do("Poll target", c => PollTarget(c, c.DungeonRunner.Solver.TargetRoom?.Room))
                        .Subtree(Fight())
                    .End()
                    .Selector("Room Selector")
                        .Subtree(BossRoom(bossRoomTree))
                        .Subtree(TransverseFloors())
                        .Subtree(NavigateFloor())
                    .End()
                .End()
                .Build();
        }

        public static IBehaviour<TContext> NonLeader(Func<IBehaviour<TContext>> bossRoomTree)
        {
            return FluentBuilder.Create<TContext>()
                .Selector("NonLeader")
                    .Sequence("Boss Room")
                        .Condition("In Boss Room", c => c.DungeonRunner.Solver.IsOnBossFloor && !c.ExitingDungeon)
                        .Subtree(BossRoom(bossRoomTree))
                    .End()
                    .Subtree(Fight())
                    .Subtree(FollowLeader())
                .End()
                .Build();
        }

        public static IBehaviour<TContext> TryCompleteObjective()
        {
            return FluentBuilder.Create<TContext>()
                .Sequence("TryCompleteObjective")
                    .Condition("Has mission", c => c.ActiveMission != null)
                    .Condition("Objective in sight", c => c.ActiveMission.GetMissionTarget() != null)
                    .Do("Move to objective", MoveToObjective)
                    .Selector("Complete or idle")
                        .Do("Complete Objective", CompleteObjective)
                        .Do("Idle", IdleBossRoom)
                    .End()
                    .Do("Exit Dungeon", ExitDungeon)
                .End()
                .Build();
        }

        public static IBehaviour<TContext> FollowLeader()
        {
            return FluentBuilder.Create<TContext>()
                .Sequence("Follow Leader")
                    .Condition("Has Leader Movement", c => c.LeaderMovement.HasValue)
                    .Selector("Handle Leader Movement")
                        .Sequence("Exit Dungeon")
                            .Condition("Is Exiting", c => c.ExitingDungeon)
                            .Subtree(TransverseFloors())
                        .End()

                        .Sequence("Move if correct floor")
                            .Condition("Is on correct floor", c => c.LeaderMovement.Value.Floor == DynelManager.LocalPlayer.Room.AbsFloor)
                            .Do("Move with leader", MoveWithLeader)
                        .End()

                        .Sequence("Move up to correct floor")
                            .Condition("Is lift found", c => c.DungeonRunner.Solver.IsLiftFound || c.ExitingDungeon)
                            .Subtree(TransverseFloors())
                        .End()

                        .Subtree(NavigateFloor())
                    .End()
                .End()
                .Build();
        }

        public static IBehaviour<TContext> TransverseFloors()
        {
            return FluentBuilder.Create<TContext>()
                .Sequence("Go to next floor")
                    .Condition("Is floor clear?", c => IsFloorClear(c) || !c.DungeonRunner.IsLeader)
                    .Do("Move to lift", MoveToLift)
                    .Do("Use Lift", UseLift)
                    .Condition("Is Exiting", c => c.ExitingDungeon && DynelManager.LocalPlayer.Room.Floor == 0)
                    .Do("Move To Exit", MoveToExit)
                .End()
                .Build();
        }

        public static IBehaviour<TContext> NavigateFloor()
        {
            return FluentBuilder.Create<TContext>()
                .Selector("Navigate Floor")
                    .Sequence("Select Room")
                        .Condition("Is current room stale or empty", c => IsRoomStaleOrEmpty(c))
                        .Do("Get next room", GetNextRoom)
                        .Do("Move back to room", MoveToRoom)
                    .End()
                    .Do("Move back to room", MoveToRoom)
                .End()
                .Build();
        }

        public static IBehaviour<TContext> BossRoom(Func<IBehaviour<TContext>> bossRoomTree)
        {
            return FluentBuilder.Create<TContext>()
                .Sequence("Boss Room")
                    .Condition("Is in boss room?", c => Playfield.NumFloors > 1 && c.DungeonRunner.Solver.IsOnBossFloor && !c.ExitingDungeon)
                    .Subtree(bossRoomTree())
                .End()
                .Build();
        }

        public static IBehaviour<TContext> Fight()
        {
            return FluentBuilder.Create<TContext>()
                .Sequence("Fight Target")
                    .Condition("Has target?", c => c.TargetId.HasValue)
                    .Condition("Can see target", c => DynelManager.Find(c.TargetId.Value, out SimpleChar _))
                    .Do("Move to attack pos", MoveToAttackPos)
                    .Do("Fight", FightTarget)
                .End()
                .Build();
        }

        public static BehaviourStatus Idle(TContext context)
        {   
            return BehaviourStatus.Running;
        }

        public static BehaviourStatus IdleBossRoom(TContext context)
        {
            Dynel target = context.ActiveMission.GetMissionTarget();

            if (target == null)
                return BehaviourStatus.Running;

            return BehaviourStatus.Failed;
        }

        public static BehaviourStatus GetNextRoom(TContext context)
        {
            if (context.DungeonRunner.Solver.Progress())
                context.Logger.Debug($"New RoomToClear == {context.DungeonRunner.Solver.TargetRoom.Room} - Door: {context.DungeonRunner.Solver.TargetRoom.Door}");
            else if (!context.DungeonRunner.Solver.IsOnBossFloor)
            {
                context.Logger.Debug("Floor done.");

                //if (DynelManager.NPCs.Any(x => !x.IsPet && x.IsAlive))
                //    context.DungeonRunner.Stop();
                return BehaviourStatus.Failed;
            }

            return BehaviourStatus.Succeeded;
        }

        public static BehaviourStatus MoveToExit(TContext context)
        {
            Playfield.Rooms.First().GetDoorPosRot(Playfield.GetEntranceDoor(), out Vector3 doorPos, out _);

            SetDestination(doorPos, context);

            if (DynelManager.LocalPlayer.Position.DistanceFrom(doorPos) < 0.2f)
                return BehaviourStatus.Succeeded;

            return BehaviourStatus.Running;
        }

        public static BehaviourStatus MoveToRoom(TContext context)
        {
            if (context.DungeonRunner.Solver.IsCurrentRoomStale)
            {
                context.IsPathStale = true;
                return BehaviourStatus.Failed;
            }

            if (context.DungeonRunner.Solver.Mode == SolverMode.Clear && context.DungeonRunner.FindFightableTarget(context.DungeonRunner.Solver.TargetRoom?.Room, out _))
                return BehaviourStatus.Failed;

            if (context.DungeonRunner.Solver.TargetRoom == null)
                return BehaviourStatus.Failed;

            //context.DungeonRunner.Solver.TargetRoom.Room.GetDoorPosRot(context.DungeonRunner.Solver.TargetRoom.Door, out Vector3 doorPos, out _);
            Vector3 doorPos = context.DungeonRunner.Solver.TargetRoom.Room.GetDoorForward(context.DungeonRunner.Solver.TargetRoom.Door);

            if (context.IsPathStale && context.DungeonRunner.IsLeader)
                context.DungeonRunner.AnnounceLeaderMovement(doorPos);

            if (DynelManager.LocalPlayer.Position.Distance2DFrom(doorPos) < 1f)
                return BehaviourStatus.Succeeded;

            SetDestination(doorPos, context);

            return BehaviourStatus.Running;
        }

        public static BehaviourStatus MoveToLift(TContext context)
        {
            if (!context.ExitingDungeon && context.DungeonRunner.Solver.Mode == SolverMode.Clear && context.DungeonRunner.FindFightableTarget(null, out _))
                return BehaviourStatus.Failed;

            LiftDirection liftDirection = context.ExitingDungeon ? LiftDirection.Backward : LiftDirection.Forward;

            if (liftDirection == LiftDirection.Backward && DynelManager.LocalPlayer.Room.Floor == 0)
                return BehaviourStatus.Succeeded;

            if (context.DungeonRunner.Solver.TryGetLift(liftDirection, out Lift lift))
            {
                if (context.IsPathStale && !context.ExitingDungeon && Team.IsLeader)
                    context.DungeonRunner.AnnounceGoingToLift();

                SetDestination(lift.Position, context);
            }
            else
            {
                Halt(context);
                context.DungeonRunner.Stop();
                context.Logger.Error("Lift was not found.");
                context.Logger.Error($"Floor: {DynelManager.LocalPlayer.Room.Floor}");
                context.Logger.Error($"Room: {DynelManager.LocalPlayer.Room}");
                context.Logger.Error($"LiftDirection: {liftDirection}");
                return BehaviourStatus.Failed;
            }

            if (DynelManager.LocalPlayer.Position.DistanceFrom(lift.Position) < 1f)
                return BehaviourStatus.Succeeded;

            return BehaviourStatus.Running;
        }

        public static BehaviourStatus UseLift(TContext context)
        {
            if (context.ExitingDungeon && DynelManager.LocalPlayer.Room.Floor == 0)
                return BehaviourStatus.Succeeded;

            if (context.FloorChanged == null)
            {
                if (DynelManager.LocalPlayer.Cooldowns.ContainsKey(Stat.Level))
                    return BehaviourStatus.Running;

                context.FloorChanged = new ManualResetEvent(false);
            }
            else if (context.FloorChanged.WaitOne(0))
            {
                context.FloorChanged = null;
                context.Logger.Debug($"IsOnBossFloor: {context.DungeonRunner.Solver.IsOnBossFloor}");
                return BehaviourStatus.Succeeded;
            }

            if (!context.LiftRetryInterval.Elapsed)
                return BehaviourStatus.Running;

            LiftDirection liftDirection = context.ExitingDungeon ? LiftDirection.Backward : LiftDirection.Forward;

            if (context.DungeonRunner.Solver.TryGetLift(liftDirection, out Lift lift) && DynelManager.Find(lift.Identity, out Dynel liftTerminal))
            {
                if (DynelManager.LocalPlayer.Position.DistanceFrom(lift.Position) >= 1f)
                    return BehaviourStatus.Failed;

                liftTerminal.Use();
            }
            else
                return BehaviourStatus.Failed;

            return BehaviourStatus.Running;
        }

        public static BehaviourStatus PollTarget(TContext context, Room targetRoom)
        {
            if (context.TargetPollEnded == null)
            {
                if (!context.DungeonRunner.FindFightableTarget(targetRoom, out SimpleChar target))
                    return BehaviourStatus.Failed;

                context.Logger.Debug($"Starting Target Poll. My Target: {target.Name} ({target.Identity})");

                context.TargetPollEnded = new ManualResetEvent(false);
                context.DungeonRunner.TargetPoll.StartPoll(target);
            }

            if (context.TargetPollEnded.WaitOne(0))
            {
                context.TargetPollEnded = null;

                context.Logger.Debug($"Target Result: {context.TargetId}");
                return context.TargetId.HasValue && DynelManager.Exists(context.TargetId.Value) ? BehaviourStatus.Succeeded : BehaviourStatus.Failed;
            }

            return BehaviourStatus.Running;
        }

        public static BehaviourStatus MoveToAttackPos(TContext context)
        {
            if (Team.IsLeader && context.DungeonRunner.Solver.IsCurrentRoomStale)
            {
                context.IsPathStale = true;
                context.Logger.Debug("Failing due to staleness");
                return BehaviourStatus.Failed;
            }

            if (!DynelManager.Find(context.TargetId.Value, out SimpleChar target))
            {
                //context.Logger.Information($"Unable to locate target {context.TargetId.Value}. Wiping target id..");
                //context.TargetId = null;
                return BehaviourStatus.Failed;
            }

            if (target.IsInLineOfSight && target.IsInAttackRange(true))
            {
                Halt(context);

                return BehaviourStatus.Succeeded;
            }

            if (context.IsPathStale && context.DungeonRunner.IsLeader)
                context.DungeonRunner.AnnounceLeaderMovement(target.Position);

            SetDestination(target.Position, context);

            return BehaviourStatus.Running;
        }

        public static BehaviourStatus FightTarget(TContext context)
        {
            if (!context.TargetId.HasValue || !DynelManager.Find(context.TargetId.Value, out SimpleChar target))
            {
                context.Logger.Debug($"Unable to locate target {context.TargetId.Value}. (FightTarget) Wiping target id..");
                context.TargetId = null;
                return BehaviourStatus.Failed;
            }

            if (!target.IsInLineOfSight)
                return BehaviourStatus.Failed;

            if (!target.IsInAttackRange(true))
                return BehaviourStatus.Failed;

            if (!target.IsAlive)
            {
                context.Logger.Debug($"Target is dead. {context.TargetId.Value}. Wiping target id..");
                context.TargetId = null;
                return BehaviourStatus.Succeeded;
            }

            if (!DynelManager.LocalPlayer.IsAttackPending)
            {
                if (!DynelManager.LocalPlayer.IsAttacking || DynelManager.LocalPlayer.FightingTarget.Identity != context.TargetId)
                    DynelManager.LocalPlayer.Attack(target);
            }

            return BehaviourStatus.Running;
        }

        public static BehaviourStatus MoveWithLeader(TContext context)
        {
            if (context.LeaderMovement.Value.Destination == Vector3.Zero)
                return BehaviourStatus.Succeeded;

            if (context.TargetId.HasValue && DynelManager.Find(context.TargetId.Value, out SimpleChar _))
                return BehaviourStatus.Succeeded;

            if (DynelManager.LocalPlayer.Position.DistanceFrom(context.LeaderMovement.Value.Destination) < 1f)
                return BehaviourStatus.Succeeded;

            SetDestination(context.LeaderMovement.Value.Destination, context);

            return BehaviourStatus.Running;
        }


        public static BehaviourStatus MoveToObjective(TContext context)
        {
            Dynel target = context.ActiveMission.GetMissionTarget();

            if (target == null)
                return BehaviourStatus.Failed;

            SetDestination(target.Position, context);

            if (DynelManager.LocalPlayer.Position.DistanceFrom(target.Position) < 1f)
                return BehaviourStatus.Succeeded;

            return BehaviourStatus.Running;
        }

        public static BehaviourStatus CompleteObjective(TContext context)
        {
            Dynel target = context.ActiveMission.GetMissionTarget();

            if (target == null)
                return BehaviourStatus.Failed;

            MissionAction action = context.ActiveMission.Actions.First();

            if (action is FindItemAction || action is FindPersonAction)
            {
                target.Target();
            }
            else if (action is UseItemOnItemAction useItemOnItemAction)
            {
                Item sourceItem = Inventory.Items.FirstOrDefault(x => x.UniqueIdentity == useItemOnItemAction.Source);
                if (sourceItem != null)
                {
                    sourceItem.UseOn(target);
                }
                else
                {
                    context.Logger.Error($"Source item {useItemOnItemAction.Source} not found in inventory.");
                    return BehaviourStatus.Failed;
                }
            }

            context.ActiveMission = null;
            return BehaviourStatus.Succeeded;
        }

        public static BehaviourStatus ReadyCheck(TContext context, int duration)
        {
            if (!Team.IsLeader || (context.ReadyPollEnded == null && context.TeamReady) || context.DungeonRunner.Roster.Count == 1)
                return BehaviourStatus.Succeeded;

            if (context.ReadyPollEnded == null)
            {
                context.Logger.Debug("Starting Ready Check");

                context.ReadyPollEnded = new ManualResetEvent(false);
                context.DungeonRunner.ReadyPoll.StartPoll(duration);
            }

            if (context.ReadyPollEnded.WaitOne(0))
            {
                context.ReadyPollEnded = null;

                context.Logger.Debug($"Ready check passed");
                return BehaviourStatus.Succeeded;
            }

            return BehaviourStatus.Running;
        }

        public static void SetDestination(Vector3 destination, TContext context)
        {
            if (context.IsPathStale || !SMovementController.IsNavigating())
            {
                SMovementController.SetNavDestination(destination);

                if (SMovementController.IsNavigating())
                    context.IsPathStale = false;

                context.Logger.Debug($"Setting nav dest {destination}");
            }
        }

        public static void Halt(TContext context)
        {
            SMovementController.Halt();
            context.IsPathStale = true;
        }

        public static BehaviourStatus ExitDungeon(TContext context)
        {
            context.Logger.Debug("ExitDungeon");
            context.DungeonRunner.AnnounceExitingDungeon();
            context.ExitingDungeon = true;
            return BehaviourStatus.Succeeded;
        }

        public static bool IsRoomStaleOrEmpty(TContext context)
        {
            if (context.DungeonRunner.Solver.IsCurrentRoomStale)
                return true;

            if (context.DungeonRunner.Solver.TargetRoom == null)
                return true;

            if (context.DungeonRunner.Solver.TargetRoom.Room.IsMainHall() && !context.DungeonRunner.Solver.IsFloorClear)
                return true;

            if (context.DungeonRunner.Solver.Mode == SolverMode.Blitz && !context.DungeonRunner.Solver.IsFloorClear)
                return true;

            return false;
        }

        public static bool IsFloorClear(TContext context)
        {
            return context.DungeonRunner.Solver.IsFloorClear || context.ExitingDungeon;
        }

        public static bool IsNearLeader(TContext context)
        {
            return DynelManager.Find(context.DungeonRunner.Leader, out SimpleChar leader) && 
                    (leader.Room.Instance == DynelManager.LocalPlayer.Room.Instance || leader.DistanceFrom(DynelManager.LocalPlayer) < 10f);
        }
    }
}
