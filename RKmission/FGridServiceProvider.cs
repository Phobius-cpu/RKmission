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
            public string Source { get; set; } = string.Empty;
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
        private readonly Dictionary<int, LearnedExit> _legacyLearnedExits = new Dictionary<int, LearnedExit>();
        private readonly Dictionary<int, List<SurveyExit>> _knownExits = new Dictionary<int, List<SurveyExit>>();
        private readonly Dictionary<int, SurveyExit> _runtimeExits = new Dictionary<int, SurveyExit>();
        private readonly string _learnedExitPath;
        private readonly string _runtimeExitPath;
        private readonly string _canonicalExitPath;
        private readonly string _navMeshPath;
        private readonly Dictionary<int, SurveyExit> _surveyExits = new Dictionary<int, SurveyExit>();
        private readonly string _surveyPath;
        private readonly Dictionary<int, Vector3> _entrancePositions = new Dictionary<int, Vector3>();
        private readonly HashSet<uint> _expectedInviters = new HashSet<uint>();
        private readonly Action<string> _say;
        private readonly MovementArbiter _movement;
        private readonly NavigationRouteRecorder _routes;
        private readonly FGridRecastPlanner _recast;
        private List<Vector3> _recastWaypoints;
        private Vector3? _recastDestination;
        private int _recastIndex, _recastIssuedIndex = -1, _recastFloor = -1;
        private Vector3? _recastRejectedTarget;
        private Vector3 _recastLastPosition;
        private DateTime _recastLastProgress;
        private string _activeNavSource = "none";
        private State _state;
        private SimpleItem _terminal;
        private SimpleItem _exit;
        private ExitRoute _route;
        private List<SurveyExit> _rankedExits = new List<SurveyExit>();
        private SurveyExit _selectedExit;
        private int _exitIndex;
        private Vector3? _missionAnchor;
        private bool _selectedPortalVerified;
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
        private Vector3? _meshDestination, _meshRejectedDestination;
        private Vector3 _meshLastPosition;
        private DateTime _meshLastProgress;
        private LearnedExit _observedExit;
        private DateTime _observedExitZoneEnded;
        private int _observedDestination;
        private bool _runtimeExitsWritable = true;
        private bool _surveyWritable = true;

        public string LastFailure { get; private set; }
        public bool IsConfigured => _services.Count > 0;
        public bool CanStartFromCurrentPlayfield =>
            Playfield.ModelIdentity.Instance == (int)PlayfieldId.FixerGrid ||
            _services.Any(x => x.PlayfieldId <= 0 ||
                x.PlayfieldId == Playfield.ModelIdentity.Instance);
        public bool IsActive => _state != State.Idle && _state != State.Done && _state != State.Failed;
        public bool CanRoute(int targetId) => _knownExits.ContainsKey(targetId) || _exitRoutes.ContainsKey(targetId);
        public bool TryGetBestArrival(int targetId, Vector3 anchor, out Vector3 arrival,
            out float distance, out int portalIdentity)
        {
            arrival = default;
            distance = 0;
            portalIdentity = 0;
            if (!AcceptedMissions.Finite(anchor) ||
                !_knownExits.TryGetValue(targetId, out List<SurveyExit> exits)) return false;
            int minimumFloor = Playfield.ModelIdentity.Instance == (int)PlayfieldId.FixerGrid &&
                DynelManager.LocalPlayer != null
                ? Math.Max(0, (int)(DynelManager.LocalPlayer.Position.Y / 10f)) : 0;
            SurveyExit best = exits.Where(x => x.Floor >= minimumFloor)
                .OrderBy(x => OutdoorCost(x, anchor)).FirstOrDefault();
            if (best == null) return false;
            arrival = new Vector3(best.ArrivalPosition[0], best.ArrivalPosition[1],
                best.ArrivalPosition[2]);
            distance = OutdoorCost(best, anchor);
            portalIdentity = best.PortalIdentity;
            return true;
        }
        public int SurveyedDestinationCount => _knownExits.Count;
        public string MappedDestinations => !_exitRoutes.Any() && !_knownExits.Any() ? "none" :
            string.Join(",", _exitRoutes.Keys.Concat(_knownExits.Keys).Distinct().OrderBy(x => x));
        public string TargetStatus(int targetId) =>
            $"target={targetId}, verified exits={(_knownExits.TryGetValue(targetId, out List<SurveyExit> exits) ? exits.Count : 0)}, " +
            (_selectedExit == null ? "selected=none" :
                $"selected portal={_selectedExit.PortalIdentity} floor={_selectedExit.Floor}, " +
                $"arrival=({string.Join(",", _selectedExit.ArrivalPosition.Select(x => x.ToString("0.0")))})" +
                (_missionAnchor.HasValue ? $", remaining={OutdoorCost(_selectedExit, _missionAnchor.Value):0}m" : ", remaining=unknown"));
        public string SurveySummary => $"FGrid survey: {_knownExits.Values.Sum(x => x.Count)}/78 verified exits across {_knownExits.Count} destinations" +
            (_surveyWritable ? "." : "; survey file unreadable, saving disabled.");
        public string SurveyFloorCounts => "Floors: " + string.Join(", ",
            Enumerable.Range(1, 10).Select(floor =>
                $"{floor}={_knownExits.Values.SelectMany(x => x).Count(x => x.Floor == floor)}/{SurveyExitsPerFloor[floor - 1]}")) + ".";
        public string SurveyFilePath => _surveyPath;

        public FGridServiceProvider(string pluginDir, Action<string> say, MovementArbiter movement, NavigationRouteRecorder routes)
        {
            _say = say;
            _movement = movement;
            _routes = routes;
            _recast = new FGridRecastPlanner(pluginDir);
            _learnedExitPath = System.IO.Path.Combine(pluginDir, "RKMissionData", "fixer-grid-exits.json");
            _runtimeExitPath = System.IO.Path.Combine(pluginDir, "RKMissionData", "fixer-grid-exits-v2.json");
            _canonicalExitPath = System.IO.Path.Combine(pluginDir, "Data", "FixerGridSurveyExits.json");
            _navMeshPath = System.IO.Path.Combine(pluginDir, "NavMeshes", $"{(int)PlayfieldId.FixerGrid}.nav");
            _surveyPath = System.IO.Path.Combine(pluginDir, "RKMissionData", "fixer-grid-survey.json");
            Load(System.IO.Path.Combine(pluginDir, "Data", "FGridServices.json"));
            LoadRoutes(pluginDir);
            LoadCanonicalExits();
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

        private static bool ValidExit(SurveyExit exit) => exit != null &&
            exit.PortalIdentity > 0 && exit.Floor >= 1 && exit.Floor <= 10 &&
            exit.DestinationPlayfield > 0 &&
            exit.DestinationPlayfield != (int)PlayfieldId.FixerGrid &&
            ValidPosition(exit.FGridPosition) && ValidPosition(exit.ArrivalPosition) &&
            exit.ArrivalPosition.Any(x => x != 0f);

        private static bool SamePortalPosition(SurveyExit first, SurveyExit second) =>
            first.Floor == second.Floor &&
            Math.Abs(first.FGridPosition[0] - second.FGridPosition[0]) < 1.5f &&
            Math.Abs(first.FGridPosition[1] - second.FGridPosition[1]) < 1.5f &&
            Math.Abs(first.FGridPosition[2] - second.FGridPosition[2]) < 1.5f;

        private bool MergeKnown(SurveyExit candidate)
        {
            SurveyExit byIdentity = _knownExits.Values.SelectMany(x => x)
                .FirstOrDefault(x => x.PortalIdentity == candidate.PortalIdentity);
            if (byIdentity != null)
            {
                if (byIdentity.DestinationPlayfield != candidate.DestinationPlayfield ||
                    !SamePortalPosition(byIdentity, candidate))
                    _say($"FGrid portal {candidate.PortalIdentity} conflicts with a verified exit; existing record preserved.");
                return false;
            }
            SurveyExit byPosition = _knownExits.Values.SelectMany(x => x)
                .FirstOrDefault(x => SamePortalPosition(x, candidate));
            if (byPosition != null)
            {
                if (byPosition.DestinationPlayfield != candidate.DestinationPlayfield)
                    _say($"FGrid portal {candidate.PortalIdentity} conflicts with portal {byPosition.PortalIdentity} at the same position; existing record preserved.");
                return false;
            }
            if (!_knownExits.TryGetValue(candidate.DestinationPlayfield, out List<SurveyExit> exits))
                _knownExits[candidate.DestinationPlayfield] = exits = new List<SurveyExit>();
            exits.Add(candidate);
            return true;
        }

        private void LoadCanonicalExits()
        {
            if (!File.Exists(_canonicalExitPath)) return;
            try
            {
                List<SurveyExit> exits = JsonConvert.DeserializeObject<List<SurveyExit>>(
                    File.ReadAllText(_canonicalExitPath));
                if (exits == null || exits.Any(x => !ValidExit(x)))
                    throw new InvalidDataException("invalid canonical FGrid exit record");
                foreach (SurveyExit exit in exits)
                {
                    if (string.IsNullOrWhiteSpace(exit.Source)) exit.Source = "shipped survey";
                    MergeKnown(exit);
                }
                _say($"Loaded {_knownExits.Values.Sum(x => x.Count)} shipped FGrid exits for {_knownExits.Count} playfields.");
            }
            catch (Exception ex)
            {
                _knownExits.Clear();
                _say("Canonical Fixer Grid exit data could not be read: " + ex.Message);
            }
        }

        private void LoadLearnedExits()
        {
            try
            {
                if (File.Exists(_learnedExitPath))
                {
                    JObject saved = JObject.Parse(File.ReadAllText(_learnedExitPath));
                    foreach (JProperty entry in saved.Properties())
                    {
                        if (!int.TryParse(entry.Name, out int destination) || destination <= 0)
                            throw new InvalidDataException("invalid legacy destination key");
                        if (entry.Value is JArray many)
                        {
                            foreach (JToken raw in many)
                            {
                                SurveyExit exit = raw.ToObject<SurveyExit>();
                                if (exit.DestinationPlayfield == 0) exit.DestinationPlayfield = destination;
                                if (!ValidExit(exit) || exit.DestinationPlayfield != destination)
                                    throw new InvalidDataException("invalid multi-exit learning record");
                                if (string.IsNullOrWhiteSpace(exit.Source)) exit.Source = "legacy multi-exit file";
                                MergeKnown(exit);
                            }
                        }
                        else
                        {
                            LearnedExit exit = entry.Value.ToObject<LearnedExit>();
                            if (exit == null || exit.Floor < 1 || exit.Floor > 10 ||
                                !Finite(exit.X) || !Finite(exit.Y) || !Finite(exit.Z))
                                throw new InvalidDataException("invalid legacy Fixer Grid exit record");
                            _legacyLearnedExits[destination] = exit;
                        }
                    }
                    _say($"Read {_legacyLearnedExits.Count} old FGrid route(s) without arrival data; original file preserved.");
                }
                if (File.Exists(_runtimeExitPath))
                {
                    List<SurveyExit> saved = JsonConvert.DeserializeObject<List<SurveyExit>>(
                        File.ReadAllText(_runtimeExitPath));
                    if (saved == null || saved.Any(x => !ValidExit(x)))
                        throw new InvalidDataException("invalid multi-exit learning file");
                    foreach (SurveyExit exit in saved)
                    {
                        if (string.IsNullOrWhiteSpace(exit.Source)) exit.Source = "runtime learning";
                        if (_runtimeExits.ContainsKey(exit.PortalIdentity))
                            throw new InvalidDataException("duplicate portal identity in multi-exit learning file");
                        _runtimeExits.Add(exit.PortalIdentity, exit);
                        MergeKnown(exit);
                    }
                }
            }
            catch (Exception ex)
            {
                _runtimeExitsWritable = false;
                _say("FGrid learning file could not be read; existing files preserved and v2 saving disabled: " + ex.Message);
            }
        }

        private void SaveRuntimeExits()
        {
            if (!_runtimeExitsWritable) return;
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_runtimeExitPath));
                string temp = _runtimeExitPath + ".new";
                File.WriteAllText(temp, JsonConvert.SerializeObject(_runtimeExits.Values
                    .OrderBy(x => x.Floor).ThenBy(x => x.PortalIdentity), Formatting.Indented));
                if (File.Exists(_runtimeExitPath)) File.Replace(temp, _runtimeExitPath, null);
                else File.Move(temp, _runtimeExitPath);
            }
            catch (Exception ex)
            {
                _say("Fixer Grid multi-exit learning could not be saved: " + ex.Message);
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
                {
                    _surveyExits.Add(entry.Key, entry.Value);
                    if (string.IsNullOrWhiteSpace(entry.Value.Source)) entry.Value.Source = "local survey";
                    MergeKnown(entry.Value);
                }
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

            SurveyExit verified = new SurveyExit
            {
                PortalIdentity = observed.Identity,
                Floor = observed.Floor,
                FGridPosition = new[] { observed.X, observed.Y, observed.Z },
                DestinationPlayfield = destination,
                ArrivalPosition = new[] { arrival.X, arrival.Y, arrival.Z },
                VerifiedAtUtc = DateTime.UtcNow,
                Source = "runtime survey"
            };
            _surveyExits[observed.Identity] = verified;
            if (MergeKnown(verified) && _runtimeExitsWritable)
            {
                _runtimeExits[verified.PortalIdentity] = verified;
                SaveRuntimeExits();
            }
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

        // Horizontal outdoor distance matches LocalRoutePlanner's initial fly cost.
        // Keep this isolated so a reliable ground route estimate can replace it later.
        private static float OutdoorCost(SurveyExit exit, Vector3 anchor)
        {
            float dx = exit.ArrivalPosition[0] - anchor.X;
            float dz = exit.ArrivalPosition[2] - anchor.Z;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        private void PrepareRoute(int targetId, Vector3? missionAnchor)
        {
            if (missionAnchor.HasValue && !AcceptedMissions.Finite(missionAnchor.Value))
                missionAnchor = null;
            _missionAnchor = missionAnchor;
            int minimumFloor = Playfield.ModelIdentity.Instance == (int)PlayfieldId.FixerGrid &&
                DynelManager.LocalPlayer != null
                ? Math.Max(0, (int)(DynelManager.LocalPlayer.Position.Y / 10f)) : 0;
            _rankedExits = _knownExits.TryGetValue(targetId, out List<SurveyExit> exits)
                ? (missionAnchor.HasValue
                    ? exits.Where(x => x.Floor >= minimumFloor)
                        .OrderBy(x => OutdoorCost(x, missionAnchor.Value))
                    : exits.Where(x => x.Floor >= minimumFloor)
                        .OrderBy(x => x.Floor).ThenBy(x => x.PortalIdentity)).ToList()
                : new List<SurveyExit>();
            _exitIndex = 0;
            _selectedExit = _rankedExits.FirstOrDefault();
            _route = _selectedExit == null
                ? (!_knownExits.ContainsKey(targetId) && _exitRoutes.TryGetValue(targetId, out ExitRoute legacy)
                    ? legacy : null)
                : new ExitRoute { Floor = _selectedExit.Floor };
            if (_selectedExit == null && minimumFloor > 0)
                _say($"No verified FGrid exit for playfield {targetId} is reachable from floor {minimumFloor}; lifts only ascend.");
            if (_selectedExit != null)
                _say($"FGrid chose portal {_selectedExit.PortalIdentity}, floor {_selectedExit.Floor}, " +
                    $"arrival ({string.Join(",", _selectedExit.ArrivalPosition.Select(x => x.ToString("0.0")))}), " +
                    (missionAnchor.HasValue
                        ? $"estimated {_rankedExits.Count} exit(s), {OutdoorCost(_selectedExit, missionAnchor.Value):0}m remaining to mission."
                        : "no mission anchor available; using deterministic portal order."));
        }

        private bool TryNextExit(string reason)
        {
            int floor = Playfield.ModelIdentity.Instance == (int)PlayfieldId.FixerGrid &&
                DynelManager.LocalPlayer != null
                ? Math.Max(0, (int)(DynelManager.LocalPlayer.Position.Y / 10f)) : 0;
            for (int next = _exitIndex + 1; next < _rankedExits.Count; next++)
            {
                if (_rankedExits[next].Floor < floor) continue; // FGrid lifts ascend only.
                _say(reason + $" Trying verified alternative {next + 1}/{_rankedExits.Count}.");
                _exitIndex = next;
                _selectedExit = _rankedExits[next];
                _route = new ExitRoute { Floor = _selectedExit.Floor };
                _exit = null;
                _exitLogged = _selectedPortalVerified = _teleportStarted = false;
                _lastFloor = -1;
                _started = DateTime.UtcNow;
                _state = State.Ascending;
                _movement.Release(MovementOwner.FGridTravel);
                ResetMeshNavigation();
                _routes.StopPlayback();
                _say($"FGrid fallback portal {_selectedExit.PortalIdentity}, floor {_selectedExit.Floor}, " +
                    (_missionAnchor.HasValue
                        ? $"estimated {OutdoorCost(_selectedExit, _missionAnchor.Value):0}m to mission."
                        : "mission distance unknown."));
                return true;
            }
            Fail(reason + " No reachable verified alternative remains.");
            return false;
        }

        public string NavMeshStatus(Vector3? probe = null)
        {
            if (Playfield.ModelIdentity.Instance != (int)PlayfieldId.FixerGrid ||
                DynelManager.LocalPlayer == null)
                return "FGrid navmesh check requires standing inside Fixer Grid.";
            _recast.EnsureReady();
            Vector3 player = DynelManager.LocalPlayer.Position;
            int floor = Math.Max(0, (int)(player.Y / 10f));
            Vector3? goal = probe ?? (_route != null && floor < _route.Floor && floor < UpLifts.Length
                ? UpLifts[floor]
                : _exit?.Position ?? (_selectedExit != null && _selectedExit.Floor == floor
                    ? new Vector3(_selectedExit.FGridPosition[0], _selectedExit.FGridPosition[1], _selectedExit.FGridPosition[2])
                    : (Vector3?)null));
            string recastPath = goal.HasValue && _recast.Available
                ? RecastPathStatus(player, goal.Value, IsSurveyedPortal(goal.Value, floor))
                : goal.HasValue ? "not queried (mesh unavailable)" : "no active lift/portal target";
            string sharp = !File.Exists(_navMeshPath) ? "missing" :
                SMovementController.NavAgent?.HasPathfinder == true ? "loaded" : "file present, pathfinder unavailable";
            string sharpPath = "not queried";
            if (goal.HasValue && sharp == "loaded")
                try { sharpPath = LocalRoutePlanner.TryFGridGroundCost(player, goal.Value, out float cost)
                    ? $"valid {cost:0.0} m" : "no complete supported corridor"; }
                catch (Exception ex) { sharpPath = "query failed: " + ex.Message; }
            return $"FGrid nav: Recast available={_recast.Available}, status={_recast.Status}, file={(_recast.FileExists ? "present" : "missing")}, path={recastPath}; " +
                $"SharpNav={sharp}, path={sharpPath}; active leg={_activeNavSource}; recorded fallback available when endpoints match.";
        }

        private bool IsSurveyedPortal(Vector3 target, int floor) =>
            _surveyExits.Values.Any(exit => exit.Floor == floor && exit.FGridPosition?.Length == 3 &&
                Vector3.Distance(target, new Vector3(exit.FGridPosition[0], exit.FGridPosition[1],
                    exit.FGridPosition[2])) < 0.5f);

        private string RecastPathStatus(Vector3 player, Vector3 target, bool portal)
        {
            if (portal) _recast.TryPortalApproach(player, target, out _);
            else _recast.TryPath(player, target, out _);
            return _recast.LastPath;
        }

        private void ResetMeshNavigation()
        {
            _meshDestination = _meshRejectedDestination = null;
            _meshLastProgress = DateTime.MinValue;
            _recastWaypoints = null;
            _recastDestination = null;
            _recastRejectedTarget = null;
            _recastIssuedIndex = -1;
            _recastFloor = -1;
            _activeNavSource = "none";
        }

        private bool TryNavigateRecast(Vector3 target, bool portal, out string reason)
        {
            reason = _recast.LastPath;
            if (_routes.IsRecording || Game.IsZoning || DynelManager.LocalPlayer == null) return false;
            Vector3 player = DynelManager.LocalPlayer.Position;
            int floor = (int)(player.Y / 10f);
            if (_recastRejectedTarget.HasValue && Vector3.Distance(_recastRejectedTarget.Value, target) < 0.5f)
            { reason = "Recast movement to this target previously failed"; return false; }
            if (_recastWaypoints != null && (!_recastDestination.HasValue ||
                Vector3.Distance(_recastDestination.Value, target) > 0.5f || floor != _recastFloor))
            {
                _movement.Release(MovementOwner.FGridTravel);
                _recastWaypoints = null;
                _recastDestination = null;
            }
            if (_recastWaypoints == null)
            {
                if (!(portal
                    ? _recast.TryPortalApproach(player, target, out _recastWaypoints)
                    : _recast.TryPath(player, target, out _recastWaypoints)))
                { reason = _recast.LastPath; return false; }
                _recastDestination = target;
                _recastFloor = floor;
                _recastIndex = 1;
                _recastIssuedIndex = -1;
                _recastLastPosition = player;
                _recastLastProgress = DateTime.UtcNow;
                _meshDestination = null;
                _routes.StopPlayback();
                _say("FGrid Recast: " + _recast.LastPath + ".");
            }
            while (_recastIndex < _recastWaypoints.Count - 1 &&
                Vector3.Distance(player, _recastWaypoints[_recastIndex]) < 0.9f &&
                LocalRoutePlanner.SupportedFGridSegment(player, _recastWaypoints[_recastIndex + 1],
                    _recastWaypoints[0].Y)) _recastIndex++;
            if (Vector3.Distance(player, target) < 0.6f) return true;
            Vector3 next = _recastWaypoints[_recastIndex];
            Vector3 previous = _recastWaypoints[_recastIndex - 1];
            Vector3 leg = next - previous;
            float legSquared = Vector3.Dot(leg, leg);
            float progress = legSquared < 0.01f ? 0f :
                Math.Max(0f, Math.Min(1f, Vector3.Dot(player - previous, leg) / legSquared));
            if (Math.Abs(player.Y - _recastWaypoints[0].Y) > 2f ||
                Vector3.Distance(player, previous + leg * progress) > 1.25f)
            { reason = "deviated from validated Recast corridor"; goto Reject; }
            if (!LocalRoutePlanner.SupportedFGridSegment(player, next, _recastWaypoints[0].Y,
                out string legReason))
            { reason = $"Recast leg {_recastIndex}/{_recastWaypoints.Count - 1} rejected: {legReason}"; goto Reject; }
            if (Vector3.Distance(player, _recastLastPosition) > 0.4f)
            { _recastLastPosition = player; _recastLastProgress = DateTime.UtcNow; }
            if (DateTime.UtcNow - _recastLastProgress > TimeSpan.FromSeconds(9))
            { reason = "Recast waypoint movement stalled"; goto Reject; }
            if (_movement.Owner != MovementOwner.FGridTravel || !SMovementController.IsNavigating() ||
                _recastIssuedIndex != _recastIndex)
            {
                if (!_movement.SetDestination(MovementOwner.FGridTravel, next))
                { reason = "controller rejected Recast waypoint"; goto Reject; }
                _recastIssuedIndex = _recastIndex;
            }
            _activeNavSource = "Recast";
            return true;
        Reject:
            _movement.Release(MovementOwner.FGridTravel);
            _recastWaypoints = null;
            _recastDestination = null;
            _recastRejectedTarget = target;
            _activeNavSource = "none";
            return false;
        }

        private bool TryNavigateFGrid(Vector3 target, bool portal, out string reason)
        {
            reason = "";
            if (_routes.IsRecording) { reason = "nav recorder is active"; return false; }
            if (TryNavigateRecast(target, portal, out string recastReason)) return true;
            reason = "Recast: " + recastReason;
            if (!File.Exists(_navMeshPath)) { reason += "; NavMeshes/4107.nav is missing"; return false; }
            if (SMovementController.NavAgent?.HasPathfinder != true)
            { reason = "4107.nav is not loaded by AO#"; return false; }
            Vector3 player = DynelManager.LocalPlayer.Position;
            if (_meshRejectedDestination.HasValue && Vector3.Distance(_meshRejectedDestination.Value, target) < 0.5f)
            { reason = "mesh movement to this target previously stopped"; return false; }
            if (_meshDestination.HasValue && Vector3.Distance(_meshDestination.Value, target) < 0.5f)
            {
                if (_movement.Owner == MovementOwner.FGridTravel && SMovementController.IsNavigating())
                {
                    if (Vector3.Distance(player, _meshLastPosition) > 0.6f)
                    {
                        _meshLastPosition = player;
                        _meshLastProgress = DateTime.UtcNow;
                    }
                    if (DateTime.UtcNow - _meshLastProgress <= TimeSpan.FromSeconds(9)) return true;
                    reason = "mesh movement made no progress for nine seconds";
                }
                else if (_movement.Owner == MovementOwner.FGridTravel &&
                    DateTime.UtcNow - _meshLastProgress < TimeSpan.FromSeconds(2))
                    return true; // AO# may pause briefly between mesh waypoints.
                else reason = "mesh movement stopped before reaching the target";
                _movement.Release(MovementOwner.FGridTravel);
                _meshDestination = null;
                _meshRejectedDestination = target;
                return false;
            }
            try
            {
                if (!LocalRoutePlanner.TryFGridGroundCost(player, target, out float cost))
                { reason = "no complete floor-supported mesh corridor"; return false; }
                if (!_movement.SetNavDestination(MovementOwner.FGridTravel, target))
                {
                    _meshRejectedDestination = target;
                    reason = "AO# rejected the mesh destination";
                    return false;
                }
                _routes.StopPlayback();
                _meshDestination = target;
                _meshLastPosition = player;
                _meshLastProgress = DateTime.UtcNow;
                _activeNavSource = "SharpNav";
                _say($"FGrid navmesh: complete supported corridor {cost:0.0} m to ({target.X:0.0},{target.Y:0.0},{target.Z:0.0}).");
                return true;
            }
            catch (Exception ex) { reason = "mesh query failed: " + ex.Message; return false; }
        }

        public FGridServiceResult Tick(int targetId, Vector3? missionAnchor = null)
        {
            if (Playfield.ModelIdentity.Instance == targetId &&
                (_state == State.Exit || _state == State.Settling))
            {
                if (Game.IsZoning) return FGridServiceResult.InProgress;
                if (_selectedExit != null && !_selectedPortalVerified)
                {
                    Fail($"FGrid zone reached {targetId} without verification of selected portal {_selectedExit.PortalIdentity}.");
                    return FGridServiceResult.Failed;
                }
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
            if (!CanRoute(targetId))
            {
                LastFailure = $"No verified Fixer Grid exit route is mapped for playfield {targetId}.";
                return FGridServiceResult.Unavailable;
            }

            if (Playfield.ModelIdentity.Instance == (int)PlayfieldId.FixerGrid &&
                (_state == State.Idle || _targetId != targetId))
            {
                ResetAttempt();
                _targetId = targetId;
                PrepareRoute(targetId, missionAnchor);
                if (_route == null)
                {
                    Fail($"No verified FGrid portal for playfield {targetId} is reachable from this floor.", false);
                    return FGridServiceResult.Failed;
                }
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
                PrepareRoute(targetId, missionAnchor);
                if (_route == null)
                {
                    Fail($"No verified FGrid portal for playfield {targetId} is reachable from this floor.", false);
                    return FGridServiceResult.Failed;
                }
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
                else if (Playfield.ModelIdentity.Instance == 655 &&
                    _routes.TryGetEndpoint("Terminal to grid ICC", 655,
                        DynelManager.LocalPlayer.Position, out Vector3 recordedTerminal))
                    _terminalPosition = recordedTerminal;
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
                    if (!_routes.TryNavigate(_terminalPosition, _movement, MovementOwner.FGridTravel) &&
                        (_movement.Owner != MovementOwner.FGridTravel || !SMovementController.IsNavigating()))
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
                    _movement.Release(MovementOwner.FGridTravel);
                    ResetMeshNavigation();
                    _routes.StopPlayback();
                    _lastFloor = floor;
                    _started = DateTime.UtcNow;
                    _say($"Fixer Grid floor {floor}; target floor {_route.Floor}.");
                }
                if (floor == _route.Floor)
                {
                    _movement.Release(MovementOwner.FGridTravel);
                    ResetMeshNavigation();
                    _routes.StopPlayback();
                    _state = State.Exit;
                    _started = DateTime.UtcNow;
                    return FGridServiceResult.InProgress;
                }
                if (floor > _route.Floor || floor >= UpLifts.Length ||
                    DateTime.UtcNow - _started > TimeSpan.FromSeconds(_recast.Pending ? 65 : 30))
                {
                    Fail($"Could not reach Fixer Grid floor {_route.Floor}; stopped on floor {floor}.");
                    return FGridServiceResult.Failed;
                }
                Vector3 lift = UpLifts[floor];
                if (Vector3.Distance(DynelManager.LocalPlayer.Position, lift) > 0.8f)
                {
                    if (!TryNavigateFGrid(lift, false, out string meshReason) &&
                        !_routes.TryNavigate(lift, _movement, MovementOwner.FGridTravel))
                    {
                        // The zone loader can publish the playfield before the
                        // optional mesh has finished loading.
                        if (_recast.Pending && DateTime.UtcNow - _started < TimeSpan.FromSeconds(60))
                            return FGridServiceResult.InProgress;
                        if (File.Exists(_navMeshPath) && SMovementController.NavAgent?.HasPathfinder != true &&
                            DateTime.UtcNow - _started < TimeSpan.FromSeconds(5))
                            return FGridServiceResult.InProgress;
                        Fail($"No safe FGrid route reaches the floor {floor} lift ({meshReason}). Record a verified walkway with /rkm nav record fgrid-floor-{floor}-lift, then /rkm nav stop.");
                        return FGridServiceResult.Failed;
                    }
                    else if (_routes.IsPlaying) _activeNavSource = "recorded fallback";
                }
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
                        string reason = $"Selected FGrid portal {(_selectedExit == null ? "unknown" : _selectedExit.PortalIdentity.ToString())} " +
                            $"was unavailable on floor {_route.Floor}" + (seen.Length == 0 ? "." : $"; {seen}.");
                        return TryNextExit(reason) ? FGridServiceResult.InProgress : FGridServiceResult.Failed;
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
                    if (!TryNavigateFGrid(_exit.Position, true, out string meshReason) &&
                        !_routes.TryNavigate(_exit.Position, _movement, MovementOwner.FGridTravel))
                    {
                        if (_recast.Pending && DateTime.UtcNow - _started < TimeSpan.FromSeconds(60))
                            return FGridServiceResult.InProgress;
                        if (File.Exists(_navMeshPath) && SMovementController.NavAgent?.HasPathfinder != true &&
                            DateTime.UtcNow - _started < TimeSpan.FromSeconds(5))
                            return FGridServiceResult.InProgress;
                        string routeFailure = $"No safe FGrid route reaches portal {_exit.Identity} on floor {_route.Floor} ({meshReason}). " +
                            $"Record a verified walkway with /rkm nav record fgrid-floor-{_route.Floor}-portal-{_exit.Identity.Instance}, then /rkm nav stop.";
                        return TryNextExit(routeFailure) ? FGridServiceResult.InProgress : FGridServiceResult.Failed;
                    }
                    else if (_routes.IsPlaying) _activeNavSource = "recorded fallback";
                }
                else if (DateTime.UtcNow - _lastUse > TimeSpan.FromSeconds(3))
                {
                    _movement.Release(MovementOwner.FGridTravel);
                    ResetMeshNavigation();
                    _routes.StopPlayback();
                    _exit.Use();
                    _lastUse = DateTime.UtcNow;
                }
                if (DateTime.UtcNow - _started > TimeSpan.FromSeconds(35))
                {
                    string reason = $"Fixer Grid portal {_exit.Identity} did not start zoning to playfield {_targetId}.";
                    return TryNextExit(reason) ? FGridServiceResult.InProgress : FGridServiceResult.Failed;
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
            if (_selectedExit != null)
            {
                var candidates = DynelManager.Terminals.Where(x =>
                    Math.Abs(x.Position.Y - height) < 4f &&
                    string.Equals(x.Name, "Exit the Grid", StringComparison.OrdinalIgnoreCase) &&
                    Math.Abs(x.Position.X - _selectedExit.FGridPosition[0]) < 1.5f &&
                    Math.Abs(x.Position.Y - _selectedExit.FGridPosition[1]) < 1.5f &&
                    Math.Abs(x.Position.Z - _selectedExit.FGridPosition[2]) < 1.5f).ToList();
                SimpleItem exact = candidates.FirstOrDefault(x =>
                    x.Identity.Instance == _selectedExit.PortalIdentity);
                if (exact != null) return exact;
                if (candidates.Count == 1)
                {
                    if (!_exitLogged)
                        _say($"FGrid portal identity changed from {_selectedExit.PortalIdentity} to {candidates[0].Identity}; unique surveyed position matched.");
                    return candidates[0];
                }
                return null;
            }
            if (_legacyLearnedExits.TryGetValue(_targetId, out LearnedExit learned) &&
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
            if (_state == State.Exit && _selectedExit != null)
                _selectedPortalVerified = _observedExit != null &&
                    SamePortalPosition(_selectedExit, _observedExit);
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
            ResetMeshNavigation();
            _routes.StopPlayback();
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
            ResetMeshNavigation();
            _routes.StopPlayback();
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
            _rankedExits.Clear();
            _selectedExit = null;
            _exitIndex = 0;
            _missionAnchor = null;
            _selectedPortalVerified = false;
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
            ResetMeshNavigation();
            _routes.StopPlayback();
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
