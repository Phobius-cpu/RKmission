using System;
using System.Collections.Generic;
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
    // no bot availability is baked into the plugin.
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
        private string[] _targetAliases;
        private DateTime _started;
        private DateTime _backoffUntil;
        private DateTime _zonedAt;
        private bool _helpRetried, _teleportStarted;
        private bool _joinedByProvider;
        private string _lastReply;
        private int _helpReplies, _menuPageCount;
        private readonly HashSet<int> _menuPages = new HashSet<int>();
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
                _helpReplies = _menuPageCount = 0;
                _menuPages.Clear();
                LastFailure = null;
                _targetName = Normalize(((PlayfieldId)targetId).ToString());
                _targetAliases = MenuAliasesFor(targetId, _targetName);
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
            if (_state == State.Help && !_helpRetried &&
                DateTime.UtcNow - _started > TimeSpan.FromSeconds(10) &&
                (_helpReplies == 0 || _menuPageCount > _menuPages.Count))
            {
                Chat.SendPrivateMessage(_botId, "!help");
                _helpRetried = true;
                _started = DateTime.UtcNow;
                _say("Scottyboi's warp menu is incomplete; requesting its pages once more.");
            }
            TimeSpan timeout = _state == State.Warp ? TimeSpan.FromSeconds(35) :
                _state == State.Invite ? TimeSpan.FromSeconds(20) :
                _state == State.Help ? TimeSpan.FromSeconds(18) : TimeSpan.FromSeconds(12);
            if (DateTime.UtcNow - _started > timeout)
                Fail(_state == State.Help
                    ? $"Scottyboi offered no recognized command for playfield {_targetId} ({_targetName}); " +
                      $"menu pages seen: {(_menuPages.Count == 0 ? "none" : string.Join(",", _menuPages))}" +
                      (_menuPageCount > 0 ? $" of {_menuPageCount}" : "") +
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
            _helpReplies++;
            foreach (Match page in Regex.Matches(reply.Text,
                @"Warp destinations\s*\((\d+)\s*/\s*(\d+)\)", RegexOptions.IgnoreCase))
                if (int.TryParse(page.Groups[1].Value, out int number) &&
                    int.TryParse(page.Groups[2].Value, out int count))
                { _menuPages.Add(number); _menuPageCount = Math.Max(_menuPageCount, count); }
            _lastReply = Regex.Replace(reply.Text, "<[^>]+>", " ").Trim();
            if (_lastReply.Length > 180) _lastReply = _lastReply.Substring(0, 180) + "...";
            foreach (string alias in _targetAliases)
            {
                if (TryParseMenuMessage(reply.Text, alias, out string command))
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
                string label = Regex.Replace(anchor.Groups["label"].Value, "<[^>]+>", " ");
                if (MatchesTarget(label, target) && TryCommandFromUrl(url, out command)) return true;
            }
            Match legacyLink = Regex.Match(line,
                @"(?:chatcmd:///tell|/tell)\s+Scottyboi\s+(?<command>!?[a-z][a-z0-9_-]{1,20})[^>]*>(?<label>[^<]+)",
                RegexOptions.IgnoreCase);
            if (legacyLink.Success && MatchesTarget(legacyLink.Groups["label"].Value, target))
            { command = legacyLink.Groups["command"].Value; return true; }
            if (line.IndexOf("<a", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            string clean = Regex.Replace(WebUtility.HtmlDecode(line), "<[^>]+>", " ").Trim();
            string[] parts = Regex.Split(clean, @"\s*(?:=>|[-:=])\s*");
            if (parts.Length != 2) return false;
            for (int i = 0; i < 2; i++)
            {
                if (!MatchesTarget(parts[i], target)) continue;
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

        private static bool TryParseMenuMessage(string message, string target, out string command)
        {
            command = null;
            // Nadybot puts each menu page in the href of a text:// blob link.
            // The visible label only says "Warp destinations (1 / 3)".
            foreach (Match blob in Regex.Matches(message,
                @"<a\b[^>]*?href\s*=\s*(['""])text://(?<body>.*?)\1[^>]*>.*?</a>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                string body = WebUtility.HtmlDecode(blob.Groups["body"].Value);
                if (TryParseMenuLine(body, target, out command)) return true;
                if (TryParseMenuSection(body, target, out command)) return true;
                foreach (string line in body.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    if (TryParseMenuLine(line, target, out command)) return true;
            }
            if (TryParseMenuSection(message, target, out command)) return true;
            foreach (string line in message.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                if (TryParseMenuLine(line, target, out command)) return true;
            return false;
        }

        private static bool TryParseMenuSection(string body, string target, out string command)
        {
            command = null;
            bool inSection = false;
            foreach (string line in Regex.Split(body, @"\r\n|\r|\n|<br\s*/?>", RegexOptions.IgnoreCase))
            {
                int firstLink = line.IndexOf("<a", StringComparison.OrdinalIgnoreCase);
                string prefix = firstLink < 0 ? line : line.Substring(0, firstLink);
                string heading = Regex.Replace(WebUtility.HtmlDecode(prefix), "<[^>]+>", " ").Trim();
                if (MatchesTarget(heading, target)) inSection = true;
                else if (inSection && heading.Length > 0 &&
                    !heading.StartsWith("•") && !heading.StartsWith("-"))
                    inSection = false;
                if (!inSection) continue;
                foreach (Match anchor in Regex.Matches(line,
                    @"<a\b[^>]*?href\s*=\s*(['""])(?<url>.*?)\1[^>]*>(?<label>.*?)</a>",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline))
                {
                    string label = Regex.Replace(anchor.Groups["label"].Value, "<[^>]+>", " ");
                    if (Normalize(label) == "wp" || Normalize(label) == "waypoint") continue;
                    string url = WebUtility.HtmlDecode(Uri.UnescapeDataString(anchor.Groups["url"].Value));
                    if (TryCommandFromUrl(url, out command)) return true;
                }
            }
            return false;
        }

        private static bool TryCommandFromUrl(string url, out string command)
        {
            command = null;
            Match tell = Regex.Match(url,
                @"(?:chatcmd:///tell|/tell)\s+Scottyboi\s+(?<command>!?[a-z][a-z0-9_-]{0,20}(?:\s+[a-z0-9_-]{1,30}){0,3})\s*$",
                RegexOptions.IgnoreCase);
            if (!tell.Success) return false;
            command = tell.Groups["command"].Value.Trim();
            return !Regex.IsMatch(command, @"^!?help(?:\s|$)", RegexOptions.IgnoreCase);
        }

        private static bool MatchesTarget(string label, string target)
        {
            string normalized = Normalize(WebUtility.HtmlDecode(label));
            return normalized == target || normalized == "warpto" + target ||
                normalized == target + "warp";
        }

        private static string Normalize(string text) =>
            Regex.Replace(text ?? "", "[^a-z0-9]", "", RegexOptions.IgnoreCase).ToLowerInvariant();

        private static string[] MenuAliasesFor(int playfieldId, string enumName)
        {
            // The section parser applies to every playfield. Only names that
            // differ from AOSharp's enum need additional menu spellings.
            switch ((PlayfieldId)playfieldId)
            {
                case PlayfieldId.Mort:
                    return new[] { enumName, "sentinels", "sentinelsmort", "mortsentinels" };
                case PlayfieldId.OmniTrade:
                    return new[] { enumName, "omni1trade" };
                case PlayfieldId.GreaterOmniForest:
                    return new[] { enumName, "omnigreaterforest" };
                default:
                    return new[] { enumName };
            }
        }

        public void Reset()
        {
            if (_joinedByProvider && Team.IsInTeam) Team.Leave();
            _joinedByProvider = false;
            _state = State.Idle;
            _teleportStarted = _helpRetried = false;
            _lastReply = null;
            _targetAliases = null;
            _helpReplies = _menuPageCount = 0;
            _menuPages.Clear();
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
