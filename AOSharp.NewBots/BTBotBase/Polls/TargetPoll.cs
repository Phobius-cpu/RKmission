using AOBotBase;
using AOBotBase.IPCMessages;
using AOSharp.Common.GameData;
using AOSharp.Core;
using AOSharp.Core.IPC;
using AOSharp.Core.UI;
using Newtonsoft.Json;
using Serilog.Core;
using SmokeLounge.AOtomation.Messaging.GameData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BTBotBase.Polls
{
    public class TargetPollAnswer
    {
        public Identity Target { get; set; }
    }

    public class TargetPoll : TeamPoll<PollType, TargetPollAnswer>
    {
        public readonly int PollDuration = 500;

        public TargetPoll(IPCChannel ipcChannel, Logger logger) : base(PollType.TargetSelect, ipcChannel, logger)
        {
            _ipcChannel = ipcChannel;
        }

        public void StartPoll(SimpleChar target)
        {
            StartPoll(new TargetPollAnswer
            {
                Target = target.Identity
            }, PollDuration);
        }

        public void Vote(SimpleChar target)
        {
            Vote(new TargetPollAnswer
            {
                Target = target != null ? target.Identity : Identity.None
            });
        }

        protected override void RecordVote(string answer)
        {
            if (JsonConvert.DeserializeObject<TargetPollAnswer>(answer).Target == Identity.None)
                return;

            base.RecordVote(answer);
        }
    }
}
