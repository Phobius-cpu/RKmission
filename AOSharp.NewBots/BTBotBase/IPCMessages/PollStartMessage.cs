using AOSharp.Common.GameData;
using AOSharp.Core.IPC;
using SmokeLounge.AOtomation.Messaging.Serialization.MappingAttributes;
using System;

namespace AOBotBase.IPCMessages
{
    [AoContract((int)BTBotBaseIPCOpcode.PollStart)]
    public class PollStartMessage : IPCMessage
    {
        [AoMember(0)]
        public PlayfieldId PlayfieldId { get; set; }

        [AoMember(1)]
        public int PollId { get; set; }
    }
}
