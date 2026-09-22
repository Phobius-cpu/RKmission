using AOSharp.Common.GameData;
using AOSharp.Core;
using AOSharp.Core.Misc;
using AOSharp.Core.Movement;
using AOSharp.Core.UI;
using AOSharp.Pathfinding;
using BehaviourTree;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Remoting.Contexts;
using System.Text;
using System.Threading.Tasks;

namespace BTBotBase.BaseBehavior
{
    public static class BaseBehaviors<TContext> where TContext : IBotContext
    {
        public static BehaviourStatus MoveTo(BotContext context, Vector3 pos, float stopDist = 1f, bool halt = true, Func<bool> interruptFunc = null)
        {
            if (interruptFunc != null && interruptFunc())
                return BehaviourStatus.Failed;

            if (DynelManager.LocalPlayer.Position.DistanceFrom(pos) < stopDist)
            {
                if (halt && SMovementController.IsNavigating())
                    SMovementController.Halt();

                return BehaviourStatus.Succeeded;
            }

            if (context.LastNavPos == pos || !SMovementController.IsNavigating() || context.NavUpdateInterval.Elapsed)
            {
                if (!SMovementController.NavAgent.HasPathfinder)
                    SMovementController.SetDestination(pos);
                else
                    SMovementController.SetNavDestination(pos);

                context.LastNavPos = pos;
            }

            return BehaviourStatus.Running;
        }

        public static BehaviourStatus WaitForZoneChange(TContext context, PlayfieldId pfId)
        {
            if (Playfield.ModelId == pfId)
                return BehaviourStatus.Succeeded;

            return BehaviourStatus.Running;
        }

        public static BehaviourStatus SimpleAction(TContext context, Action action)
        {
            action();
            return BehaviourStatus.Succeeded;
        }

        public static BehaviourStatus Wait(BotContext context, int duration, Func<bool> interruptFunc = null)
        {
            if (context.WaitTimer == null)
            {
                context.WaitTimer = new Interval(duration);
                return BehaviourStatus.Running;
            }
            else if (context.WaitTimer.Elapsed || (interruptFunc != null && interruptFunc()))
            {
                context.WaitTimer = null;
                return BehaviourStatus.Succeeded;
            }

            return BehaviourStatus.Running;
        }

        public static BehaviourStatus Debug(TContext context, Func<TContext, string> messageResolver, BehaviourStatus result, Func<bool> condition = null)
        {
            if (condition == null || condition())
                Chat.WriteLine(messageResolver(context), ChatColor.Green);

            return result;
        }
    }
}
