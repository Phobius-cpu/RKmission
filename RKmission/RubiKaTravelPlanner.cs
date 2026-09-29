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
        private readonly ScottyboiWarpProvider _warp;
        private readonly MovementArbiter _movement;
        private readonly Action<string> _say;
        private Link _step;
        private int _target;
        private bool _warpFailed;
        private DateTime _stepStarted, _lastUse;
        public bool IsActive => _target != 0;
        public string LastFailure { get; private set; }

        public RubiKaTravelPlanner(string pluginDir, ScottyboiWarpProvider warp,
            MovementArbiter movement, Action<string> say)
        {
            _warp = warp;
            _movement = movement;
            _say = say;
            string path = Path.Combine(pluginDir, "Data", "PlayfieldLinks.json");
            if (!File.Exists(path)) return;
            JObject root = JObject.Parse(File.ReadAllText(path));
            foreach (JProperty node in root.Properties())
            {
                if (!int.TryParse(node.Name, out int from)) continue;
                var links = new List<Link>();
                foreach (JToken raw in node.Value["Links"] ?? new JArray())
                {
                    int to = raw.Value<int?>("DstId") ?? 0;
                    string kind = raw.Value<string>("$type");
                    JToken point = raw["TerminalPos"] ?? raw["TeleporterPos"] ??
                        raw["TransitionSpots"]?.FirstOrDefault();
                    if (to <= 0 || point == null || point.Count() != 3) continue;
                    links.Add(new Link
                    {
                        From = from, To = to, Kind = kind,
                        TerminalName = raw.Value<string>("TerminalName") ?? "Enter The Grid",
                        Position = new Vector3(point[0].Value<float>(), point[1].Value<float>(), point[2].Value<float>())
                    });
                }
                _graph[from] = links;
            }
        }

        public TravelResult Tick(int target)
        {
            int current = Playfield.ModelIdentity.Instance;
            if (current == target)
            {
                _movement.Release(MovementOwner.OutdoorTravel);
                return TravelResult.Arrived;
            }
            if (_target != target) Reset(target);
            if (!_warpFailed)
            {
                WarpResult result = _warp.Tick(target);
                if (result == WarpResult.InProgress) return TravelResult.InProgress;
                if (result == WarpResult.Succeeded) return TravelResult.Arrived;
                _warpFailed = true;
            }
            if (_step != null && current == _step.To)
            {
                _say($"Travel transition {_step.From}->{_step.To} verified.");
                _movement.Release(MovementOwner.OutdoorTravel);
                _step = null;
            }
            else if (_step != null && current != _step.From)
            {
                LastFailure = $"Travel reached unexpected playfield {current}; expected {_step.To}.";
                _movement.Release(MovementOwner.OutdoorTravel);
                return TravelResult.Blocked;
            }
            if (_step == null)
            {
                _step = FirstLink(current, target);
                if (_step == null)
                {
                    LastFailure = $"No verified fallback link path from {current} to {target}.";
                    return TravelResult.Blocked;
                }
                _stepStarted = DateTime.UtcNow;
                _lastUse = DateTime.MinValue;
                _say($"Fallback travel: {_step.Kind} from {_step.From} to {_step.To}.");
            }
            if (DateTime.UtcNow - _stepStarted > TimeSpan.FromSeconds(90))
            {
                LastFailure = $"Transition {_step.From}->{_step.To} timed out.";
                _movement.Release(MovementOwner.OutdoorTravel);
                return TravelResult.Blocked;
            }
            if (Vector3.Distance(DynelManager.LocalPlayer.Position, _step.Position) > 3f)
            {
                if (!_movementIsNavigating()) _movement.SetDestination(MovementOwner.OutdoorTravel, _step.Position);
                return TravelResult.InProgress;
            }
            if (_step.Kind == "GridTerminalLink" || _step.Kind == "TerminalLink")
            {
                if (DateTime.UtcNow - _lastUse > TimeSpan.FromSeconds(3))
                {
                    SimpleItem terminal = DynelManager.Terminals.Where(x =>
                        x.Name == _step.TerminalName &&
                        Vector3.Distance(x.Position, _step.Position) < 6f)
                        .OrderBy(x => Vector3.Distance(x.Position, _step.Position)).FirstOrDefault();
                    terminal?.Use();
                    _lastUse = DateTime.UtcNow;
                }
            }
            else if (_step.Kind != "TeleporterLink" && _step.Kind != "ZoneBorderLink")
            {
                LastFailure = $"Unsupported fallback link type {_step.Kind}.";
                return TravelResult.Blocked;
            }
            return TravelResult.InProgress;
        }

        private bool _movementIsNavigating() =>
            _movement.Owner == MovementOwner.OutdoorTravel && AOSharp.Pathfinding.SMovementController.IsNavigating();

        private Link FirstLink(int from, int to)
        {
            var queue = new Queue<int>();
            var parent = new Dictionary<int, Link>();
            queue.Enqueue(from);
            parent[from] = null;
            while (queue.Count > 0 && !parent.ContainsKey(to))
            {
                int node = queue.Dequeue();
                if (!_graph.TryGetValue(node, out List<Link> links)) continue;
                foreach (Link link in links)
                {
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

        public void Reset(int target = 0)
        {
            _target = target;
            _step = null;
            _warpFailed = false;
            LastFailure = null;
            _movement.Release(MovementOwner.OutdoorTravel);
            _warp.Reset();
        }

        public void InvalidatePath() => _movement.Release(MovementOwner.OutdoorTravel);
    }
}
