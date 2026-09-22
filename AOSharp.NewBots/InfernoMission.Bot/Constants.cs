using AOSharp.Common.GameData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InfernoMission.Bot
{
    internal static class Constants
    {
        public const string SpiritNPCName = "Guardian Spirit of Purification";
        public const string QuestStarterName = "One Who Obeys Precepts";
        public const string QuestGiverName = "The Retainer Of Ergo";
        public static string[] IgnoredNames = new string[] {
            "Guardian Spirit of Purification",
            "One Who Obeys Precepts"
        };
        public static Vector3[] DefendPositions = new Vector3[]
        {
             new Vector3(214.8f, 2.3f, 142.1f),
             new Vector3(165.7f, 2.2f, 186.4f),
             new Vector3(221.1f, 2.2f, 234.6f),
        };
        public static Vector3 WallBugFixSpot = new Vector3(2724.3f, 24.6f, 3325.6f);
        public static Vector3 ErgoVicinity = new Vector3(2791, 24.6f, 3360);
        public static Vector3 MissionCenter = new Vector3(192, 1.7f, 196.5f);
        public static Vector3 EntrancePos = new Vector3(2727f, 25.9f, 3340f);
        public static Vector3 EntranceFinalPos = new Vector3(2729.4f, 25.5f, 3345.2f);
        public static Vector3 ExitPos = new Vector3(158.9f, 2.5f, 100.9f);
        public static Vector3 ExitFinalPos = new Vector3(155.5f, 2.3f, 95.5f);
        public static Vector3 QuestGiverPos = new Vector3(2806.5f, 25.5f, 3377.0f);
        public static Vector3 QuestStarterPos = new Vector3(185.4f, 1, 163.3f);
        public static Vector3 LeechSpot = new Vector3(155.5f, 2.7f, 99.6f);
        public static Vector3 LeechMissionExit = new Vector3(161.2f, 2.7f, 104.2f);
        public const int InfernoId = 4605;
        public const int NewInfMissionId = 9042;
    }
}
