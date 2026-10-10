using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AOSharp.Common.GameData;
using AOSharp.Core;
using AOSharp.Pathfinding;
using Newtonsoft.Json;

namespace RKmission
{
    // Records user-walked routes and replays them only when both endpoints match.
    // This deliberately does not synthesize FGrid paths across unobserved gaps.
    internal sealed class NavigationRouteRecorder : IDisposable
    {
        private const float RecordedCorridorTolerance = 1f;
        private const float RecordedHeightTolerance = 1.25f;
        private sealed class RouteFile { public List<Route> Routes { get; set; } = new List<Route>(); }
        private sealed class Route
        {
            public string Name { get; set; }
            public int Playfield { get; set; }
            public List<float[]> Points { get; set; } = new List<float[]>();
            public DateTime RecordedAtUtc { get; set; }
            [JsonIgnore] public bool Bundled { get; set; }
        }

        private readonly string _path;
        private readonly Action<string> _say;
        private readonly List<Route> _routes = new List<Route>();
        private Route _recording, _playing;
        private List<Vector3> _playPoints;
        private int _playIndex;
        private DateTime _nextSample;
        private bool _writable = true;

        public bool IsRecording => _recording != null;
        public bool IsPlaying => _playing != null;
        public string Status => IsRecording ? $"recording '{_recording.Name}' ({_recording.Points.Count} points)" :
            _playing != null ? $"playing '{_playing.Name}' {_playIndex + 1}/{_playPoints.Count}" :
            $"{_routes.Count} saved route(s)";
        public IReadOnlyList<string> RouteSummaries => _routes
            .OrderBy(x => x.Playfield).ThenBy(x => x.Name)
            .Select(x => $"{x.Name}  |  PF {x.Playfield}  |  {x.Points.Count} pts  |  {x.RecordedAtUtc:u}")
            .ToList();

        public NavigationRouteRecorder(string pluginDir, Action<string> say)
        {
            _say = say;
            _path = System.IO.Path.Combine(pluginDir, "RKMissionData", "navigation-routes.json");
            LoadBundled(System.IO.Path.Combine(pluginDir, "Data", "NavigationRoutes.json"));
            Load();
            Game.OnUpdate += OnUpdate;
        }

        public void Start(string name)
        {
            if (DynelManager.LocalPlayer == null || Game.IsZoning) { _say("Nav recording requires a stable playfield."); return; }
            if (string.IsNullOrWhiteSpace(name)) name = $"route-{Playfield.ModelIdentity.Instance}-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
            StopPlayback();
            _recording = new Route { Name = name.Trim(), Playfield = Playfield.ModelIdentity.Instance, RecordedAtUtc = DateTime.UtcNow };
            AddPoint(DynelManager.LocalPlayer.Position, true);
            _say($"Nav recorder started '{_recording.Name}' in playfield {_recording.Playfield}; walk the safe route, then /rkm nav stop.");
        }

        public void Stop()
        {
            if (_recording == null) { _say("Nav recorder is not recording."); return; }
            if (DynelManager.LocalPlayer != null) AddPoint(DynelManager.LocalPlayer.Position, true);
            Route route = _recording; _recording = null;
            if (route.Points.Count < 2) { _say($"Nav route '{route.Name}' discarded: fewer than two distinct points."); return; }
            _routes.RemoveAll(x => string.Equals(x.Name, route.Name, StringComparison.OrdinalIgnoreCase));
            _routes.Add(route); Save();
            _say($"Nav route '{route.Name}' saved with {route.Points.Count} points in playfield {route.Playfield}.");
        }

        public void List()
        {
            if (_routes.Count == 0) { _say("No recorded navigation routes."); return; }
            foreach (Route route in _routes.OrderBy(x => x.Playfield).ThenBy(x => x.Name))
                _say($"Nav route '{route.Name}': PF {route.Playfield}, {route.Points.Count} points, {route.RecordedAtUtc:u}.");
        }

        // A route endpoint is a positioning hint, never proof that a Grid
        // terminal exists there. The caller must verify the live terminal.
        public bool TryGetEndpoint(string name, int playfield, Vector3 origin, out Vector3 endpoint)
        {
            endpoint = default(Vector3);
            Route route = _routes.FirstOrDefault(x => x.Playfield == playfield &&
                string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase) &&
                x.Points.Count >= 2 && Vector3.Distance(V(x.Points[0]), origin) <= 6f);
            if (route == null) return false;
            endpoint = V(route.Points[route.Points.Count - 1]);
            return true;
        }

        public bool TryNavigate(Vector3 target, MovementArbiter movement, MovementOwner owner, float endpointTolerance = 6f)
            => TryNavigate(null, target, movement, owner, endpointTolerance);

        public bool TryNavigate(string routeName, Vector3 target, MovementArbiter movement,
            MovementOwner owner, float endpointTolerance = 6f)
        {
            var player = DynelManager.LocalPlayer;
            if (player == null || IsRecording || Game.IsZoning) return false;
            if (_playing == null || _playing.Playfield != Playfield.ModelIdentity.Instance ||
                (!string.IsNullOrWhiteSpace(routeName) &&
                 !string.Equals(_playing.Name, routeName, StringComparison.OrdinalIgnoreCase)) ||
                _playPoints == null || Vector3.Distance(_playPoints[_playPoints.Count - 1], target) > endpointTolerance)
            {
                StopPlayback();
                Route best = null; bool reverse = false; float bestCost = float.MaxValue;
                foreach (Route route in _routes.Where(x =>
                    x.Playfield == Playfield.ModelIdentity.Instance && x.Points.Count >= 2 &&
                    (string.IsNullOrWhiteSpace(routeName) ||
                     string.Equals(x.Name, routeName, StringComparison.OrdinalIgnoreCase))))
                {
                    Vector3 first = V(route.Points[0]), last = V(route.Points[route.Points.Count - 1]);
                    float forward = Vector3.Distance(player.Position, first) + Vector3.Distance(target, last);
                    float backward = Vector3.Distance(player.Position, last) + Vector3.Distance(target, first);
                    if (Vector3.Distance(player.Position, first) <= endpointTolerance && Vector3.Distance(target, last) <= endpointTolerance && forward < bestCost &&
                        (Playfield.ModelIdentity.Instance != (int)PlayfieldId.FixerGrid ||
                         SupportedRecordedFGridRoute(route.Points.Select(V).ToList(), player.Position, target)))
                    { best = route; reverse = false; bestCost = forward; }
                    if (Vector3.Distance(player.Position, last) <= endpointTolerance && Vector3.Distance(target, first) <= endpointTolerance && backward < bestCost &&
                        (Playfield.ModelIdentity.Instance != (int)PlayfieldId.FixerGrid ||
                         SupportedRecordedFGridRoute(route.Points.Select(V).Reverse().ToList(), player.Position, target)))
                    { best = route; reverse = true; bestCost = backward; }
                }
                if (best == null) return false;
                List<Vector3> candidate = best.Points.Select(V).ToList();
                if (reverse) candidate.Reverse();
                bool fixerGrid = Playfield.ModelIdentity.Instance == (int)PlayfieldId.FixerGrid;
                if (fixerGrid)
                {
                    // A manually recorded walkway is useful only when both
                    // connectors and every saved leg remain on its floor.
                    if (!SupportedRecordedFGridRoute(candidate, player.Position, target))
                    {
                        _say($"Recorded FGrid route '{best.Name}' rejected: endpoint or walkway connector is unsupported.");
                        return false;
                    }
                }
                int recordedPointCount = candidate.Count;
                if (fixerGrid)
                    candidate = SimplifySupportedFGridPath(candidate, player.Position.Y);
                else if (IsTerminalToGridRoute(best.Name))
                    candidate = SimplifyRecordedPath(candidate);
                if (fixerGrid && !SupportedRecordedFGridRoute(candidate, player.Position, target))
                {
                    _say($"Recorded FGrid route '{best.Name}' rejected after smoothing: a retained leg lost floor support.");
                    return false;
                }
                _playing = best;
                _playPoints = candidate;
                _playIndex = 0;
                _say(fixerGrid
                    ? $"Using recorded FGrid route '{best.Name}' toward ({target.X:0.0},{target.Y:0.0},{target.Z:0.0}); " +
                      $"{recordedPointCount} samples reduced to {_playPoints.Count - 1} walkway-supported leg(s)."
                    : IsTerminalToGridRoute(best.Name)
                    ? $"Using recorded nav route '{best.Name}' toward ({target.X:0.0},{target.Y:0.0},{target.Z:0.0}); " +
                      $"{recordedPointCount} recorded samples reduced to {_playPoints.Count - 1} corridor-verified leg(s)."
                    : $"Using recorded nav route '{best.Name}' toward ({target.X:0.0},{target.Y:0.0},{target.Z:0.0}); no straight-line shortcut.");
            }

            while (_playIndex < _playPoints.Count - 1 && Vector3.Distance(player.Position, _playPoints[_playIndex]) <= 1.2f) _playIndex++;
            Vector3 waypoint = _playPoints[Math.Min(_playIndex, _playPoints.Count - 1)];
            if (Playfield.ModelIdentity.Instance == (int)PlayfieldId.FixerGrid &&
                !LocalRoutePlanner.SupportedFGridSegment(player.Position, waypoint, player.Position.Y))
            { StopPlayback(); return false; }
            if (Vector3.Distance(player.Position, target) <= 1.2f) { StopPlayback(); return true; }
            if (movement.Owner != owner || !SMovementController.IsNavigating())
                if (!movement.SetDestination(owner, waypoint)) { StopPlayback(); return false; }
            return true;
        }

        public void StopPlayback() { _playing = null; _playPoints = null; _playIndex = 0; }

        private static bool SupportedRecordedFGridRoute(List<Vector3> points, Vector3 start, Vector3 target) =>
            points.Count >= 2 && Vector3.Distance(points[points.Count - 1], target) <= 1.5f &&
            LocalRoutePlanner.SupportedFGridSegment(start, points[0], start.Y) &&
            Enumerable.Range(1, points.Count - 1).All(i =>
                LocalRoutePlanner.SupportedFGridSegment(points[i - 1], points[i], start.Y));

        private static bool IsTerminalToGridRoute(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            string normalized = name.Replace(" ", "").Replace("-", "");
            return normalized.IndexOf("terminal", StringComparison.OrdinalIgnoreCase) >= 0 &&
                normalized.IndexOf("grid", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // Recorder samples describe the authoritative walked corridor. Terminal-to-Grid
        // approaches can skip samples only when every omitted point remains close to the
        // proposed horizontal leg and its interpolated height. This retains real corners,
        // ramps and both endpoints while avoiding a movement submission every 0.75 metres.
        private static List<Vector3> SimplifyRecordedPath(List<Vector3> points)
        {
            if (points == null || points.Count < 3)
                return points == null ? new List<Vector3>() : new List<Vector3>(points);
            int[] legs = Enumerable.Repeat(int.MaxValue, points.Count).ToArray();
            int[] previous = Enumerable.Repeat(-1, points.Count).ToArray();
            legs[0] = 0;
            for (int candidate = 1; candidate < points.Count; candidate++)
            {
                for (int anchor = 0; anchor < candidate; anchor++)
                {
                    if (legs[anchor] == int.MaxValue ||
                        legs[anchor] + 1 >= legs[candidate] ||
                        !RecordedShortcutSafe(points, anchor, candidate)) continue;
                    legs[candidate] = legs[anchor] + 1;
                    previous[candidate] = anchor;
                }
            }
            if (previous[points.Count - 1] < 0) return new List<Vector3>(points);
            var simplified = new List<Vector3>();
            for (int at = points.Count - 1; at >= 0; at = previous[at])
            {
                simplified.Add(points[at]);
                if (at == 0) break;
            }
            simplified.Reverse();
            return simplified;
        }

        private static bool RecordedShortcutSafe(List<Vector3> points, int anchor, int candidate)
        {
            if (points == null || anchor < 0 || candidate <= anchor || candidate >= points.Count)
                return false;
            Vector3 start = points[anchor], end = points[candidate];
            Vector3 horizontal = end - start;
            horizontal.Y = 0;
            float squared = horizontal.X * horizontal.X + horizontal.Z * horizontal.Z;
            if (squared < 0.01f) return false;
            for (int i = anchor + 1; i < candidate; i++)
            {
                Vector3 point = points[i];
                float projection = ((point.X - start.X) * horizontal.X +
                    (point.Z - start.Z) * horizontal.Z) / squared;
                projection = Math.Max(0, Math.Min(1, projection));
                Vector3 onSegment = new Vector3(start.X + horizontal.X * projection,
                    start.Y + (end.Y - start.Y) * projection,
                    start.Z + horizontal.Z * projection);
                Vector3 offset = point - onSegment;
                float horizontalOffset = (float)Math.Sqrt(offset.X * offset.X + offset.Z * offset.Z);
                if (horizontalOffset > RecordedCorridorTolerance ||
                    Math.Abs(offset.Y) > RecordedHeightTolerance) return false;
            }
            return true;
        }

        // Recorded FGrid samples remain the corridor authority. From each retained
        // anchor, take the farthest later sample whose straight chord stays in that
        // corridor and whose complete centre/edge tracks have live floor support.
        private static List<Vector3> SimplifySupportedFGridPath(List<Vector3> points, float floorHeight)
        {
            if (points == null || points.Count < 3)
                return points == null ? new List<Vector3>() : new List<Vector3>(points);
            var simplified = new List<Vector3> { points[0] };
            int anchor = 0;
            while (anchor < points.Count - 1)
            {
                int next = anchor + 1;
                for (int candidate = points.Count - 1; candidate > anchor + 1; candidate--)
                {
                    if (!RecordedShortcutSafe(points, anchor, candidate) ||
                        !LocalRoutePlanner.SupportedFGridSegment(
                            points[anchor], points[candidate], floorHeight)) continue;
                    next = candidate;
                    break;
                }
                simplified.Add(points[next]);
                anchor = next;
            }
            return simplified;
        }

        private void OnUpdate(object sender, float deltaTime)
        {
            if (_recording == null || DynelManager.LocalPlayer == null || Game.IsZoning) return;
            if (Playfield.ModelIdentity.Instance != _recording.Playfield) { _say("Nav recording stopped without saving because the playfield changed."); _recording = null; return; }
            if (DateTime.UtcNow < _nextSample) return;
            _nextSample = DateTime.UtcNow.AddMilliseconds(150);
            AddPoint(DynelManager.LocalPlayer.Position, false);
        }

        private void AddPoint(Vector3 p, bool force)
        {
            if (_recording == null) return;
            Vector3 previous = _recording.Points.Count == 0 ? p : V(_recording.Points[_recording.Points.Count - 1]);
            if (!force && Vector3.Distance(previous, p) < 0.75f) return;
            if (force && _recording.Points.Count > 0 && Vector3.Distance(previous, p) < 0.1f) return;
            _recording.Points.Add(new[] { p.X, p.Y, p.Z });
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(_path)) return;
                RouteFile file = ReadRoutes(_path);
                foreach (Route route in file.Routes)
                    _routes.RemoveAll(x => x.Playfield == route.Playfield &&
                        string.Equals(x.Name, route.Name, StringComparison.OrdinalIgnoreCase));
                _routes.AddRange(file.Routes);
            }
            catch (Exception ex) { _writable = false; _say("Navigation route file could not be read; existing file preserved: " + ex.Message); }
        }

        private void LoadBundled(string path)
        {
            try
            {
                if (!File.Exists(path)) return;
                RouteFile file = ReadRoutes(path);
                foreach (Route route in file.Routes) route.Bundled = true;
                _routes.AddRange(file.Routes);
                _say($"Loaded {file.Routes.Count} bundled navigation route(s).");
            }
            catch (Exception ex) { _say("Bundled navigation routes were not loaded: " + ex.Message); }
        }

        private static RouteFile ReadRoutes(string path)
        {
            RouteFile file = JsonConvert.DeserializeObject<RouteFile>(File.ReadAllText(path)) ?? new RouteFile();
            if (file.Routes == null || file.Routes.Any(x => x == null || x.Playfield <= 0 ||
                x.Points == null || x.Points.Count < 2 || x.Points.Any(p => p == null || p.Length != 3 ||
                    p.Any(c => float.IsNaN(c) || float.IsInfinity(c)))))
                throw new InvalidDataException("invalid route record");
            return file;
        }

        private void Save()
        {
            if (!_writable) return;
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path));
                string temp = _path + ".new";
                File.WriteAllText(temp, JsonConvert.SerializeObject(new RouteFile
                { Routes = _routes.Where(x => !x.Bundled).ToList() }, Formatting.Indented));
                if (File.Exists(_path)) File.Replace(temp, _path, null); else File.Move(temp, _path);
            }
            catch (Exception ex) { _say("Navigation route save failed: " + ex.Message); }
        }

        private static Vector3 V(float[] p) => new Vector3(p[0], p[1], p[2]);
        public void Dispose() { Game.OnUpdate -= OnUpdate; StopPlayback(); }
    }
}
