using AOSharp.Common.GameData;
using AOSharp.Core;
using AOSharp.Core.IPC;
using Dungeon.Runner;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AIMission.Bot
{
    public class BTContext : DungeonRunnerContext<BTContext>
    {
        public AIMissionBot Bot;
        public Identity? ActiveAIMission;
        public MissionDifficulty CurrentDifficulty = MissionDifficulty.Easy;

        public BTContext(AIMissionBot bot) : base(bot)
        {
            Bot = bot;
        }
    }
}
