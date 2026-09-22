using AOSharp.Common.GameData;
using AOSharp.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Remoting.Contexts;
using System.Text;
using System.Threading.Tasks;

namespace Dungeon.Runner
{
    internal static class Extensions
    {
        internal static Dynel GetMissionTarget(this Mission mission)
        {
            Dynel target = null;

            if (!mission.Actions.Any())
                return target;

            MissionAction action = mission.Actions.First();

            if (action is FindItemAction findItemAction)
                target = DynelManager.GetDynel(findItemAction.Target);
            else if (action is FindPersonAction findPersonAction)
                target = DynelManager.GetDynel(findPersonAction.Target);
            else if (action is UseItemOnItemAction useItemOnItemAction)
                target = DynelManager.GetDynel(useItemOnItemAction.Destination);

            return target;
        }

        internal static Vector3 GetDoorForward(this Room room, int doorIdx)
        {
            room.GetDoorPosRot(doorIdx, out Vector3 position, out Quaternion rotation);

            return position + (rotation * (room.GetDoorConnectZone(doorIdx) == room.Instance ? -Vector3.Forward : Vector3.Forward));
        }
    }
}
