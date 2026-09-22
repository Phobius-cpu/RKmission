using AOSharp.Common.GameData;
using AOSharp.Core;
using AOSharp.Core.IPC;
using SmokeLounge.AOtomation.Messaging.Serialization;
using SmokeLounge.AOtomation.Messaging.Serialization.MappingAttributes;
using System.Collections.Generic;

namespace AOBotBase.IPCMessages
{
    [AoContract((int)BTBotBaseIPCOpcode.RosterUpdate)]
    public class RosterUpdateMessage : IPCMessage
    {
        public override short Opcode => (int)BTBotBaseIPCOpcode.RosterUpdate;

        [AoMember(0)]
        public Identity Leader { get; set; }

        [AoMember(1, SerializeSize=ArraySizeType.Byte)]
        public Companion[] Roster { get; set; }

        public class Companion
        {
            [AoMember(0)]
            public Identity Identity { get; set; }

            [AoMember(1, SerializeSize = ArraySizeType.Byte)]
            public string Name { get; set; }

            [AoMember(2)]
            public BotRole Role { get; set; }
        }
    }
}
