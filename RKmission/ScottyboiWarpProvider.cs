using System;
using System.Net;
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
        private enum State { Idle, Lookup, Help, Invite, Warp, Settling, Done, Failed }
        private const string BotName = "Scottyboi";
        private readonly Action<string> _say;
        private readonly MovementArbiter _movement;
        private State _state;
        private uint _botId;
        private int _targetId;
        private string _targetName;
        private DateTime _started;
        private DateTime _backoffUntil;
        private DateTime _zonedAt;
        private bool _helpRetried, _teleportStarted;
        private bool _joinedByProvider;
        private string _lastReply;
        public string LastFailure { get; private set; }

        public ScottyboiWarpProvider(Action<string> say, MovementArbiter movement)
        {
            _say = say;
            _movement = movement;
            Network.ChatMessageReceived += OnChatMessage;
            Team.TeamRequest += OnTeamRequest;
            Game.TeleportStarted += OnTeleportStarted;
            Game.TeleportEnded += OnTeleportEnded;
        }

        public WarpResult Tick(int targetId)
        {
            if (Playfield.ModelIdentity.Instance == targetId && _state == State.Idle) return WarpResult.Succeeded;
            if (_state == State.Done && _targetId == targetId) return WarpResult.Succeeded;
            if (_state == State.Failed && _targetId == targetId) return WarpResult.Failed;
            if (_state == State.Idle || _targetId != targetId)
            {
                if (DateTime.UtcNow < _backoffUntil)
                { LastFailure = "Scottyboi is in cooldown after a failed request."; return WarpResult.Failed; }
                if (Team.IsInTeam)
                { LastFailure = "Already in a team; Scottyboi cannot invite this character."; _say(LastFailure); return WarpResult.Failed; }
                _targetId = targetId;
                _helpRetried = _teleportStarted = false;
                _lastReply = null;
                LastFailure = null;
                _targetName = Normalize(((PlayfieldId)targetId).ToString());
                _started = DateTime.UtcNow;
                _state = State.Lookup;
                _movement.Halt(MovementOwner.WarpTravel);
                Network.Send(new LookupMessage { Id = 0, Name = BotName });
                _say($"Asking {BotName} for its current warp menu for playfield {targetId}.");
                return WarpResult.InProgress;
            }
            if (_state == State.Settling)
            {
                if (DateTime.UtcNow - _zonedAt < TimeSpan.FromSeconds(2)) return WarpResult.InProgress;
                if (Playfield.ModelIdentity.Instance != _targetId)
                { Fail($"Warp ended in playfield {Playfield.ModelIdentity.Instance}, not {_targetId}."); return WarpResult.Failed; }
                _state = State.Done;
                _movement.Release(MovementOwner.WarpTravel);
                if (_joinedByProvider && Team.IsInTeam) Team.Leave();
                _joinedByProvider = false;
                _say($"Warp to playfield {_targetId} verified after zoning settled.");
                return WarpResult.Succeeded;
            }
            if (_state == State.Help && !_helpRetried && DateTime.UtcNow - _started > TimeSpan.FromSeconds(6))
            {
                Chat.SendPrivateMessage(_botId, "help");
                _helpRetried = true;
                _started = DateTime.UtcNow;
            }
            TimeSpan timeout = _state == State.Warp ? TimeSpan.FromSeconds(35) :
                _state == State.Invite ? TimeSpan.FromSeconds(20) : TimeSpan.FromSeconds(12);
            if (DateTime.UtcNow - _started > timeout)
                Fail(_state == State.Help
                    ? "Scottyboi replied without a recognized destination command" +
                      (_lastReply == null ? "." : $". Last reply: {_lastReply}")
                    : _state == State.Invite
                        ? "Scottyboi did not send a team invite after the destination command."
                        : "Warp bot did not complete the current step in time.");
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
            _lastReply = Regex.Replace(reply.Text, "<[^>]+>", " ").Trim();
            if (_lastReply.Length > 180) _lastReply = _lastReply.Substring(0, 180) + "...";
            if (TryParseMenuLine(reply.Text, _targetName, out string linkedCommand))
            {
                RequestWarp(linkedCommand);
            }
            else foreach (string line in reply.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (TryParseMenuLine(line, _targetName, out string command))
                { RequestWarp(command); return; }
            }
        }

        private void RequestWarp(string command)
        {
            Chat.SendPrivateMessage(_botId, command);
            _state = State.Invite;
            _started = DateTime.UtcNow;
            _say($"Sent Scottyboi command '{command}' for playfield {_targetId}; waiting for a team invite and zoning.");
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

        private void OnTeleportStarted(object sender, EventArgs args)
        {
            if (_state == State.Invite || _state == State.Warp)
                _teleportStarted = true;
        }

        private void OnTeleportEnded(object sender, EventArgs args)
        {
            if ((_state != State.Invite && _state != State.Warp) || !_teleportStarted) return;
            _state = State.Settling;
            _zonedAt = DateTime.UtcNow;
        }

        private void Fail(string reason)
        {
            LastFailure = reason;
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
            // Chat links can contain nested formatting and URL-escaped spaces.
            foreach (Match anchor in Regex.Matches(line,
                @"<a\b[^>]*?href\s*=\s*(['""])(?<url>.*?)\1[^>]*>(?<label>.*?)</a>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                string url = WebUtility.HtmlDecode(Uri.UnescapeDataString(anchor.Groups["url"].Value));
                Match tell = Regex.Match(url,
                    @"(?:chatcmd:///tell|/tell)\s+Scottyboi\s+(?<command>!?[a-z][a-z0-9_-]{1,20})(?:\s|$)",
                    RegexOptions.IgnoreCase);
                string label = Regex.Replace(anchor.Groups["label"].Value, "<[^>]+>", " ");
                if (tell.Success && Normalize(WebUtility.HtmlDecode(label)) == target)
                { command = tell.Groups["command"].Value; return true; }
            }
            Match legacyLink = Regex.Match(line,
                @"(?:chatcmd:///tell|/tell)\s+Scottyboi\s+(?<command>!?[a-z][a-z0-9_-]{1,20})[^>]*>(?<label>[^<]+)",
                RegexOptions.IgnoreCase);
            if (legacyLink.Success && Normalize(legacyLink.Groups["label"].Value) == target)
            { command = legacyLink.Groups["command"].Value; return true; }
            string clean = Regex.Replace(WebUtility.HtmlDecode(line), "<[^>]+>", " ").Trim();
            string[] parts = Regex.Split(clean, @"\s*(?:=>|[-:=])\s*");
            if (parts.Length != 2) return false;
            for (int i = 0; i < 2; i++)
            {
                if (Normalize(parts[i]) != target) continue;
                string candidate = parts[1 - i].Trim();
                Match tell = Regex.Match(candidate,
                    @"^(?:/tell\s+Scottyboi\s+)?(?<command>!?[a-z][a-z0-9_-]{1,20})$",
                    RegexOptions.IgnoreCase);
                if (!tell.Success) continue;
                command = tell.Groups["command"].Value;
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
            _teleportStarted = _helpRetried = false;
            _lastReply = null;
            LastFailure = null;
            _movement.Release(MovementOwner.WarpTravel);
        }

        public void Dispose()
        {
            Reset();
            Network.ChatMessageReceived -= OnChatMessage;
            Team.TeamRequest -= OnTeamRequest;
            Game.TeleportStarted -= OnTeleportStarted;
            Game.TeleportEnded -= OnTeleportEnded;
        }
    }
}
