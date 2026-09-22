using AOSharp.Core;
using AOSharp.Core.IPC;
using SmokeLounge.AOtomation.Messaging.Serialization;
using SmokeLounge.AOtomation.Messaging.Serialization.MappingAttributes;

namespace AOBotBase.IPCMessages
{
    [AoContract((int)BTBotBaseIPCOpcode.BotJoin)]
    public class BotJoinMessage : IPCMessage
    {
        public override short Opcode => (int)BTBotBaseIPCOpcode.BotJoin;


        [AoMember(0, SerializeSize=ArraySizeType.Byte)]
        public string Name { get; set; }

        [AoMember(1)]
        public BotRole Role { get; set; }
    }
}
