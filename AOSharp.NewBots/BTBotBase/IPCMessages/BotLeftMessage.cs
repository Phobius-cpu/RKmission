using AOSharp.Core;
using AOSharp.Core.IPC;
using SmokeLounge.AOtomation.Messaging.Serialization.MappingAttributes;

namespace AOBotBase.IPCMessages
{
    [AoContract((int)BTBotBaseIPCOpcode.BotLeft)]
    public class BotLeftMessage : IPCMessage
    {
        public override short Opcode => (int)BTBotBaseIPCOpcode.BotLeft;
    }
}
