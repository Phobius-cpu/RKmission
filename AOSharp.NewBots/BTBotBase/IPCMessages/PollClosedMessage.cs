using AOSharp.Common.GameData;
using AOSharp.Core.IPC;
using SmokeLounge.AOtomation.Messaging.Serialization.MappingAttributes;
using System;

namespace AOBotBase.IPCMessages
{
    [AoContract((int)BTBotBaseIPCOpcode.PollClosed)]
    public class PollClosedMessage : IPCMessage
    {
        [AoMember(0)]
        public int PollId { get; set; }
    }
}
