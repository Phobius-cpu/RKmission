using AOSharp.Common.GameData;
using AOSharp.Core.Misc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BTBotBase
{
    public interface IBotContext
    {
        void Reset();
    }

    public class BotContext : IBotContext
    {
        public Interval WaitTimer;
        public AutoResetInterval NavUpdateInterval = new AutoResetInterval(500);
        public Vector3 LastNavPos = Vector3.Zero;

        public virtual void Reset()
        {
        }
    }
}
