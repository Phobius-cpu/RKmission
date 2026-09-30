using System;
using System.Collections.Generic;
using System.Linq;
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

    // A best-effort provider. Menu commands are learned from the current help
    // reply; Hope is a confirmed fallback when Mort's menu page is missing.
    internal sealed class ScottyboiWarpProvider : IDisposable
    {
        private enum State { Idle, Lookup, Help, CommandLookup, Invite, Warp, Settling, Done, Failed }
        private sealed class WarpCommand
        {
            public string Recipient, Text;
            public WarpCommand(string recipient, string text)
            { Recipient = recipient; Text = text; }
        }
        private const string BotName = "Scottyboi";
        private const string MenuName = "scty";
        private const string ReplyName = "Scottyboi1";
        private static readonly HashSet<string> MenuZoneHeadings = new HashSet<string>(
            Enum.GetValues(typeof(PlayfieldId)).Cast<PlayfieldId>()
                .SelectMany(id => MenuAliasesFor((int)id, Normalize(id.ToString())))
                .Select(Normalize), StringComparer.OrdinalIgnoreCase);
        private readonly Action<string> _say;
        private readonly MovementArbiter _movement;
        private State _state;
        private uint _botId, _menuId, _replyId, _helpId, _recipientId, _warperId;
        private string _warperName;
        private WarpCommand _pendingCommand;
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
                _botId = _menuId = _replyId = _helpId = _recipientId = _warperId = 0;
                _warperName = null;
                _pendingCommand = null;
                _helpReplies = _menuPageCount = 0;
                _menuPages.Clear();
                LastFailure = null;
                _targetName = Normalize(((PlayfieldId)targetId).ToString());
                _targetAliases = MenuAliasesFor(targetId, _targetName);
                _started = DateTime.UtcNow;
                _state = State.Lookup;
                _movement.Halt(MovementOwner.WarpTravel);
                Network.Send(new LookupMessage { Id = 0, Name = BotName });
                Network.Send(new LookupMessage { Id = 0, Name = MenuName });
                Network.Send(new LookupMessage { Id = 0, Name = ReplyName });
                _say($"Asking {BotName} for its current warp menu for playfield {targetId}.");
                return WarpResult.InProgress;
            }
            if (_state == State.Lookup && _menuId == 0 && _botId != 0 &&
                DateTime.UtcNow - _started > TimeSpan.FromSeconds(3))
                StartHelp(_botId);
            if ((_state == State.Invite || _state == State.Warp) &&
                Playfield.ModelIdentity.Instance == _targetId)
            {
                _state = State.Settling;
                _zonedAt = DateTime.UtcNow;
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
            if (_state == State.Help && _targetId == (int)PlayfieldId.Mort &&
                DateTime.UtcNow - _started > TimeSpan.FromSeconds(10))
            {
                _say("Mort's menu page is missing; using the confirmed Hope command.");
                RequestWarp(new WarpCommand(MenuName, "hope"));
            }
            if (_state == State.Help && !_helpRetried &&
                DateTime.UtcNow - _started > TimeSpan.FromSeconds(10) &&
                (_helpReplies == 0 || _menuPageCount > _menuPages.Count))
            {
                Chat.SendPrivateMessage(_helpId, "!help");
                _helpRetried = true;
                _started = DateTime.UtcNow;
                _say("Scottyboi's warp menu is incomplete; requesting its pages once more.");
            }
            if (_state == State.Invite && Team.IsInTeam && _warperName != null &&
                Team.Members != null &&
                Team.Members.Any(member =>
                    string.Equals(member.Name, _warperName, StringComparison.OrdinalIgnoreCase)))
            {
                _joinedByProvider = true;
                _state = State.Warp;
                _started = DateTime.UtcNow;
                _say($"Joined assigned warper {_warperName}'s team; waiting for the warp and destination verification.");
            }
            TimeSpan timeout = _state == State.Warp ? TimeSpan.FromSeconds(60) :
                _state == State.Invite ? TimeSpan.FromSeconds(45) :
                _state == State.Help ? TimeSpan.FromSeconds(18) : TimeSpan.FromSeconds(12);
            if (DateTime.UtcNow - _started > timeout)
                Fail(_state == State.Help
                    ? $"Scottyboi offered no recognized command for playfield {_targetId} ({_targetName}); " +
                      $"menu pages seen: {(_menuPages.Count == 0 ? "none" : string.Join(",", _menuPages))}" +
                      (_menuPageCount > 0 ? $" of {_menuPageCount}" : "") +
                      (_lastReply == null ? "." : $". Last reply: {_lastReply}")
                    : _state == State.Invite
                        ? $"Scottyboi did not send a team invite after '{_pendingCommand?.Text}'" +
                          (_warperName == null ? "." : $" from {_warperName}.")
                        : _state == State.CommandLookup
                            ? $"Could not resolve Scottyboi menu recipient {_pendingCommand?.Recipient}."
                        : "Warp bot did not complete the current step in time.");
            return _state == State.Failed ? WarpResult.Failed : WarpResult.InProgress;
        }

        private void OnChatMessage(object sender, ChatMessageBody message)
        {
            if (message is LookupMessage lookup)
            {
                if (string.Equals(lookup.Name, BotName, StringComparison.OrdinalIgnoreCase))
                    _botId = lookup.Id;
                else if (string.Equals(lookup.Name, MenuName, StringComparison.OrdinalIgnoreCase))
                {
                    _menuId = lookup.Id;
                    if (_state == State.Lookup && _menuId != 0) StartHelp(_menuId);
                }
                else if (string.Equals(lookup.Name, ReplyName, StringComparison.OrdinalIgnoreCase))
                {
                    _replyId = lookup.Id;
                    // The chat alias scty may resolve to Scottyboi1's canonical name.
                    if (_menuId == 0) _menuId = lookup.Id;
                    if (_state == State.Lookup && _menuId != 0) StartHelp(_menuId);
                }
                else if (_warperName != null &&
                    string.Equals(lookup.Name, _warperName, StringComparison.OrdinalIgnoreCase))
                    _warperId = lookup.Id;
                if (_state == State.CommandLookup && _pendingCommand != null &&
                    (string.Equals(lookup.Name, _pendingCommand.Recipient, StringComparison.OrdinalIgnoreCase) ||
                     (string.Equals(_pendingCommand.Recipient, MenuName, StringComparison.OrdinalIgnoreCase) &&
                      string.Equals(lookup.Name, ReplyName, StringComparison.OrdinalIgnoreCase))) &&
                    lookup.Id != 0)
                    SendWarpCommand(lookup.Id);
                return;
            }
            if (!(message is PrivateMsgMessage reply) || reply.Text == null)
                return;
            if (_state == State.Invite || _state == State.Warp)
            {
                HandleWarpReply(reply);
                return;
            }
            // The paged menu can be delivered by numbered Scottyboi accounts,
            // not necessarily the account that received !help.
            if (_state != State.Help ||
                reply.Text.IndexOf("Warp destinations", StringComparison.OrdinalIgnoreCase) < 0) return;
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
                if (TryParseMenuMessage(reply.Text, alias, out WarpCommand command))
                { RequestWarp(command); return; }
            }
        }

        private void StartHelp(uint recipientId)
        {
            _helpId = recipientId;
            Chat.SendPrivateMessage(recipientId, "!help");
            _state = State.Help;
            _started = DateTime.UtcNow;
        }

        private void RequestWarp(WarpCommand command)
        {
            _pendingCommand = command;
            _started = DateTime.UtcNow;
            uint recipientId = string.Equals(command.Recipient, MenuName, StringComparison.OrdinalIgnoreCase)
                ? _menuId : string.Equals(command.Recipient, ReplyName, StringComparison.OrdinalIgnoreCase)
                    ? _replyId : string.Equals(command.Recipient, BotName, StringComparison.OrdinalIgnoreCase)
                        ? _botId : 0;
            if (recipientId != 0) SendWarpCommand(recipientId);
            else
            {
                _state = State.CommandLookup;
                Network.Send(new LookupMessage { Id = 0, Name = command.Recipient });
            }
        }

        private void SendWarpCommand(uint recipientId)
        {
            _recipientId = recipientId;
            Chat.SendPrivateMessage(recipientId, _pendingCommand.Text);
            _state = State.Invite;
            _started = DateTime.UtcNow;
            _say($"Sent Scottyboi command '{_pendingCommand.Text}' to {_pendingCommand.Recipient} " +
                $"for playfield {_targetId}; waiting for a team invite and zoning.");
        }

        private void HandleWarpReply(PrivateMsgMessage reply)
        {
            string text = Regex.Replace(WebUtility.HtmlDecode(reply.Text), "<[^>]+>", " ");
            if (text.IndexOf("queue to get warped to", StringComparison.OrdinalIgnoreCase) < 0)
                return;
            Match destination = Regex.Match(text,
                @"queue to get warped to\s+(?<zone>[^,.]+)", RegexOptions.IgnoreCase);
            if (!destination.Success ||
                Array.FindIndex(_targetAliases, alias =>
                    Normalize(destination.Groups["zone"].Value) == alias) < 0) return;
            Match warper = Regex.Match(text, @"warper\s*\((?<name>[a-z][a-z0-9_-]{2,24})\)",
                RegexOptions.IgnoreCase);
            if (!warper.Success) return;
            bool offline = Regex.IsMatch(text, @"\bis offline\b|\bneeds to log on\b",
                RegexOptions.IgnoreCase);
            string assignedWarper = warper.Groups["name"].Value;
            if (!string.Equals(_warperName, assignedWarper, StringComparison.OrdinalIgnoreCase))
            {
                _warperName = assignedWarper;
                if (!offline)
                {
                    _started = DateTime.UtcNow;
                    _say($"Scottyboi assigned warper {_warperName}; waiting for its team invite.");
                }
            }
            if (_state == State.Invite && offline)
            {
                Fail($"Scottyboi queued '{_pendingCommand?.Text}', but warper {_warperName} is offline.", false);
                return;
            }
            Network.Send(new LookupMessage { Id = 0, Name = _warperName });
        }

        private void OnTeamRequest(object sender, TeamRequestEventArgs request)
        {
            uint requesterId = unchecked((uint)request.Requester.Instance);
            if ((_state != State.Invite && _state != State.Warp) ||
                Team.IsInTeam ||
                (requesterId != _botId &&
                 requesterId != _recipientId &&
                 requesterId != _menuId &&
                 requesterId != _replyId &&
                 requesterId != _warperId))
                return;
            request.Accept();
            _joinedByProvider = true;
            _state = State.Warp;
            _started = DateTime.UtcNow;
            _say($"Accepted Scottyboi team invite from identity {requesterId}; waiting for the warp.");
        }

        private void OnTeleportStarted(object sender, EventArgs args)
        {
            if (_state == State.Invite || _state == State.Warp)
            {
                _teleportStarted = true;
                _state = State.Warp;
                _started = DateTime.UtcNow;
                _say("Scottyboi warp zoning started; waiting for destination verification.");
            }
        }

        private void OnTeleportEnded(object sender, EventArgs args)
        {
            if ((_state != State.Invite && _state != State.Warp) || !_teleportStarted) return;
            _state = State.Settling;
            _zonedAt = DateTime.UtcNow;
        }

        private void Fail(string reason, bool applyBackoff = true)
        {
            LastFailure = reason;
            _state = State.Failed;
            if (applyBackoff)
                _backoffUntil = DateTime.UtcNow.AddMinutes(2);
            _movement.Release(MovementOwner.WarpTravel);
            if (_joinedByProvider && Team.IsInTeam) Team.Leave();
            _joinedByProvider = false;
            _say(reason + " Normal travel fallback is required.");
        }

        private static bool TryParseMenuLine(string line, string target, out WarpCommand command)
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
                @"(?:chatcmd:///tell|/tell)\s+(?<recipient>Scottyboi\d*|scty)\s+(?<command>!?[a-z][a-z0-9_-]{1,20})[^>]*>(?<label>[^<]+)",
                RegexOptions.IgnoreCase);
            if (legacyLink.Success && MatchesTarget(legacyLink.Groups["label"].Value, target))
            {
                command = new WarpCommand(legacyLink.Groups["recipient"].Value,
                    legacyLink.Groups["command"].Value);
                return true;
            }
            if (line.IndexOf("<a", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            string clean = Regex.Replace(WebUtility.HtmlDecode(line), "<[^>]+>", " ").Trim();
            string[] parts = Regex.Split(clean, @"\s*(?:=>|[-:=])\s*");
            if (parts.Length != 2) return false;
            for (int i = 0; i < 2; i++)
            {
                if (!MatchesTarget(parts[i], target)) continue;
                string candidate = parts[1 - i].Trim();
                Match tell = Regex.Match(candidate,
                    @"^(?:/tell\s+(?<recipient>Scottyboi\d*|scty)\s+)?(?<command>!?[a-z][a-z0-9_-]{1,20})$",
                    RegexOptions.IgnoreCase);
                if (!tell.Success) continue;
                command = new WarpCommand(tell.Groups["recipient"].Success
                    ? tell.Groups["recipient"].Value : BotName, tell.Groups["command"].Value);
                return true;
            }
            return false;
        }

        private static bool TryParseMenuMessage(string message, string target, out WarpCommand command)
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

        private static bool TryParseMenuSection(string body, string target, out WarpCommand command)
        {
            command = null;
            bool inSection = false;
            foreach (string line in Regex.Split(body, @"\r\n|\r|\n|<br\s*/?>", RegexOptions.IgnoreCase))
            {
                int firstLink = line.IndexOf("<a", StringComparison.OrdinalIgnoreCase);
                string prefix = firstLink < 0 ? line : line.Substring(0, firstLink);
                // Nadybot may put a location name and decoration before the
                // first link. Read a marked zone header when one is present.
                Match markedHeading = Regex.Match(prefix,
                    @"<header\d*[^>]*>(?<name>.*?)<end>",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline);
                string heading = Regex.Replace(WebUtility.HtmlDecode(markedHeading.Success
                    ? markedHeading.Groups["name"].Value : prefix), "<[^>]+>", " ").Trim();
                if (MatchesTarget(heading, target)) inSection = true;
                else if (inSection &&
                    (markedHeading.Success || MenuZoneHeadings.Contains(Normalize(heading))))
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

        private static bool TryCommandFromUrl(string url, out WarpCommand command)
        {
            command = null;
            Match tell = Regex.Match(url,
                @"(?:chatcmd:///tell|/tell)\s+(?<recipient>Scottyboi\d*|scty)\s+(?<command>!?[a-z][a-z0-9_-]{0,20}(?:\s+[a-z0-9_-]{1,30}){0,3})\s*$",
                RegexOptions.IgnoreCase);
            if (!tell.Success) return false;
            string text = tell.Groups["command"].Value.Trim();
            if (Regex.IsMatch(text, @"^!?help(?:\s|$)", RegexOptions.IgnoreCase)) return false;
            command = new WarpCommand(tell.Groups["recipient"].Value, text);
            return true;
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
            // The section parser applies to every playfield. Add menu names
            // that differ from AOSharp's enum or name a known location within it.
            switch ((PlayfieldId)playfieldId)
            {
                case PlayfieldId.AthenWest:
                    return new[] { enumName, "westathen" };
                case PlayfieldId.Andromeda:
                    return new[] { enumName, "uturn" };
                case PlayfieldId.BorealisCity:
                    return new[] { enumName, "borealis" };
                case PlayfieldId.Mort:
                    return new[] { enumName, "sentinels", "sentinelsmort", "mortsentinels" };
                case PlayfieldId.OmniTrade:
                    return new[] { enumName, "omni1trade" };
                case PlayfieldId.GreaterOmniForest:
                    return new[] { enumName, "omnigreaterforest" };
                case PlayfieldId.RomeBluedistrict:
                    return new[] { enumName, "romeblue" };
                case PlayfieldId.UnicornDefenceHub:
                    return new[] { enumName, "unicorndefensehub" };
                // AOSharp's enum omits the outdoor Unicorn Outpost (4364).
                case (PlayfieldId)4364:
                    return new[] { enumName, "unicornoutpost" };
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
            _botId = _menuId = _replyId = _helpId = _recipientId = _warperId = 0;
            _warperName = null;
            _pendingCommand = null;
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
