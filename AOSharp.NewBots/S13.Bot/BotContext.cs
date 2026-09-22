using AOSharp.Common.GameData;
using AOSharp.Core.Misc;
using BTBotBase;
using org.critterai.nav;
using Serilog.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace S13.Bot
{
    public class BotContext : IBotContext
    {
        public S13Bot S13Bot;
        public Logger Logger;
        public Identity? TargetId;
        public Identity? PullTargetId;
        public bool NeedsToReform = false;
        public double FightStartTime;
        public ManualResetEvent PollingEnded;
        public Interval ReformBuffer;
        public AutoResetInterval NavUpdateInterval = new AutoResetInterval(500);
        public List<Identity> TeamSnapshot;
        public bool InvitesSent = false;

        public BotContext(S13Bot bot)
        {
            S13Bot = bot;
            Logger = bot.Logger;
        }

        public void Reset()
        {
            TargetId = null;
            PullTargetId = null;
            PollingEnded = null;
        }
    }
}
