using BehaviourTree.FluentBuilder;
using BehaviourTree;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AOSharp.Common.GameData;
using AOSharp.Core.UI;
using AOSharp.Core;
using AOSharp.Core.Movement;

namespace S13.Bot.Behaviors
{
    internal static class HubBehavior
    {
        internal static Vector3 S13EntrancePos = new Vector3(145.7f, 0.5f, 207.1f);

        internal static IBehaviour<BotContext> Compile()
        {
            return FluentBuilder.Create<BotContext>()
                .Sequence("Hub")
                    .Condition("Is In HUB", c => Playfield.ModelId == PlayfieldId.APFHub)
                    .Sequence("Hub Activities")
                        .Do("Convert To Raid", ConvertToRaid)
                        .Do("Move To S13", MoveToS13)
                    .End()
                .End()
                .Build();
        }

        internal static BehaviourStatus Rezz(BotContext context)
        {
            if (DynelManager.LocalPlayer.GetStat(Stat.TemporarySkillReduction) == 0)
            {
                if (DynelManager.LocalPlayer.MovementState == MovementState.Sit)
                    MovementController.Instance.SetMovement(MovementAction.LeaveSit);

                return BehaviourStatus.Succeeded;
            }

            if (DynelManager.LocalPlayer.MovementState != MovementState.Sit)
                MovementController.Instance.SetMovement(MovementAction.SwitchToSit);

            return BehaviourStatus.Running;
        }

        public static BehaviourStatus ConvertToRaid(BotContext context)
        {
            if (!Team.IsInTeam)
                return BehaviourStatus.Failed;

            if (Team.IsLeader && !Team.IsRaid)
                Team.ConvertToRaid();

            return BehaviourStatus.Succeeded;
        }

        public static BehaviourStatus MoveToS13(BotContext context)
        {
            if (Playfield.ModelId == PlayfieldId.Sector13)
                return BehaviourStatus.Succeeded;

            if (Playfield.ModelId != PlayfieldId.APFHub)
            {
                context.S13Bot.Stop();
                context.Logger.Information("Expected to be in APF Hub. Cannot continue.");

                return BehaviourStatus.Failed;
            }

            if (MovementController.Instance.IsNavigating)
                return BehaviourStatus.Running;

            if (Vector3.Distance(DynelManager.LocalPlayer.Position, S13EntrancePos) < 1f)
                return BehaviourStatus.Running;

            context.S13Bot.NavMovementController.SetNavMeshDestination(S13EntrancePos);

            return BehaviourStatus.Running;
        }
    }
}
