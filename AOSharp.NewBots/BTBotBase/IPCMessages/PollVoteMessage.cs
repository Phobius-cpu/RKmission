using AOSharp.Common.GameData;
using AOSharp.Core.IPC;
using SmokeLounge.AOtomation.Messaging.Serialization;
using SmokeLounge.AOtomation.Messaging.Serialization.MappingAttributes;

namespace AOBotBase.IPCMessages
{
    [AoContract((int)BTBotBaseIPCOpcode.PollVote)]
    public class PollVoteMessage : IPCMessage
    {
        [AoMember(0)]
        public int PollId { get; set; }

        [AoMember(1, SerializeSize=ArraySizeType.Int32)]
        public string Vote { get; set; }
    }
}
