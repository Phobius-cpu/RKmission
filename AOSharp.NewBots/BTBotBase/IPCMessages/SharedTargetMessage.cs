using AOSharp.Common.GameData;
using AOSharp.Core.IPC;
using SmokeLounge.AOtomation.Messaging.Serialization.MappingAttributes;

namespace AOBotBase.IPCMessages
{
    [AoContract((int)BTBotBaseIPCOpcode.SharedTarget)]
    public class SharedTargetMessage : IPCMessage
    {
        [AoMember(0)]
        public Identity Target { get; set; }
    }
}
