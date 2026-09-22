using AOSharp.Common.GameData;
using AOSharp.Core;
using AOSharp.Core.Misc;
using AOSharp.Core.Movement;
using AOSharp.Pathfinding;
using BehaviourTree;
using BehaviourTree.FluentBuilder;
using BTBotBase.BaseBehavior;
using Serilog.Core;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Remoting.Contexts;
using System.Text;
using System.Threading.Tasks;

namespace InfernoMission.Bot
{
    public static class Behaviors
    {
        private static Mission PurificationRitualMission => Mission.List.FirstOrDefault(x => x.DisplayName.StartsWith("The Purification Ritual - "));

        public static IBehaviour<BTContext> Root()
        {
            return FluentBuilder.Create<BTContext>()
                .Selector("Root")
                    .Subtree(ErgoArea())
                    .Subtree(MissionArea())
                .End()
                .Build();
        }

        public static IBehaviour<BTContext> MissionArea()
        {
            return FluentBuilder.Create<BTContext>()
                .Sequence("Mission Area")
                    .Condition("In Mission Area", c => Playfield.ModelId == PlayfieldId.XanReliquary)
                    .Selector("Mission Checker")
                        .Sequence("")
                            .Selector("Ensure missions have loaded")
                                .Condition("Missions Loaded", c => c.MissionsLoaded)
                                .Do("Wait for missions to load", WaitForMissionsToLoad)
                            .End()
                            .Condition("Has Mission", HasMission)
                        .End()
                        .Sequence("Leave Mission")
                            .Selector("")
                                .Condition("Not Leader", c => !c.Bot.IsLeader)
                                .Do("Disband", DisbandTeam)
                            .End()
                            .Do("Wait for zone", c => BaseBehaviors<BTContext>.WaitForZoneChange(c, PlayfieldId.Inferno))
                        .End()
                    .End()
                    .Selector("Start")
                        .Condition("Is not leader", c => !Team.IsLeader)
                        .Condition("Mission Started", c => IsMissionStarted(c))
                        .Subtree(StartMission())
                    .End()
                    .Selector("Defend")
                        .Subtree(SitKit())
                        .Sequence("Fight")
                            .Condition("Has Target", c => c.TargetId.HasValue)
                            .Do("Move To Attack Pos", MoveToAttackPos)
                            .Do("Fight Target", FightTarget)
                        .End()
                        .Do("Find Target", FindTarget)
                        .Selector("")
                            .Sequence("")
                                .Condition("Wave Segment Ended", c => c.SegmentKey == string.Empty)
                                .Do("Move to center", c => BaseBehaviors<BTContext>.MoveTo(c, Constants.MissionCenter))
                                .Do("Always Succeed", c => BehaviourStatus.Succeeded)
                            .End()
                            .Selector("")
                                .Sequence("Follower")
                                    .Condition("Is follower", c => !c.Bot.IsLeader)
                                    .Do("Follow leader", MoveToLeader)
                                .End()
                                .Sequence("")
                                    .Selector("Go to Spirit Area")
                                        //.Do("Debug Spirit Area", c => Debug(c, (ctx) => $"SegmentKey is {c.SegmentKey}!", BehaviourStatus.Failed))
                                        .Condition("Spirit Area", c => Vector3.Distance(DynelManager.LocalPlayer.Position, GetSpiritPos(c)) < 20f)
                                        .Sequence("Move")
                                            .Do("Move to spirit", c => BaseBehaviors<BTContext>.MoveTo(c, GetSpiritPos(c), interruptFunc: () => FindTarget(c) == BehaviourStatus.Succeeded))
                                            .Do("Set Context", (c) => {
                                                c.LastSpiritChange = Time.NormalTime;
                                                return BehaviourStatus.Succeeded;
                                            })
                                        .End()
                                    .End()
                                    .Do("Pick Spirit", PickSpirit)
                                .End()
                            .End()
                        .End()
                    .End()
                .End()
                .Build();
        }

        public static IBehaviour<BTContext> ErgoArea()
        {
            return FluentBuilder.Create<BTContext>()
                .Sequence("Ergo Area")
                    .Condition("In Ergo Area", c => Playfield.ModelId == PlayfieldId.Inferno)
                    .Do("Stuck Fix", StuckFix) // Temp
                    .Selector("Reformer")
                        .Condition("Is team not stale", c => !c.IsTeamStale)
                        .Selector("Reform")
                            .Sequence("Wait or reform")
                                .Condition("Is team leader", c => c.Bot.IsLeader)
                                .Subtree(Reform())
                            .End()
                            .Do("Wait for team", WaitForTeam)
                        .End()
                    .End()
                    .Do("Lock Mission Selection", LockMissionSelection)
                    .Selector("Mission Roller")
                        .Selector("Got Mission?")
                            .Condition("Has Mission", c => HasMission(c))
                            .Sequence("Delete Extra Missions")
                                .Condition("Has Wrong Mission", c => PurificationRitualMission != null)
                                .Do("Delete Wrong Mission", DeleteWrongMission) //Always fails
                            .End()
                        .End()
                        .Subtree(GrabMission())
                    .End()
                    .Do("Enter Mission", c => BaseBehaviors<BTContext>.MoveTo(c, Constants.EntrancePos, stopDist: 0, halt:false))
                    .Do("Wait for zone", c => BaseBehaviors<BTContext>.WaitForZoneChange(c, PlayfieldId.XanReliquary))
                .End()
                .Build();
        }

        public static IBehaviour<BTContext> GrabMission()
        {
            return FluentBuilder.Create<BTContext>()
                .Sequence("Grab Mission")
                    .Do("Move to Retainer", (c) => BaseBehaviors<BTContext>.MoveTo(c, Constants.QuestGiverPos))
                    .Do("Talk to Retainer", TalkToMissionGiver)
                .End()
                .Build();
        }

        public static IBehaviour<BTContext> StartMission()
        {
            return FluentBuilder.Create<BTContext>()
                .Sequence("Start Mission")
                    .Do("Move to Prophet", (c) => BaseBehaviors<BTContext>.MoveTo(c, Constants.QuestStarterPos))
                    .Do("Talk to Prophet", TalkToMissionStarter)
                .End()
                .Build();
        }


        public static IBehaviour<BTContext> Reform()
        {
            return FluentBuilder.Create<BTContext>()
                .Sequence("Reform")
                    .Do("Wait (Pre-Invite Buffer)", WaitPreInviteBuffer)
                    .Do("Invite Players", InvitePlayers)
                .End()
                .Build();
        }

        public static IBehaviour<BTContext> SitKit()
        {
            return FluentBuilder.Create<BTContext>()
                .Sequence("SitKit")
                    .Condition("Can SitKit", c => CanSitKit())
                    .Condition("Needs to kit", c => NeedsToKit(c))
                    .Do("Sit", c => BaseBehaviors<BTContext>.SimpleAction(c, () => SMovementController.SetMovement(MovementAction.SwitchToSit)))
                    .Do("Pause", c => BaseBehaviors<BTContext>.Wait(c, 1000, () => !NeedsToKit(c)))
                    .Do("Stand", c => BaseBehaviors<BTContext>.SimpleAction(c, () => SMovementController.SetMovement(MovementAction.LeaveSit)))
                .End()
                .Build();
        }

        public static BehaviourStatus DisbandTeam(BTContext context)
        {
            context.TeamSnapshot = Team.Members.Select(x => x.Identity).Where(x => x != DynelManager.LocalPlayer.Identity).ToList();
            Team.Disband();

            context.IsTeamStale = true;

            return BehaviourStatus.Succeeded;
        }

        public static BehaviourStatus InvitePlayers(BTContext context)
        {
            if (!context.InvitesSent)
            {
                foreach (var teamMember in context.TeamSnapshot.Except(Team.Members.Select(x => x.Identity)))
                {
                    context.Bot.Logger.Information($"Inviting late {teamMember}");
                    Team.Invite(teamMember);
                }

                context.InvitesSent = true;
            }

            if (!Team.IsInTeam)
                return BehaviourStatus.Running;

            context.IsTeamStale = false;
            context.InvitesSent = false;
            return BehaviourStatus.Succeeded;
        }

        public static BehaviourStatus WaitPreInviteBuffer(BTContext context)
        {
            return BaseBehaviors<BTContext>.Wait(context, context.Bot.Config.PreInviteBuffer * 1000, () => !context.TeamSnapshot.Except(Team.Members.Select(x => x.Identity)).Any());
        }

        private static bool HasMission(BTContext context)
        {
            return Mission.Find(context.Bot.GetMissionName(context.MissionDifficulty, context.MissionSide), out _);
        }

        private static BehaviourStatus LockMissionSelection(BTContext context)
        {
            context.MissionDifficulty = context.Bot.Config.MissionDifficulty;
            context.MissionSide = context.Bot.Config.MissionSide;
            return BehaviourStatus.Succeeded;
        }

        private static BehaviourStatus DeleteWrongMission(BTContext context)
        {
            context.Bot.Logger.Debug($"Deleting mission: {PurificationRitualMission.DisplayName}");
            PurificationRitualMission.Delete();

            //Always fail because having extra missions is bad
            return BehaviourStatus.Failed;
        }

        public static BehaviourStatus TalkToMissionGiver(BTContext context)
        {
            if (!Team.IsInTeam)
            {
                context.Bot.Logger.Error("Stopping due to lack of team.");
                context.Bot.Stop();
                return BehaviourStatus.Failed;
            }

            if (HasMission(context))
                return BehaviourStatus.Succeeded;

            if (context.NPCInteractRetryInterval == null || context.NPCInteractRetryInterval.Elapsed)
            {
                SimpleChar questGiver = DynelManager.NPCs.FirstOrDefault(x => x.Name == Constants.QuestGiverName);
                if (questGiver == null)
                    return BehaviourStatus.Failed;


                if (DynelManager.LocalPlayer.DistanceFrom(questGiver) > 4)
                        return BehaviourStatus.Failed;

                //if (context.NPCInteractRetryInterval != null)
                //{
                //    //Put this in the core
                //    Network.Send(new KnuBotCloseChatWindowMessage
                //    {
                //        Unknown1 = 2,
                //        Target = questGiver.Identity
                //    });
                //}

                questGiver.Use();
                context.NPCInteractRetryInterval = new Interval(context.Bot.Config.NPCInteractRetryInterval * 1000);
            }

            return BehaviourStatus.Running;
        }

        public static BehaviourStatus TalkToMissionStarter(BTContext context)
        {
            if (IsMissionStarted(context))
            {
                context.SegmentKey = "Somphos Logee";
                context.MissionStartTime = Time.NormalTime;
                context.Bot.SyncContext();
                return BehaviourStatus.Succeeded;
            }

            if (context.NPCInteractRetryInterval == null || context.NPCInteractRetryInterval.Elapsed)
            {
                SimpleChar questStarter = DynelManager.NPCs.FirstOrDefault(x => x.Name == Constants.QuestStarterName);
                if (questStarter == null)
                    return BehaviourStatus.Failed;


                if (DynelManager.LocalPlayer.DistanceFrom(questStarter) > 4)
                    return BehaviourStatus.Failed;

                questStarter.Use();
                context.YuttoId = questStarter.Identity.Instance;
                context.NPCInteractRetryInterval = new Interval(context.Bot.Config.NPCInteractRetryInterval * 1000);
            }

            return BehaviourStatus.Running;
        }

        public static BehaviourStatus FindTarget(BTContext context)
        {
            if (!Team.IsLeader && DynelManager.Find(context.Bot.Leader, out SimpleChar leader) && leader.FightingTarget != null)
            {
                context.TargetId = leader.FightingTarget.Identity;
                return BehaviourStatus.Succeeded;
            } 
            else
            { 
                var target = DynelManager.NPCs.Where(c => c.IsAlive &&
                                                          !c.IsPet &&
                                                          !Constants.IgnoredNames.Contains(c.Name) &&
                                                          (c.FightingTarget != null || context.IsWaveMob(c)))
                            .OrderBy(c => DynelManager.LocalPlayer.DistanceFrom(c))
                            .FirstOrDefault();

                if (target == null)
                    return BehaviourStatus.Failed;

                context.TargetId = target.Identity;

                return BehaviourStatus.Succeeded;
            }
        }

        public static BehaviourStatus MoveToAttackPos(BTContext context)
        {
            if (!DynelManager.Find(context.TargetId.Value, out SimpleChar target))
            {
                context.TargetId = null;
                return BehaviourStatus.Failed;
            }

            if (target.IsInLineOfSight && target.IsInAttackRange(true) && DynelManager.LocalPlayer.DistanceFrom(target) < context.Bot.Config.MaxFightDistance)
            {
                if (SMovementController.IsNavigating())
                    SMovementController.Halt();
                return BehaviourStatus.Succeeded;
            }

            return BaseBehaviors<BTContext>.MoveTo(context, target.Position);
        }

        public static BehaviourStatus MoveToLeader(BTContext context)
        {
            if (!DynelManager.Find(context.Bot.Leader, out SimpleChar leader))
                return BehaviourStatus.Failed;

            if (DynelManager.LocalPlayer.DistanceFrom(leader) < context.Bot.Config.LeaderFollowDistance)
            {
                if (SMovementController.IsNavigating())
                    SMovementController.Halt();
                return BehaviourStatus.Succeeded;
            }

            return BaseBehaviors<BTContext>.MoveTo(context, leader.Position);
        }

        public static BehaviourStatus FightTarget(BTContext context)
        {
            if (!context.TargetId.HasValue || !DynelManager.Find(context.TargetId.Value, out SimpleChar target))
            {
                context.TargetId = null;
                return BehaviourStatus.Failed;
            }

            if (!Team.IsLeader && DynelManager.Find(context.Bot.Leader, out SimpleChar leader))
            {
                if (leader.FightingTarget != null && leader.FightingTarget.Identity != context.TargetId)
                {
                    context.TargetId = leader.FightingTarget.Identity;
                    return BehaviourStatus.Running;
                }
            }

            if (!target.IsInLineOfSight)
                return BehaviourStatus.Failed;

            if (!target.IsInAttackRange(true))
                return BehaviourStatus.Failed;

            if (!target.IsAlive)
            {
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

        public static BehaviourStatus PickSpirit(BTContext context)
        {
            if (Time.NormalTime < context.WaveStartTime + 10)
                return BehaviourStatus.Succeeded;

            if (Time.NormalTime > context.LastSpiritChange + 5)
            {
                context.Spirit = (++context.Spirit) % context.GetCurrentWave().Spirits.Length;
                context.LastSpiritChange = Time.NormalTime;
            }

            return BehaviourStatus.Succeeded;
        }

        public static BehaviourStatus WaitForMissionsToLoad(BTContext context)
        {
            if (Mission.List.Any())
            {
                context.MissionsLoaded = true;
                return BehaviourStatus.Succeeded;
            }

            return BehaviourStatus.Running;
        }
        public static BehaviourStatus WaitForTeam(BTContext context)
        {
            if (Team.IsInTeam)
            {
                context.IsTeamStale = false;
                return BehaviourStatus.Succeeded;
            }

            return BehaviourStatus.Running;
        }

        public static BehaviourStatus StuckFix(BTContext context)
        {
            if (Vector3.Distance(DynelManager.LocalPlayer.Position, new Vector3(2729.3f, 25.4f, 3339.5f)) < 2f)
                DynelManager.LocalPlayer.Position = new Vector3(2730.8f, 24.6f, 3331.7f);

            return BehaviourStatus.Succeeded;
        }

        public static Vector3 GetSpiritPos(BTContext context)
        {
            if (context.GetCurrentWave() == null)
                context.Bot.Logger.Error("GetSpiritPos called with null current wave!");

            return Constants.DefendPositions[context.GetCurrentWave().Spirits[context.Spirit]];
        }

        private static bool IsMissionStarted(BTContext context)
        {
            return (context.YuttoId != 0 && !DynelManager.NPCs.Any(x => x.Name == Constants.QuestStarterName)) || DynelManager.NPCs.Any(x => x.Name == Constants.SpiritNPCName);
        }

        private static bool IsTeamInFight()
        {
            if (DynelManager.LocalPlayer.IsAttacking)
                return true;

            if (!Team.IsInTeam)
                return false;

            var teamMembers = Team.Members.Where(x => x.Character != null).Select(x => x.Character);

            if (teamMembers.Any(x => x.FightingTarget != null))
                return true;

            if (DynelManager.Characters.Any(x => x.FightingTarget != null && Team.Members.Select(t => t.Identity).Contains(x.FightingTarget.Identity)))
                return true;

            //TODO: Check pets

            return false;
        }

        public static bool NeedsToKit(BTContext context)
        {
            return DynelManager.LocalPlayer.HealthPercent < context.Bot.Config.SitKitHealthPercThreshold ||
                    DynelManager.LocalPlayer.NanoPercent < context.Bot.Config.SitKitNanoPercThreshold;
        }

        public static bool CanSitKit()
        {
            return !IsTeamInFight() && !DynelManager.LocalPlayer.Cooldowns.ContainsKey(Stat.Treatment);
        }
    }
}
