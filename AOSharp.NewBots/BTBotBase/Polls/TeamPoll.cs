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
    public enum PollType
    {
        TargetSelect,
        ReadyCheck
    }

    public class PollResult<TAnswer>
    {
        public TAnswer Answer;
        public int Votes;
    }

    public class TeamPoll<TPollId, TAnswer> where TPollId : Enum
    {
        public event Action<PlayfieldId> PollOpened;
        public event Action PollClosed;
        public event Action<List<PollResult<TAnswer>>, bool> PollResult;

        public bool PollOpen = false;

        protected IPCChannel _ipcChannel;
        protected Dictionary<int, bool> _voters = new Dictionary<int, bool>();
        protected Dictionary<string, int> _votes;
        protected TPollId _pollId;

        protected Logger _logger;

        public TeamPoll(TPollId pollId, IPCChannel ipcChannel, Logger logger)
        {
            _pollId = pollId;
            _ipcChannel = ipcChannel;
            _logger = logger;

            _ipcChannel.RegisterCallback((int)BTBotBaseIPCOpcode.PollStart, OnPollOpened);
            _ipcChannel.RegisterCallback((int)BTBotBaseIPCOpcode.PollClosed, OnPollClosed);
            _ipcChannel.RegisterCallback((int)BTBotBaseIPCOpcode.PollVote, OnVote);
        }

        private void OnPollOpened(int sender, IPCMessage ipcMsg)
        {
            PollStartMessage pollStartMsg = ipcMsg as PollStartMessage;

            if (pollStartMsg.PollId != Convert.ToInt32(_pollId))
                return;

            PollOpened?.Invoke(pollStartMsg.PlayfieldId);
        }

        private void OnPollClosed(int sender, IPCMessage ipcMsg)
        {
            PollClosedMessage pollClosedMsg = ipcMsg as PollClosedMessage;

            if (pollClosedMsg.PollId != Convert.ToInt32(_pollId))
                return;

            PollClosed?.Invoke();
        }

        public void StartPoll(TAnswer initialVote, int duration)
        {
            if (!Team.IsLeader)
                return;

            _votes = new Dictionary<string, int>();      // Reset Votes
            _voters = _voters.ToDictionary(x => x.Key, x => false); // Reset Voters

            PollOpen = true;

            Coroutine.ExecuteAfter(duration, ClosePoll);

            if (initialVote != null)
                RecordVote(JsonConvert.SerializeObject(initialVote));

            try
            {
                _ipcChannel.Broadcast(new PollStartMessage
                {
                    PlayfieldId = Playfield.ModelId,
                    PollId = Convert.ToInt32(_pollId)
                });
            }
            catch (Exception ex)
            {
                _logger.Information(ex.ToString());
            }
        }

        public void Vote(TAnswer answer)
        {
            _ipcChannel.Broadcast(new PollVoteMessage
            {
                PollId = Convert.ToInt32(_pollId),
                Vote = JsonConvert.SerializeObject(answer)
            });
        }

        private void OnVote(int voter, IPCMessage ipcMsg)
        {
            PollVoteMessage voteMsg = ipcMsg as PollVoteMessage;

            if (voteMsg.PollId != Convert.ToInt32(_pollId))
                return;

            if (!Team.IsLeader)
                return;

            if (!PollOpen)
                return;

            _logger.Debug($"Got vote for poll {_pollId} - {voteMsg.Vote}");

            _voters[voter] = true;

            RecordVote(voteMsg.Vote);

            _logger.Debug("Votes:");
            foreach(var v in _voters)
                _logger.Debug($"\t{v.Key}: {v.Value}");

            if (_voters.All(x => x.Value))
                ClosePoll();
        }

        protected virtual void RecordVote(string answer)
        {
            if (!_votes.ContainsKey(answer))
                _votes[answer] = 0;

            _votes[answer]++;
        }

        public void ClosePoll()
        {
            if (!Team.IsLeader)
                return;

            if (!PollOpen)
                return;

            PollOpen = false;

            try
            {
                var results = _votes.OrderByDescending(x => x.Value).Select(x => new PollResult<TAnswer>
                {
                    Answer = JsonConvert.DeserializeObject<TAnswer>(x.Key),
                    Votes = x.Value
                }).ToList();

                PollResult?.Invoke(results, _voters.Any(v => !v.Value));
            }
            catch(Exception ex)
            {
                _logger.Information(ex.ToString());
            }
        }

        public void RegisterVoter(int voter)
        {
            _voters[voter] = false;
        }

        public void UnregisterVoter(int voter)
        {
            _voters.Remove(voter);
        }
    }
}
