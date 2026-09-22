using BehaviourTree.FluentBuilder;
using BehaviourTree;
using System;
using System.Collections.Generic;
using System.Linq;
using AOSharp.Core.UI;
using AOSharp.Core;

namespace S13.Bot.Behaviors
{
    internal class CombatBehavior
    {
        internal static IBehaviour<BotContext> Compile()
        {
            return FluentBuilder.Create<BotContext>()
                .Sequence("S13")
                    .Do("Blank", Blank)
                .End()
                .Build();
        }

        public static BehaviourStatus Blank(BotContext context)
        {
            return BehaviourStatus.Succeeded;
        }
    }
}
