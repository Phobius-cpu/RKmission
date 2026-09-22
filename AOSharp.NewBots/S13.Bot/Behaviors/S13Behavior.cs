using BehaviourTree.FluentBuilder;
using BehaviourTree;
using System;
using System.Collections.Generic;
using System.Linq;
using AOSharp.Core.UI;
using AOSharp.Core;
using AOSharp.Common.GameData;
using AOSharp.Core.Movement;
using AOSharp.Core.Inventory;
using System.Threading;
using AOSharp.Recast;
using BTBotBase;

namespace S13.Bot.Behaviors
{
    internal class S13Behavior
    {
        public static Vector3 GoalPos = new Vector3(197f, 5.1f, 472f);
        public static float MovePredictTimeout = 3;

        internal static IBehaviour<BotContext> Compile()
        {
            return FluentBuilder.Create<BotContext>()
                .Sequence("S13")
                    .Condition("Is In S13", c => Playfield.ModelId == PlayfieldId.Sector13)
                    .Subtree(Team.IsLeader ? LeaderOutOfCombat() : FollowerOutOfCombat())
                    .Subtree(Team.IsLeader ? LeaderCombat() : FollowerCombat())
                .End()
                .Build();
        }

        internal static IBehaviour<BotContext> LeaderCombat()
        {
            return FluentBuilder.Create<BotContext>()
                .Selector("Engage or Pull")
                    .Sequence("Try Engage")
                        .Do("Poll Fight Target", PollTarget)
                        .Subtree(Fight())
                    .End()
                    .Subtree(PullTarget())
                .End()
                .Build();
        }

        internal static IBehaviour<BotContext> FollowerCombat()
        {
            return FluentBuilder.Create<BotContext>()
                .Selector("Fight")
                    .Do("Defend", DefendLeaderless)
                    .Subtree(Fight())
                .End()
                .Build();
        }

        internal static IBehaviour<BotContext> LeaderOutOfCombat()
        {
            return FluentBuilder.Create<BotContext>()
                .Selector("Out Of Combat")
                    .Do("Find aliens", SearchForAliens)
                .End()
                .Build();
        }

        internal static IBehaviour<BotContext> FollowerOutOfCombat()
        {
            return FluentBuilder.Create<BotContext>()
                .Selector("Out Of Combat")
                    .Do("Go near leader", GoNearLeader)
                    .Do("Find aliens", SearchForAliens)
                .End()
                .Build();
        }

        internal static IBehaviour<BotContext> Fight()
        {
            return FluentBuilder.Create<BotContext>()
                .Sequence("Fight Target")
                    .Condition("Has target?", c => c.TargetId.HasValue)
                    .Do("Move to attack pos", MoveToAttackPos)
                    .Do("Fight aliens", FightTarget)
                .End()
                .Build();
        }


        internal static IBehaviour<BotContext> PullTarget()
        {
            return FluentBuilder.Create<BotContext>()
                .Sequence("Pull Target")
                    .Condition("Is Leader", c => Team.IsLeader)
                    .Do("Pick Target", DeterminePullTarget)
                    .Do("Pull Target", PullTarget)
                    .Do("Wait For Pathing", WaitForMobPathing)
                .End()
                .Build();
        }

        public static BehaviourStatus MoveToAttackPos(BotContext context)
        {
            if (!DynelManager.Find(context.TargetId.Value, out SimpleChar target))
                return BehaviourStatus.Failed;

            if (target.IsInLineOfSight && target.IsInAttackRange(true))
            {
                if (MovementController.Instance.IsNavigating)
                    context.S13Bot.NavMovementController.Halt();

                return BehaviourStatus.Succeeded;
            }

            var combatPos = GetBestCombatPosition(context, target);
            context.S13Bot.NavMovementController.Pathfinder.GetNavMeshPoint(combatPos, new org.critterai.Vector3(2, 2, 2), out org.critterai.nav.NavmeshPoint navPos);
            context.S13Bot.NavMovementController.SetNavMeshDestination(navPos.point.ToAOVector3());

            return BehaviourStatus.Running;
        }

        public static BehaviourStatus SearchForAliens(BotContext context)
        {
            if (context.S13Bot.AliensInArea)
            {
                if (MovementController.Instance.IsNavigating)
                    context.S13Bot.NavMovementController.Halt();

                return BehaviourStatus.Succeeded;
            }

            if (MovementController.Instance.IsNavigating)
                return BehaviourStatus.Running;

            if (Vector3.Distance(DynelManager.LocalPlayer.Position, GoalPos) < 1f)
            {
                if (Team.IsLeader)
                    context.NeedsToReform = true;

                context.S13Bot.InstancesCompleted++;

                return BehaviourStatus.Succeeded;
            }

            context.S13Bot.NavMovementController.SetNavMeshDestination(GoalPos);

            return BehaviourStatus.Running;
        }

        public static BehaviourStatus GoNearLeader(BotContext context)
        {
            //if (MovementController.Instance.IsNavigating)
            //    return BehaviourStatus.Running;

            if (!DynelManager.Find(context.S13Bot.Leader, out SimpleChar leader))
                return BehaviourStatus.Failed;

            if (leader.DistanceFrom(DynelManager.LocalPlayer) < 5f)
            {
                MovementController.Instance.Halt();
                return BehaviourStatus.Succeeded;
            }

            if (context.NavUpdateInterval.Elapsed)
                context.S13Bot.NavMovementController.SetNavMeshDestination(leader.Position);

            return BehaviourStatus.Running;
        }

        public static BehaviourStatus PollTarget(BotContext context)
        {
            if (context.PollingEnded == null)
            {
                if (!context.S13Bot.FindFightableTarget(out SimpleChar target))
                    return BehaviourStatus.Failed;

                context.PollingEnded = new ManualResetEvent(false);
                context.S13Bot.TargetPoll.StartPoll(target);
            }

            if (context.PollingEnded.WaitOne(0))
            {
                context.PollingEnded = null;
                context.PullTargetId = null;

                return context.TargetId.HasValue && DynelManager.Exists(context.TargetId.Value) ? BehaviourStatus.Succeeded : BehaviourStatus.Failed;
            }

            return BehaviourStatus.Running;
        }

        public static BehaviourStatus DefendLeaderless(BotContext context)
        {
            if (Team.Members.Any() && DynelManager.Find(context.S13Bot.Leader, out SimpleChar _))
                return BehaviourStatus.Failed;

            if (context.TargetId.HasValue)
                return BehaviourStatus.Failed;

            if (!context.S13Bot.FindFightableTarget(out SimpleChar target))
                return BehaviourStatus.Succeeded;

            context.TargetId = target.Identity;
            return BehaviourStatus.Failed;
        }

        public static BehaviourStatus WaitForMobPathing(BotContext context)
        {
            if (context.S13Bot.FightableAliens)
            {
                //context.PullTargetId = null;
                return BehaviourStatus.Succeeded;
            }

            if (!DynelManager.Find(context.PullTargetId.Value, out SimpleChar pullTarget))
            {
                context.PullTargetId = null;
                return BehaviourStatus.Failed;
            }

            if (!pullTarget.IsPathing)
                return BehaviourStatus.Succeeded;

            return BehaviourStatus.Running;
        }

        public static BehaviourStatus DeterminePullTarget(BotContext context)
        {
            if (context.PullTargetId.HasValue)
                return BehaviourStatus.Succeeded;

            if (!context.S13Bot.FindPullTarget(out SimpleChar target))
                return BehaviourStatus.Failed;

            context.PullTargetId = target.Identity;
            context.FightStartTime = Time.NormalTime;
            return BehaviourStatus.Succeeded;
        }

        public static BehaviourStatus PullTarget(BotContext context)
        {
            if (!DynelManager.Find(context.PullTargetId.Value, out SimpleChar target) || !target.IsInLineOfSight)
            {
                context.PullTargetId = null;
                return BehaviourStatus.Failed;
            }

            if (DynelManager.LocalPlayer.Cooldowns.ContainsKey(Stat.Psychology))
                return BehaviourStatus.Running;

            if (target.FightingTarget != null)
            {
                context.FightStartTime = Time.NormalTime;
                return BehaviourStatus.Succeeded;
            }

            Item aggTool = Inventory.FindAll(Consts.AggTools).FirstOrDefault();

            if (aggTool != null)
                aggTool.Use(target, true);
            else
                return BehaviourStatus.Failed;

            return BehaviourStatus.Running;
        }

        public static BehaviourStatus FightTarget(BotContext context)
        {
            if (!context.TargetId.HasValue || !DynelManager.Find(context.TargetId.Value, out SimpleChar target))
                return BehaviourStatus.Failed;

            if (!target.IsInLineOfSight)
                return BehaviourStatus.Failed;

            if (!target.IsInAttackRange(true))
                return BehaviourStatus.Failed;

            if (!target.IsAlive)
            {
                context.TargetId = null;
                return BehaviourStatus.Succeeded;
            }

            if (!DynelManager.LocalPlayer.IsAttacking && !DynelManager.LocalPlayer.IsAttackPending)
                DynelManager.LocalPlayer.Attack(target);

            return BehaviourStatus.Running;
        }

        //public static bool IsSafeToAttack(BotContext context)
        //{
        //    if (!DynelManager.Find(new Identity(IdentityType.SimpleChar, context.TargetId.Value), out SimpleChar target))
        //        return false;

        //    return 
        //}

        private static Vector3 GetBestCombatPosition(BotContext context, SimpleChar targetChar)
        {
            if (Time.NormalTime < context.FightStartTime + MovePredictTimeout && targetChar.IsPathing)
            {
                //Correct Y axis using a raycast because mob waypoints do not follow the terrain
                Vector3 rayOrigin = targetChar.PathingDestination;
                rayOrigin.Y += 5f;
                Vector3 rayTarget = rayOrigin;
                rayTarget.Y = 0;

                if (Playfield.Raycast(rayOrigin, rayTarget, out Vector3 hitPos, out _))
                    return hitPos;

                return targetChar.PathingDestination;
            }

            if (targetChar.IsInAttackRange(true) && targetChar.IsInLineOfSight)
                return DynelManager.LocalPlayer.Position;

            return targetChar.Position;
        }
    }
}
