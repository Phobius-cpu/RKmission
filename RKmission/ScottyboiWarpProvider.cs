using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using AOSharp.Common.GameData;
using AOSharp.Core;
using AOSharp.Core.UI;
using Newtonsoft.Json;
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
            public string Recipient, Text, Destination;
            public WarpCommand(string recipient, string text, string? destination = null)
            { Recipient = recipient; Text = text; Destination = Normalize(destination); }
        }
        private sealed class ObservedLanding
        {
            public int DestinationPlayfield { get; set; }
            public string Warper { get; set; }
            public float[] ArrivalPosition { get; set; }
            public DateTime VerifiedUtc { get; set; }
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
        private readonly string _landingPath;
        private readonly List<ObservedLanding> _landings = new List<ObservedLanding>();
        private bool _landingsWritable = true;
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
        private DateTime _nextWarperLookup;
        private DateTime _requestWindowStarted, _nextAlternateRequest;
        private int _alternateRequests;
        private bool _blacklistedAssignment;
        private bool _helpRetried, _teleportStarted, _queueReplySeen, _unverifiedQueueReplySeen;
        private bool _joinedByProvider;
        private readonly List<TeamRequestEventArgs> _pendingWarperInvites = new List<TeamRequestEventArgs>();
        private uint _acceptedWarperInviteId;
        private DateTime _acceptedWarperInviteAt;
        private readonly HashSet<uint> _verifiedNumberedBotIds = new HashSet<uint>();
        private PrivateMsgMessage? _pendingQueueReply;
        private string _lastReply;
        private int _helpReplies, _menuPageCount;
        private readonly HashSet<int> _menuPages = new HashSet<int>();
        public string LastFailure { get; private set; }
        public bool VerifiedDestination(int targetId) => _state == State.Done && _targetId == targetId;

        // The next assignment is unknown; use the farthest observed landing so
        // an old favorable warper cannot make the estimate optimistic.
        public bool TryGetObservedArrival(int targetId, Vector3 anchor,
            out Vector3 arrival, out float distance, out string warper)
        {
            arrival = default;
            distance = 0;
            warper = null;
            if (!AcceptedMissions.Finite(anchor)) return false;
            ObservedLanding known = _landings.Where(x => x.DestinationPlayfield == targetId)
                .OrderByDescending(x => HorizontalDistance(x.ArrivalPosition, anchor))
                .FirstOrDefault();
            if (known == null) return false;
            arrival = new Vector3(known.ArrivalPosition[0], known.ArrivalPosition[1],
                known.ArrivalPosition[2]);
            distance = HorizontalDistance(known.ArrivalPosition, anchor);
            warper = known.Warper;
            return true;
        }

        public ScottyboiWarpProvider(string pluginDir, Action<string> say, MovementArbiter movement)
        {
            _say = say;
            _movement = movement;
            _landingPath = Path.Combine(pluginDir, "RKMissionData", "scottyboi-landings.json");
            LoadLandings();
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
                _helpRetried = _teleportStarted = _queueReplySeen = _unverifiedQueueReplySeen = false;
                _lastReply = null;
                _botId = _menuId = _replyId = _helpId = _recipientId = _warperId = 0;
                _warperName = null;
                _pendingWarperInvites.Clear();
                _acceptedWarperInviteId = 0;
                _acceptedWarperInviteAt = DateTime.MinValue;
                _verifiedNumberedBotIds.Clear();
                _pendingQueueReply = null;
                _pendingCommand = null;
                _alternateRequests = 0;
                _blacklistedAssignment = false;
                _requestWindowStarted = _nextAlternateRequest = DateTime.MinValue;
                _nextWarperLookup = DateTime.MinValue;
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
            if (_state == State.Warp && _teleportStarted &&
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
                _pendingWarperInvites.Clear();
                _movement.Release(MovementOwner.WarpTravel);
                if (_joinedByProvider && Team.IsInTeam) Team.Leave();
                _joinedByProvider = false;
                RecordLanding();
                _say($"Warp to playfield {_targetId} verified after zoning settled.");
                return WarpResult.Succeeded;
            }
            if (_state == State.Help && _targetId == (int)PlayfieldId.Mort &&
                DateTime.UtcNow - _started > TimeSpan.FromSeconds(10))
            {
                _say("Mort's menu page is missing; using the confirmed Hope command.");
                RequestWarp(new WarpCommand(MenuName, "hope", "hope"));
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
            if (_state == State.Invite && _warperName != null &&
                DateTime.UtcNow >= _nextWarperLookup)
            {
                Network.Send(new LookupMessage { Id = 0, Name = _warperName });
                _nextWarperLookup = DateTime.UtcNow.AddSeconds(12);
            }
            if (_state == State.Invite && _blacklistedAssignment &&
                DateTime.UtcNow >= _nextAlternateRequest)
            {
                if (_alternateRequests >= 3 ||
                    DateTime.UtcNow - _requestWindowStarted > TimeSpan.FromSeconds(180))
                    Fail("Milky Way: no alternate Scottyboi warper was assigned after rejecting Warpdude31.");
                else
                {
                    _alternateRequests++;
                    _nextAlternateRequest = DateTime.UtcNow.AddSeconds(12);
                    _started = DateTime.UtcNow;
                    Chat.SendPrivateMessage(_recipientId, _pendingCommand.Text);
                    _say($"Milky Way: requesting alternate Scottyboi warper ({_alternateRequests}/3) for the same verified destination command.");
                }
            }
            TimeSpan timeout = _state == State.Warp ? TimeSpan.FromSeconds(60) :
                _state == State.Invite ? TimeSpan.FromSeconds(_warperName != null ? 180 : 45) :
                _state == State.Help ? TimeSpan.FromSeconds(18) : TimeSpan.FromSeconds(12);
            if (DateTime.UtcNow - _started > timeout)
                Fail(_state == State.Help
                    ? $"Scottyboi offered no recognized command for playfield {_targetId} ({_targetName}); " +
                      $"menu pages seen: {(_menuPages.Count == 0 ? "none" : string.Join(",", _menuPages))}" +
                      (_menuPageCount > 0 ? $" of {_menuPageCount}" : "") +
                      (_lastReply == null ? "." : $". Last reply: {_lastReply}")
                    : _state == State.Invite
                        ? $"Scottyboi warp request '{_pendingCommand?.Text}' timed out: " +
                          (_warperName != null ? $"no accepted invite from assigned warper {_warperName}." :
                           _queueReplySeen ? "queue reply did not identify an assigned warper."
                               : _unverifiedQueueReplySeen ? "queue-like reply came from an unverified sender."
                               : "no confirmed queue reply or recognized team invite.")
                        : _state == State.CommandLookup
                            ? $"Could not resolve Scottyboi menu recipient {_pendingCommand?.Recipient}."
                        : "Warp bot did not complete the current step in time.");
            return _state == State.Failed ? WarpResult.Failed : WarpResult.InProgress;
        }

        private void OnChatMessage(object sender, ChatMessageBody message)
        {
            if (message is LookupMessage lookup)
            {
                if (lookup.Id != 0 && Regex.IsMatch(lookup.Name ?? "",
                    @"^Scottyboi(?:[1-9]|1[0-2])$", RegexOptions.IgnoreCase))
                {
                    _verifiedNumberedBotIds.Add(lookup.Id);
                    if (_pendingQueueReply != null && _pendingQueueReply.Sender == lookup.Id &&
                        (_state == State.Invite || _state == State.Warp))
                    {
                        PrivateMsgMessage pendingReply = _pendingQueueReply;
                        _pendingQueueReply = null;
                        _say($"Verified Scottyboi queue sender {lookup.Name} ({lookup.Id}); checking its assigned warper.");
                        HandleWarpReply(pendingReply);
                    }
                }
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
                {
                    if (lookup.Id != 0) _warperId = lookup.Id;
                    if (_warperId != 0 && _pendingWarperInvites.Count > 0)
                    {
                        TeamRequestEventArgs? pending = _pendingWarperInvites.FirstOrDefault(x =>
                            _warperId != 0 && _warperId == unchecked((uint)x.Requester.Instance));
                        if (pending == null && _warperId != 0)
                            _say($"Assigned warper {_warperName} resolved to identity {_warperId}; " +
                                "pending team invite identities did not match.");
                        _pendingWarperInvites.Clear();
                        if (pending != null && !Team.IsInTeam &&
                            (_state == State.Invite || _state == State.Warp))
                            AcceptWarpInvite(pending, _warperId, buffered: true);
                    }
                }
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
            _state = State.Invite;
            _started = DateTime.UtcNow;
            _requestWindowStarted = _started;
            Chat.SendPrivateMessage(recipientId, _pendingCommand.Text);
            _say($"Sent Scottyboi command '{_pendingCommand.Text}' to {_pendingCommand.Recipient} " +
                $"for playfield {_targetId}; waiting for a team invite and zoning.");
        }

        private void HandleWarpReply(PrivateMsgMessage reply)
        {
            // A numbered Scottyboi bot may answer for the menu recipient.
            // Verify that name through the chat server before trusting its
            // assignment of a team inviter.
            if (reply.Sender == 0 ||
                (reply.Sender != _recipientId && reply.Sender != _botId &&
                 reply.Sender != _menuId && reply.Sender != _replyId &&
                 !_verifiedNumberedBotIds.Contains(reply.Sender)))
            {
                if (!_unverifiedQueueReplySeen && reply.Text.IndexOf("queue to get warped to",
                    StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _unverifiedQueueReplySeen = true;
                    _pendingQueueReply = reply;
                    for (int index = 2; index <= 12; index++)
                        Network.Send(new LookupMessage { Id = 0, Name = $"Scottyboi{index}" });
                    _say($"Scottyboi-like queue reply from sender {reply.Sender}; " +
                        "checking numbered bot identities before accepting its warper assignment.");
                }
                return;
            }
            string text = Regex.Replace(WebUtility.HtmlDecode(reply.Text), "<[^>]+>", " ");
            if (text.IndexOf("queue to get warped to", StringComparison.OrdinalIgnoreCase) < 0)
                return;
            bool firstQueueReply = !_queueReplySeen;
            _queueReplySeen = true;
            if (!MatchesQueuedDestination(text, _targetAliases, _pendingCommand?.Destination ?? ""))
            {
                if (firstQueueReply)
                    _say($"Scottyboi queue reply did not match selected destination for playfield {_targetId}: " +
                        ShortReply(text));
                return;
            }
            Match warper = Regex.Match(text, @"warper\s*\(\s*(?<name>[a-z][a-z0-9_-]{2,24})\s*\)",
                RegexOptions.IgnoreCase);
            if (!warper.Success)
            {
                if (firstQueueReply)
                    _say("Scottyboi confirmed a queue but did not name a warper in the expected format: " +
                        ShortReply(text));
                return;
            }
            bool offline = Regex.IsMatch(text, @"\bis offline\b|\bneeds to log on\b",
                RegexOptions.IgnoreCase);
            string assignedWarper = warper.Groups["name"].Value;
            if (IsBlacklistedRoute(_targetId, assignedWarper))
            {
                // A late reply to an earlier request cannot replace a safe
                // alternate assignment that is already being verified.
                if (_warperName != null) return;
                _warperName = null;
                _warperId = 0;
                _pendingWarperInvites.Clear();
                _blacklistedAssignment = true;
                _say("Milky Way: assigned warper Warpdude31 is blacklisted due to verified post-warp FlyAvoidObstacle hard failure; requesting alternate.");
                if (_alternateRequests >= 3)
                    Fail("Milky Way: Scottyboi repeatedly assigned blacklisted Warpdude31.");
                return;
            }
            _blacklistedAssignment = false;
            if (!string.Equals(_warperName, assignedWarper, StringComparison.OrdinalIgnoreCase))
            {
                _warperName = assignedWarper;
                _warperId = 0;
                _started = DateTime.UtcNow;
                _nextWarperLookup = DateTime.MinValue;
                _say(offline
                    ? $"Scottyboi assigned warper {_warperName}, currently offline; the request is queued while it logs in. Waiting for its verified team invite."
                    : $"Scottyboi assigned warper {_warperName}; waiting for its team invite.");
            }
            Network.Send(new LookupMessage { Id = 0, Name = _warperName });
            _nextWarperLookup = DateTime.UtcNow.AddSeconds(12);
        }

        private static string ShortReply(string text)
        {
            string clean = Regex.Replace(text, @"\s+", " ").Trim();
            return clean.Length <= 160 ? clean : clean.Substring(0, 160) + "...";
        }

        // Destination/warper pair verified by the 22:27 local-travel hard failure
        // on mission 1442967240. No arrival coordinates were reported.
        private static bool IsBlacklistedRoute(int destination, string warper) =>
            destination == (int)PlayfieldId.MilkyWay &&
            string.Equals(warper, "Warpdude31", StringComparison.OrdinalIgnoreCase);

        private static bool MatchesQueuedDestination(string text, string[] aliases, string expectedLocation)
        {
            Match destination = Regex.Match(text,
                @"queue to get warped to\s+(?<zone>.+)", RegexOptions.IgnoreCase);
            if (!destination.Success) return false;
            string queued = Normalize(destination.Groups["zone"].Value);
            return aliases.Any(alias => queued.StartsWith(alias, StringComparison.Ordinal)) ||
                (expectedLocation.Length > 0 && queued.StartsWith(expectedLocation, StringComparison.Ordinal));
        }

        private void OnTeamRequest(object sender, TeamRequestEventArgs request)
        {
            if (request.Responded) return;
            uint requesterId = unchecked((uint)request.Requester.Instance);
            // AO# opens its native invitation window after this event unless
            // Responded is set. A repeat invite from the warper we already
            // accepted needs no second reply or visible prompt.
            if (requesterId != 0 && requesterId == _acceptedWarperInviteId &&
                DateTime.UtcNow - _acceptedWarperInviteAt < TimeSpan.FromMinutes(2) &&
                (_joinedByProvider || (Team.IsInTeam && Team.Members != null && Team.Members.Any(member =>
                    member.Identity.Instance == request.Requester.Instance))))
            {
                request.Ignore();
                return;
            }
            if ((_state != State.Invite && _state != State.Warp) || Team.IsInTeam || requesterId == 0) return;
            if (_blacklistedAssignment)
            {
                request.Ignore();
                return;
            }
            if (_targetId == (int)PlayfieldId.MilkyWay &&
                requesterId != _warperId)
            {
                request.Ignore();
                if (_warperId == 0 && _pendingWarperInvites.Count < 4 &&
                    !_pendingWarperInvites.Any(x => x.Requester == request.Requester))
                    _pendingWarperInvites.Add(request);
                return;
            }
            if (requesterId == _botId || requesterId == _recipientId ||
                requesterId == _menuId || requesterId == _replyId ||
                (_warperId != 0 && requesterId == _warperId))
            {
                AcceptWarpInvite(request, requesterId);
                return;
            }
            // The invite can arrive before Scottyboi's verified warper lookup.
            // Mark it handled now so AO# does not open a native invite window;
            // send the team reply only after the lookup matches its identity.
            if (_warperId == 0 && _pendingWarperInvites.Any(x => x.Requester == request.Requester))
            {
                request.Ignore();
                return;
            }
            if (_warperId == 0 && _pendingWarperInvites.Count < 4)
            {
                bool firstInvite = _pendingWarperInvites.Count == 0;
                request.Ignore();
                _pendingWarperInvites.Add(request);
                if (firstInvite && _warperName != null)
                    Network.Send(new LookupMessage { Id = 0, Name = _warperName });
                _say($"Team invite from identity {requesterId} is pending Scottyboi warper verification.");
            }
        }

        private void AcceptWarpInvite(TeamRequestEventArgs request, uint requesterId, bool buffered = false)
        {
            if (buffered)
                Team.Accept(request.Requester);
            else
                request.Accept();
            _acceptedWarperInviteId = requesterId;
            _acceptedWarperInviteAt = DateTime.UtcNow;
            _pendingWarperInvites.Clear();
            _pendingQueueReply = null;
            _joinedByProvider = true;
            _state = State.Warp;
            _started = DateTime.UtcNow;
            _say($"Accepted Scottyboi team invite from identity {requesterId}; waiting for the warp.");
        }

        private void OnTeleportStarted(object sender, EventArgs args)
        {
            if (_state == State.Warp && _joinedByProvider)
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
            _pendingWarperInvites.Clear();
            _pendingQueueReply = null;
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
                if (MatchesTarget(label, target) && TryCommandFromUrl(url, out command))
                { command.Destination = Normalize(label); return true; }
            }
            Match legacyLink = Regex.Match(line,
                @"(?:chatcmd:///tell|/tell)\s+(?<recipient>Scottyboi\d*|scty)\s+(?<command>!?[a-z][a-z0-9_-]{1,20})[^>]*>(?<label>[^<]+)",
                RegexOptions.IgnoreCase);
            if (legacyLink.Success && MatchesTarget(legacyLink.Groups["label"].Value, target))
            {
                command = new WarpCommand(legacyLink.Groups["recipient"].Value,
                    legacyLink.Groups["command"].Value, legacyLink.Groups["label"].Value);
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
                    ? tell.Groups["recipient"].Value : BotName, tell.Groups["command"].Value, parts[i]);
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
                    if (TryCommandFromUrl(url, out command))
                    {
                        string location = Normalize(label);
                        if (location == "warp" || location == "go" || location == "travel")
                            location = Normalize(heading);
                        command.Destination = location;
                        return true;
                    }
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

        private static string Normalize(string? text) =>
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

        private static float HorizontalDistance(float[] point, Vector3 anchor)
        {
            float dx = point[0] - anchor.X, dz = point[2] - anchor.Z;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        private static bool ValidLanding(ObservedLanding landing) =>
            landing != null && landing.DestinationPlayfield > 0 &&
            landing.DestinationPlayfield != (int)PlayfieldId.FixerGrid &&
            !string.IsNullOrWhiteSpace(landing.Warper) &&
            Regex.IsMatch(landing.Warper, "^[a-z][a-z0-9_-]{2,24}$",
                RegexOptions.IgnoreCase) &&
            !IsBlacklistedRoute(landing.DestinationPlayfield, landing.Warper) &&
            landing.ArrivalPosition != null && landing.ArrivalPosition.Length == 3 &&
            landing.ArrivalPosition.All(x => !float.IsNaN(x) && !float.IsInfinity(x) &&
                Math.Abs(x) <= 100000f) &&
            landing.ArrivalPosition.Any(x => x != 0f) &&
            landing.VerifiedUtc > new DateTime(2020, 1, 1) &&
            landing.VerifiedUtc <= DateTime.UtcNow.AddDays(1);

        private void LoadLandings()
        {
            try
            {
                if (!File.Exists(_landingPath)) return;
                var records = JsonConvert.DeserializeObject<List<ObservedLanding>>(
                    File.ReadAllText(_landingPath));
                if (records == null || records.Count > 1000 || records.Any(x => !ValidLanding(x)))
                    throw new InvalidDataException("Invalid Scottyboi landing records.");
                foreach (ObservedLanding record in records.OrderBy(x => x.VerifiedUtc))
                {
                    _landings.RemoveAll(x => x.DestinationPlayfield == record.DestinationPlayfield &&
                        string.Equals(x.Warper, record.Warper, StringComparison.OrdinalIgnoreCase));
                    _landings.Add(record);
                }
            }
            catch (Exception ex)
            {
                _landingsWritable = false;
                _say("Scottyboi landing data ignored and original file preserved: " + ex.Message);
            }
        }

        private void RecordLanding()
        {
            if (string.IsNullOrWhiteSpace(_warperName) || DynelManager.LocalPlayer == null ||
                Playfield.ModelIdentity.Instance != _targetId ||
                !AcceptedMissions.Finite(DynelManager.LocalPlayer.Position)) return;
            Vector3 position = DynelManager.LocalPlayer.Position;
            var record = new ObservedLanding
            {
                DestinationPlayfield = _targetId,
                Warper = _warperName,
                ArrivalPosition = new[] { position.X, position.Y, position.Z },
                VerifiedUtc = DateTime.UtcNow
            };
            if (!ValidLanding(record)) return;
            _landings.RemoveAll(x => x.DestinationPlayfield == _targetId &&
                string.Equals(x.Warper, _warperName, StringComparison.OrdinalIgnoreCase));
            _landings.Add(record);
            if (!_landingsWritable) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_landingPath));
                string temp = _landingPath + ".new";
                File.WriteAllText(temp, JsonConvert.SerializeObject(_landings, Formatting.Indented));
                if (File.Exists(_landingPath)) File.Replace(temp, _landingPath, null);
                else File.Move(temp, _landingPath);
                _say($"Recorded verified Scottyboi landing for {_targetId} via {_warperName}.");
            }
            catch (Exception ex) { _say("Could not save Scottyboi landing: " + ex.Message); }
        }

        public void Reset()
        {
            if (_joinedByProvider && Team.IsInTeam) Team.Leave();
            _joinedByProvider = false;
            _state = State.Idle;
            _teleportStarted = _helpRetried = _queueReplySeen = _unverifiedQueueReplySeen = false;
            _lastReply = null;
            _botId = _menuId = _replyId = _helpId = _recipientId = _warperId = 0;
            _warperName = null;
            _pendingWarperInvites.Clear();
            _acceptedWarperInviteId = 0;
            _acceptedWarperInviteAt = DateTime.MinValue;
            _verifiedNumberedBotIds.Clear();
            _pendingQueueReply = null;
            _pendingCommand = null;
            _alternateRequests = 0;
            _blacklistedAssignment = false;
            _requestWindowStarted = _nextAlternateRequest = DateTime.MinValue;
            _nextWarperLookup = DateTime.MinValue;
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
