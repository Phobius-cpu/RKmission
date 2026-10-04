using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AOSharp.Common.GameData;
using AOSharp.Core;
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

        private readonly Dictionary<int, List<Link>> _graph = new Dictionary<int, List<Link>>();
        private readonly HashSet<string> _failedLinks = new HashSet<string>();
        private readonly ScottyboiWarpProvider _warp;
        private readonly FGridServiceProvider _fgrid;
        private readonly MovementArbiter _movement;
        private readonly NavigationRouteRecorder _routes;
        private readonly Action<string> _say;
        private Link _step;
        private int _target;
        private Vector3? _targetAnchor;
        private bool _warpFailed;
        private bool _fgridFailed;
        private DateTime _stepStarted, _lastUse;

        public bool IsActive => _target != 0;
        public string CurrentProvider
        {
            get
            {
                if (_target == 0) return "Local";
                if (!_warpFailed) return "Scottyboi";
                if (!_fgridFailed && _fgrid.IsConfigured && _fgrid.CanRoute(_target))
                    return "FGridService";
                return "PlayfieldGraph";
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

            if (current == target)
            {
                // Let the active provider finish its post-zone verification
                // before the coordinator starts local mission travel.
                if (!_warpFailed)
                {
                    WarpResult warpAtDestination = _warp.Tick(target);
                    if (warpAtDestination == WarpResult.InProgress)
                        return TravelResult.InProgress;
                }
                else if (!_fgridFailed && _fgrid.IsActive)
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

            if (!_warpFailed)
            {
                WarpResult result = _warp.Tick(target);
                if (result == WarpResult.InProgress) return TravelResult.InProgress;
                if (result == WarpResult.Succeeded) return TravelResult.Arrived;
                _warpFailed = true;
            }

            if (!_fgridFailed && _fgrid.CanRoute(target))
            {
                FGridServiceResult result = _fgrid.Tick(target, missionAnchor);
                if (result == FGridServiceResult.InProgress) return TravelResult.InProgress;
                if (result == FGridServiceResult.Succeeded) return TravelResult.Arrived;
                _fgridFailed = true;
            }

            if (_step != null && current == _step.To)
            {
                _say($"Travel transition {_step.From}->{_step.To} verified.");
                _movement.Release(MovementOwner.OutdoorTravel);
                _step = null;
            }
            else if (_step != null && current != _step.From)
            {
                _say($"Travel reached playfield {current} instead of {_step.To}; replanning from the observed playfield.");
                _failedLinks.Add(LinkKey(_step));
                _movement.Release(MovementOwner.OutdoorTravel);
                _step = null;
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
                    return TravelResult.Blocked;
                }

                _stepStarted = DateTime.UtcNow;
                _lastUse = DateTime.MinValue;
                _say($"Fallback travel: {_step.Kind} from {_step.From} to {_step.To}.");
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
                return TravelResult.Blocked;
            }

            return TravelResult.InProgress;
        }

        private bool _movementIsNavigating() =>
            _movement.Owner == MovementOwner.OutdoorTravel &&
            AOSharp.Pathfinding.SMovementController.IsNavigating();

        private static string LinkKey(Link link) => $"{link.From}:{link.To}:{link.Kind}";

        private void FailStep(string reason)
        {
            _say(reason + "; blacklisting this link and replanning.");
            _failedLinks.Add(LinkKey(_step));
            _movement.Release(MovementOwner.OutdoorTravel);
            _routes.StopPlayback();
            _step = null;
        }

        private Link FirstLink(int from, int to)
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

            if (!parent.ContainsKey(to)) return null;
            Link step = parent[to];
            while (step.From != from) step = parent[step.From];
            return step;
        }

        private static bool SameAnchor(Vector3? first, Vector3? second) =>
            first.HasValue == second.HasValue &&
            (!first.HasValue || Vector3.Distance(first.Value, second.Value) <= 0.5f);

        public void Reset(int target = 0, Vector3? missionAnchor = null)
        {
            _target = target;
            _targetAnchor = missionAnchor;
            _step = null;

            // A nearby, single verified normal link is cheaper than asking a
            // public bot for either Scotty or FGrid service.
            int current = Playfield.ModelIdentity.Instance;
            bool directNearby = target > 0 && DynelManager.LocalPlayer != null &&
                _graph.TryGetValue(current, out List<Link> direct) && direct.Any(x =>
                    x.To == target &&
                    Vector3.Distance(DynelManager.LocalPlayer.Position, x.Position) <= 300f);

            // A run restarted inside Fixer Grid should continue its exit route
            // directly instead of asking an outdoor Scottyboi warper again.
            _warpFailed = directNearby || current == (int)PlayfieldId.FixerGrid;
            _fgridFailed = directNearby || !_fgrid.CanRoute(target);
            _failedLinks.Clear();
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
