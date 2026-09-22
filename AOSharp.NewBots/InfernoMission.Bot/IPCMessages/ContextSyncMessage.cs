using AOSharp.Common.GameData;
using AOSharp.Core.IPC;
using Dungeon.Runner;
using InfernoMission.Bot;
using SmokeLounge.AOtomation.Messaging.Serialization;
using SmokeLounge.AOtomation.Messaging.Serialization.MappingAttributes;

namespace Dungeon.Runner.IPCMessages
{
    [AoContract((int)InfernoMissionBotIPCOpcode.ContextSync)]
    public class ContextSyncMessage : IPCMessage
    {
        [AoMember(0)]
        public int YuttoId { get; set; }

        [AoMember(1, SerializeSize=ArraySizeType.Int32)]
        public string Segment { get; set; }

        [AoMember(2)]
        public int Wave { get; set; }
    }
}
