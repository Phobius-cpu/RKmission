using AIMission.Bot;
using AOSharp.Common.GameData;
using AOSharp.Core.IPC;
using SmokeLounge.AOtomation.Messaging.Serialization.MappingAttributes;

namespace AIMission.Bot.IPCMessages
{
    [AoContract((int)AIMissionBotIPCOpcode.SetActiveMission)]
    public class SetActiveMissionMessage : IPCMessage
    {
        [AoMember(0)]
        public Identity Identity { get; set; }
    }
}
