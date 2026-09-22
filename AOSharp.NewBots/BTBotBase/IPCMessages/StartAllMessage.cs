using AOSharp.Core;
using AOSharp.Core.IPC;
using SmokeLounge.AOtomation.Messaging.Serialization.MappingAttributes;

namespace AOBotBase.IPCMessages
{
    [AoContract((int)BTBotBaseIPCOpcode.StartAll)]
    public class StartAllMessage : IPCMessage
    {
        public override short Opcode => (int)BTBotBaseIPCOpcode.StartAll;
    }
}
