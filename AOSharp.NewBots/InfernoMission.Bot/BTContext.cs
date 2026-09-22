using AOSharp.Common.GameData;
using AOSharp.Core;
using AOSharp.Core.IPC;
using AOSharp.Core.Misc;
using AOSharp.Core.UI;
using BTBotBase;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace InfernoMission.Bot
{
    public class BTContext : BotContext
    {
        public MissionDifficulty MissionDifficulty;
        public Side MissionSide;

        public int Wave = 0;
        public int Spirit = 0;
        public int KillsThisWave = 0;
        public int Segment = 0;
        public string SegmentKey = string.Empty;

        public double MissionStartTime = 0;
        public double WaveStartTime = 0;
        public double LastSpiritChange = 0;

        public int YuttoId;
        public Identity? TargetId;

        public List<Identity> KilledWaveMobs = new List<Identity>();
        public List<Identity> TeamSnapshot;

        public bool MissionsLoaded = false;
        public bool IsTeamStale = false;
        public bool InvitesSent = false;

        public Interval ReformBuffer;
        public Interval NPCInteractRetryInterval;

        public InfernoMissionBot Bot;

        public BTContext(InfernoMissionBot bot)
        {
            Bot = bot;
        }

        public override void Reset()
        {
            Wave = 0;
            Segment = 0;
            SegmentKey = string.Empty;
            Spirit = 0;
            WaveStartTime = 0;
            KillsThisWave = 0;
            KilledWaveMobs = new List<Identity>();
            YuttoId = 0;
            LastSpiritChange = 0;
            MissionsLoaded = false;
            TargetId = null;
            ReformBuffer = null;
            NPCInteractRetryInterval = null;

            base.Reset();
        }

        public bool IsWaveMob(SimpleChar mob)
        {
            if (YuttoId == 0)
                return false;

            if (mob.IsPet)
                return false;

            if (Constants.IgnoredNames.Contains(mob.Name))
                return false;

            if (mob.Identity.Instance < YuttoId && mob.Identity.Instance > YuttoId - 10)
                return false;

            return true;
        }

        public Wave GetCurrentWave()
        {
            var waves = GetSegmentWaves();
            if (waves == null)
                return null;

            return waves[Wave];
        }

        public List<Wave> GetSegmentWaves()
        {
            if (SegmentKey == string.Empty)
                return null;

            return Bot.Waves[SegmentKey];
        }
    }
}
