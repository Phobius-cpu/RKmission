using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AOSharp.Common.GameData;
using AOSharp.Core;
using AOSharp.Core.Inventory;
using AOSharp.Core.UI;
using AOSharp.Pathfinding;
using Newtonsoft.Json;
using SmokeLounge.AOtomation.Messaging.Messages;
using SmokeLounge.AOtomation.Messaging.Messages.ChatMessages;

namespace RKmission
{
    internal enum FGridServiceResult { InProgress, Succeeded, Failed, Unavailable }

    // Requests Team Fixer Grid only after RKMission is beside a normal Grid
    // terminal. Once the temporary Data Receptacle appears, the destination
    // terminal identity from Neko's confirmed GridTerminals data is invoked.
    internal sealed class FGridServiceProvider : IDisposable
    {
        private enum State
        {
            Idle, ApproachTerminal, Lookup, Invite, Receptacle, Destination, Settling, Done, Failed
        }

        private sealed class ServiceFile
        {
            public List<ServiceConfig> Services { get; set; } = new List<ServiceConfig>();
        }

        private sealed class ServiceConfig
        {
            public string Name { get; set; }
            public string Command { get; set; }
            public List<string> InviteFrom { get; set; } = new List<string>();
            public int PlayfieldId { get; set; }
            public float[] GridTerminalPosition { get; set; }
        }

        private const int DataReceptacleTemplateId = 160978;
        private readonly List<ServiceConfig> _services = new List<ServiceConfig>();
        private readonly List<ServiceConfig> _candidates = new List<ServiceConfig>();
        private readonly HashSet<uint> _expectedInviters = new HashSet<uint>();
        private readonly Action<string> _say;
        private readonly MovementArbiter _movement;
        private State _state;
        private SimpleItem _terminal;
        private IReadOnlyList<int> _destinationTerminals;
        private int _targetId;
        private int _serviceIndex;
        private int _destinationAttempt;
        private uint _botId;
        private bool _joinedByProvider;
        private bool _teleportStarted;
        private DateTime _started;
        private DateTime _lastUse;
        private DateTime _zonedAt;
        private DateTime _backoffUntil;

        public string LastFailure { get; private set; }
        public bool IsConfigured => _services.Count > 0;
        public bool IsActive => _state != State.Idle && _state != State.Done && _state != State.Failed;

        public FGridServiceProvider(string pluginDir, Action<string> say, MovementArbiter movement)
        {
            _say = say;
            _movement = movement;
            Load(System.IO.Path.Combine(pluginDir, "Data", "FGridServices.json"));
            Network.ChatMessageReceived += OnChatMessage;
            Team.TeamRequest += OnTeamRequest;
            Game.TeleportStarted += OnTeleportStarted;
            Game.TeleportEnded += OnTeleportEnded;
        }

        private void Load(string path)
        {
            if (!File.Exists(path))
            {
                _say("FGrid service configuration is missing; public FGrid service fallback is disabled.");
                return;
            }

            try
            {
                ServiceFile file = JsonConvert.DeserializeObject<ServiceFile>(File.ReadAllText(path)) ??
                    new ServiceFile();
                foreach (ServiceConfig service in file.Services ?? new List<ServiceConfig>())
                {
                    if (service == null || string.IsNullOrWhiteSpace(service.Name) ||
                        string.IsNullOrWhiteSpace(service.Command))
                        continue;
                    service.Name = service.Name.Trim();
                    service.Command = service.Command.Trim();
                    service.InviteFrom = (service.InviteFrom ?? new List<string>())
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Select(x => x.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    _services.Add(service);
                }
            }
            catch (Exception ex)
            {
                _say("Could not load FGridServices.json: " + ex.Message);
            }

            if (_services.Count == 0)
                _say("No FGrid service bots are configured; add live bot names/commands to Data/FGridServices.json.");
            else
                _say($"Loaded {_services.Count} FGrid service bot configuration(s).");
        }

        public FGridServiceResult Tick(int targetId, IReadOnlyList<int> destinationTerminals)
        {
            if (Playfield.ModelIdentity.Instance == targetId)
            {
                Complete();
                return FGridServiceResult.Succeeded;
            }

            if (_state == State.Done && _targetId == targetId)
                return FGridServiceResult.Succeeded;
            if (_state == State.Failed && _targetId == targetId)
                return FGridServiceResult.Failed;
            if (!IsConfigured)
            {
                LastFailure = "No FGrid service bots are configured.";
                return FGridServiceResult.Unavailable;
            }
            if (destinationTerminals == null || destinationTerminals.Count == 0)
            {
                LastFailure = $"No confirmed Fixer Grid destination terminal is mapped for playfield {targetId}.";
                return FGridServiceResult.Unavailable;
            }

            if (_state == State.Idle || _targetId != targetId)
            {
                ResetAttempt();
                _targetId = targetId;
                _destinationTerminals = destinationTerminals;
                if (DateTime.UtcNow < _backoffUntil)
                {
                    LastFailure = "FGrid service bots are temporarily in cooldown after an unsuccessful request.";
                    _state = State.Failed;
                    return FGridServiceResult.Failed;
                }
                if (Team.IsInTeam)
                {
                    LastFailure = "Already in a team; RKMission will not disrupt it for public FGrid service.";
                    _state = State.Failed;
                    return FGridServiceResult.Unavailable;
                }

                _candidates.Clear();
                _candidates.AddRange(_services.Where(x => x.PlayfieldId <= 0 ||
                    x.PlayfieldId == Playfield.ModelIdentity.Instance));
                if (_candidates.Count == 0)
                {
                    LastFailure = $"No configured FGrid service bot is assigned to playfield {Playfield.ModelIdentity.Instance}.";
                    _state = State.Failed;
                    return FGridServiceResult.Unavailable;
                }

                _terminal = FindGridTerminal();
                if (_terminal == null)
                {
                    LastFailure = "No normal Grid terminal is visible in the current playfield.";
                    _state = State.Failed;
                    return FGridServiceResult.Unavailable;
                }

                _started = DateTime.UtcNow;
                _state = State.ApproachTerminal;
                _say($"FGrid service fallback selected for playfield {targetId}; positioning beside the Grid terminal before requesting service.");
            }

            if (_state == State.ApproachTerminal)
            {
                if (_terminal == null)
                {
                    Fail("The selected Grid terminal is no longer available.", false);
                    return FGridServiceResult.Failed;
                }

                if (Vector3.Distance(DynelManager.LocalPlayer.Position, _terminal.Position) > 3f)
                {
                    if (_movement.Owner != MovementOwner.FGridTravel || !SMovementController.IsNavigating())
                        _movement.SetDestination(MovementOwner.FGridTravel, _terminal.Position);
                    return FGridServiceResult.InProgress;
                }

                _movement.Halt(MovementOwner.FGridTravel);
                BeginLookup();
                return FGridServiceResult.InProgress;
            }

            if (_state == State.Lookup && DateTime.UtcNow - _started > TimeSpan.FromSeconds(6))
            {
                TryNextService($"Could not resolve FGrid service bot '{CurrentService?.Name}'.");
                return _state == State.Failed ? FGridServiceResult.Failed : FGridServiceResult.InProgress;
            }

            if ((_state == State.Invite || _state == State.Receptacle) &&
                Inventory.Find(DataReceptacleTemplateId, out Item receptacle))
            {
                UseDestination(receptacle);
                return FGridServiceResult.InProgress;
            }

            if (_state == State.Invite && DateTime.UtcNow - _started > TimeSpan.FromSeconds(18))
            {
                TryNextService($"FGrid service bot '{CurrentService?.Name}' did not invite/cast in time.");
                return _state == State.Failed ? FGridServiceResult.Failed : FGridServiceResult.InProgress;
            }

            if (_state == State.Receptacle && DateTime.UtcNow - _started > TimeSpan.FromSeconds(12))
            {
                TryNextService($"FGrid service bot '{CurrentService?.Name}' did not create a Data Receptacle.");
                return _state == State.Failed ? FGridServiceResult.Failed : FGridServiceResult.InProgress;
            }

            if (_state == State.Destination)
            {
                if (!_teleportStarted && DateTime.UtcNow - _lastUse > TimeSpan.FromSeconds(4) &&
                    Inventory.Find(DataReceptacleTemplateId, out Item retry) &&
                    _destinationAttempt < _destinationTerminals.Count)
                {
                    UseDestination(retry);
                    return FGridServiceResult.InProgress;
                }

                if (DateTime.UtcNow - _started > TimeSpan.FromSeconds(12))
                {
                    Fail($"Data Receptacle use did not zone to playfield {_targetId}.");
                    return FGridServiceResult.Failed;
                }
            }

            if (_state == State.Settling)
            {
                if (DateTime.UtcNow - _zonedAt < TimeSpan.FromSeconds(2))
                    return FGridServiceResult.InProgress;

                if (Playfield.ModelIdentity.Instance != _targetId)
                {
                    Fail($"FGrid destination ended in playfield {Playfield.ModelIdentity.Instance}, not {_targetId}.");
                    return FGridServiceResult.Failed;
                }

                Complete();
                return FGridServiceResult.Succeeded;
            }

            return _state == State.Failed ? FGridServiceResult.Failed : FGridServiceResult.InProgress;
        }

        private ServiceConfig CurrentService =>
            _serviceIndex >= 0 && _serviceIndex < _candidates.Count ? _candidates[_serviceIndex] : null;

        private SimpleItem FindGridTerminal()
        {
            IEnumerable<SimpleItem> terminals = DynelManager.Terminals.Where(x =>
                string.Equals(x.Name, "Enter The Grid", StringComparison.OrdinalIgnoreCase));
            float[] configured = CurrentService?.GridTerminalPosition;
            if (configured != null && configured.Length == 3)
            {
                var expected = new Vector3(configured[0], configured[1], configured[2]);
                return terminals
                    .Where(x => Vector3.Distance(x.Position, expected) < 30f)
                    .OrderBy(x => Vector3.Distance(x.Position, expected))
                    .FirstOrDefault();
            }
            return terminals
                .OrderBy(x => Vector3.Distance(x.Position, DynelManager.LocalPlayer.Position))
                .FirstOrDefault();
        }

        private void BeginLookup()
        {
            ServiceConfig service = CurrentService;
            if (service == null)
            {
                Fail("No usable FGrid service configuration remains.");
                return;
            }

            _botId = 0;
            _expectedInviters.Clear();
            _started = DateTime.UtcNow;
            _state = State.Lookup;
            Network.Send(new LookupMessage { Id = 0, Name = service.Name });
            foreach (string inviter in service.InviteFrom)
                if (!string.Equals(inviter, service.Name, StringComparison.OrdinalIgnoreCase))
                    Network.Send(new LookupMessage { Id = 0, Name = inviter });
            _say($"Resolving FGrid service bot {service.Name}; request will be sent only while beside the Grid terminal.");
        }

        private void SendRequest()
        {
            ServiceConfig service = CurrentService;
            if (service == null || _botId == 0) return;

            Chat.SendPrivateMessage(_botId, service.Command);
            _state = State.Invite;
            _started = DateTime.UtcNow;
            _say($"Sent FGrid service request '{service.Command}' to {service.Name}; waiting for the expected invite/cast.");
        }

        private void OnChatMessage(object sender, ChatMessageBody message)
        {
            if (!(message is LookupMessage lookup) || CurrentService == null ||
                (_state != State.Lookup && _state != State.Invite && _state != State.Receptacle))
                return;

            if (string.Equals(lookup.Name, CurrentService.Name, StringComparison.OrdinalIgnoreCase))
            {
                _botId = lookup.Id;
                if (_botId != 0)
                {
                    _expectedInviters.Add(_botId);
                    SendRequest();
                }
                return;
            }

            if (CurrentService.InviteFrom.Any(x =>
                string.Equals(x, lookup.Name, StringComparison.OrdinalIgnoreCase)) && lookup.Id != 0)
                _expectedInviters.Add(lookup.Id);
        }

        private void OnTeamRequest(object sender, TeamRequestEventArgs request)
        {
            if ((_state != State.Invite && _state != State.Receptacle) || Team.IsInTeam)
                return;

            if (!_expectedInviters.Contains(unchecked((uint)request.Requester.Instance)))
                return;

            request.Accept();
            _joinedByProvider = true;
            _state = State.Receptacle;
            _started = DateTime.UtcNow;
            _say($"Accepted expected FGrid service invite from identity {request.Requester.Instance}; waiting for Data Receptacle.");
        }

        private void UseDestination(Item receptacle)
        {
            if (_destinationTerminals == null || _destinationAttempt >= _destinationTerminals.Count)
            {
                Fail($"All mapped Fixer Grid terminal identities for playfield {_targetId} were tried without zoning.");
                return;
            }

            _movement.Halt(MovementOwner.FGridTravel);
            int terminalId = _destinationTerminals[_destinationAttempt++];
            Item.UseItemOnItem(receptacle.Slot,
                new Identity(IdentityType.Terminal, terminalId));
            _lastUse = DateTime.UtcNow;
            _started = DateTime.UtcNow;
            _teleportStarted = false;
            _state = State.Destination;
            _say($"Data Receptacle detected; using mapped Fixer Grid destination terminal {unchecked((uint)terminalId)} for playfield {_targetId}.");
        }

        private void OnTeleportStarted(object sender, EventArgs args)
        {
            if (_state == State.Destination)
                _teleportStarted = true;
        }

        private void OnTeleportEnded(object sender, EventArgs args)
        {
            if (_state != State.Destination || !_teleportStarted) return;
            _state = State.Settling;
            _zonedAt = DateTime.UtcNow;
        }

        private void TryNextService(string reason)
        {
            _say(reason);
            if (_joinedByProvider && Team.IsInTeam)
                Team.Leave();
            _joinedByProvider = false;
            _serviceIndex++;
            _botId = 0;
            _expectedInviters.Clear();
            if (_serviceIndex < _candidates.Count)
            {
                _terminal = FindGridTerminal();
                if (_terminal == null)
                {
                    TryNextService($"Configured Grid terminal for FGrid service '{CurrentService?.Name}' is not visible.");
                    return;
                }
                _state = State.ApproachTerminal;
                _started = DateTime.UtcNow;
                return;
            }
            Fail("All configured FGrid service bots were unavailable or did not complete the request.");
        }

        private void Complete()
        {
            _state = State.Done;
            LastFailure = null;
            _movement.Release(MovementOwner.FGridTravel);
            if (_joinedByProvider && Team.IsInTeam)
                Team.Leave();
            _joinedByProvider = false;
            _say($"Fixer Grid service route to playfield {_targetId} verified after zoning settled.");
        }

        private void Fail(string reason, bool applyBackoff = true)
        {
            LastFailure = reason;
            _state = State.Failed;
            if (applyBackoff)
                _backoffUntil = DateTime.UtcNow.AddMinutes(2);
            _movement.Release(MovementOwner.FGridTravel);
            if (_joinedByProvider && Team.IsInTeam)
                Team.Leave();
            _joinedByProvider = false;
            _say(reason + " Travel planner will try another verified provider if one exists.");
        }

        private void ResetAttempt()
        {
            if (_joinedByProvider && Team.IsInTeam)
                Team.Leave();
            _joinedByProvider = false;
            _state = State.Idle;
            _terminal = null;
            _destinationTerminals = null;
            _targetId = 0;
            _serviceIndex = 0;
            _destinationAttempt = 0;
            _botId = 0;
            _expectedInviters.Clear();
            _candidates.Clear();
            _teleportStarted = false;
            LastFailure = null;
            _movement.Release(MovementOwner.FGridTravel);
        }

        public void Reset()
        {
            ResetAttempt();
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
