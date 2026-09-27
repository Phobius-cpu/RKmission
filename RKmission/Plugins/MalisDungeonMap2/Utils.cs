using AOSharp.Common.GameData;
using AOSharp.Core;
using AOSharp.Core.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace MalisDungeonMap2
{
    public class Utils
    {
        [StructLayout(LayoutKind.Explicit, Pack = 0, Size = 0x100)]
        private struct MissionMemStruct
        {
            [FieldOffset(0xB4)]
            public Identity Playfield;
        }

        public static string Vector3ToHex(Vector3 color)
        {
            int r = (int)(color.X * 255);
            int g = (int)(color.Y * 255);
            int b = (int)(color.Z * 255);

            return $"{r:X2}{g:X2}{b:X2}";
        }

        public static Vector3 HexToVector3(string hex)
        {
            if (hex.Length != 6)
                return new Vector3(1,1,1);

            int r = int.Parse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber);
            int g = int.Parse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber);
            int b = int.Parse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber);

            return new Vector3(r / 255f, g / 255f, b / 255f);
        }

        public static unsafe bool GetMissionDynel(out Dynel objectiveDynel)
        {
            objectiveDynel = null;
            Mission mission = Mission.List.FirstOrDefault(x => (*(MissionMemStruct*)x.Pointer).Playfield == Playfield.ModelIdentity);

            if (mission == null)
                return false;

            List<Dynel> allDynels = new List<Dynel>();
            Identity target = new Identity();

            foreach (MissionAction missionAction in mission.Actions)
            {
                switch (missionAction.Type)
                {
                    case MissionActionType.FindPerson:
                        target = ((FindPersonAction)missionAction).Target;
                        break;
                    case MissionActionType.FindItem:
                        target = ((FindItemAction)missionAction).Target;
                        break;
                    case MissionActionType.UseItemOnItem:
                        if (((UseItemOnItemAction)missionAction).Source != null)
                            target = ((UseItemOnItemAction)missionAction).Source;
                        else if (((UseItemOnItemAction)missionAction).Destination != null)
                            target = ((UseItemOnItemAction)missionAction).Destination;
                        break;
                    case MissionActionType.KillPerson:
                        target = ((KillPersonAction)missionAction).Target;
                        break;
                }

                allDynels.Add(DynelManager.AllDynels.FirstOrDefault(x => x.Identity == target));
            }

            objectiveDynel = allDynels.FirstOrDefault(x => x != null);

            return objectiveDynel != null;
        }
    }
}