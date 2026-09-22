using AOSharp.Common.GameData;
using AOSharp.Core.IPC;
using Dungeon.Runner;
using SmokeLounge.AOtomation.Messaging.Serialization.MappingAttributes;

namespace Dungeon.Runner.IPCMessages
{
    [AoContract((int)DungeonRunnerIPCOpcode.LeaderMovement)]
    public class LeaderMovementMessage : IPCMessage
    {
        [AoMember(0)]
        public int Floor { get; set; }

        [AoMember(1)]
        public Vector3 Destination { get; set; }
    }
}
