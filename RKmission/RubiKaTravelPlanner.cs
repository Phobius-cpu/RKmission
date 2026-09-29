using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AOSharp.Common.GameData;
using AOSharp.Core;
using AOSharp.Core.Inventory;
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
            public List<int> TerminalIds;
        }

        private readonly Dictionary<int, List<Link>> _graph = new Dictionary<int, List<Link>>();
        private readonly HashSet<string> _failedLinks = new HashSet<string>();
        private readonly ScottyboiWarpProvider _warp;
        private readonly MovementArbiter _movement;
        private readonly Action<string> _say;
        private Link _step;
        private int _target;
        private bool _warpFailed;
        private DateTime _stepStarted, _lastUse;
        private int _terminalAttempt;
        public bool IsActive => _target != 0;
        public string CurrentProvider => _target == 0 ? "Local" : _warpFailed ? "PlayfieldGraph" : "Scottyboi";
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
                    int to = raw.Value<int?>("DstId") ??
                        (raw.Value<string>("$type") == "GridTerminalLink" ? (int)PlayfieldId.Grid : 0);
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
            string gridPath = Path.Combine(pluginDir, "Data", "GridTerminals.json");
            if (File.Exists(gridPath))
            {
                JObject exits = JObject.Parse(File.ReadAllText(gridPath));
                int fixerGrid = (int)PlayfieldId.FixerGrid;
                if (!_graph.TryGetValue(fixerGrid, out List<Link> fixerLinks))
                    _graph[fixerGrid] = fixerLinks = new List<Link>();
                foreach (JProperty destination in exits.Properties())
                {
                    if (!int.TryParse(destination.Name, out int to)) continue;
                    List<int> ids = destination.Value.Values<uint>()
                        .Select(x => unchecked((int)x)).ToList();
                    if (ids.Count > 0)
                        fixerLinks.Add(new Link { From = fixerGrid, To = to,
                            Kind = "FixerGridExit", TerminalIds = ids });
                }
                foreach (List<Link> links in _graph.Values.ToList())
                    foreach (Link gridEntry in links.Where(x => x.To == (int)PlayfieldId.Grid &&
                        (x.Kind == "GridTerminalLink" || x.Kind == "TerminalLink")).ToList())
                        links.Add(new Link { From = gridEntry.From, To = fixerGrid,
                            Kind = "FixerGridTerminalLink", TerminalName = gridEntry.TerminalName,
                            Position = gridEntry.Position });
            }
        }

        public TravelResult Tick(int target)
        {
            int current = Playfield.ModelIdentity.Instance;
            if (current == target)
            {
                if (_target == target && !_warpFailed && _warp.Tick(target) == WarpResult.InProgress)
                    return TravelResult.InProgress;
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
                    LastFailure = (string.IsNullOrEmpty(warpReason) ? "" : $"Scottyboi: {warpReason} ") +
                        $"No mapped fallback path from playfield {current} to {target}.";
                    return TravelResult.Blocked;
                }
                _stepStarted = DateTime.UtcNow;
                _lastUse = DateTime.MinValue;
                _terminalAttempt = 0;
                _say($"Fallback travel: {_step.Kind} from {_step.From} to {_step.To}.");
            }
            if (DateTime.UtcNow - _stepStarted > TimeSpan.FromSeconds(90))
            {
                FailStep($"Transition {_step.From}->{_step.To} timed out");
                return TravelResult.InProgress;
            }
            if (_step.Kind == "FixerGridExit")
            {
                if (!Inventory.Find(160978, out Item receptacle))
                { FailStep("Data Receptacle is unavailable for the Fixer Grid exit"); return TravelResult.InProgress; }
                if (_terminalAttempt >= _step.TerminalIds.Count &&
                    DateTime.UtcNow - _lastUse > TimeSpan.FromSeconds(10))
                { FailStep($"Fixer Grid exit to {_step.To} did not zone"); return TravelResult.InProgress; }
                if (DateTime.UtcNow - _lastUse > TimeSpan.FromSeconds(5) &&
                    _terminalAttempt < _step.TerminalIds.Count)
                {
                    Item.UseItemOnItem(receptacle.Slot,
                        new Identity(IdentityType.Terminal, _step.TerminalIds[_terminalAttempt++]));
                    _lastUse = DateTime.UtcNow;
                }
                return TravelResult.InProgress;
            }
            float arrivalRadius = _step.Kind == "ZoneBorderLink" ? 0.75f :
                _step.Kind == "TeleporterLink" ? 2f : 3f;
            if (Vector3.Distance(DynelManager.LocalPlayer.Position, _step.Position) > arrivalRadius)
            {
                if (!_movementIsNavigating()) _movement.SetDestination(MovementOwner.OutdoorTravel, _step.Position);
                return TravelResult.InProgress;
            }
            if (_step.Kind == "GridTerminalLink" || _step.Kind == "TerminalLink" ||
                _step.Kind == "FixerGridTerminalLink")
            {
                _movement.Release(MovementOwner.OutdoorTravel);
                if (DateTime.UtcNow - _lastUse > TimeSpan.FromSeconds(3))
                {
                    SimpleItem terminal = DynelManager.Terminals.Where(x =>
                        string.Equals(x.Name, _step.TerminalName, StringComparison.OrdinalIgnoreCase) &&
                        Vector3.Distance(x.Position, _step.Position) < 12f)
                        .OrderBy(x => Vector3.Distance(x.Position, _step.Position)).FirstOrDefault();
                    if (_step.Kind == "FixerGridTerminalLink")
                    {
                        if (terminal != null && Inventory.Find(160978, out Item receptacle))
                            receptacle.UseOn(terminal.Identity);
                    }
                    else terminal?.Use();
                    _lastUse = DateTime.UtcNow;
                }
            }
            else if (_step.Kind == "TeleporterLink" && DateTime.UtcNow - _lastUse > TimeSpan.FromSeconds(3))
            {
                // Some Grid exits are interactable terminals; others zone on
                // crossing. Try a terminal only when it is at the mapped exit.
                SimpleItem teleporter = DynelManager.Terminals
                    .Where(x => Vector3.Distance(x.Position, _step.Position) < 5f)
                    .OrderBy(x => Vector3.Distance(x.Position, _step.Position)).FirstOrDefault();
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
            _movement.Owner == MovementOwner.OutdoorTravel && AOSharp.Pathfinding.SMovementController.IsNavigating();

        private static string LinkKey(Link link) => $"{link.From}:{link.To}:{link.Kind}";

        private void FailStep(string reason)
        {
            _say(reason + "; blacklisting this link and replanning.");
            _failedLinks.Add(LinkKey(_step));
            _movement.Release(MovementOwner.OutdoorTravel);
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
                            string.Equals(x.Name, "Enter The Grid", StringComparison.OrdinalIgnoreCase))
                        .OrderBy(x => Vector3.Distance(x.Position, DynelManager.LocalPlayer.Position))
                        .FirstOrDefault();
                    if (grid != null)
                    {
                        links = links == null ? new List<Link>() : new List<Link>(links);
                        links.Add(new Link { From = from, To = (int)PlayfieldId.Grid,
                            Kind = "GridTerminalLink", TerminalName = grid.Name, Position = grid.Position });
                    }
                }
                if (links == null) continue;
                foreach (Link link in links)
                {
                    if (_failedLinks.Contains(LinkKey(link))) continue;
                    if ((link.Kind == "FixerGridTerminalLink" || link.Kind == "FixerGridExit") &&
                        !Inventory.Find(160978, out _)) continue;
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
            // A nearby, single verified link is cheaper and avoids bothering a
            // public warp bot for a short border/terminal transition.
            int current = Playfield.ModelIdentity.Instance;
            _warpFailed = target > 0 && DynelManager.LocalPlayer != null &&
                _graph.TryGetValue(current, out List<Link> direct) && direct.Any(x =>
                    x.To == target && (x.Kind == "FixerGridExit" ||
                        Vector3.Distance(DynelManager.LocalPlayer.Position, x.Position) <= 300f));
            _failedLinks.Clear();
            LastFailure = null;
            _movement.Release(MovementOwner.OutdoorTravel);
            _warp.Reset();
        }

        public void InvalidatePath() => _movement.Release(MovementOwner.OutdoorTravel);
    }
}
