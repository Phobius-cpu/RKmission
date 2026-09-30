using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AOSharp.Common.GameData;
using AOSharp.Core;
using AOSharp.Core.Inventory;
using AOSharp.Pathfinding;
using Newtonsoft.Json;
using SmokeLounge.AOtomation.Messaging.Messages;
using SmokeLounge.AOtomation.Messaging.Messages.ChatMessages;

namespace RKmission
{
    internal enum FGridServiceResult { InProgress, Entered, Failed, Unavailable }

    // Requests a public Team Fixer Grid service only after RKMission is already
    // standing beside a normal Grid terminal. The temporary Data Receptacle is
    // then consumed on that terminal and success is accepted only after zoning
    // into the actual Fixer Grid playfield.
    internal sealed class FGridServiceProvider : IDisposable
    {
        private enum State
        {
            Idle, ApproachTerminal, Lookup, Invite, Receptacle, Zone, Settling, Done, Failed
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
        }

        private const int DataReceptacleTemplateId = 160978;
        private readonly List<ServiceConfig> _services = new List<ServiceConfig>();
        private readonly HashSet<uint> _expectedInviters = new HashSet<uint>();
        private readonly Action<string> _say;
        private readonly MovementArbiter _movement;
        private State _state;
        private SimpleItem _terminal;
        private int _serviceIndex;
        private uint _botId;
        private bool _joinedByProvider;
        private bool _teleportStarted;
        private int _useAttempts;
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
            Load(Path.Combine(pluginDir, "Data", "FGridServices.json"));
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

        public FGridServiceResult Tick()
        {
            if (Playfield.ModelIdentity.Instance == (int)PlayfieldId.FixerGrid)
            {
                Complete();
                return FGridServiceResult.Entered;
            }

            if (_state == State.Done) return FGridServiceResult.Entered;
            if (_state == State.Failed) return FGridServiceResult.Failed;
            if (!IsConfigured)
            {
                LastFailure = "No FGrid service bots are configured.";
                return FGridServiceResult.Unavailable;
            }

            if (_state == State.Idle)
            {
                if (DateTime.UtcNow < _backoffUntil)
                {
                    LastFailure = "FGrid service bots are temporarily in cooldown after an unsuccessful request.";
                    return FGridServiceResult.Failed;
                }
                if (Team.IsInTeam)
                {
                    LastFailure = "Already in a team; RKMission will not disrupt it for public FGrid service.";
                    return FGridServiceResult.Unavailable;
                }

                _terminal = FindGridTerminal();
                if (_terminal == null)
                {
                    LastFailure = "No normal Grid terminal is visible in the current playfield.";
                    return FGridServiceResult.Unavailable;
                }

                _serviceIndex = 0;
                _botId = 0;
                _expectedInviters.Clear();
                _teleportStarted = false;
                _useAttempts = 0;
                LastFailure = null;
                _started = DateTime.UtcNow;
                _state = State.ApproachTerminal;
                _say("FGrid fallback selected; positioning beside a normal Grid terminal before requesting service.");
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
                UseReceptacle(receptacle);
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

            if (_state == State.Zone)
            {
                if (!_teleportStarted && DateTime.UtcNow - _lastUse > TimeSpan.FromSeconds(2) &&
                    _useAttempts < 2 && Inventory.Find(DataReceptacleTemplateId, out Item retry))
                {
                    UseReceptacle(retry);
                    return FGridServiceResult.InProgress;
                }

                if (DateTime.UtcNow - _started > TimeSpan.FromSeconds(10))
                {
                    Fail("The Data Receptacle was used but Fixer Grid zoning was not observed.");
                    return FGridServiceResult.Failed;
                }
            }

            if (_state == State.Settling)
            {
                if (DateTime.UtcNow - _zonedAt < TimeSpan.FromSeconds(2))
                    return FGridServiceResult.InProgress;

                if (Playfield.ModelIdentity.Instance != (int)PlayfieldId.FixerGrid)
                {
                    Fail($"FGrid service zoning ended in playfield {Playfield.ModelIdentity.Instance}, not the Fixer Grid.");
                    return FGridServiceResult.Failed;
                }

                Complete();
                return FGridServiceResult.Entered;
            }

            return _state == State.Failed ? FGridServiceResult.Failed : FGridServiceResult.InProgress;
        }

        private ServiceConfig CurrentService =>
            _serviceIndex >= 0 && _serviceIndex < _services.Count ? _services[_serviceIndex] : null;

        private SimpleItem FindGridTerminal() =>
            DynelManager.Terminals
                .Where(x => string.Equals(x.Name, "Enter The Grid", StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => Vector3.Distance(x.Position, DynelManager.LocalPlayer.Position))
                .FirstOrDefault();

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
            if (!(message is LookupMessage lookup) || _state != State.Lookup || CurrentService == null)
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

            if (!_expectedInviters.Contains(request.Requester.Instance))
                return;

            request.Accept();
            _joinedByProvider = true;
            _state = State.Receptacle;
            _started = DateTime.UtcNow;
            _say($"Accepted expected FGrid service invite from identity {request.Requester.Instance}; waiting for Data Receptacle.");
        }

        private void UseReceptacle(Item receptacle)
        {
            if (_terminal == null)
            {
                Fail("The Grid terminal disappeared before the Data Receptacle could be used.");
                return;
            }

            _movement.Halt(MovementOwner.FGridTravel);
            receptacle.UseOn(_terminal.Identity);
            _useAttempts++;
            _lastUse = DateTime.UtcNow;
            _started = DateTime.UtcNow;
            _teleportStarted = false;
            _state = State.Zone;
            _say($"Data Receptacle detected and used on Grid terminal {_terminal.Identity.Instance}; waiting for Fixer Grid zoning.");
        }

        private void OnTeleportStarted(object sender, EventArgs args)
        {
            if (_state == State.Zone)
                _teleportStarted = true;
        }

        private void OnTeleportEnded(object sender, EventArgs args)
        {
            if (_state != State.Zone || !_teleportStarted) return;
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
            if (_serviceIndex < _services.Count)
            {
                BeginLookup();
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
            _say("Fixer Grid entry verified after zoning settled.");
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

        public void Reset()
        {
            if (_joinedByProvider && Team.IsInTeam)
                Team.Leave();
            _joinedByProvider = false;
            _state = State.Idle;
            _terminal = null;
            _serviceIndex = 0;
            _botId = 0;
            _expectedInviters.Clear();
            _teleportStarted = false;
            _useAttempts = 0;
            LastFailure = null;
            _movement.Release(MovementOwner.FGridTravel);
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
