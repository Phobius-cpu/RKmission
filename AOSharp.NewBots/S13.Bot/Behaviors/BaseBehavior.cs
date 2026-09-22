using BehaviourTree.FluentBuilder;
using BehaviourTree;
using System;
using System.Collections.Generic;
using System.Linq;
using AOSharp.Common.GameData;
using AOSharp.Core.Movement;
using AOSharp.Core;
using AOSharp.Core.Misc;
using AOSharp.Core.UI;

namespace S13.Bot.Behaviors
{
    internal static class BaseBehavior
    {
        internal static IBehaviour<BotContext> Compile()
        {
            var builder = FluentBuilder.Create<BotContext>().Selector("Root");

            if (Team.IsLeader)
                builder = builder.Subtree(Reform());

            builder.Do("Rested", Rest)
                .Selector("Playfield Selector")
                    .Subtree(HubBehavior.Compile())
                    .Subtree(S13Behavior.Compile())
                .End()
            .End();

            return builder.Build();
        }

        public static BehaviourStatus Rest(BotContext context)
        {
            if (context.S13Bot.FightableAliens || context.S13Bot.IsRested())
            {
                if (DynelManager.LocalPlayer.MovementState == MovementState.Sit)
                    MovementController.Instance.SetMovement(MovementAction.LeaveSit);

                return BehaviourStatus.Failed;
            }

            if (DynelManager.LocalPlayer.MovementState != MovementState.Sit)
                MovementController.Instance.SetMovement(MovementAction.SwitchToSit);

            return BehaviourStatus.Running;
        }

        public static IBehaviour<BotContext> Reform()
        {
            return FluentBuilder.Create<BotContext>()
                .Sequence("Reformer")
                    .Condition("Needs to reform", c => c.NeedsToReform)
                    .Selector("PF Selector")
                        .Sequence("S13")
                            .Condition("In S13", c => Playfield.ModelId == PlayfieldId.Sector13)
                            .Do("Disband Team", DisbandTeam)
                            .Do("Wait for zone kick", WaitForZoneKick)
                        .End()
                        .Sequence("Hub")
                            .Condition("In Hub", c => Playfield.ModelId == PlayfieldId.APFHub)
                            .Do("Wait (Pre-Invite Buffer)", WaitPreInviteBuffer)
                            .Do("Invite Players", InvitePlayers)
                            //.Do("Wait (Post-Invite Buffer)", WaitPostInviteBuffer)
                        .End()
                    .End()
                .End()
                .Build();
        }

        public static BehaviourStatus DisbandTeam(BotContext context)
        {
            context.TeamSnapshot = Team.Members.Select(x => x.Identity).Where(x => x != DynelManager.LocalPlayer.Identity).ToList();
            Team.Disband();

            return BehaviourStatus.Succeeded;
        }

        public static BehaviourStatus WaitForZoneKick(BotContext context)
        {
            if (Playfield.ModelId == PlayfieldId.APFHub)
                return BehaviourStatus.Succeeded;

            return BehaviourStatus.Running;
        }

        public static BehaviourStatus WaitPreInviteBuffer(BotContext context)
        {
            if (context.ReformBuffer == null)
            {
                context.ReformBuffer = new Interval(context.S13Bot.ActiveSettings.PreInviteBuffer * 1000);
                return BehaviourStatus.Running;
            }
            else if(context.ReformBuffer.Elapsed || !context.TeamSnapshot.Except(Team.Members.Select(x => x.Identity)).Any())
            {
                context.ReformBuffer = null;
                return BehaviourStatus.Succeeded;
            }

            return BehaviourStatus.Running;
        }

        public static BehaviourStatus InvitePlayers(BotContext context)
        {
            if (!context.InvitesSent)
            {
                foreach (var teamMember in context.TeamSnapshot.Except(Team.Members.Select(x => x.Identity)))
                {
                    context.Logger.Information($"Inviting late {teamMember}");
                    Team.Invite(teamMember);
                }

                context.InvitesSent = true;
            }

            if (!Team.IsInTeam)
                return BehaviourStatus.Running;

            context.NeedsToReform = false;
            context.InvitesSent = false;
            return BehaviourStatus.Succeeded;
        }
    }
}
