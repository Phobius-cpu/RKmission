using AOSharp.Common.GameData;
using AOSharp.Core;
using AOSharp.Core.Inventory;
using AOSharp.Pathfinding;
using BehaviourTree;
using BehaviourTree.FluentBuilder;
using BTBotBase.BaseBehavior;
using Dungeon.Runner;
using Dungeon.Runner.Behaviors;
using Serilog.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AIMission.Bot.Behaviors
{
    internal class RollBehavior
    {
        internal static readonly Vector3 ShipEntrancePos = new Vector3(64.3, 101.1f, 181.8f);
        internal static readonly Vector3 UnicornRecruiterPos = new Vector3(151.2, 100.9, 164.3);
        internal static bool IsInMission => Playfield.TilemapResourceId == 385;
        private static int PreRollReadyCheckDuration = 120000;

        internal static IBehaviour<BTContext> Compile()
        {
            return FluentBuilder.Create<BTContext>()
                .Selector("Pre Dungeon")
                    .Condition("In Mission", c => IsInMission)
                    .Sequence("Unicorn Outpost")
                        .Condition("In Team", c => Team.IsInTeam)
                        .Subtree(Team.IsLeader ? LeaderUnicornOutpost() : NonLeaderUnicornOutpost())
                    .End()
                .End()
                .Build();
        }

        private static IBehaviour<BTContext> LeaderUnicornOutpost()
        {
            return FluentBuilder.Create<BTContext>()
                .Sequence("Leader")
                    .Do("Ready Check", c => DungeonRunnerBehavior<BTContext>.ReadyCheck(c, PreRollReadyCheckDuration))
                    .Selector("Mission Roller")
                        .Condition("Has Mission", c => FindMission(c))
                        .Subtree(RollMission())
                    .End()
                    .Do("Enter Mission", (c) => BaseBehaviors<BTContext>.MoveTo(c, ShipEntrancePos))
                    .Do("Wait for ship teleport", WaitForEntranceTeleport)
                .End()
                .Build();
        }

        private static IBehaviour<BTContext> NonLeaderUnicornOutpost()
        {
            return FluentBuilder.Create<BTContext>()
                .Sequence("Non-Leader")
                    .Do("Wait for mission", WaitForActiveMission)
                    .Do("Enter Mission", (c) => BaseBehaviors<BTContext>.MoveTo(c, ShipEntrancePos))
                    .Do("Wait for ship teleport", WaitForEntranceTeleport)
                .End()
                .Build();
        }

        private static IBehaviour<BTContext> RollMission()
        {
            return FluentBuilder.Create<BTContext>()
                .Sequence("Roll Mission")
                    .Do("Move to Unicorn Recruiter", (c) => BaseBehaviors<BTContext>.MoveTo(c, UnicornRecruiterPos))
                    .Do("Talk to Recruiter", TalkToRecruiter)
                .End()
                .Build();
        }

        public static BehaviourStatus TalkToRecruiter(BTContext context)
        {
            if (!Team.IsInTeam)
            {
                context.Logger.Information("Stopping due to lack of team.");
                context.DungeonRunner.Stop();
                return BehaviourStatus.Failed;
            }

            if (FindMission(context))
                return BehaviourStatus.Succeeded;

            if (DynelManager.Find(AIMissionBot.RecruiterName, out SimpleChar recruiter))
            {
                context.CurrentDifficulty = AIMissionBot.Config.MissionDifficulty;

                recruiter.Use();
            }
            else
                return BehaviourStatus.Failed;

            if (DynelManager.LocalPlayer.DistanceFrom(recruiter) > 2)
                return BehaviourStatus.Failed;

            return BehaviourStatus.Running;
        }

        public static BehaviourStatus WaitForActiveMission(BTContext context)
        {
            if (context.ActiveAIMission.HasValue)
                return BehaviourStatus.Succeeded;

            return BehaviourStatus.Running;
        }

        public static BehaviourStatus WaitForZoneChange(BTContext context, PlayfieldId pfId)
        {
            if (Playfield.ModelId == pfId)
                return BehaviourStatus.Succeeded;

            return BehaviourStatus.Running;
        }

        public static BehaviourStatus WaitForEntranceTeleport(BTContext context)
        {
            if (!Mission.Find(context.ActiveAIMission.Value, out Mission _))
            {
                context.ActiveAIMission = null;
                return BehaviourStatus.Failed;
            }

            if (Inventory.Find("Mission Key to", out Item item))
                item.UseOn(DynelManager.AllDynels.FirstOrDefault(x => x.Name == "Alien Mothership" && x.Identity.Type == IdentityType.ACGEntrance).Identity);

            //BT will be reset by teleport
            return BehaviourStatus.Running;
        }

        private static bool FindMission(BTContext context)
        {
            bool hasMission = Mission.Find(AIMissionBot.MissionName, out Mission mission);

            if (hasMission)
                context.ActiveAIMission = mission.Identity;

            return hasMission;
        }
    }
}
