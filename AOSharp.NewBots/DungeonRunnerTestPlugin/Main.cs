using AOSharp.Core;
using BehaviourTree;
using BehaviourTree.FluentBuilder;
using Dungeon.Runner;
using Dungeon.Runner.Behaviors;
using Dungeon.Solver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DungeonRunnerTestPlugin
{
    public class Main : DungeonRunner<BTContext>
    {
        public override SolverMode SolverMode => SolverMode.Blitz;
        
        //protected override IBehaviour<BTContext> BossRoomTree()
        //{
        //    return ShipBehavior.BossRoom();
        //}
    }

    public static class ShipBehavior
    {
        private static Corpse BossCorpse => DynelManager.Corpses.FirstOrDefault(x => x.Name.StartsWith("Remains of General - "));

        public static IBehaviour<BTContext> BossRoom()
        {
            return FluentBuilder.Create<BTContext>()
                .Sequence("ShipBossRoom")
                    .UntilSuccess("Fight Until Room Clear")
                        .Selector("Fight or Check")
                            .Condition("Is room clear?", c => IsRoomClear())
                            .Sequence("Try Fight")
                                .Do("Poll target", c => DungeonRunnerBehavior<BTContext>.PollTarget(c, DynelManager.LocalPlayer.Room))
                                .Subtree(DungeonRunnerBehavior<BTContext>.Fight())
                                .Condition("Is room clear?", c => IsRoomClear())
                            .End()
                        .End()
                    .End()
                    .Subtree(LootBoss())
                    .Do("Exit Dungeon", ExitDungeon)
                .End()
                .Build();
        }

        public static IBehaviour<BTContext> LootBoss()
        {
            return FluentBuilder.Create<BTContext>()
                .Sequence("LootBoss")
                    .Do("Wait For Boss Corpse", WaitForBossCorpse)
                    .Do("Move to boss", MoveToBossCorpse)
                    .Do("Open Corpse", OpenBossCorpse)
                .End()
                .Build();
        }

        public static bool IsRoomClear()
        {
            int bossRoomInst = DynelManager.LocalPlayer.Room.Instance;

            return !DynelManager.NPCs.Where(x => !x.IsPet && x.IsAlive && x.Room.Instance == bossRoomInst).Any();
        }

        public static BehaviourStatus ExitDungeon(BTContext context)
        {
            context.ExitingDungeon = true;
            return BehaviourStatus.Succeeded;
        }

        public static BehaviourStatus WaitForBossCorpse(BTContext context)
        {
            if (BossCorpse == null)
                return BehaviourStatus.Running;

            return BehaviourStatus.Succeeded;
        }

        public static BehaviourStatus MoveToBossCorpse(BTContext context)
        {
            Corpse corpse = BossCorpse;

            if (corpse == null)
                return BehaviourStatus.Succeeded;

            if (DynelManager.LocalPlayer.Position.DistanceFrom(corpse.Position) < 1f)
                return BehaviourStatus.Succeeded;

            DungeonRunnerBehavior<BTContext>.SetDestination(corpse.Position, context);

            return BehaviourStatus.Running;
        }

        public static BehaviourStatus OpenBossCorpse(BTContext context)
        {
            Corpse corpse = BossCorpse;

            if (corpse == null)
                return BehaviourStatus.Succeeded;

            corpse.Use();

            return BehaviourStatus.Succeeded;
        }
    }

    public class BTContext : DungeonRunnerContext<BTContext>
    {
        public BTContext(DungeonRunner<BTContext> bot) : base(bot)
        {
        }
    }
}
