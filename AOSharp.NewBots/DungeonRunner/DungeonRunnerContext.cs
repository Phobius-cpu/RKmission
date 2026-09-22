using AOSharp.Common.GameData;
using AOSharp.Core;
using AOSharp.Core.Misc;
using BTBotBase;
using Dungeon.Solver;
using Serilog.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Dungeon.Runner
{
    public class DungeonRunnerContext<TContext> : BotContext where TContext : DungeonRunnerContext<TContext>
    {
        public DungeonRunner<TContext> DungeonRunner;
        public Logger Logger;
        public Identity? TargetId;
        public ManualResetEvent TargetPollEnded;
        public bool TeamReady = false;
        public ManualResetEvent ReadyPollEnded;
        public ManualResetEvent FloorChanged;
        public AutoResetInterval LiftRetryInterval = new AutoResetInterval(1000);
        public bool IsPathStale = true;
        public bool ExitingDungeon = false;
        public LeaderMovement? LeaderMovement;
        public Mission ActiveMission;

        public DungeonRunnerContext(DungeonRunner<TContext> bot)
        {
            DungeonRunner = bot;
            Logger = bot.Logger;
        }

        public override void Reset()
        {
            TeamReady = false;
            TargetId = null;
            TargetPollEnded = null;
            ReadyPollEnded = null;
            IsPathStale = false;
            ExitingDungeon = false;
            ActiveMission = null;

            base.Reset();
        }
    }

    public struct LeaderMovement
    {
        public int Floor;
        public Vector3 Destination;
    }
}
