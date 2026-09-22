using AOBotBase;
using AOBotBase.IPCMessages;
using AOSharp.Common.GameData;
using AOSharp.Core;
using AOSharp.Core.IPC;
using AOSharp.Core.UI;
using Serilog.Core;
using SmokeLounge.AOtomation.Messaging.GameData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BTBotBase.Polls
{
    public class ReadyPollAnswer
    {
        public bool IsReady { get; set; }
    }

    public class ReadyPoll : TeamPoll<PollType, ReadyPollAnswer>
    {
        public ReadyPoll(IPCChannel ipcChannel, Logger logger) : base(PollType.ReadyCheck, ipcChannel, logger)
        {
            _ipcChannel = ipcChannel;
        }

        public void StartPoll(int duration)
        {
            StartPoll(new ReadyPollAnswer
            {
                IsReady = true
            }, duration);
        }

        public void Vote(bool isReady)
        {
            Vote(new ReadyPollAnswer
            {
                IsReady = isReady
            });
        }
    }
}
