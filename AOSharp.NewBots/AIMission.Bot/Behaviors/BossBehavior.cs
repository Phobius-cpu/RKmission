using AOSharp.Common.GameData;
using AOSharp.Core;
using AOSharp.Pathfinding;
using BehaviourTree;
using BehaviourTree.FluentBuilder;
using BTBotBase.BaseBehavior;
using Dungeon.Runner.Behaviors;
using Dungeon.Solver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

//Coccoon Attendant - Cha'Heru
//Master of Time and Space // Alien Defense System // Automated Defense System
//Master of Silence // Nanovoider
//Master of Biological Metamorphoses // Regeneration Conduit
//Master of Nanovoid
//Master of PsyMod

namespace AIMission.Bot.Behaviors
{
    internal class BossBehavior
    {
        private static float DefenseSystemAvoidDistance = 10;

        public static IBehaviour<BTContext> Leader()
        {
            return FluentBuilder.Create<BTContext>()
                .Sequence("BossRoom")
                    .Do("Wait for boss to load", WaitForBossSpawn)
                    .UntilSuccess("Until Mission Complete")
                        .Selector("")
                            .Condition("Is Mission Complete", MissionIsOver)
                            .Selector("")
                                .Subtree(DodgeDefenseSystem())
                                .Sequence("Fight")
                                    .Selector("")
                                        .Condition("Has target?", c => c.TargetId.HasValue)
                                        .Do("Poll target", c => DungeonRunnerBehavior<BTContext>.PollTarget(c, DynelManager.LocalPlayer.Room))
                                    .End()
                                    .Subtree(BossFight())
                                    .Condition("Is Mission Complete", MissionIsOver)
                                .End()
                            .End()
                        .End()
                    .End()
                    //.Subtree(LootBoss())
                    .Do("Move to lift", MoveToLiftBossRoom)
                    .Do("Exit Dungeon", DungeonRunnerBehavior<BTContext>.ExitDungeon)
                .End()
                .Build();
        }

        public static IBehaviour<BTContext> NonLeader()
        {
            return FluentBuilder.Create<BTContext>()
                .Sequence("BossRoom")
                    .Do("Wait for boss to load", WaitForBossSpawn)
                    .UntilSuccess("Until Mission Complete")
                        .Selector("")
                            .Condition("Is Mission Complete", MissionIsOver)
                            .Selector("")
                                .Subtree(DodgeDefenseSystem())
                                .Sequence("Fight")
                                    .Subtree(BossFight())
                                    .Condition("Is Mission Complete", MissionIsOver)
                                .End()
                            .End()
                        .End()
                    .End()
                    .Do("Move to lift", MoveToLiftBossRoom)
                    .Do("Exit Dungeon", DungeonRunnerBehavior<BTContext>.ExitDungeon)
                .End()
                .Build();
        }

        public static IBehaviour<BTContext> BossFight()
        {
            return FluentBuilder.Create<BTContext>()
                .Sequence("Fight Target")
                    .Condition("Has target?", c => c.TargetId.HasValue)
                    .Condition("Can see target", c => DynelManager.Find(c.TargetId.Value, out SimpleChar _))
                    .Do("Move to attack pos", DungeonRunnerBehavior<BTContext>.MoveToAttackPos)
                    .Do("Fight", BossFightTarget)
                .End()
                .Build();
        }

        public static IBehaviour<BTContext> DodgeDefenseSystem()
        {
            Vector3 dodgePos = Vector3.Zero;

            return FluentBuilder.Create<BTContext>()
                .AlwaysFail("")
                    .Sequence("Dodge Defense System")
                        .Condition("IsStandingOnADS", c => IsStandingOnADS())
                        .Condition("Has Dodge Position", c => GetDodgePosition(out dodgePos))
                        .Do("Move to dodge pos", c => BaseBehaviors<BTContext>.MoveTo(c, dodgePos))
                    .End()
                .End()
                .Build();
        }

        public static BehaviourStatus BossFightTarget(BTContext context)
        {
            if (IsStandingOnADS())
                return BehaviourStatus.Failed;

            if (DynelManager.Find("Regeneration Conduit", out SimpleChar regenCond) && 
                regenCond.IsAlive && 
                DynelManager.Find(context.TargetId.Value, out SimpleChar target) && 
                target.Name != "Regeneration Conduit")
            {
                context.TargetId = null;
                return BehaviourStatus.Failed;
            }

            return DungeonRunnerBehavior<BTContext>.FightTarget(context);
        }

        public static bool IsStandingOnADS()
        {
            return IsPosNearADS(DynelManager.LocalPlayer.Position);
        }

        public static bool IsPosNearADS(Vector3 pos)
        {
            var defenseSystems = DynelManager.NPCs.Where(x => x.Name == "Automated Defense System");

            if (defenseSystems.Any(x => Vector3.Distance(x.Position, pos) < DefenseSystemAvoidDistance))
                return true;

            return false;
        }

        public static bool GetDodgePosition(out Vector3 pos)
        {
            pos = DynelManager.LocalPlayer.Position;
            var boss = DynelManager.NPCs.FirstOrDefault(x => x.Name == "Master of Time and Space");
            if (boss == null)
                return false;

            float radius = 8f;
            int sampleCount = 36;
            float closestDist = float.MaxValue;
            Vector3 bestPos = pos;
            bool found = false;

            for (int i = 0; i < sampleCount; i++)
            {
                float angle = (float)(i * (Math.PI * 2 / sampleCount));
                Vector3 samplePos = boss.Position + new Vector3((float)Math.Cos(angle), 0, (float)Math.Sin(angle)) * radius;

                if (!IsPosNearADS(samplePos))
                {
                    float dist = Vector3.Distance(samplePos, DynelManager.LocalPlayer.Position);
                    if (dist < closestDist)
                    {
                        closestDist = dist;
                        bestPos = samplePos;
                        found = true;
                    }
                }
            }

            if (found)
            {
                pos = bestPos;
                return true;
            }

            return false;
        }


        public static BehaviourStatus MoveToLiftBossRoom(BTContext context)
        {
            LiftDirection liftDirection = LiftDirection.Backward;

            if (context.DungeonRunner.Solver.TryGetLift(liftDirection, out Lift lift))
            {
                SetDestinationBossRoom(lift.Position, context);
            }
            else
            {
                return BehaviourStatus.Failed;
            }

            if (DynelManager.LocalPlayer.Position.DistanceFrom(lift.Position) < 1f)
                return BehaviourStatus.Succeeded;

            return BehaviourStatus.Running;
        }

        public static void SetDestinationBossRoom(Vector3 destination, BTContext context)
        {
            if (context.IsPathStale || !SMovementController.IsNavigating())
            {
                var defenseSystems = DynelManager.NPCs.Where(x => x.Name == "Automated Defense System");
                SMovementController.SetNavDestination(destination, defenseSystems, 10, true);

                if (SMovementController.IsNavigating())
                    context.IsPathStale = false;

                context.Logger.Debug($"Setting nav dest {destination}");
            }
        }

        private static bool MissionIsOver(BTContext context)
        {
            if (AIMissionBot.HasAlienMission())
                return false;

            if (AIMissionBot.Config.ClearCoccoons && DynelManager.NPCs.Any(x => x.Name == "Alien Coccoon"))
                return false;

            //if (DynelManager.NPCs.Any(x => x.Name == "Alien Defense System"))
            //    return false;

            return true;
        }

        public static BehaviourStatus WaitForBossSpawn(BTContext context)
        {
            if (DynelManager.NPCs.Any(x => AIMissionBot.Config.Bosses.ContainsKey(x.Name)))
                return BehaviourStatus.Succeeded;

            return BehaviourStatus.Running;
        }

    }
}
