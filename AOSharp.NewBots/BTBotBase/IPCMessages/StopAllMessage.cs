using AOSharp.Core;
using AOSharp.Core.IPC;
using SmokeLounge.AOtomation.Messaging.Serialization.MappingAttributes;

namespace AOBotBase.IPCMessages
{
    [AoContract((int)BTBotBaseIPCOpcode.StopAll)]
    public class StopAllMessage : IPCMessage
    {
        public override short Opcode => (int)BTBotBaseIPCOpcode.StopAll;
    }
}
