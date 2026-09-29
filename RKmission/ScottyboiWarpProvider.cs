using System;
using System.Text.RegularExpressions;
using AOSharp.Common.GameData;
using AOSharp.Core;
using AOSharp.Core.UI;
using SmokeLounge.AOtomation.Messaging.Messages;
using SmokeLounge.AOtomation.Messaging.Messages.ChatMessages;

namespace RKmission
{
    internal enum WarpResult { InProgress, Succeeded, Failed }

    // A best-effort provider. Commands are learned from the current help reply;
    // no destination aliases or bot availability are baked into the plugin.
    internal sealed class ScottyboiWarpProvider : IDisposable
    {
        private enum State { Idle, Lookup, Help, Invite, Warp, Done, Failed }
        private const string BotName = "Scottyboi";
        private readonly Action<string> _say;
        private readonly MovementArbiter _movement;
        private State _state;
        private uint _botId;
        private int _targetId;
        private string _targetName;
        private DateTime _started;
        private DateTime _backoffUntil;
        private bool _joinedByProvider;

        public ScottyboiWarpProvider(Action<string> say, MovementArbiter movement)
        {
            _say = say;
            _movement = movement;
            Network.ChatMessageReceived += OnChatMessage;
            Team.TeamRequest += OnTeamRequest;
            Game.TeleportEnded += OnTeleportEnded;
        }

        public WarpResult Tick(int targetId)
        {
            if (Playfield.ModelIdentity.Instance == targetId) return WarpResult.Succeeded;
            if (_state == State.Done && _targetId == targetId) return WarpResult.Succeeded;
            if (_state == State.Failed && _targetId == targetId) return WarpResult.Failed;
            if (_state == State.Idle || _targetId != targetId)
            {
                if (DateTime.UtcNow < _backoffUntil || Team.IsInTeam) return WarpResult.Failed;
                _targetId = targetId;
                _targetName = Normalize(((PlayfieldId)targetId).ToString());
                _started = DateTime.UtcNow;
                _state = State.Lookup;
                _movement.Halt(MovementOwner.WarpTravel);
                Network.Send(new LookupMessage { Id = 0, Name = BotName });
                _say($"Asking {BotName} for its current warp menu for playfield {targetId}.");
                return WarpResult.InProgress;
            }
            TimeSpan timeout = _state == State.Warp ? TimeSpan.FromSeconds(35) :
                _state == State.Invite ? TimeSpan.FromSeconds(20) : TimeSpan.FromSeconds(12);
            if (DateTime.UtcNow - _started > timeout)
                Fail("Warp bot did not complete the current step in time.");
            return _state == State.Failed ? WarpResult.Failed : WarpResult.InProgress;
        }

        private void OnChatMessage(object sender, ChatMessageBody message)
        {
            if (_state == State.Lookup && message is LookupMessage lookup &&
                string.Equals(lookup.Name, BotName, StringComparison.OrdinalIgnoreCase) && lookup.Id != 0)
            {
                _botId = lookup.Id;
                Chat.SendPrivateMessage(_botId, "!help");
                _state = State.Help;
                _started = DateTime.UtcNow;
                return;
            }
            if (_state != State.Help || !(message is PrivateMsgMessage reply) ||
                reply.Sender != _botId || reply.Text == null)
                return;
            foreach (string line in reply.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!TryParseMenuLine(line, _targetName, out string command)) continue;
                Chat.SendPrivateMessage(_botId, command);
                _state = State.Invite;
                _started = DateTime.UtcNow;
                _say($"Requested the menu-listed warp for playfield {_targetId}; waiting for a team invite and zoning.");
                return;
            }
        }

        private void OnTeamRequest(object sender, TeamRequestEventArgs request)
        {
            if ((_state != State.Invite && _state != State.Warp) ||
                request.Requester.Instance != _botId || Team.IsInTeam)
                return;
            request.Accept();
            _joinedByProvider = true;
            _state = State.Warp;
            _started = DateTime.UtcNow;
        }

        private void OnTeleportEnded(object sender, EventArgs args)
        {
            if (_state != State.Invite && _state != State.Warp) return;
            if (Playfield.ModelIdentity.Instance != _targetId)
            {
                Fail($"Warp ended in playfield {Playfield.ModelIdentity.Instance}, not {_targetId}.");
                return;
            }
            _state = State.Done;
            _movement.Release(MovementOwner.WarpTravel);
            if (_joinedByProvider && Team.IsInTeam) Team.Leave();
            _joinedByProvider = false;
            _say($"Warp to playfield {_targetId} verified.");
        }

        private void Fail(string reason)
        {
            _state = State.Failed;
            _backoffUntil = DateTime.UtcNow.AddMinutes(2);
            _movement.Release(MovementOwner.WarpTravel);
            if (_joinedByProvider && Team.IsInTeam) Team.Leave();
            _joinedByProvider = false;
            _say(reason + " Normal travel fallback is required.");
        }

        private static bool TryParseMenuLine(string line, string target, out string command)
        {
            command = null;
            string clean = Regex.Replace(line, "<[^>]+>", " ").Trim();
            string[] parts = Regex.Split(clean, @"\s*(?:=>|[-:=])\s*");
            if (parts.Length != 2) return false;
            for (int i = 0; i < 2; i++)
            {
                if (Normalize(parts[i]) != target) continue;
                string candidate = parts[1 - i].Trim();
                if (!Regex.IsMatch(candidate, @"^!?[a-z][a-z0-9_-]{1,20}$", RegexOptions.IgnoreCase))
                    continue;
                command = candidate;
                return true;
            }
            return false;
        }

        private static string Normalize(string text) =>
            Regex.Replace(text ?? "", "[^a-z0-9]", "", RegexOptions.IgnoreCase).ToLowerInvariant();

        public void Reset()
        {
            if (_joinedByProvider && Team.IsInTeam) Team.Leave();
            _joinedByProvider = false;
            _state = State.Idle;
            _movement.Release(MovementOwner.WarpTravel);
        }

        public void Dispose()
        {
            Reset();
            Network.ChatMessageReceived -= OnChatMessage;
            Team.TeamRequest -= OnTeamRequest;
            Game.TeleportEnded -= OnTeleportEnded;
        }
    }
}
