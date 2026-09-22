using AOSharp.Common.GameData;
using AOSharp.Core.IPC;
using Dungeon.Runner;
using InfernoMission.Bot;
using SmokeLounge.AOtomation.Messaging.Serialization;
using SmokeLounge.AOtomation.Messaging.Serialization.MappingAttributes;

namespace Dungeon.Runner.IPCMessages
{
    [AoContract((int)InfernoMissionBotIPCOpcode.WaveUpdate)]
    public class WaveUpdateMessage : IPCMessage
    {
        [AoMember(0)]
        public Identity MonsterId { get; set; }
    }
}
