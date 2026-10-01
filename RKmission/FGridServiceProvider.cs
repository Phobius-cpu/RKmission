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
using Newtonsoft.Json.Linq;
using SmokeLounge.AOtomation.Messaging.Messages;
using SmokeLounge.AOtomation.Messaging.Messages.ChatMessages;

namespace RKmission
{
    internal enum FGridServiceResult { InProgress, Succeeded, Failed, Unavailable }

    // Requests Team Fixer Grid beside a normal Grid terminal, uses the Data
    // Receptacle on that nearby terminal, then traverses a mapped FGrid floor.
    internal sealed class FGridServiceProvider : IDisposable
    {
        private enum State
        {
            Idle, ApproachTerminal, Lookup, Invite, Receptacle, Entering,
            Ascending, Exit, Settling, Done, Failed
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
        private sealed class ExitRoute
        {
            public int Floor { get; set; }
            public List<string> Names { get; set; } = new List<string>();
        }
        private sealed class LearnedExit
        {
            public int Floor { get; set; }
            public int Identity { get; set; }
            public float X { get; set; }
            public float Y { get; set; }
            public float Z { get; set; }
        }
        private sealed class SurveyExit
        {
            public int PortalIdentity { get; set; }
            public int Floor { get; set; }
            public float[] FGridPosition { get; set; } = Array.Empty<float>();
            public int DestinationPlayfield { get; set; }
            public float[] ArrivalPosition { get; set; } = Array.Empty<float>();
            public DateTime VerifiedAtUtc { get; set; }
        }
        private static readonly int[] SurveyExitsPerFloor = { 8, 8, 8, 8, 8, 8, 8, 8, 8, 6 };
        // Lift coordinates from AOSharp.Navigator's FixerGridElevators.
        private static readonly Vector3[] UpLifts =
        {
            new Vector3(307f, 1.313771f, 67f),
            new Vector3(313.8423f, 12.31377f, 66.95627f),
            new Vector3(313.4414f, 22.31378f, 64.6021f),
            new Vector3(312.2277f, 32.31377f, 62.54662f),
            new Vector3(310.3886f, 42.31377f, 61.06009f),
            new Vector3(308.1369f, 52.31377f, 60.25761f),
            new Vector3(305.7449f, 62.31377f, 60.24995f),
            new Vector3(303.5269f, 72.31377f, 61.13066f),
            new Vector3(301.7581f, 82.31377f, 62.66596f),
            new Vector3(300.5934f, 92.31377f, 64.73015f)
        };
        private readonly List<ServiceConfig> _services = new List<ServiceConfig>();
        private readonly List<ServiceConfig> _candidates = new List<ServiceConfig>();
        private readonly Dictionary<int, ExitRoute> _exitRoutes = new Dictionary<int, ExitRoute>();
        private readonly Dictionary<int, LearnedExit> _learnedExits = new Dictionary<int, LearnedExit>();
        private readonly string _learnedExitPath;
        private readonly Dictionary<int, SurveyExit> _surveyExits = new Dictionary<int, SurveyExit>();
        private readonly string _surveyPath;
        private readonly Dictionary<int, Vector3> _entrancePositions = new Dictionary<int, Vector3>();
        private readonly HashSet<uint> _expectedInviters = new HashSet<uint>();
        private readonly Action<string> _say;
        private readonly MovementArbiter _movement;
        private State _state;
        private SimpleItem _terminal;
        private SimpleItem _exit;
        private ExitRoute _route;
        private Vector3 _terminalPosition;
        private int _targetId;
        private int _serviceIndex;
        private int _lastFloor = -1;
        private uint _botId;
        private bool _joinedByProvider;
        private bool _teleportStarted, _exitLogged;
        private DateTime _started;
        private DateTime _lastUse;
        private DateTime _zonedAt;
        private DateTime _backoffUntil;
        private LearnedExit _observedExit;
        private DateTime _observedExitZoneEnded;
        private int _observedDestination;
        private bool _observedLearned;
        private bool _learnedExitsWritable = true;
        private bool _surveyWritable = true;

        public string LastFailure { get; private set; }
        public bool IsConfigured => _services.Count > 0;
        public bool IsActive => _state != State.Idle && _state != State.Done && _state != State.Failed;
        public bool CanRoute(int targetId) => _exitRoutes.ContainsKey(targetId);
        public string MappedDestinations => _exitRoutes.Count == 0 ? "none" :
            string.Join(",", _exitRoutes.Keys.OrderBy(x => x));
        public string SurveySummary => $"FGrid survey: {_surveyExits.Count}/78 verified portal identities" +
            (_surveyWritable ? "." : "; survey file unreadable, saving disabled.");
        public string SurveyFloorCounts => "Floors: " + string.Join(", ",
            Enumerable.Range(1, 10).Select(floor =>
                $"{floor}={_surveyExits.Values.Count(x => x.Floor == floor)}/{SurveyExitsPerFloor[floor - 1]}")) + ".";
        public string SurveyFilePath => _surveyPath;

        public FGridServiceProvider(string pluginDir, Action<string> say, MovementArbiter movement)
        {
            _say = say;
            _movement = movement;
            _learnedExitPath = System.IO.Path.Combine(pluginDir, "RKMissionData", "fixer-grid-exits.json");
            _surveyPath = System.IO.Path.Combine(pluginDir, "RKMissionData", "fixer-grid-survey.json");
            Load(System.IO.Path.Combine(pluginDir, "Data", "FGridServices.json"));
            LoadRoutes(pluginDir);
            LoadLearnedExits();
            LoadSurvey();
            Network.ChatMessageReceived += OnChatMessage;
            Team.TeamRequest += OnTeamRequest;
            Game.TeleportStarted += OnTeleportStarted;
            Game.TeleportEnded += OnTeleportEnded;
            Game.OnUpdate += OnUpdate;
        }

        private void LoadRoutes(string pluginDir)
        {
            try
            {
                string exitPath = System.IO.Path.Combine(pluginDir, "Data", "FixerGridExits.json");
                if (File.Exists(exitPath))
                {
                    var routes = JsonConvert.DeserializeObject<Dictionary<int, ExitRoute>>(File.ReadAllText(exitPath));
                    foreach (var entry in routes ?? new Dictionary<int, ExitRoute>())
                        if (entry.Value != null && entry.Value.Floor >= 1 && entry.Value.Floor <= 10 &&
                            entry.Value.Names != null && entry.Value.Names.Any(x => !string.IsNullOrWhiteSpace(x)))
                            _exitRoutes[entry.Key] = entry.Value;
                }

                string linksPath = System.IO.Path.Combine(pluginDir, "Data", "PlayfieldLinks.json");
                if (!File.Exists(linksPath)) return;
                JObject playfields = JObject.Parse(File.ReadAllText(linksPath));
                foreach (JProperty field in playfields.Properties())
                {
                    if (!int.TryParse(field.Name, out int playfieldId)) continue;
                    foreach (JToken link in field.Value["Links"] ?? new JArray())
                    {
                        if (!string.Equals(link.Value<string>("TerminalName"), "Enter The Grid",
                                StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(link.Value<string>("$type"), "GridTerminalLink",
                                StringComparison.OrdinalIgnoreCase)) continue;
                        JToken position = link["TerminalPos"];
                        if (position == null || position.Count() != 3) continue;
                        _entrancePositions[playfieldId] = new Vector3(position[0].Value<float>(),
                            position[1].Value<float>(), position[2].Value<float>());
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                _say("Could not load Fixer Grid route data: " + ex.Message);
            }
        }

        private void LoadLearnedExits()
        {
            try
            {
                if (!File.Exists(_learnedExitPath)) return;
                var saved = JsonConvert.DeserializeObject<Dictionary<int, LearnedExit>>(
                    File.ReadAllText(_learnedExitPath));
                if (saved == null) throw new InvalidDataException("empty Fixer Grid exit learning file");
                foreach (var entry in saved)
                    if (_exitRoutes.TryGetValue(entry.Key, out ExitRoute route) &&
                        entry.Value != null && entry.Value.Floor == route.Floor &&
                        !float.IsNaN(entry.Value.X) && !float.IsNaN(entry.Value.Z) &&
                        !float.IsInfinity(entry.Value.X) && !float.IsInfinity(entry.Value.Z))
                        _learnedExits[entry.Key] = entry.Value;
                if (_learnedExits.Count > 0)
                    _say($"Loaded {_learnedExits.Count} learned Fixer Grid exit route(s).");
            }
            catch (Exception ex)
            {
                _learnedExits.Clear();
                _learnedExitsWritable = false;
                _say("Fixer Grid exit learning file could not be read; file preserved: " + ex.Message);
            }
        }

        private void SaveLearnedExits()
        {
            if (!_learnedExitsWritable) return;
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_learnedExitPath));
                string temp = _learnedExitPath + ".new";
                File.WriteAllText(temp, JsonConvert.SerializeObject(_learnedExits, Formatting.Indented));
                if (File.Exists(_learnedExitPath)) File.Replace(temp, _learnedExitPath, null);
                else File.Move(temp, _learnedExitPath);
            }
            catch (Exception ex)
            {
                _say("Fixer Grid exit learning could not be saved: " + ex.Message);
            }
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static bool ValidPosition(float[] position) => position != null &&
            position.Length == 3 && position.All(Finite);

        private static bool SamePortalPosition(SurveyExit saved, LearnedExit observed) =>
            saved.Floor == observed.Floor &&
            Math.Abs(saved.FGridPosition[0] - observed.X) < 1.5f &&
            Math.Abs(saved.FGridPosition[1] - observed.Y) < 1.5f &&
            Math.Abs(saved.FGridPosition[2] - observed.Z) < 1.5f;

        private void LoadSurvey()
        {
            try
            {
                if (!File.Exists(_surveyPath)) return;
                var saved = JsonConvert.DeserializeObject<Dictionary<int, SurveyExit>>(
                    File.ReadAllText(_surveyPath));
                if (saved == null || saved.Any(entry => entry.Key <= 0 || entry.Value == null ||
                    entry.Value.PortalIdentity != entry.Key || entry.Value.Floor < 1 ||
                    entry.Value.Floor > 10 || entry.Value.DestinationPlayfield <= 0 ||
                    entry.Value.DestinationPlayfield == (int)PlayfieldId.FixerGrid ||
                    !ValidPosition(entry.Value.FGridPosition) ||
                    !ValidPosition(entry.Value.ArrivalPosition)))
                    throw new InvalidDataException("invalid Fixer Grid survey record");
                foreach (var entry in saved)
                    _surveyExits.Add(entry.Key, entry.Value);
                _say($"Loaded {_surveyExits.Count} verified Fixer Grid survey portal(s).");
            }
            catch (Exception ex)
            {
                _surveyExits.Clear();
                _surveyWritable = false;
                _say("Fixer Grid survey file could not be read; file preserved and saving disabled: " + ex.Message);
            }
        }

        private void SaveSurvey()
        {
            if (!_surveyWritable) return;
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_surveyPath));
                string temp = _surveyPath + ".new";
                File.WriteAllText(temp, JsonConvert.SerializeObject(_surveyExits, Formatting.Indented));
                if (File.Exists(_surveyPath)) File.Replace(temp, _surveyPath, null);
                else File.Move(temp, _surveyPath);
            }
            catch (Exception ex)
            {
                _say("Fixer Grid survey could not be saved: " + ex.Message);
            }
        }

        private void RecordSurveyExit(LearnedExit observed, int destination, Vector3 arrival)
        {
            if (!_surveyWritable || observed.Identity <= 0 || observed.Floor < 1 ||
                observed.Floor > 10 || !Finite(observed.X) || !Finite(observed.Y) ||
                !Finite(observed.Z) || !Finite(arrival.X) || !Finite(arrival.Y) ||
                !Finite(arrival.Z) ||
                (arrival.X == 0f && arrival.Y == 0f && arrival.Z == 0f)) return;

            if (_surveyExits.TryGetValue(observed.Identity, out SurveyExit previous))
            {
                if (!SamePortalPosition(previous, observed) || previous.DestinationPlayfield != destination)
                    _say($"FGrid survey portal {observed.Identity} conflicts with its saved position or destination; existing record preserved.");
                return;
            }

            // The live identity is the key. Position also catches an old identity
            // for the same physical portal, so the survey cannot count it twice.
            SurveyExit atPosition = _surveyExits.Values.FirstOrDefault(x => SamePortalPosition(x, observed));
            if (atPosition != null)
            {
                if (atPosition.DestinationPlayfield != destination)
                {
                    _say($"FGrid survey portal {observed.Identity} conflicts with saved portal {atPosition.PortalIdentity} at the same position; existing record preserved.");
                    return;
                }
                _surveyExits.Remove(atPosition.PortalIdentity);
                _say($"FGrid survey portal identity {atPosition.PortalIdentity} replaced by live identity {observed.Identity} at the same position.");
            }

            _surveyExits[observed.Identity] = new SurveyExit
            {
                PortalIdentity = observed.Identity,
                Floor = observed.Floor,
                FGridPosition = new[] { observed.X, observed.Y, observed.Z },
                DestinationPlayfield = destination,
                ArrivalPosition = new[] { arrival.X, arrival.Y, arrival.Z },
                VerifiedAtUtc = DateTime.UtcNow
            };
            SaveSurvey();
            _say($"FGrid survey verified portal {observed.Identity}, floor {observed.Floor}, " +
                $"destination {destination}, arrival ({arrival.X:0.0},{arrival.Y:0.0},{arrival.Z:0.0}); " +
                $"{_surveyExits.Count}/78 recorded.");
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

        public FGridServiceResult Tick(int targetId)
        {
            if (Playfield.ModelIdentity.Instance == targetId &&
                (_state == State.Exit || _state == State.Settling))
            {
                if (_state != State.Settling)
                {
                    _state = State.Settling;
                    _zonedAt = DateTime.UtcNow;
                }
                if (DateTime.UtcNow - _zonedAt < TimeSpan.FromSeconds(2))
                    return FGridServiceResult.InProgress;
                Complete();
                return FGridServiceResult.Succeeded;
            }

            if (_state == State.Done && _targetId == targetId)
                return FGridServiceResult.Succeeded;
            if (_state == State.Failed && _targetId == targetId)
                return FGridServiceResult.Failed;
            if (!_exitRoutes.TryGetValue(targetId, out ExitRoute exitRoute))
            {
                LastFailure = $"No verified Fixer Grid exit route is mapped for playfield {targetId}.";
                return FGridServiceResult.Unavailable;
            }

            if (Playfield.ModelIdentity.Instance == (int)PlayfieldId.FixerGrid &&
                (_state == State.Idle || _targetId != targetId))
            {
                ResetAttempt();
                _targetId = targetId;
                _route = exitRoute;
                _state = State.Ascending;
                _started = DateTime.UtcNow;
                _say($"Resuming Fixer Grid travel toward playfield {targetId} from the current floor.");
            }
            if (!IsConfigured && _state == State.Idle)
            {
                LastFailure = "No FGrid service bots are configured.";
                return FGridServiceResult.Unavailable;
            }

            if (_state == State.Idle || _targetId != targetId)
            {
                ResetAttempt();
                _targetId = targetId;
                _route = exitRoute;
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
                if (_terminal != null)
                    _terminalPosition = _terminal.Position;
                else if (CurrentService.GridTerminalPosition is float[] configured && configured.Length == 3)
                    _terminalPosition = new Vector3(configured[0], configured[1], configured[2]);
                else if (!_entrancePositions.TryGetValue(Playfield.ModelIdentity.Instance,
                    out _terminalPosition))
                {
                    LastFailure = "No normal Grid terminal is visible or mapped in the current playfield.";
                    _state = State.Failed;
                    return FGridServiceResult.Unavailable;
                }

                _started = DateTime.UtcNow;
                _state = State.ApproachTerminal;
                _say($"FGrid service fallback selected for playfield {targetId}; positioning beside the Grid terminal before requesting service.");
            }

            if (_state == State.ApproachTerminal)
            {
                if (DateTime.UtcNow - _started > TimeSpan.FromMinutes(5))
                {
                    Fail("Could not reach the mapped normal Grid entrance within five minutes.", false);
                    return FGridServiceResult.Failed;
                }
                if (Vector3.Distance(DynelManager.LocalPlayer.Position, _terminalPosition) > 3f)
                {
                    if (_movement.Owner != MovementOwner.FGridTravel || !SMovementController.IsNavigating())
                        _movement.SetDestination(MovementOwner.FGridTravel, _terminalPosition);
                    return FGridServiceResult.InProgress;
                }

                _terminal = FindGridTerminal();
                if (_terminal == null)
                {
                    Fail("The mapped normal Grid terminal was not visible at its expected location.", false);
                    return FGridServiceResult.Failed;
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
                EnterFixerGrid(receptacle);
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

            if (_state == State.Entering)
            {
                if (Playfield.ModelIdentity.Instance == (int)PlayfieldId.FixerGrid)
                {
                    _state = State.Ascending;
                    _started = DateTime.UtcNow;
                    _lastFloor = -1;
                    _say($"Entered Fixer Grid; moving to floor {_route.Floor} for playfield {_targetId}.");
                    return FGridServiceResult.InProgress;
                }
                if (DateTime.UtcNow - _started > TimeSpan.FromSeconds(12))
                {
                    Fail("Data Receptacle use did not enter Fixer Grid.");
                    return FGridServiceResult.Failed;
                }
            }

            if (_state == State.Ascending)
            {
                if (Playfield.ModelIdentity.Instance != (int)PlayfieldId.FixerGrid)
                {
                    Fail($"Fixer Grid floor traversal left for unexpected playfield {Playfield.ModelIdentity.Instance}.");
                    return FGridServiceResult.Failed;
                }
                int floor = Math.Max(0, (int)(DynelManager.LocalPlayer.Position.Y / 10f));
                if (floor != _lastFloor)
                {
                    _lastFloor = floor;
                    _started = DateTime.UtcNow;
                    _say($"Fixer Grid floor {floor}; target floor {_route.Floor}.");
                }
                if (floor == _route.Floor)
                {
                    _movement.Release(MovementOwner.FGridTravel);
                    _state = State.Exit;
                    _started = DateTime.UtcNow;
                    return FGridServiceResult.InProgress;
                }
                if (floor > _route.Floor || floor >= UpLifts.Length ||
                    DateTime.UtcNow - _started > TimeSpan.FromSeconds(30))
                {
                    Fail($"Could not reach Fixer Grid floor {_route.Floor}; stopped on floor {floor}.");
                    return FGridServiceResult.Failed;
                }
                Vector3 lift = UpLifts[floor];
                if (Vector3.Distance(DynelManager.LocalPlayer.Position, lift) > 0.8f &&
                    (_movement.Owner != MovementOwner.FGridTravel || !SMovementController.IsNavigating()))
                    _movement.SetDestination(MovementOwner.FGridTravel, lift);
            }

            if (_state == State.Exit)
            {
                if (_teleportStarted)
                {
                    if (DateTime.UtcNow - _started > TimeSpan.FromSeconds(35))
                    {
                        Fail($"Fixer Grid exit zoning did not finish for playfield {_targetId}.");
                        return FGridServiceResult.Failed;
                    }
                    return FGridServiceResult.InProgress;
                }
                if (Playfield.ModelIdentity.Instance != (int)PlayfieldId.FixerGrid)
                {
                    Fail($"Fixer Grid exit reached unexpected playfield {Playfield.ModelIdentity.Instance} instead of {_targetId}.");
                    return FGridServiceResult.Failed;
                }
                _exit = FindExit();
                if (_exit == null)
                {
                    if (DateTime.UtcNow - _started > TimeSpan.FromSeconds(20))
                    {
                        string seen = DescribeFloorExits();
                        Fail($"No verified Fixer Grid exit for playfield {_targetId} was found on floor {_route.Floor}" +
                            (seen.Length == 0 ? "." : $"; {seen}."));
                        return FGridServiceResult.Failed;
                    }
                    return FGridServiceResult.InProgress;
                }
                if (!_exitLogged)
                {
                    _exitLogged = true;
                    _say($"Fixer Grid exit candidate {_exit.Name} ({_exit.Identity}) on floor {_route.Floor}; approaching.");
                }
                if (Vector3.Distance(DynelManager.LocalPlayer.Position, _exit.Position) > 1.5f)
                {
                    if (_movement.Owner != MovementOwner.FGridTravel || !SMovementController.IsNavigating())
                        _movement.SetDestination(MovementOwner.FGridTravel, _exit.Position);
                }
                else if (DateTime.UtcNow - _lastUse > TimeSpan.FromSeconds(3))
                {
                    _exit.Use();
                    _lastUse = DateTime.UtcNow;
                }
                if (DateTime.UtcNow - _started > TimeSpan.FromSeconds(35))
                {
                    Fail($"Fixer Grid exit {_exit.Name} did not zone to playfield {_targetId}.");
                    return FGridServiceResult.Failed;
                }
            }

            if (_state == State.Settling && DateTime.UtcNow - _zonedAt > TimeSpan.FromSeconds(2))
            {
                Fail($"Fixer Grid exit ended in playfield {Playfield.ModelIdentity.Instance}, not {_targetId}.");
                return FGridServiceResult.Failed;
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

        private SimpleItem FindExit()
        {
            if (_route == null) return null;
            float height = DynelManager.LocalPlayer.Position.Y;
            if (_learnedExits.TryGetValue(_targetId, out LearnedExit learned) &&
                learned.Floor == _route.Floor)
            {
                SimpleItem mapped = DynelManager.Terminals.FirstOrDefault(x =>
                    Math.Abs(x.Position.Y - height) < 4f &&
                    string.Equals(x.Name, "Exit the Grid", StringComparison.OrdinalIgnoreCase) &&
                    Math.Abs(x.Position.X - learned.X) < 1.5f &&
                    Math.Abs(x.Position.Z - learned.Z) < 1.5f);
                if (mapped != null) return mapped;
            }
            SimpleItem named = DynelManager.Terminals.Where(x =>
                    Math.Abs(x.Position.Y - height) < 4f &&
                    _route.Names.Any(name => x.Name != null &&
                        x.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0))
                .OrderBy(x => Vector3.Distance(x.Position, DynelManager.LocalPlayer.Position))
                .FirstOrDefault();
            if (named != null) return named;

            // FGrid portals can all be named "Exit the Grid". Match a portal
            // only when another live dynel labels its destination nearby.
            var labels = DynelManager.AllDynels.Where(x =>
                    Math.Abs(x.Position.Y - height) < 4f && x.Name != null &&
                    _route.Names.Any(name => x.Name.IndexOf(name,
                        StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
            var portals = DynelManager.Terminals.Where(x =>
                    Math.Abs(x.Position.Y - height) < 4f &&
                    string.Equals(x.Name, "Exit the Grid", StringComparison.OrdinalIgnoreCase)).ToList();
            var verified = new List<SimpleItem>();
            foreach (Dynel label in labels)
            {
                var closest = portals.OrderBy(portal =>
                    Vector3.Distance(label.Position, portal.Position)).Take(2).ToList();
                if (closest.Count == 0) continue;
                float firstDistance = Vector3.Distance(label.Position, closest[0].Position);
                float secondDistance = closest.Count > 1
                    ? Vector3.Distance(label.Position, closest[1].Position) : float.MaxValue;
                if (firstDistance <= 5f && secondDistance - firstDistance >= 3f)
                    verified.Add(closest[0]);
            }
            return verified
                .OrderBy(x => Vector3.Distance(x.Position, DynelManager.LocalPlayer.Position))
                .FirstOrDefault();
        }

        private string DescribeFloorExits()
        {
            float height = DynelManager.LocalPlayer.Position.Y;
            var dynels = DynelManager.AllDynels.Where(x =>
                Math.Abs(x.Position.Y - height) < 4f &&
                !string.IsNullOrWhiteSpace(x.Name)).ToList();
            var portals = DynelManager.Terminals.Where(x =>
                    Math.Abs(x.Position.Y - height) < 4f &&
                    string.Equals(x.Name, "Exit the Grid", StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.Position.X).ThenBy(x => x.Position.Z).Take(8).ToList();
            if (portals.Count == 0)
                return "no Exit the Grid terminals were visible";
            string candidates = "portal candidates: " + string.Join("; ", portals.Select(portal =>
            {
                string labels = string.Join("/", dynels.Where(x =>
                        x.Identity != portal.Identity &&
                        !string.Equals(x.Name, "Exit the Grid", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(x.Name, "Elevator", StringComparison.OrdinalIgnoreCase) &&
                        Vector3.Distance(x.Position, portal.Position) <= 12f)
                    .OrderBy(x => Vector3.Distance(x.Position, portal.Position))
                    .Select(x => x.Name).Distinct().Take(3));
                return $"{portal.Identity} at ({portal.Position.X:0.0},{portal.Position.Z:0.0})" +
                    (labels.Length == 0 ? "" : $" labels={labels}");
            }));
            return candidates + $"; scan position ({DynelManager.LocalPlayer.Position.X:0.0}," +
                $"{DynelManager.LocalPlayer.Position.Z:0.0})";
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

        private void EnterFixerGrid(Item receptacle)
        {
            if (_terminal == null)
            {
                Fail("The normal Grid terminal disappeared before Data Receptacle use.");
                return;
            }

            _movement.Halt(MovementOwner.FGridTravel);
            Item.UseItemOnItem(receptacle.Slot, _terminal.Identity);
            _lastUse = DateTime.UtcNow;
            _started = DateTime.UtcNow;
            _teleportStarted = _exitLogged = false;
            _state = State.Entering;
            _say($"Data Receptacle detected; using nearby Grid entrance {_terminal.Identity} to enter Fixer Grid.");
        }

        private void OnTeleportStarted(object sender, EventArgs args)
        {
            _observedExit = null;
            _observedExitZoneEnded = DateTime.MinValue;
            _observedDestination = 0;
            _observedLearned = false;
            if (Playfield.ModelIdentity.Instance == (int)PlayfieldId.FixerGrid &&
                DynelManager.LocalPlayer != null)
            {
                Vector3 playerPosition = DynelManager.LocalPlayer.Position;
                var nearest = DynelManager.Terminals.Where(x =>
                        Math.Abs(x.Position.Y - playerPosition.Y) < 4f &&
                        string.Equals(x.Name, "Exit the Grid", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(x => Vector3.Distance(x.Position, playerPosition)).Take(2).ToList();
                if (nearest.Count > 0 && Vector3.Distance(nearest[0].Position, playerPosition) <= 3f &&
                    (nearest.Count == 1 || Vector3.Distance(nearest[1].Position, playerPosition) > 5f))
                    _observedExit = new LearnedExit
                    {
                        Floor = Math.Max(0, (int)(playerPosition.Y / 10f)),
                        Identity = nearest[0].Identity.Instance,
                        X = nearest[0].Position.X,
                        Y = nearest[0].Position.Y,
                        Z = nearest[0].Position.Z
                    };
            }
            if (_state == State.Entering || _state == State.Exit)
                _teleportStarted = true;
        }

        private void OnTeleportEnded(object sender, EventArgs args)
        {
            if (_observedExit != null)
                _observedExitZoneEnded = DateTime.UtcNow;
            if (!_teleportStarted) return;
            if (_state == State.Entering)
            {
                _teleportStarted = false;
                return;
            }
            if (_state == State.Exit)
            {
                _state = State.Settling;
                _zonedAt = DateTime.UtcNow;
                _teleportStarted = false;
            }
        }

        private void OnUpdate(object sender, float deltaTime)
        {
            if (_observedExit == null || _observedExitZoneEnded == DateTime.MinValue) return;
            int destination = Playfield.ModelIdentity.Instance;
            if (destination > 0 && destination != (int)PlayfieldId.FixerGrid)
            {
                if (!_observedLearned)
                {
                    _observedLearned = true;
                    if (_exitRoutes.TryGetValue(destination, out ExitRoute route) &&
                        route.Floor == _observedExit.Floor)
                    {
                        _learnedExits[destination] = _observedExit;
                        SaveLearnedExits();
                        _say($"Learned Fixer Grid floor {route.Floor} portal {_observedExit.Identity} " +
                            $"at ({_observedExit.X:0.0},{_observedExit.Z:0.0}) for playfield {destination} " +
                            "from verified zoning.");
                    }
                }
                if (Playfield.IsDungeon || Game.IsZoning || DynelManager.LocalPlayer == null)
                {
                    if (DateTime.UtcNow - _observedExitZoneEnded > TimeSpan.FromSeconds(10))
                        _observedExit = null;
                    return;
                }
                if (_observedDestination == 0)
                    _observedDestination = destination;
                if (_observedDestination != destination)
                {
                    _observedExit = null;
                    return;
                }
                if (DateTime.UtcNow - _observedExitZoneEnded < TimeSpan.FromSeconds(2)) return;

                RecordSurveyExit(_observedExit, destination, DynelManager.LocalPlayer.Position);
                _observedExit = null;
            }
            else if (DateTime.UtcNow - _observedExitZoneEnded > TimeSpan.FromSeconds(10))
                _observedExit = null;
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
                if (_terminal != null)
                    _terminalPosition = _terminal.Position;
                else if (CurrentService.GridTerminalPosition is float[] configured && configured.Length == 3)
                    _terminalPosition = new Vector3(configured[0], configured[1], configured[2]);
                else if (!_entrancePositions.TryGetValue(Playfield.ModelIdentity.Instance,
                    out _terminalPosition))
                {
                    TryNextService($"No Grid entrance is visible or mapped for FGrid service '{CurrentService?.Name}'.");
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
            _exit = null;
            _route = null;
            _terminalPosition = Vector3.Zero;
            _targetId = 0;
            _serviceIndex = 0;
            _lastFloor = -1;
            _botId = 0;
            _expectedInviters.Clear();
            _candidates.Clear();
            _teleportStarted = _exitLogged = false;
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
            Game.OnUpdate -= OnUpdate;
        }
    }
}
