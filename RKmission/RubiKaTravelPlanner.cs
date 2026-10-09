using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AOSharp.Common.GameData;
using AOSharp.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RKmission
{
    internal enum TravelResult { InProgress, Arrived, Blocked }

    // Adapted from AOSharp.Navigator's playfield-link idea. RKMission owns the
    // movement controller and advances a link only after observing its DstId.
    internal sealed class RubiKaTravelPlanner
    {
        private sealed class Link
        {
            public int From, To;
            public string Kind, TerminalName;
            public Vector3 Position;
        }
        private enum Provider { Scottyboi, FGrid, Graph }
        private sealed class Candidate
        {
            public Provider Provider;
            public float? Score, Distance, Penalty;
            public string Detail;
        }
        private sealed class ObservedLinkLanding
        {
            public int From { get; set; }
            public int To { get; set; }
            public string Kind { get; set; }
            public float[] SourcePosition { get; set; }
            public float[] ArrivalPosition { get; set; }
            public DateTime VerifiedUtc { get; set; }
        }

        private readonly Dictionary<int, List<Link>> _graph = new Dictionary<int, List<Link>>();
        private readonly HashSet<string> _failedLinks = new HashSet<string>();
        private readonly ScottyboiWarpProvider _warp;
        private readonly FGridServiceProvider _fgrid;
        private readonly MovementArbiter _movement;
        private readonly NavigationRouteRecorder _routes;
        private readonly Action<string> _say;
        private readonly string _landingPath;
        private readonly List<ObservedLinkLanding> _landings = new List<ObservedLinkLanding>();
        private bool _landingsWritable = true;
        private List<Candidate> _candidates = new List<Candidate>();
        private int _candidateIndex;
        private Link _step;
        private int _target;
        private Vector3? _targetAnchor;
        private DateTime _stepStarted, _lastUse, _arrivalObservedAt;

        public bool IsActive => _target != 0;
        public string CurrentProvider
        {
            get
            {
                if (_target == 0) return "Local";
                if (_candidateIndex >= _candidates.Count) return "Unavailable";
                return _candidates[_candidateIndex].Provider == Provider.Scottyboi ? "Scottyboi" :
                    _candidates[_candidateIndex].Provider == Provider.FGrid ? "FGridService" :
                    "PlayfieldGraph";
            }
        }
        public string LastFailure { get; private set; }

        public RubiKaTravelPlanner(string pluginDir, ScottyboiWarpProvider warp,
            FGridServiceProvider fgrid, MovementArbiter movement,
            NavigationRouteRecorder routes, Action<string> say)
        {
            _warp = warp;
            _fgrid = fgrid;
            _movement = movement;
            _routes = routes;
            _say = say;
            _landingPath = Path.Combine(pluginDir, "RKMissionData", "playfield-link-landings.json");
            LoadLandings();

            string path = Path.Combine(pluginDir, "Data", "PlayfieldLinks.json");
            if (File.Exists(path))
            {
                JObject root = JObject.Parse(File.ReadAllText(path));
                foreach (JProperty node in root.Properties())
                {
                    if (!int.TryParse(node.Name, out int from)) continue;
                    var links = new List<Link>();
                    foreach (JToken raw in node.Value["Links"] ?? new JArray())
                    {
                        int to = raw.Value<int?>("DstId") ??
                            (raw.Value<string>("$type") == "GridTerminalLink"
                                ? (int)PlayfieldId.Grid : 0);
                        string kind = raw.Value<string>("$type");
                        JToken point = raw["TerminalPos"] ?? raw["TeleporterPos"] ??
                            raw["TransitionSpots"]?.FirstOrDefault();
                        if (to <= 0 || point == null || point.Count() != 3) continue;
                        links.Add(new Link
                        {
                            From = from,
                            To = to,
                            Kind = kind,
                            TerminalName = raw.Value<string>("TerminalName") ?? "Enter The Grid",
                            Position = new Vector3(point[0].Value<float>(),
                                point[1].Value<float>(), point[2].Value<float>())
                        });
                    }
                    _graph[from] = links;
                }
            }

        }

        public TravelResult Tick(int target, Vector3? missionAnchor = null)
        {
            int current = Playfield.ModelIdentity.Instance;
            if (_target != target || !SameAnchor(_targetAnchor, missionAnchor)) Reset(target, missionAnchor);
            Provider? active = _candidateIndex < _candidates.Count
                ? _candidates[_candidateIndex].Provider : (Provider?)null;

            if (current == target)
            {
                if (active == Provider.Graph && _step != null && _step.To == current)
                {
                    if (!ArrivalSettled()) return TravelResult.InProgress;
                    _say($"Travel transition {_step.From}->{_step.To} verified.");
                    RecordLanding(_step);
                    _step = null;
                    _arrivalObservedAt = DateTime.MinValue;
                }
                // Let the active provider finish its post-zone verification
                // before the coordinator starts local mission travel.
                if (active == Provider.Scottyboi)
                {
                    WarpResult warpAtDestination = _warp.Tick(target, missionAnchor);
                    if (warpAtDestination == WarpResult.InProgress)
                        return TravelResult.InProgress;
                    if (warpAtDestination == WarpResult.Failed)
                    {
                        LastFailure = _warp.LastFailure ?? "Scottyboi destination verification failed.";
                        return TravelResult.Blocked;
                    }
                }
                else if (active == Provider.FGrid && _fgrid.IsActive)
                {
                    FGridServiceResult fgridAtDestination = _fgrid.Tick(target, missionAnchor);
                    if (fgridAtDestination == FGridServiceResult.InProgress)
                        return TravelResult.InProgress;
                    if (fgridAtDestination != FGridServiceResult.Succeeded)
                    {
                        LastFailure = _fgrid.LastFailure ?? "FGrid destination verification failed.";
                        return TravelResult.Blocked;
                    }
                }

                _movement.Release(MovementOwner.OutdoorTravel);
                _movement.Release(MovementOwner.FGridTravel);
                _routes.StopPlayback();
                return TravelResult.Arrived;
            }

            if (active == Provider.Scottyboi)
            {
                WarpResult result = _warp.Tick(target, missionAnchor);
                if (result == WarpResult.InProgress) return TravelResult.InProgress;
                if (result == WarpResult.Succeeded) return TravelResult.Arrived;
                if (AdvanceProvider()) return Tick(target, missionAnchor);
                LastFailure = _warp.LastFailure ?? "Scottyboi travel failed.";
                return TravelResult.Blocked;
            }

            if (active == Provider.FGrid)
            {
                FGridServiceResult result = _fgrid.Tick(target, missionAnchor);
                if (result == FGridServiceResult.InProgress) return TravelResult.InProgress;
                if (result == FGridServiceResult.Succeeded) return TravelResult.Arrived;
                if (AdvanceProvider()) return Tick(target, missionAnchor);
                LastFailure = _fgrid.LastFailure ?? "FGrid travel failed.";
                return TravelResult.Blocked;
            }

            if (_step != null && current == _step.To)
            {
                if (!ArrivalSettled()) return TravelResult.InProgress;
                _say($"Travel transition {_step.From}->{_step.To} verified.");
                RecordLanding(_step);
                _movement.Release(MovementOwner.OutdoorTravel);
                _step = null;
                _arrivalObservedAt = DateTime.MinValue;
            }
            else if (_step != null && current != _step.From)
            {
                _say($"Travel reached playfield {current} instead of {_step.To}; replanning from the observed playfield.");
                _failedLinks.Add(LinkKey(_step));
                _movement.Release(MovementOwner.OutdoorTravel);
                _step = null;
                _arrivalObservedAt = DateTime.MinValue;
            }

            if (_step == null)
            {
                _step = FirstLink(current, target);
                if (_step == null)
                {
                    string warpReason = _warp.LastFailure;
                    string fgridReason = _fgrid.CanRoute(target)
                        ? _fgrid.LastFailure
                        : $"No verified Fixer Grid exit route is mapped for playfield {target}.";
                    LastFailure =
                        (string.IsNullOrEmpty(warpReason) ? "" : $"Scottyboi: {warpReason} ") +
                        (string.IsNullOrEmpty(fgridReason) ? "" : $"FGrid: {fgridReason} ") +
                        $"No mapped normal-travel fallback path from playfield {current} to {target}.";
                    if (AdvanceProvider()) return Tick(target, missionAnchor);
                    return TravelResult.Blocked;
                }

                _stepStarted = DateTime.UtcNow;
                _arrivalObservedAt = DateTime.MinValue;
                _lastUse = DateTime.MinValue;
                _say($"Mapped travel: {_step.Kind} from {_step.From} to {_step.To}.");
            }

            if (DateTime.UtcNow - _stepStarted > TimeSpan.FromSeconds(90))
            {
                FailStep($"Transition {_step.From}->{_step.To} timed out");
                return TravelResult.InProgress;
            }

            float arrivalRadius = _step.Kind == "ZoneBorderLink" ? 0.75f :
                _step.Kind == "TeleporterLink" ? 2f : 3f;
            if (Vector3.Distance(DynelManager.LocalPlayer.Position, _step.Position) > arrivalRadius)
            {
                if ((_step.Kind == "GridTerminalLink" || _step.Kind == "TerminalLink") &&
                    _routes.TryNavigate(_step.Position, _movement, MovementOwner.OutdoorTravel))
                    return TravelResult.InProgress;
                if (!_movementIsNavigating())
                    _movement.SetDestination(MovementOwner.OutdoorTravel, _step.Position);
                return TravelResult.InProgress;
            }

            if (_step.Kind == "GridTerminalLink" || _step.Kind == "TerminalLink")
            {
                _movement.Release(MovementOwner.OutdoorTravel);
                if (DateTime.UtcNow - _lastUse > TimeSpan.FromSeconds(3))
                {
                    SimpleItem terminal = DynelManager.Terminals.Where(x =>
                            string.Equals(x.Name, _step.TerminalName,
                                StringComparison.OrdinalIgnoreCase) &&
                            Vector3.Distance(x.Position, _step.Position) < 12f)
                        .OrderBy(x => Vector3.Distance(x.Position, _step.Position))
                        .FirstOrDefault();
                    terminal?.Use();
                    _lastUse = DateTime.UtcNow;
                }
            }
            else if (_step.Kind == "TeleporterLink" &&
                DateTime.UtcNow - _lastUse > TimeSpan.FromSeconds(3))
            {
                SimpleItem teleporter = DynelManager.Terminals
                    .Where(x => Vector3.Distance(x.Position, _step.Position) < 5f)
                    .OrderBy(x => Vector3.Distance(x.Position, _step.Position))
                    .FirstOrDefault();
                teleporter?.Use();
                _lastUse = DateTime.UtcNow;
            }
            else if (_step.Kind != "TeleporterLink" && _step.Kind != "ZoneBorderLink")
            {
                LastFailure = $"Unsupported fallback link type {_step.Kind}.";
                if (AdvanceProvider()) return Tick(target, missionAnchor);
                return TravelResult.Blocked;
            }

            return TravelResult.InProgress;
        }

        private bool _movementIsNavigating() =>
            _movement.Owner == MovementOwner.OutdoorTravel &&
            AOSharp.Pathfinding.SMovementController.IsNavigating();

        private static string LinkKey(Link link) => $"{link.From}:{link.To}:{link.Kind}";

        private bool ArrivalSettled()
        {
            if (_arrivalObservedAt == DateTime.MinValue)
                _arrivalObservedAt = DateTime.UtcNow;
            return DateTime.UtcNow - _arrivalObservedAt >= TimeSpan.FromSeconds(2);
        }

        private void FailStep(string reason)
        {
            _say(reason + "; blacklisting this link and replanning.");
            _failedLinks.Add(LinkKey(_step));
            _movement.Release(MovementOwner.OutdoorTravel);
            _routes.StopPlayback();
            _step = null;
            _arrivalObservedAt = DateTime.MinValue;
        }

        private Link FirstLink(int from, int to) => FindPath(from, to).FirstOrDefault();

        private List<Link> FindPath(int from, int to)
        {
            var queue = new Queue<int>();
            var parent = new Dictionary<int, Link>();
            queue.Enqueue(from);
            parent[from] = null;
            while (queue.Count > 0 && !parent.ContainsKey(to))
            {
                int node = queue.Dequeue();
                _graph.TryGetValue(node, out List<Link> links);
                if (node == from)
                {
                    SimpleItem grid = DynelManager.Terminals.Where(x =>
                            string.Equals(x.Name, "Enter The Grid",
                                StringComparison.OrdinalIgnoreCase))
                        .OrderBy(x => Vector3.Distance(x.Position,
                            DynelManager.LocalPlayer.Position))
                        .FirstOrDefault();
                    if (grid != null)
                    {
                        links = links == null ? new List<Link>() : new List<Link>(links);
                        links.Add(new Link
                        {
                            From = from,
                            To = (int)PlayfieldId.Grid,
                            Kind = "GridTerminalLink",
                            TerminalName = grid.Name,
                            Position = grid.Position
                        });
                    }
                }

                if (links == null) continue;
                foreach (Link link in links)
                {
                    if (_failedLinks.Contains(LinkKey(link))) continue;
                    if (parent.ContainsKey(link.To)) continue;
                    parent[link.To] = link;
                    queue.Enqueue(link.To);
                }
            }

            if (!parent.ContainsKey(to) || from == to) return new List<Link>();
            var path = new List<Link>();
            Link step = parent[to];
            while (step != null)
            {
                path.Add(step);
                step = parent[step.From];
            }
            path.Reverse();
            return path;
        }

        private static bool SameAnchor(Vector3? first, Vector3? second) =>
            first.HasValue == second.HasValue &&
            (!first.HasValue || Vector3.Distance(first.Value, second.Value) <= 0.5f);

        private static float OutdoorDistance(Vector3 arrival, Vector3 anchor)
        {
            float dx = arrival.X - anchor.X, dz = arrival.Z - anchor.Z;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        private static float[] Coordinates(Vector3 position) =>
            new[] { position.X, position.Y, position.Z };

        private static bool ValidCoordinates(float[] position) => position != null &&
            position.Length == 3 && position.All(x => !float.IsNaN(x) &&
                !float.IsInfinity(x) && Math.Abs(x) <= 100000f) &&
            position.Any(x => x != 0f);

        private static bool ValidLanding(ObservedLinkLanding record) => record != null &&
            record.From > 0 && record.To > 0 && record.From != record.To &&
            (record.Kind == "GridTerminalLink" || record.Kind == "TerminalLink" ||
             record.Kind == "ZoneBorderLink" || record.Kind == "TeleporterLink") &&
            ValidCoordinates(record.SourcePosition) && ValidCoordinates(record.ArrivalPosition) &&
            record.VerifiedUtc > new DateTime(2020, 1, 1) &&
            record.VerifiedUtc <= DateTime.UtcNow.AddDays(1);

        private static bool SameLandingLink(ObservedLinkLanding record, Link link) =>
            record.From == link.From && record.To == link.To && record.Kind == link.Kind &&
            Vector3.Distance(new Vector3(record.SourcePosition[0], record.SourcePosition[1],
                record.SourcePosition[2]), link.Position) < 2f;

        private void LoadLandings()
        {
            try
            {
                if (!File.Exists(_landingPath)) return;
                var records = JsonConvert.DeserializeObject<List<ObservedLinkLanding>>(
                    File.ReadAllText(_landingPath));
                if (records == null || records.Count > 1000 || records.Any(x => !ValidLanding(x)))
                    throw new InvalidDataException("Invalid playfield-link landing records.");
                _landings.AddRange(records);
            }
            catch (Exception ex)
            {
                _landingsWritable = false;
                _say("Playfield-link landing data ignored and original file preserved: " + ex.Message);
            }
        }

        private void RecordLanding(Link link)
        {
            if (DynelManager.LocalPlayer == null || Playfield.ModelIdentity.Instance != link.To ||
                !AcceptedMissions.Finite(DynelManager.LocalPlayer.Position)) return;
            var record = new ObservedLinkLanding
            {
                From = link.From, To = link.To, Kind = link.Kind,
                SourcePosition = Coordinates(link.Position),
                ArrivalPosition = Coordinates(DynelManager.LocalPlayer.Position),
                VerifiedUtc = DateTime.UtcNow
            };
            if (!ValidLanding(record)) return;
            _landings.RemoveAll(x => SameLandingLink(x, link));
            _landings.Add(record);
            if (!_landingsWritable) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_landingPath));
                string temp = _landingPath + ".new";
                File.WriteAllText(temp, JsonConvert.SerializeObject(_landings, Formatting.Indented));
                if (File.Exists(_landingPath)) File.Replace(temp, _landingPath, null);
                else File.Move(temp, _landingPath);
                _say($"Recorded verified {link.Kind} landing for {link.From}->{link.To}.");
            }
            catch (Exception ex) { _say("Could not save playfield-link landing: " + ex.Message); }
        }

        private bool TryGetLanding(Link link, out Vector3 arrival)
        {
            arrival = default;
            ObservedLinkLanding known = _landings.Where(x => SameLandingLink(x, link))
                .OrderByDescending(x => x.VerifiedUtc).FirstOrDefault();
            if (known == null) return false;
            arrival = new Vector3(known.ArrivalPosition[0], known.ArrivalPosition[1],
                known.ArrivalPosition[2]);
            return true;
        }

        private static Candidate Unknown(Provider provider, string detail) =>
            new Candidate { Provider = provider, Detail = detail };

        private List<Candidate> FallbackCandidates(bool directNearby)
        {
            var candidates = new List<Candidate>();
            if (directNearby)
                candidates.Add(Unknown(Provider.Graph, "no observed final-link landing"));
            if (Playfield.ModelIdentity.Instance != (int)PlayfieldId.FixerGrid)
                candidates.Add(Unknown(Provider.Scottyboi, "no observed landing for this destination"));
            if (_fgrid.CanRoute(_target))
                candidates.Add(Unknown(Provider.FGrid, "no reachable verified exit arrival or service"));
            if (!directNearby)
                candidates.Add(Unknown(Provider.Graph, "no observed final-link landing"));
            return candidates;
        }

        private List<Candidate> BuildCandidates(Vector3 anchor, bool directNearby)
        {
            List<Candidate> candidates = FallbackCandidates(directNearby);
            int current = Playfield.ModelIdentity.Instance;
            if (!AcceptedMissions.Finite(anchor) || DynelManager.LocalPlayer == null)
                return candidates;
            foreach (Candidate candidate in candidates)
            {
                if (candidate.Provider == Provider.Scottyboi &&
                    _warp.TryGetObservedArrival(_target, anchor, out Vector3 warpArrival,
                        out float warpDistance, out string warper))
                {
                    candidate.Distance = warpDistance;
                    candidate.Penalty = 120f; // service wait and variable assignment
                    candidate.Score = warpDistance + candidate.Penalty;
                    candidate.Detail = $"historical warper {warper}, arrival {warpArrival}";
                }
                else if (candidate.Provider == Provider.FGrid &&
                    _fgrid.CanStartFromCurrentPlayfield &&
                    _fgrid.TryGetBestArrival(_target, anchor, out Vector3 gridArrival,
                        out float gridDistance, out int portal))
                {
                    candidate.Distance = gridDistance;
                    candidate.Penalty = 100f; // terminal access, service wait, portal traversal
                    candidate.Score = gridDistance + candidate.Penalty;
                    candidate.Detail = $"verified portal {portal}, arrival {gridArrival}";
                }
                else if (candidate.Provider == Provider.Graph)
                {
                    List<Link> path = FindPath(current, _target);
                    if (path.Count == 0)
                        candidate.Detail = "no mapped path from the current playfield";
                    if (path.Count > 0 && TryGetLanding(path[path.Count - 1], out Vector3 arrival))
                    {
                        float distance = OutdoorDistance(arrival, anchor);
                        float access = Math.Min(80f, OutdoorDistance(
                            DynelManager.LocalPlayer.Position, path[0].Position) * 0.15f);
                        candidate.Distance = distance;
                        candidate.Penalty = 20f + access + Math.Min(100f, path.Count * 30f);
                        candidate.Score = distance + candidate.Penalty;
                        candidate.Detail = $"{path.Count} mapped link(s), recorded final arrival {arrival}";
                    }
                }
                if (candidate.Provider == Provider.FGrid && !_fgrid.CanStartFromCurrentPlayfield)
                    candidate.Detail = "no configured service from the current playfield";
                if (candidate.Score.HasValue &&
                    (float.IsNaN(candidate.Score.Value) || float.IsInfinity(candidate.Score.Value)))
                {
                    candidate.Score = candidate.Distance = candidate.Penalty = null;
                    candidate.Detail = "distance estimate is outside supported range";
                }
            }
            return candidates.OrderBy(x => x.Score.HasValue ? 0 : 1)
                .ThenBy(x => x.Score ?? float.MaxValue).ToList();
        }

        private bool AdvanceProvider()
        {
            _candidateIndex++;
            _step = null;
            _arrivalObservedAt = DateTime.MinValue;
            _movement.Release(MovementOwner.OutdoorTravel);
            _movement.Release(MovementOwner.FGridTravel);
            _routes.StopPlayback();
            if (_candidateIndex >= _candidates.Count) return false;
            _say($"Travel provider fallback: {CurrentProvider}.");
            return true;
        }

        public void Reset(int target = 0, Vector3? missionAnchor = null)
        {
            _target = target;
            _targetAnchor = missionAnchor;
            _step = null;
            _arrivalObservedAt = DateTime.MinValue;

            // Retain nearby direct-link priority among routes whose arrival is
            // still unknown; observed arrivals can instead be compared.
            int current = Playfield.ModelIdentity.Instance;
            bool directNearby = target > 0 && DynelManager.LocalPlayer != null &&
                _graph.TryGetValue(current, out List<Link> direct) && direct.Any(x =>
                    x.To == target &&
                    Vector3.Distance(DynelManager.LocalPlayer.Position, x.Position) <= 300f);

            _failedLinks.Clear();
            try
            {
                _candidates = target > 0 && missionAnchor.HasValue
                    ? BuildCandidates(missionAnchor.Value, directNearby)
                    : FallbackCandidates(directNearby);
            }
            catch (Exception ex)
            {
                _say("Travel scoring failed; using established provider order: " + ex.Message);
                _candidates = FallbackCandidates(directNearby);
            }
            _candidateIndex = 0;
            if (target > 0)
            {
                foreach (Candidate candidate in _candidates)
                    _say($"Travel candidate {candidate.Provider}: " +
                        (candidate.Score.HasValue
                            ? $"outdoor={candidate.Distance:0}m + penalty={candidate.Penalty:0}m = {candidate.Score:0}m; {candidate.Detail}"
                            : $"Unknown; {candidate.Detail}."));
                _say($"Travel candidate order: {string.Join(" > ", _candidates.Select(x => x.Provider.ToString()))}.");
            }
            LastFailure = null;
            _movement.Release(MovementOwner.OutdoorTravel);
            _movement.Release(MovementOwner.FGridTravel);
            _routes.StopPlayback();
            _warp.Reset();
            _fgrid.Reset();
        }

        public void InvalidatePath()
        {
            _movement.Release(MovementOwner.OutdoorTravel);
            _movement.Release(MovementOwner.FGridTravel);
            _routes.StopPlayback();
        }
    }
}
