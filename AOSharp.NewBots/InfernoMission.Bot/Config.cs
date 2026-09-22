using AOSharp.Common.GameData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InfernoMission.Bot
{
    public class Config
    {
        public MissionDifficulty MissionDifficulty = MissionDifficulty.Easy;
        public Side MissionSide = Side.Neutral;
        public float MaxFightDistance = 8f;
        public float LeaderFollowDistance = 5f;
        public int PreInviteBuffer = 10;
        public int NPCInteractRetryInterval = 10;
        public int SitKitHealthPercThreshold = 85;
        public int SitKitNanoPercThreshold = 50;
    }
}
