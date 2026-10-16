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
    internal enum CityReturnRouteResult { NoMatch, InProgress, Arrived }

    // Records user-walked routes and replays matching ordered route sections.
    // This deliberately does not synthesize FGrid paths across unobserved gaps.
    internal sealed class NavigationRouteRecorder : IDisposable
    {
        private const float RecordedCorridorTolerance = 1f;
        private const float RecordedHeightTolerance = 1.25f;
        private const float DungeonSourceAttachDistance = 4f;
        private const float DungeonTargetAttachDistance = 4f;
        private const float DungeonMinimumVerticalSpan = 1.5f;
        private sealed class RouteFile { public List<Route> Routes { get; set; } = new List<Route>(); }
        private sealed class Route
        {
            public string Name { get; set; }
            public int Playfield { get; set; }
            public List<float[]> Points { get; set; } = new List<float[]>();
            public DateTime RecordedAtUtc { get; set; }
            // Optional v2 metadata. Missing fields in existing files deserialize
            // to conservative Foot/Bidirectional behavior below.
            public string MovementMode { get; set; }
            public string AltitudeProfile { get; set; }
            public string Directionality { get; set; }
            public bool DungeonTopologyOnly { get; set; }
            public string SourceRoomName { get; set; }
            public string SourceRoomFingerprint { get; set; }
            public float[] SourceRoomPosition { get; set; }
            public float SourceRoomRotationDegrees { get; set; }
            public float[] SourceTemplatePosition { get; set; }
            public float SourceTemplateRotationDegrees { get; set; }
            public float[] SourceLocalRect { get; set; }
            public int SourceSurfaceCount { get; set; }
            public int SourceDoorCount { get; set; }
            public int FirstReusablePoint { get; set; }
            public bool ReusableTemplateValidated { get; set; }
            [JsonIgnore] public bool Bundled { get; set; }
        }
        private sealed class DungeonSubpath
        {
            public Route Route;
            public List<Vector3> Points;
            public int SourceIndex;
            public int TargetIndex;
            public bool Reversed;
            public float SourceDistance;
            public float TargetDistance;
            public float VerticalSpan;
            public float Cost;
        }

        internal sealed class DungeonTopologyContinuation
        {
            public string RouteName;
            public int Playfield;
            public List<Vector3> Points;
            public List<Vector3> PhysicalPoints;
            public int ProgressIndex;
            public Vector3 TargetSide;
        }

        private readonly string _path;
        private readonly Action<string> _say;
        private readonly List<Route> _routes = new List<Route>();
        private Route _recording, _playing;
        private DungeonRoomCapture.Snapshot _recordingRoomCapture;
        private List<Vector3> _playPoints, _dungeonPhysicalPoints;
        private List<Vector3> _dungeonStartConnector;
        private int _dungeonStartConnectorIndex;
        private int _playIndex;
        private bool _dungeonPlayback;
        private Vector3 _dungeonRequestedTarget;
        private bool _cityReturnPlayback;
        private int _lastDungeonReportedIndex;
        private int _issuedPlayIndex = -1, _playRecoveryAttempts;
        private Vector3 _playLastPosition;
        private float _playLastWaypointDistance = float.MaxValue;
        private DateTime _playLastProgress, _nextPlayDiagnostic;
        private DateTime _nextSample;
        private bool _writable = true;
        public string LastPlaybackFailure { get; private set; }

        public bool IsRecording => _recording != null;
        public bool IsPlaying => _playing != null;
        public string Status => IsRecording ? $"recording '{_recording.Name}' ({_recording.Points.Count} points)" :
            _playing != null ? $"playing '{_playing.Name}' {_playIndex + 1}/{_playPoints.Count}" :
            $"{_routes.Count} saved route(s)";
        public IReadOnlyList<string> RouteSummaries => _routes
            .OrderBy(x => x.Playfield).ThenBy(x => x.Name)
            .Select(x => $"{x.Name}  |  PF {x.Playfield}  |  {RouteMode(x)}  |  {RouteDirection(x)}  |  {x.Points.Count} pts  |  {x.RecordedAtUtc:u}")
            .ToList();

        public NavigationRouteRecorder(string pluginDir, Action<string> say)
        {
            _say = say;
            _path = System.IO.Path.Combine(pluginDir, "RKMissionData", "navigation-routes.json");
            LoadBundled(System.IO.Path.Combine(pluginDir, "Data", "NavigationRoutes.json"));
            Load();
            LoadDungeonTopology(System.IO.Path.Combine(pluginDir, "Data", "DungeonTopologyRoutes.json"));
            Game.OnUpdate += OnUpdate;
        }

        public void Start(string name)
            => Start(name, "Auto", "Bidirectional");

        public void Start(string name, string movementMode, string directionality)
        {
            if (DynelManager.LocalPlayer == null || Game.IsZoning) { _say("Nav recording requires a stable playfield."); return; }
            if (string.IsNullOrWhiteSpace(name)) name = $"route-{Playfield.ModelIdentity.Instance}-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
            StopPlayback();
            string mode = string.Equals(movementMode, "Vehicle", StringComparison.OrdinalIgnoreCase) ? "Vehicle" :
                string.Equals(movementMode, "Foot", StringComparison.OrdinalIgnoreCase) ? "Foot" : "Auto";
            string direction = string.Equals(directionality, "Forward", StringComparison.OrdinalIgnoreCase) ? "Forward" : "Bidirectional";
            _recording = new Route { Name = name.Trim(), Playfield = Playfield.ModelIdentity.Instance,
                RecordedAtUtc = DateTime.UtcNow, MovementMode = mode, Directionality = direction,
                AltitudeProfile = mode == "Vehicle" ? "Recorded" : "Ground" };
            _recordingRoomCapture = null;
            if (Playfield.IsDungeon)
            {
                try { _recordingRoomCapture = DungeonRoomCapture.ReadCurrent(); }
                catch (Exception ex) { _say("Room metadata unavailable; ordinary route recording continues: " + ex.Message); }
            }
            AddPoint(DynelManager.LocalPlayer.Position, true);
            _say($"Nav recorder started '{_recording.Name}' in playfield {_recording.Playfield}; mode={mode}, altitude={_recording.AltitudeProfile}, direction={direction}. Follow the safe route, then /rkm nav stop.");
        }

        public void Stop()
        {
            if (_recording == null) { _say("Nav recorder is not recording."); return; }
            if (DynelManager.LocalPlayer != null) AddPoint(DynelManager.LocalPlayer.Position, true);
            Route route = _recording; _recording = null;
            if (route.Points.Count < 2) { _say($"Nav route '{route.Name}' discarded: fewer than two distinct points."); return; }
            _routes.RemoveAll(x => string.Equals(x.Name, route.Name, StringComparison.OrdinalIgnoreCase));
            _routes.Add(route); Save();
            if (_recordingRoomCapture != null)
            {
                try
                {
                    string capture = DungeonRoomCapture.Save(RoomCaptureDirectory,
                        _recordingRoomCapture, route.Name, route.Points);
                    _say($"Room metadata and route evidence saved: {capture}; candidate fingerprint={_recordingRoomCapture.CandidateFingerprint}; reusable playback remains disabled.");
                }
                catch (Exception ex) { _say("Room metadata save failed; recorded route preserved: " + ex.Message); }
                _recordingRoomCapture = null;
            }
            _say($"Nav route '{route.Name}' saved with {route.Points.Count} points in playfield {route.Playfield}.");
        }

        private string RoomCaptureDirectory => System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(_path), "room-captures");

        public void CaptureCurrentRoom()
        {
            try
            {
                var capture = DungeonRoomCapture.ReadCurrent();
                string path = DungeonRoomCapture.Save(RoomCaptureDirectory, capture);
                _say($"Room metadata captured: room={capture.RoomInstance}, name='{capture.RoomName}', " +
                    $"surface meshes={capture.Surfaces.Count}, doors={capture.Doors.Count}, candidate fingerprint={capture.CandidateFingerprint}; file={path}. Reusable playback is not enabled.");
            }
            catch (Exception ex) { _say("Room capture failed: " + ex.Message); }
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
            Route route = _routes.FirstOrDefault(x => !x.DungeonTopologyOnly && x.Playfield == playfield &&
                string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase) &&
                x.Points.Count >= 2 && Vector3.Distance(V(x.Points[0]), origin) <= 6f);
            if (route == null) return false;
            endpoint = V(route.Points[route.Points.Count - 1]);
            return true;
        }

        public bool TryNavigate(Vector3 target, MovementArbiter movement, MovementOwner owner, float endpointTolerance = 6f)
            => TryNavigate(null, target, movement, owner, endpointTolerance);

        public bool HasFootRouteTo(Vector3 target, float endpointTolerance = 6f)
        {
            SimpleChar player = DynelManager.LocalPlayer;
            if (player == null) return false;
            return _routes.Any(route => !route.DungeonTopologyOnly && route.Playfield == Playfield.ModelIdentity.Instance &&
                route.Points.Count >= 2 && RouteMode(route) == "Foot" &&
                (Vector3.Distance(player.Position, V(route.Points[0])) <= endpointTolerance &&
                 Vector3.Distance(target, V(route.Points[route.Points.Count - 1])) <= endpointTolerance ||
                 RouteDirection(route) != "Forward" &&
                 Vector3.Distance(player.Position, V(route.Points[route.Points.Count - 1])) <= endpointTolerance &&
                 Vector3.Distance(target, V(route.Points[0])) <= endpointTolerance));
        }

        // The bundled city-return recordings are evidence for one narrow journey:
        // a verified Scotty landing to the saved roller terminal. Select only an
        // endpoint-compatible route in the observed city and attach to its recorded
        // corridor. The caller still owns exact live terminal verification.
        public CityReturnRouteResult TryNavigateCityReturn(Vector3 observedLanding,
            Vector3 target, MovementArbiter movement, MovementOwner owner)
        {
            SimpleChar player = DynelManager.LocalPlayer;
            if (player == null || IsRecording || Game.IsZoning)
                return CityReturnRouteResult.NoMatch;

            if (!_cityReturnPlayback || _playing == null || _playPoints == null ||
                _playing.Playfield != Playfield.ModelIdentity.Instance ||
                Vector3.Distance(_playPoints[_playPoints.Count - 1], target) > 6f)
            {
                StopPlayback();
                Route best = null;
                List<Vector3> bestPath = null;
                float bestCost = float.MaxValue, bestCorridor = float.MaxValue;
                int bestSegment = 0;
                bool bestReverse = false;
                foreach (Route route in _routes.Where(x =>
                    IsCityReturnRoute(x) &&
                    x.Playfield == Playfield.ModelIdentity.Instance &&
                    x.Points.Count >= 2 &&
                    RouteModeMatches(x, player.MovementState == MovementState.Fly)))
                {
                    List<Vector3> recorded = route.Points.Select(V).ToList();
                    foreach (bool reverse in new[] { false, true })
                    {
                        if (reverse && RouteDirection(route) == "Forward") continue;
                        List<Vector3> oriented = reverse
                            ? recorded.AsEnumerable().Reverse().ToList() : recorded;
                        float targetDistance = Vector3.Distance(target,
                            oriented[oriented.Count - 1]);
                        if (targetDistance > 6f ||
                            !TryAttachCityCorridor(oriented, observedLanding,
                                out _, out float landingCorridor, out _, out _ ) ||
                            !TryAttachCityCorridor(oriented, player.Position,
                                out List<Vector3> resumed, out float currentCorridor,
                                out float currentVertical, out int segment)) continue;
                        float cost = landingCorridor + currentCorridor + currentVertical +
                            targetDistance;
                        if (cost >= bestCost) continue;
                        best = route;
                        bestPath = resumed;
                        bestCost = cost;
                        bestCorridor = landingCorridor;
                        bestSegment = segment;
                        bestReverse = reverse;
                    }
                }
                if (best == null) return CityReturnRouteResult.NoMatch;

                int recordedPointCount = bestPath.Count;
                // ICC's lift/statue recordings retain every supplied height sample.
                // OA and Borealis use the existing corridor/elevation-bounded smoother.
                if (best.Playfield != 655)
                    bestPath = SimplifyRecordedPath(bestPath);
                _playing = best;
                _playPoints = bestPath;
                _playIndex = 0;
                _cityReturnPlayback = true;
                float startDistance = Vector3.Distance(observedLanding,
                    V(best.Points[bestReverse ? best.Points.Count - 1 : 0]));
                _say($"Selected city return route '{best.Name}' in PF {best.Playfield}; " +
                    $"landing-to-start={startDistance:0.0}m, landing-to-corridor={bestCorridor:0.0}m, " +
                    $"forward-reacquired={(bestSegment > 1 ? "yes" : "no")}, mode={RouteMode(best)}, " +
                    $"altitude={RouteAltitude(best)}, direction={RouteDirection(best)}; " +
                    $"{recordedPointCount} retained recorded sample(s), {_playPoints.Count} playback point(s)." );
            }

            while (_playIndex < _playPoints.Count - 1 &&
                Vector3.Distance(player.Position, _playPoints[_playIndex]) <= 1.2f)
                _playIndex++;
            Vector3 waypoint = _playPoints[_playIndex];
            if (_playIndex == _playPoints.Count - 1 &&
                Vector3.Distance(player.Position, waypoint) <= 1.5f)
            {
                StopPlayback();
                return CityReturnRouteResult.Arrived;
            }
            if (movement.Owner != owner || !SMovementController.IsNavigating())
                if (!movement.SetDestination(owner, waypoint))
                { StopPlayback(); return CityReturnRouteResult.NoMatch; }
            return CityReturnRouteResult.InProgress;
        }

        // Returns a short direction back toward the recorded walkway skeleton.
        // FGrid recovery uses this without changing playback state, so a local
        // correction can rejoin the same route instead of restarting the floor.
        public bool TryGetFGridCenterlineDirection(string routeName, Vector3 position,
            Vector3 target, out Vector3 direction)
        {
            direction = Vector3.Zero;
            return TryGetFGridRecoveryGeometry(routeName, position, target,
                out direction, out _, out _);
        }

        // Describes the oriented route at the nearest forward segment. Recovery
        // can therefore distinguish the roomy ring from the final radial approach
        // and combine a centreline correction with progress toward the endpoint.
        public bool TryGetFGridRecoveryGeometry(string routeName, Vector3 position,
            Vector3 target, out Vector3 centreline, out Vector3 routeForward,
            out bool nearRadialApproach)
        {
            centreline = routeForward = Vector3.Zero;
            nearRadialApproach = false;
            if (Playfield.ModelIdentity.Instance != (int)PlayfieldId.FixerGrid ||
                string.IsNullOrWhiteSpace(routeName)) return false;
            Route route = _routes.Where(x => x.Playfield == (int)PlayfieldId.FixerGrid &&
                    string.Equals(x.Name?.Trim(), routeName.Trim(), StringComparison.Ordinal) &&
                    x.Points.Count >= 2)
                .OrderByDescending(x => x.RecordedAtUtc).FirstOrDefault();
            if (route == null) return false;
            List<Vector3> points = route.Points.Select(V).ToList();
            float forward = Vector3.Distance(position, points[0]) +
                Vector3.Distance(target, points[points.Count - 1]);
            float reverse = Vector3.Distance(position, points[points.Count - 1]) +
                Vector3.Distance(target, points[0]);
            if (reverse < forward) points.Reverse();

            float bestDistance = float.MaxValue;
            Vector3 best = Vector3.Zero;
            int bestSegment = 0;
            int finalAlignmentSegment = points.Count;
            for (int i = 1; i < points.Count - 1; i++)
            {
                Vector3 before = points[i] - points[i - 1]; before.Y = 0;
                Vector3 after = points[i + 1] - points[i]; after.Y = 0;
                if (before.Magnitude >= 0.2f && after.Magnitude >= 0.2f &&
                    Vector3.Dot(before.Normalize(), after.Normalize()) <= 0.5f)
                    finalAlignmentSegment = i + 1;
            }
            for (int i = 1; i < points.Count; i++)
            {
                Vector3 leg = points[i] - points[i - 1]; leg.Y = 0;
                float squared = Vector3.Dot(leg, leg);
                if (squared < 0.01f) continue;
                float amount = Math.Max(0f, Math.Min(1f,
                    Vector3.Dot(position - points[i - 1], leg) / squared));
                Vector3 projection = points[i - 1] + leg * amount;
                projection.Y = position.Y;
                float distance = LocalRoutePlanner.HorizontalDistance(position, projection);
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = projection;
                bestSegment = i;
            }
            if (bestSegment == 0) return false;
            Vector3 legForward = points[bestSegment] - points[bestSegment - 1];
            legForward.Y = 0;
            if (legForward.Magnitude < 0.1f) return false;
            routeForward = legForward.Normalize();
            nearRadialApproach = finalAlignmentSegment < points.Count &&
                (bestSegment >= finalAlignmentSegment ||
                 LocalRoutePlanner.HorizontalDistance(position,
                     points[finalAlignmentSegment - 1]) <= 3f);
            Vector3 correction = best - position; correction.Y = 0;
            // Already close to the skeleton: aim along it, past the nearest
            // sample. This is the useful direction at lift and exit bottlenecks.
            if (correction.Magnitude < 0.55f)
            {
                int ahead = Math.Min(points.Count - 1, bestSegment + 2);
                correction = points[ahead] - position; correction.Y = 0;
            }
            if (correction.Magnitude < 0.1f) return false;
            centreline = correction.Normalize();
            return true;
        }

        // Finds the last deliberate sharp turn in the oriented floor recording.
        // That point is the boundary between circular-corridor travel and the
        // straight radial leg to a lift. Callers can stabilize on the corridor
        // and travel to this point without prematurely submitting the lift itself.
        public bool TryGetFGridLiftApproachGeometry(string routeName, Vector3 position,
            Vector3 lift, out Vector3 corridorProjection, out Vector3 alignmentPoint,
            out Vector3 corridorForward, out Vector3 radialForward, out float crossTrack,
            out bool onRadialLeg)
        {
            corridorProjection = alignmentPoint = corridorForward = radialForward = Vector3.Zero;
            crossTrack = float.MaxValue;
            onRadialLeg = false;
            if (Playfield.ModelIdentity.Instance != (int)PlayfieldId.FixerGrid ||
                string.IsNullOrWhiteSpace(routeName)) return false;
            Route route = _routes.Where(x => x.Playfield == (int)PlayfieldId.FixerGrid &&
                    string.Equals(x.Name?.Trim(), routeName.Trim(), StringComparison.Ordinal) &&
                    x.Points.Count >= 3)
                .OrderByDescending(x => x.RecordedAtUtc).FirstOrDefault();
            if (route == null) return false;
            List<Vector3> points = route.Points.Select(V).ToList();
            if (Vector3.Distance(lift, points[0]) < Vector3.Distance(lift, points[points.Count - 1]))
                points.Reverse();

            int turn = FindFGridLiftAlignmentIndex(points, lift);
            if (turn < 1) return false;
            alignmentPoint = points[turn]; alignmentPoint.Y = position.Y;
            corridorForward = points[turn] - points[turn - 1]; corridorForward.Y = 0;
            radialForward = points[turn + 1] - points[turn]; radialForward.Y = 0;
            if (corridorForward.Magnitude < 0.1f || radialForward.Magnitude < 0.1f) return false;
            corridorForward = corridorForward.Normalize();
            radialForward = radialForward.Normalize();

            float bestCorridor = float.MaxValue;
            for (int i = 1; i <= turn; i++)
            {
                Vector3 projection = ProjectHorizontal(position, points[i - 1], points[i]);
                float distance = LocalRoutePlanner.HorizontalDistance(position, projection);
                if (distance >= bestCorridor) continue;
                bestCorridor = distance;
                corridorProjection = projection;
            }
            float bestRadial = float.MaxValue;
            for (int i = turn + 1; i < points.Count; i++)
                bestRadial = Math.Min(bestRadial, LocalRoutePlanner.HorizontalDistance(position,
                    ProjectHorizontal(position, points[i - 1], points[i])));
            crossTrack = bestCorridor;
            onRadialLeg = bestRadial + 0.15f < bestCorridor &&
                Vector3.Dot(position - alignmentPoint, radialForward) > 0.15f;
            return bestCorridor < float.MaxValue;
        }

        // Recordings sample a manually walked turn. On some floors the nominal
        // 90-degree corner is spread over several short samples, so requiring one
        // sample pair to contain the whole turn misses a valid approach. Walk the
        // lift-facing suffix backwards and keep the earliest point whose remaining
        // samples form the straight radial leg. The preceding segment must still
        // differ materially from that radial heading.
        private static int FindFGridLiftAlignmentIndex(List<Vector3> points, Vector3 lift)
        {
            int sharpTurn = -1;
            for (int i = 1; i < points.Count - 1; i++)
            {
                Vector3 before = points[i] - points[i - 1]; before.Y = 0;
                Vector3 after = points[i + 1] - points[i]; after.Y = 0;
                if (before.Magnitude >= 0.2f && after.Magnitude >= 0.2f &&
                    Vector3.Dot(before.Normalize(), after.Normalize()) <= 0.5f)
                    sharpTurn = i;
            }
            if (sharpTurn >= 1) return sharpTurn;

            int alignment = -1;
            for (int candidate = points.Count - 2; candidate >= 1; candidate--)
            {
                Vector3 radial = lift - points[candidate]; radial.Y = 0;
                Vector3 before = points[candidate] - points[candidate - 1]; before.Y = 0;
                if (radial.Magnitude < 0.5f || before.Magnitude < 0.2f) continue;
                radial = radial.Normalize();
                if (Vector3.Dot(before.Normalize(), radial) > 0.82f) continue;
                bool straightSuffix = true;
                for (int i = candidate + 1; i < points.Count; i++)
                {
                    Vector3 leg = points[i] - points[i - 1]; leg.Y = 0;
                    if (leg.Magnitude < 0.2f || Vector3.Dot(leg.Normalize(), radial) < 0.88f)
                    { straightSuffix = false; break; }
                }
                if (!straightSuffix) continue;
                alignment = candidate;
            }
            return alignment;
        }

        public bool TryNavigateFGridCorridorToAlignment(string routeName, Vector3 lift,
            Vector3 alignmentPoint, MovementArbiter movement, MovementOwner owner)
        {
            var player = DynelManager.LocalPlayer;
            if (player == null || IsRecording || Game.IsZoning) return false;
            Route route = _routes.Where(x => x.Playfield == (int)PlayfieldId.FixerGrid &&
                    string.Equals(x.Name?.Trim(), routeName.Trim(), StringComparison.Ordinal) &&
                    x.Points.Count >= 3)
                .OrderByDescending(x => x.RecordedAtUtc).FirstOrDefault();
            if (route == null) return false;
            List<Vector3> points = route.Points.Select(V).ToList();
            if (Vector3.Distance(lift, points[0]) < Vector3.Distance(lift, points[points.Count - 1]))
                points.Reverse();
            int alignmentIndex = points.FindLastIndex(x =>
                LocalRoutePlanner.HorizontalDistance(x, alignmentPoint) <= 0.35f);
            if (alignmentIndex < 1) return false;
            points = ReacquireForwardFGridPath(points.Take(alignmentIndex + 1).ToList(),
                player.Position);
            if (points.Count < 2 ||
                !LocalRoutePlanner.SupportedFGridSegment(player.Position, points[0], player.Position.Y) ||
                !Enumerable.Range(1, points.Count - 1).All(i =>
                    LocalRoutePlanner.SupportedFGridSegment(points[i - 1], points[i], player.Position.Y)))
                return false;
            points = SimplifySupportedFGridPath(points, player.Position.Y);
            if (_playing != route || _playPoints == null ||
                LocalRoutePlanner.HorizontalDistance(_playPoints[_playPoints.Count - 1], alignmentPoint) > 0.5f)
            {
                StopPlayback();
                _playing = route;
                _playPoints = points;
                _playIndex = 0;
                _say($"Using recorded FGrid corridor '{route.Name}' to the lift alignment point; " +
                    $"radial approach remains uncommitted ({_playPoints.Count - 1} supported leg(s)).");
            }
            while (_playIndex < _playPoints.Count - 1 &&
                Vector3.Distance(player.Position, _playPoints[_playIndex]) <= 1.0f) _playIndex++;
            if (LocalRoutePlanner.HorizontalDistance(player.Position, alignmentPoint) <= 0.65f)
            { StopPlayback(); return true; }
            Vector3 waypoint = _playPoints[Math.Min(_playIndex, _playPoints.Count - 1)];
            if (!LocalRoutePlanner.SupportedFGridSegment(player.Position, waypoint, player.Position.Y))
            { StopPlayback(); return false; }
            if (movement.Owner != owner || !SMovementController.IsNavigating())
                if (!movement.SetDestination(owner, waypoint)) { StopPlayback(); return false; }
            return true;
        }

        private static Vector3 ProjectHorizontal(Vector3 position, Vector3 start, Vector3 end)
        {
            Vector3 leg = end - start; leg.Y = 0;
            float squared = Vector3.Dot(leg, leg);
            float amount = squared < 0.01f ? 0f : Math.Max(0f, Math.Min(1f,
                Vector3.Dot(position - start, leg) / squared));
            Vector3 projection = start + leg * amount;
            projection.Y = position.Y;
            return projection;
        }

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
                    !x.DungeonTopologyOnly && RouteModeMatches(x, player.MovementState == MovementState.Fly) &&
                    (string.IsNullOrWhiteSpace(routeName) ||
                     string.Equals(x.Name, routeName, StringComparison.OrdinalIgnoreCase))))
                {
                    Vector3 first = V(route.Points[0]), last = V(route.Points[route.Points.Count - 1]);
                    float forward = Vector3.Distance(player.Position, first) + Vector3.Distance(target, last);
                    float backward = Vector3.Distance(player.Position, last) + Vector3.Distance(target, first);
                    bool isFGridCandidate = Playfield.ModelIdentity.Instance == (int)PlayfieldId.FixerGrid;
                    bool vehicleCandidate = !isFGridCandidate && RouteMode(route) == "Vehicle";
                    if ((isFGridCandidate
                            ? CanReacquireFGridRoute(route.Points.Select(V).ToList(), player.Position, target)
                            : vehicleCandidate ? CanReacquireRoute(route.Points.Select(V).ToList(), player.Position, target, endpointTolerance)
                            : Vector3.Distance(player.Position, first) <= endpointTolerance) &&
                        Vector3.Distance(target, last) <= endpointTolerance && forward < bestCost)
                    { best = route; reverse = false; bestCost = forward; }
                    if (RouteDirection(route) != "Forward" && (isFGridCandidate
                            ? CanReacquireFGridRoute(route.Points.Select(V).Reverse().ToList(), player.Position, target)
                            : vehicleCandidate ? CanReacquireRoute(route.Points.Select(V).Reverse().ToList(), player.Position, target, endpointTolerance)
                            : Vector3.Distance(player.Position, last) <= endpointTolerance) &&
                        Vector3.Distance(target, first) <= endpointTolerance && backward < bestCost)
                    { best = route; reverse = true; bestCost = backward; }
                }
                if (best == null) return false;
                List<Vector3> candidate = best.Points.Select(V).ToList();
                if (reverse) candidate.Reverse();
                bool fixerGrid = Playfield.ModelIdentity.Instance == (int)PlayfieldId.FixerGrid;
                bool vehicleRoute = RouteMode(best) == "Vehicle";
                if (fixerGrid || vehicleRoute)
                {
                    candidate = ReacquireForwardFGridPath(candidate, player.Position);
                    // A manually recorded walkway is useful only when both
                    // connectors and every saved leg remain on its floor.
                    if (fixerGrid && !SupportedRecordedFGridRoute(candidate, player.Position, target))
                    {
                        _say($"Recorded FGrid route '{best.Name}' rejected: endpoint or walkway connector is unsupported.");
                        return false;
                    }
                }
                int recordedPointCount = candidate.Count;
                if (fixerGrid)
                {
                    candidate = SimplifySupportedFGridPath(candidate, player.Position.Y);
                }
                else if (vehicleRoute || IsTerminalToGridRoute(best.Name))
                    candidate = SimplifyRecordedPath(candidate);
                if (fixerGrid && !SupportedRecordedFGridRoute(candidate, player.Position, target))
                {
                    _say($"Recorded FGrid route '{best.Name}' rejected after smoothing: a retained leg lost floor support.");
                    return false;
                }
                _playing = best;
                _playPoints = candidate;
                _playIndex = 0;
                BeginPlaybackProgress(player.Position);
                _say(fixerGrid
                    ? $"Using recorded FGrid route '{best.Name}' toward ({target.X:0.0},{target.Y:0.0},{target.Z:0.0}); " +
                      $"{recordedPointCount} samples reduced to {_playPoints.Count - 1} walkway-supported leg(s)."
                    : vehicleRoute
                    ? $"Using Vehicle nav profile '{best.Name}' with safe forward reacquisition toward ({target.X:0.0},{target.Y:0.0},{target.Z:0.0}); " +
                      $"{recordedPointCount} recorded samples reduced to {_playPoints.Count - 1} smooth leg(s); grounded precision remains at the endpoint."
                    : IsTerminalToGridRoute(best.Name)
                    ? $"Using recorded nav route '{best.Name}' toward ({target.X:0.0},{target.Y:0.0},{target.Z:0.0}); " +
                      $"{recordedPointCount} recorded samples reduced to {_playPoints.Count - 1} corridor-verified leg(s)."
                    : $"Using recorded nav route '{best.Name}' toward ({target.X:0.0},{target.Y:0.0},{target.Z:0.0}); no straight-line shortcut.");
            }

            int previousIndex = _playIndex;
            while (_playIndex < _playPoints.Count - 1 && Vector3.Distance(player.Position, _playPoints[_playIndex]) <= 1.2f) _playIndex++;
            if (_playIndex != previousIndex) _issuedPlayIndex = -1;
            Vector3 waypoint = _playPoints[Math.Min(_playIndex, _playPoints.Count - 1)];
            if (Playfield.ModelIdentity.Instance == (int)PlayfieldId.FixerGrid &&
                !LocalRoutePlanner.SupportedFGridSegment(player.Position, waypoint, player.Position.Y))
            { StopPlayback(); return false; }
            if (Vector3.Distance(player.Position, target) <= 1.2f) { StopPlayback(); return true; }
            float waypointDistance = Vector3.Distance(player.Position, waypoint);
            if (Vector3.Distance(player.Position, _playLastPosition) >= 0.5f ||
                _playLastWaypointDistance - waypointDistance >= 0.5f)
            {
                _playLastPosition = player.Position;
                _playLastWaypointDistance = waypointDistance;
                _playLastProgress = DateTime.UtcNow;
                _playRecoveryAttempts = 0;
            }
            bool navigating = SMovementController.IsNavigating();
            if (DateTime.UtcNow >= _nextPlayDiagnostic)
            {
                _nextPlayDiagnostic = DateTime.UtcNow.AddSeconds(5);
                _say($"Recorded route progress: route='{_playing.Name}', mode={RouteMode(_playing)}, " +
                    $"waypoint={_playIndex + 1}/{_playPoints.Count}, destination=({waypoint.X:0.0},{waypoint.Y:0.0},{waypoint.Z:0.0}), " +
                    $"distance={waypointDistance:0.0}m, owner={movement.Owner}, navigating={navigating}, facing=controller-managed, " +
                    $"stuck-recoveries={_playRecoveryAttempts}.");
            }
            if (DateTime.UtcNow - _playLastProgress > TimeSpan.FromSeconds(8))
            {
                _playRecoveryAttempts++;
                movement.Halt(owner);
                _issuedPlayIndex = -1;
                _playLastPosition = player.Position;
                _playLastWaypointDistance = waypointDistance;
                _playLastProgress = DateTime.UtcNow;
                _say($"Recorded route no-progress recovery {_playRecoveryAttempts}/3: route='{_playing.Name}', " +
                    $"waypoint={_playIndex + 1}/{_playPoints.Count}, distance={waypointDistance:0.0}m; halted stale steering before resubmission.");
                if (_playRecoveryAttempts >= 3)
                {
                    LastPlaybackFailure = $"Recorded route '{_playing.Name}' made no meaningful displacement after three bounded waypoint resets; " +
                        $"mode={RouteMode(_playing)}, waypoint={_playIndex + 1}/{_playPoints.Count}, distance={waypointDistance:0.0}m, " +
                        $"owner={movement.Owner}, navigating={navigating}.";
                    _say(LastPlaybackFailure);
                    StopPlayback(false);
                    return false;
                }
            }
            if (_issuedPlayIndex != _playIndex || movement.Owner != owner || !SMovementController.IsNavigating())
            {
                if (!movement.SetDestination(owner, waypoint)) { StopPlayback(); return false; }
                _issuedPlayIndex = _playIndex;
            }
            return true;
        }

        // A user-walked dungeon recording is a topology skeleton, not necessarily
        // an endpoint-to-endpoint journey. Select one ordered interior section and
        // then follow it strictly; playback never re-ranks points by target distance.
        public bool TryPrepareDungeonTopology(DungeonLayout layout, int roomId,
            Vector3 from, Vector3 target, out string routeName)
        {
            routeName = null;
            if (layout == null || IsRecording || _dungeonPlayback) return _dungeonPlayback;
            DungeonSubpath best = null;
            int eligibleRecordings = 0;
            string reusableRejection = null;
            DungeonRoomCapture.Snapshot live = null;
            bool captureAttempted = false;
            foreach (Route route in _routes.Where(x => (!x.Bundled || x.DungeonTopologyOnly) && x.Points.Count >= 2))
            {
                List<Vector3> points;
                bool reusable = route.DungeonTopologyOnly && route.ReusableTemplateValidated;
                if (reusable)
                {
                    if (!captureAttempted)
                    {
                        captureAttempted = true;
                        try { live = DungeonRoomCapture.ReadCurrent(); }
                        catch (Exception ex)
                        {
                            _say("Reusable dungeon topology rejected: live room capture unavailable: " + ex.Message);
                        }
                    }
                    if (!DungeonRoomCapture.Compatible(live, route.SourceRoomName,
                        route.SourceRoomFingerprint, route.SourceTemplatePosition,
                        route.SourceTemplateRotationDegrees, route.SourceLocalRect,
                        route.SourceSurfaceCount, route.SourceDoorCount, out string mismatch))
                    {
                        _say($"Reusable dungeon topology '{route.Name}' rejected: {mismatch}.");
                        continue;
                    }
                    points = TransformReusableDungeonRoute(route, live);
                    if (!PreflightReusableDungeonRoute(route, points, layout, roomId,
                        from, out string preflight, out string surfaceAdvisory))
                    {
                        _say($"Reusable dungeon topology '{route.Name}' rejected by preflight: {preflight}.");
                        continue;
                    }
                    _say($"Reusable dungeon topology template match: route='{route.Name}', fingerprint={route.SourceRoomFingerprint}, " +
                        $"source PF={route.Playfield}, live PF={Playfield.ModelIdentity.Instance}, room={roomId}, " +
                        $"rotation={route.SourceRoomRotationDegrees:0}->{live.RotationDegrees:0}, translation=({live.Position[0] - route.SourceRoomPosition[0]:0.0}," +
                        $"{live.Position[1] - route.SourceRoomPosition[1]:0.0},{live.Position[2] - route.SourceRoomPosition[2]:0.0}); " +
                        $"all {points.Count} points transformed, trusted playback starts at point {route.FirstReusablePoint + 1}; {surfaceAdvisory}.");
                    points = points.Skip(route.FirstReusablePoint).ToList();
                }
                else points = route.Points.Select(V).ToList();
                float minimumY = points.Min(x => x.Y), maximumY = points.Max(x => x.Y);
                // Combat topology recovery starts at a 1.5m separation. Keep
                // the recording gate above incidental floor noise while still
                // admitting real switchbacks such as a roughly 2.7m ramp. A
                // user-walked route is the physical authority here: generated
                // room polygons can clip a valid switchback near a wall/edge.
                if (maximumY - minimumY < DungeonMinimumVerticalSpan ||
                    !IsContinuousDungeonRecording(points)) continue;
                eligibleRecordings++;
                foreach (bool reverse in new[] { false, true })
                {
                    if (reverse && route.DungeonTopologyOnly && RouteDirection(route) == "Forward") continue;
                    List<Vector3> oriented = reverse
                        ? points.AsEnumerable().Reverse().ToList() : points;
                    DungeonSubpath candidate = FindDungeonSubpath(route, oriented,
                        reverse, from, target, reusable ? 30f : DungeonSourceAttachDistance,
                        DungeonTargetAttachDistance, reusable);
                    if (candidate == null)
                    {
                        if (reusable) reusableRejection =
                            $"no ordered same-level endpoint reached within {DungeonTargetAttachDistance:0.0}m of the target; reusable source attachment may use a generated connector up to 30m";
                        continue;
                    }
                    if (reusable && !SafeReusableTargetHandoff(layout, roomId,
                        candidate.Points[candidate.Points.Count - 1], target,
                        out reusableRejection)) continue;
                    // Operational derivatives win only after conservative physical
                    // attachment gates; no room translation is inferred.
                    if (route.DungeonTopologyOnly && !reusable &&
                        (route.Playfield != Playfield.ModelIdentity.Instance ||
                         !layout.IsInside(roomId, candidate.Points[0]) ||
                         !layout.IsInside(roomId, candidate.Points[candidate.Points.Count - 1]))) continue;
                    if (best != null &&
                        (best.Route.DungeonTopologyOnly && !route.DungeonTopologyOnly ||
                         best.Route.DungeonTopologyOnly == route.DungeonTopologyOnly && candidate.Cost >= best.Cost)) continue;
                    best = candidate;
                }
            }
            if (best == null)
            {
                if (eligibleRecordings > 0)
                    _say(reusableRejection != null
                        ? $"No viable reusable dungeon topology subpath: {reusableRejection}."
                        : $"No viable recorded dungeon topology subpath: checked {eligibleRecordings} continuous multi-level recording(s); " +
                          $"source attach must be <= {DungeonSourceAttachDistance:0.0}m and target attach <= {DungeonTargetAttachDistance:0.0}m with ordered vertical progress.");
                return false;
            }
            List<Vector3> startConnector = null;
            if (best.Route.ReusableTemplateValidated &&
                best.SourceDistance > DungeonSourceAttachDistance &&
                !TryBuildReusableStartConnector(layout, roomId, from, best.Points[0],
                    out startConnector, out string connectorFailure))
            {
                _say($"Reusable dungeon topology '{best.Route.Name}' rejected: {connectorFailure}.");
                return false;
            }
            StopPlayback();
            _playing = best.Route;
            _dungeonPhysicalPoints = new List<Vector3>(best.Points);
            _playPoints = SimplifyDungeonTopology(best.Points);
            _say($"Recorded dungeon topology '{best.Route.Name}' reduced {best.Points.Count - 1} sampled legs to {_playPoints.Count - 1} physical corridor leg(s); turns and slope changes retained.");
            _playIndex = 0;
            _dungeonPlayback = true;
            _dungeonStartConnector = startConnector;
            _dungeonStartConnectorIndex = 0;
            _dungeonRequestedTarget = target;
            _lastDungeonReportedIndex = 0;
            routeName = best.Route.Name;
            string direction = best.Reversed ? "reverse" : "forward";
            _say($"Prepared recorded dungeon topology '{best.Route.Name}' subpath {best.SourceIndex}->{best.TargetIndex} " +
                $"({_playPoints.Count} points, {direction}); source attach={best.SourceDistance:0.0}m, " +
                $"target attach={best.TargetDistance:0.0}m, vertical span={best.VerticalSpan:0.0}m, " +
                $"start connector={(startConnector == null ? "not required" : startConnector.Count + " generated same-level waypoint(s)")}.");
            return true;
        }

        private static bool TryBuildReusableStartConnector(DungeonLayout layout, int roomId,
            Vector3 from, Vector3 trustedStart, out List<Vector3> points, out string reason)
        {
            points = null;
            reason = null;
            Vector3 groundTarget = trustedStart;
            groundTarget.Y = from.Y;
            DungeonTransitPlanner.Route connector = DungeonTransitPlanner.Candidates(layout,
                    roomId, from, groundTarget, new[] { 1.5f, 2f, 2.5f, 3f }, 0.3f)
                .Where(route => route.Waypoints != null && route.Waypoints.Count > 0 &&
                    Math.Abs(route.Destination.Y - from.Y) <= 0.75f &&
                    Vector3.Distance(route.Destination, trustedStart) <= DungeonSourceAttachDistance)
                .OrderBy(route => route.Cost).FirstOrDefault();
            if (connector == null)
            {
                reason = $"no complete same-level generated connector reaches within {DungeonSourceAttachDistance:0.0}m of trusted point 3 from {Vector3.Distance(from, trustedStart):0.0}m away";
                return false;
            }
            points = new List<Vector3>(connector.Waypoints);
            if (Vector3.Distance(points[points.Count - 1], connector.Destination) > 0.5f)
                points.Add(connector.Destination);
            return points.Count > 0;
        }

        private static List<Vector3> TransformReusableDungeonRoute(Route route,
            DungeonRoomCapture.Snapshot live)
        {
            return route.Points.Select(point =>
            {
                float[] local = DungeonRoomCapture.CandidateLocal(point,
                    route.SourceRoomPosition, route.SourceRoomRotationDegrees);
                return DungeonRoomCapture.WorldFromCandidateLocal(local, live.Position,
                    live.RotationDegrees);
            }).ToList();
        }

        private static bool PreflightReusableDungeonRoute(Route route, List<Vector3> points,
            DungeonLayout layout, int roomId, Vector3 from, out string reason,
            out string surfaceAdvisory)
        {
            reason = null;
            int missingSurfaceRays = 0, wrongHeightSurfaceRays = 0,
                failedSurfaceRays = 0, outlinePointMisses = 0,
                outlineMidpointMisses = 0;
            surfaceAdvisory = "surface-ray advisory unavailable";
            int first = route.FirstReusablePoint;
            if (first < 0 || first >= points.Count - 1)
                reason = "validated reusable start index is invalid";
            else if (first > 0 && first != 2)
                reason = "unsupported prefix was not explicitly reviewed";
            else if (points.Max(x => x.Y) - points.Min(x => x.Y) < 7.5f)
                reason = "transformed route lost its recorded multi-level span";
            else if (!layout.IsInside(roomId, points[first], 0.1f) ||
                !layout.IsInside(roomId, points[points.Count - 1], 0.1f))
                reason = "trusted start or end lies outside the live room";
            else if (Math.Abs(from.Y - points[first].Y) > 0.9f ||
                Vector3.Distance(from, points[first]) > 30f)
                reason = $"player cannot safely approach trusted start (distance={Vector3.Distance(from, points[first]):0.0}m, vertical={Math.Abs(from.Y - points[first].Y):0.0}m)";
            else
            {
                for (int i = first + 1; i < points.Count; i++)
                {
                    if (Vector3.Distance(points[i - 1], points[i]) > 3f)
                    { reason = $"recorded leg {i}->{i + 1} is discontinuous"; break; }
                    if (!layout.IsInside(roomId, points[i], 0.05f))
                        outlinePointMisses++;
                    Vector3 midpoint = (points[i - 1] + points[i]) * 0.5f;
                    if (!layout.IsInside(roomId, midpoint, 0.02f))
                        outlineMidpointMisses++;
                    try
                    {
                        Vector3 top = midpoint + Vector3.Up * 1.2f;
                        Vector3 bottom = midpoint - Vector3.Up * 1.2f;
                        if (!Playfield.Raycast(top, bottom, out Vector3 floor, out _))
                            missingSurfaceRays++;
                        else if (Math.Abs(floor.Y - midpoint.Y) > 0.45f)
                            wrongHeightSurfaceRays++;
                    }
                    catch { failedSurfaceRays++; }
                }
            }
            // AO raycasts report the inclined ramp itself as an obstruction and
            // can see the lower floor through it. They are evidence only here:
            // the exact validated mesh fingerprint plus the user-walked samples
            // are the static corridor authority. Runtime stalls remain bounded.
            // Mali's 2D outline clips this template's valid ramp switchbacks.
            // Start/end containment remains mandatory; intermediate misses are
            // diagnostic because the exact captured mesh and walked samples are
            // more precise evidence for the interior multi-level corridor.
            surfaceAdvisory = $"captured fingerprint supplies ramp support; Mali outline point-misses={outlinePointMisses}, midpoint-misses={outlineMidpointMisses}; live downward rays no-hit={missingSurfaceRays}, wrong-height={wrongHeightSurfaceRays}, failed={failedSurfaceRays}";
            return reason == null;
        }

        private static DungeonSubpath FindDungeonSubpath(Route route, List<Vector3> points,
            bool reversed, Vector3 from, Vector3 target,
            float sourceAttachDistance = DungeonSourceAttachDistance,
            float targetAttachDistance = DungeonTargetAttachDistance,
            bool requireTargetLevel = false)
        {
            float initialVerticalGap = Math.Abs(target.Y - from.Y);
            if (initialVerticalGap < DungeonMinimumVerticalSpan) return null;
            float direction = Math.Sign(target.Y - from.Y);
            DungeonSubpath best = null;
            for (int source = 0; source < points.Count - 1; source++)
            {
                float sourceDistance = Vector3.Distance(from, points[source]);
                if (sourceDistance > sourceAttachDistance ||
                    route.DungeonTopologyOnly && Math.Abs(from.Y - points[source].Y) > 0.75f) continue;
                float pathLength = 0f;
                float verticalRegression = 0f;
                float minimumY = points[source].Y, maximumY = points[source].Y;
                for (int end = source + 1; end < points.Count; end++)
                {
                    Vector3 leg = points[end] - points[end - 1];
                    pathLength += leg.Magnitude;
                    verticalRegression += Math.Max(0f, -direction * (points[end].Y - points[end - 1].Y));
                    minimumY = Math.Min(minimumY, points[end].Y);
                    maximumY = Math.Max(maximumY, points[end].Y);
                    float targetDistance = Vector3.Distance(target, points[end]);
                    if (targetDistance > targetAttachDistance ||
                        requireTargetLevel && Math.Abs(target.Y - points[end].Y) > 0.9f ||
                        route.DungeonTopologyOnly && Math.Abs(target.Y - points[end].Y) > 0.75f) continue;
                    float verticalSpan = maximumY - minimumY;
                    float finalVerticalGap = Math.Abs(target.Y - points[end].Y);
                    float verticalProgress = initialVerticalGap - finalVerticalGap;
                    float directedProgress = direction * (points[end].Y - points[source].Y);
                    if (verticalSpan < DungeonMinimumVerticalSpan ||
                        verticalProgress < Math.Min(1f, initialVerticalGap * 0.5f) ||
                        directedProgress < 0.75f || pathLength < 1.5f) continue;

                    float directLength = Vector3.Distance(points[source], points[end]);
                    float unnecessaryDistance = Math.Max(0f, pathLength - directLength);
                    float cost = sourceDistance * 3f + targetDistance * 3f +
                        pathLength + unnecessaryDistance * 0.5f + verticalRegression * 8f +
                        finalVerticalGap * 2f + (reversed ? 0.05f : 0f) +
                        (route.Playfield == Playfield.ModelIdentity.Instance ? 0f : 1f);
                    if (best != null && cost >= best.Cost) continue;
                    int originalSource = reversed ? points.Count - 1 - source : source;
                    int originalTarget = reversed ? points.Count - 1 - end : end;
                    best = new DungeonSubpath
                    {
                        Route = route,
                        Points = points.GetRange(source, end - source + 1),
                        SourceIndex = originalSource,
                        TargetIndex = originalTarget,
                        Reversed = reversed,
                        SourceDistance = sourceDistance,
                        TargetDistance = targetDistance,
                        VerticalSpan = verticalSpan,
                        Cost = cost
                    };
                }
            }
            return best;
        }

        private static bool SafeReusableTargetHandoff(DungeonLayout layout, int roomId,
            Vector3 routeEnd, Vector3 target, out string reason)
        {
            reason = null;
            if (!layout.IsInside(roomId, target)) reason = "target lies outside the live room";
            else if (Math.Abs(routeEnd.Y - target.Y) > 0.9f)
                reason = $"route landing is not on the target level (vertical gap={Math.Abs(routeEnd.Y - target.Y):0.0}m)";
            else if (Vector3.Distance(routeEnd, target) > DungeonTargetAttachDistance)
                reason = $"same-level route landing is {Vector3.Distance(routeEnd, target):0.0}m from target (maximum {DungeonTargetAttachDistance:0.0}m)";
            return reason == null;
        }

        private static bool IsContinuousDungeonRecording(List<Vector3> points) =>
            points != null && points.Count >= 2 &&
            Enumerable.Range(1, points.Count - 1).All(i =>
                Vector3.Distance(points[i - 1], points[i]) <= 5f);

        public bool TryNavigatePreparedDungeonTopology(Vector3 target,
            MovementArbiter movement, MovementOwner owner, out bool completed)
            => TryNavigatePreparedDungeonTopology(target, movement, owner, out completed, out _);

        public bool TryNavigatePreparedDungeonTopology(Vector3 target,
            MovementArbiter movement, MovementOwner owner, out bool completed,
            out DungeonTopologyContinuation completedPath)
        {
            completedPath = null;
            completed = false;
            SimpleChar player = DynelManager.LocalPlayer;
            if (!_dungeonPlayback || _playing == null || _playPoints == null ||
                player == null || Game.IsZoning ||
                Vector3.Distance(target, _dungeonRequestedTarget) > 0.5f) return false;
            if (_dungeonStartConnector != null &&
                _dungeonStartConnectorIndex < _dungeonStartConnector.Count)
            {
                while (_dungeonStartConnectorIndex < _dungeonStartConnector.Count &&
                    Vector3.Distance(player.Position,
                        _dungeonStartConnector[_dungeonStartConnectorIndex]) <= 1.5f)
                    _dungeonStartConnectorIndex++;
                if (_dungeonStartConnectorIndex < _dungeonStartConnector.Count)
                {
                    Vector3 connector = _dungeonStartConnector[_dungeonStartConnectorIndex];
                    if (movement.Owner != owner || !SMovementController.IsNavigating())
                        if (!movement.SetDestination(owner, connector))
                        { StopPlayback(); return false; }
                    return true;
                }
                _say($"Recorded dungeon topology '{_playing.Name}' reached generated same-level start connector; beginning trusted playback at point 3.");
                _dungeonStartConnector = null;
            }
            int previousIndex = _playIndex;
            while (_playIndex < _playPoints.Count - 1 &&
                Vector3.Distance(player.Position, _playPoints[_playIndex]) <= 1.2f)
                _playIndex++;
            Vector3 waypoint = _playPoints[_playIndex];
            if (_playIndex > previousIndex && (_playIndex == _playPoints.Count - 1 ||
                _playIndex - _lastDungeonReportedIndex >= 5 ||
                Math.Abs(waypoint.Y - _playPoints[_lastDungeonReportedIndex].Y) >= 0.75f))
            {
                _lastDungeonReportedIndex = _playIndex;
                _say($"Recorded dungeon topology '{_playing.Name}' forward progress " +
                    $"{_playIndex + 1}/{_playPoints.Count}: waypoint={waypoint}.");
            }
            if (_playIndex == _playPoints.Count - 1 &&
                Vector3.Distance(player.Position, waypoint) <= 1.5f)
            {
                completedPath = new DungeonTopologyContinuation
                {
                    RouteName = _playing.Name,
                    Playfield = Playfield.ModelIdentity.Instance,
                    Points = new List<Vector3>(_playPoints),
                    PhysicalPoints = new List<Vector3>(_dungeonPhysicalPoints ?? _playPoints),
                    ProgressIndex = _playIndex,
                    TargetSide = _dungeonRequestedTarget
                };
                completed = true;
                StopPlayback();
                return true;
            }
            // Raw recorded legs are the fallback precisely when the generated
            // navmesh cannot express the connected switchback corridor.
            if (movement.Owner != owner || !SMovementController.IsNavigating())
                if (!movement.SetDestination(owner, waypoint))
                { StopPlayback(); return false; }
            return true;
        }

        // Preserve only the selected, oriented dungeon subpath. Reacquisition can
        // move forward within this slice, but can never enter a later descending
        // branch from the original recording.
        public bool PauseDungeonTopology(Vector3 targetSide,
            out DungeonTopologyContinuation continuation)
        {
            continuation = null;
            if (!_dungeonPlayback || _playing == null || _playPoints == null ||
                _playPoints.Count < 2) return false;
            continuation = new DungeonTopologyContinuation
            {
                RouteName = _playing.Name,
                Playfield = Playfield.ModelIdentity.Instance,
                Points = new List<Vector3>(_playPoints),
                PhysicalPoints = new List<Vector3>(_dungeonPhysicalPoints ?? _playPoints),
                ProgressIndex = Math.Max(0, Math.Min(_playIndex, _playPoints.Count - 1)),
                TargetSide = targetSide
            };
            StopPlayback();
            return true;
        }

        public bool TryPrepareDungeonTopologyContinuation(
            DungeonTopologyContinuation continuation, Vector3 from, Vector3 target,
            out string routeName)
        {
            routeName = null;
            if (continuation == null || continuation.Playfield != Playfield.ModelIdentity.Instance ||
                continuation.Points == null || continuation.Points.Count < 2 ||
                Vector3.Distance(target, continuation.TargetSide) > 6f ||
                Math.Abs(target.Y - continuation.TargetSide.Y) > DungeonMinimumVerticalSpan)
                return false;

            int first = Math.Max(0, Math.Min(continuation.ProgressIndex,
                continuation.Points.Count - 1));
            int best = first;
            float bestDistance = Vector3.Distance(from, continuation.Points[first]);
            for (int i = first + 1; i < continuation.Points.Count; i++)
            {
                float distance = Vector3.Distance(from, continuation.Points[i]);
                if (distance < bestDistance) { best = i; bestDistance = distance; }
            }
            if (bestDistance > DungeonSourceAttachDistance ||
                Vector3.Distance(target, continuation.Points[continuation.Points.Count - 1]) >
                    DungeonTargetAttachDistance)
                return false;

            int start = Math.Max(first, best - 1);
            List<Vector3> remaining = continuation.Points.GetRange(start,
                continuation.Points.Count - start);
            if (remaining.Count < 2) return false;
            StopPlayback();
            _playing = new Route { Name = continuation.RouteName,
                Playfield = continuation.Playfield };
            _playPoints = remaining;
            _dungeonPhysicalPoints = new List<Vector3>(continuation.PhysicalPoints ?? continuation.Points);
            _playIndex = 0;
            _dungeonPlayback = true;
            _dungeonRequestedTarget = target;
            _lastDungeonReportedIndex = 0;
            routeName = continuation.RouteName;
            _say($"Prepared recorded loot topology '{routeName}' from forward combat progress " +
                $"{start + 1}/{continuation.Points.Count} ({remaining.Count} point(s)); " +
                $"source attach={bestDistance:0.0}m, target attach=" +
                $"{Vector3.Distance(target, remaining[remaining.Count - 1]):0.0}m.");
            return true;
        }

        // Find the minimum number of legs inside the sampled ramp corridor.
        // Every output vertex is an original physical point. Mandatory corners
        // and slope joints cannot be bypassed, even when spatially close.
        internal static List<Vector3> SimplifyDungeonTopology(List<Vector3> points)
        {
            if (points == null || points.Count < 3)
                return points == null ? new List<Vector3>() : new List<Vector3>(points);
            bool[] keep = new bool[points.Count];
            keep[0] = keep[points.Count - 1] = true;
            for (int i = 1; i < points.Count - 1; i++)
            {
                Vector3 incoming = points[i] - points[i - 1], outgoing = points[i + 1] - points[i];
                float a = (float)Math.Sqrt(incoming.X * incoming.X + incoming.Z * incoming.Z);
                float b = (float)Math.Sqrt(outgoing.X * outgoing.X + outgoing.Z * outgoing.Z);
                if (a < 0.05f || b < 0.05f) { keep[i] = true; continue; }
                float cosine = (incoming.X * outgoing.X + incoming.Z * outgoing.Z) / (a * b);
                bool turn = a >= 0.35f && b >= 0.35f && cosine < 0.94f;
                bool slopeJoint = (Math.Abs(incoming.Y) >= 0.15f || Math.Abs(outgoing.Y) >= 0.15f) &&
                    Math.Abs(incoming.Y / a - outgoing.Y / b) >= 0.2f;
                keep[i] = turn || slopeJoint;
            }
            int[] legs = Enumerable.Repeat(int.MaxValue, points.Count).ToArray();
            int[] previous = Enumerable.Repeat(-1, points.Count).ToArray();
            legs[0] = 0;
            for (int end = 1; end < points.Count; end++)
                for (int start = 0; start < end; start++)
                {
                    if (legs[start] == int.MaxValue || legs[start] + 1 >= legs[end]) continue;
                    if (!DungeonRecordedLeg(points, keep, start, end)) continue;
                    legs[end] = legs[start] + 1;
                    previous[end] = start;
                }
            var result = new List<Vector3>();
            for (int i = points.Count - 1; i >= 0; i = previous[i])
            {
                result.Add(points[i]);
                if (i == 0) break;
            }
            result.Reverse();
            return result;
        }

        private static bool DungeonRecordedLeg(List<Vector3> points, bool[] keep, int start, int end)
        {
            if (end == start + 1) return true;
            Vector3 leg = points[end] - points[start];
            float squared = leg.X * leg.X + leg.Z * leg.Z;
            if (squared < 0.01f) return false;
            float previousAmount = -0.001f;
            for (int i = start + 1; i < end; i++)
            {
                if (keep[i]) return false;
                Vector3 offset = points[i] - points[start];
                float amount = (offset.X * leg.X + offset.Z * leg.Z) / squared;
                if (amount < previousAmount || amount < 0f || amount > 1f) return false;
                float dx = offset.X - leg.X * amount, dz = offset.Z - leg.Z * amount;
                if (dx * dx + dz * dz > 0.2f * 0.2f ||
                    Math.Abs(offset.Y - leg.Y * amount) > 0.15f) return false;
                previousAmount = amount;
            }
            return true;
        }

        // Reverse only the actual selected ascent slice, never the full recording
        // or a newly matched route. Connectors must remain on the proven levels.
        internal static List<Vector3> RecordedReturnPoints(DungeonTopologyContinuation ascent,
            Vector3 from, Vector3 doorApproach, int playfield)
        {
            if (ascent == null || ascent.Playfield != playfield || ascent.Points == null ||
                ascent.Points.Count < 2) return null;
            Vector3 ground = ascent.Points[0], upper = ascent.Points[ascent.Points.Count - 1];
            if (upper.Y - ground.Y < DungeonMinimumVerticalSpan ||
                Vector3.Distance(from, upper) > DungeonSourceAttachDistance ||
                Math.Abs(from.Y - upper.Y) > 0.75f ||
                Math.Abs(doorApproach.Y - ground.Y) > 1.25f ||
                !IsContinuousDungeonRecording(ascent.PhysicalPoints ?? ascent.Points) ||
                !IsRecordedSubsequence(ascent.PhysicalPoints ?? ascent.Points, ascent.Points)) return null;
            return ascent.Points.AsEnumerable().Reverse().ToList();
        }

        private static bool IsRecordedSubsequence(List<Vector3> physical, List<Vector3> retained)
        {
            int next = 0;
            foreach (Vector3 point in physical)
                if (next < retained.Count && Vector3.Distance(point, retained[next]) <= 0.001f) next++;
            return next == retained.Count;
        }

        public bool TryPrepareRecordedReturn(DungeonTopologyContinuation ascent,
            Vector3 from, Vector3 doorApproach, out Vector3 ground)
        {
            ground = default(Vector3);
            List<Vector3> points = RecordedReturnPoints(ascent, from, doorApproach,
                Playfield.ModelIdentity.Instance);
            if (points == null) return false;
            StopPlayback();
            _playing = new Route { Name = ascent.RouteName + " (return descent)",
                Playfield = ascent.Playfield };
            _playPoints = points;
            _dungeonPhysicalPoints = (ascent.PhysicalPoints ?? ascent.Points).AsEnumerable().Reverse().ToList();
            _playIndex = 0;
            _dungeonPlayback = true;
            _lastDungeonReportedIndex = 0;
            ground = points[points.Count - 1];
            _dungeonRequestedTarget = ground;
            _say($"Prepared recorded return descent '{ascent.RouteName}': {points.Count} reversed physical point(s), ground attachment={ground}; doorway crossing held.");
            return true;
        }

        public void StopPlayback() => StopPlayback(true);

        private void StopPlayback(bool clearFailure)
        { _playing = null; _playPoints = null; _dungeonPhysicalPoints = null; _playIndex = 0; _dungeonPlayback = false;
            _dungeonStartConnector = null; _dungeonStartConnectorIndex = 0;
            _dungeonRequestedTarget = Vector3.Zero;
            _cityReturnPlayback = false;
            _lastDungeonReportedIndex = 0; _issuedPlayIndex = -1; _playRecoveryAttempts = 0;
            _playLastWaypointDistance = float.MaxValue; _playLastProgress = _nextPlayDiagnostic = DateTime.MinValue;
            if (clearFailure) LastPlaybackFailure = null; }

        private void BeginPlaybackProgress(Vector3 position)
        {
            LastPlaybackFailure = null;
            _issuedPlayIndex = -1;
            _playRecoveryAttempts = 0;
            _playLastPosition = position;
            _playLastWaypointDistance = float.MaxValue;
            _playLastProgress = DateTime.UtcNow;
            _nextPlayDiagnostic = DateTime.UtcNow;
        }

        private static bool SupportedRecordedFGridRoute(List<Vector3> points, Vector3 start, Vector3 target) =>
            points.Count >= 2 && Vector3.Distance(points[points.Count - 1], target) <= 1.5f &&
            LocalRoutePlanner.SupportedFGridSegment(start, points[0], start.Y) &&
            Enumerable.Range(1, points.Count - 1).All(i =>
                LocalRoutePlanner.SupportedFGridSegment(points[i - 1], points[i], start.Y));

        private static bool CanReacquireFGridRoute(List<Vector3> points, Vector3 start,
            Vector3 target)
        {
            List<Vector3> resumed = ReacquireForwardFGridPath(points, start);
            return SupportedRecordedFGridRoute(resumed, start, target);
        }

        private static bool CanReacquireRoute(List<Vector3> points, Vector3 start,
            Vector3 target, float endpointTolerance)
        {
            List<Vector3> resumed = ReacquireForwardFGridPath(points, start);
            return resumed != null && resumed.Count >= 2 &&
                LocalRoutePlanner.HorizontalDistance(start, resumed[0]) <= Math.Max(12f, endpointTolerance * 2f) &&
                Vector3.Distance(target, resumed[resumed.Count - 1]) <= endpointTolerance;
        }

        private static bool IsCityReturnRoute(Route route)
        {
            if (route == null || string.IsNullOrWhiteSpace(route.Name)) return false;
            switch (route.Name.Trim())
            {
                case "OA Sirocco to Terminal - Flying":
                    return route.Playfield == 540;
                case "OA Clan Agency to Terminal - Flying":
                    return route.Playfield == 540;
                case "ICC Lift to Terminal":
                    return route.Playfield == 655;
                case "ICC Statue to Terminal":
                    return route.Playfield == 655;
                case "Bor Baffbots to Terminal":
                    return route.Playfield == 800;
                default:
                    return false;
            }
        }

        // The horizontal bound matches existing vehicle-route reacquisition.
        // A separate height bound prevents ICC's lift and statue recordings from
        // being selected from the wrong level while retaining their vertical path.
        private static bool TryAttachCityCorridor(List<Vector3> points, Vector3 position,
            out List<Vector3> resumed, out float horizontalDistance,
            out float verticalDistance, out int segment)
        {
            resumed = null;
            horizontalDistance = verticalDistance = float.MaxValue;
            segment = 0;
            if (points == null || points.Count < 2) return false;
            Vector3 bestProjection = points[0];
            float bestScore = float.MaxValue;
            for (int i = 1; i < points.Count; i++)
            {
                Vector3 leg = points[i] - points[i - 1];
                Vector3 horizontal = leg; horizontal.Y = 0;
                float squared = Vector3.Dot(horizontal, horizontal);
                if (squared < 0.01f) continue;
                float amount = Math.Max(0f, Math.Min(1f,
                    ((position.X - points[i - 1].X) * horizontal.X +
                     (position.Z - points[i - 1].Z) * horizontal.Z) / squared));
                Vector3 projection = points[i - 1] + leg * amount;
                float crossTrack = LocalRoutePlanner.HorizontalDistance(position, projection);
                float vertical = Math.Abs(position.Y - projection.Y);
                float score = crossTrack + vertical;
                if (score >= bestScore) continue;
                bestScore = score;
                horizontalDistance = crossTrack;
                verticalDistance = vertical;
                bestProjection = projection;
                segment = i;
            }
            if (segment == 0 || horizontalDistance > 12f || verticalDistance > 6f)
                return false;
            resumed = new List<Vector3> { bestProjection };
            resumed.AddRange(points.Skip(segment));
            return resumed.Count >= 2;
        }

        // Resume at the closest point on the oriented recording, then retain only
        // later samples. This prevents local recovery from pulling back across the
        // ring-to-radial alignment turn or toward the route's original start.
        private static List<Vector3> ReacquireForwardFGridPath(List<Vector3> points,
            Vector3 position)
        {
            if (points == null || points.Count < 2) return points;
            float bestDistance = float.MaxValue;
            int bestSegment = 1;
            Vector3 bestProjection = points[0];
            for (int i = 1; i < points.Count; i++)
            {
                Vector3 leg = points[i] - points[i - 1]; leg.Y = 0;
                float squared = Vector3.Dot(leg, leg);
                if (squared < 0.01f) continue;
                float amount = Math.Max(0f, Math.Min(1f,
                    Vector3.Dot(position - points[i - 1], leg) / squared));
                Vector3 projection = points[i - 1] + leg * amount;
                projection.Y = position.Y;
                float distance = LocalRoutePlanner.HorizontalDistance(position, projection);
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                bestSegment = i;
                bestProjection = projection;
            }
            var resumed = new List<Vector3> { bestProjection };
            resumed.AddRange(points.Skip(bestSegment));
            return resumed;
        }

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
                        !PreservesFGridAlignmentTurns(points, anchor, candidate) ||
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

        // Keep the deliberate ring-to-radial corner. A shortcut across a sharp
        // recorded turn is exactly the diagonal corner cutting that strands the
        // character near FGrid lift and exit platform edges.
        private static bool PreservesFGridAlignmentTurns(List<Vector3> points,
            int anchor, int candidate)
        {
            for (int i = anchor + 1; i < candidate; i++)
            {
                Vector3 before = points[i] - points[i - 1]; before.Y = 0;
                Vector3 after = points[i + 1] - points[i]; after.Y = 0;
                if (before.Magnitude < 0.2f || after.Magnitude < 0.2f) continue;
                float cosine = Vector3.Dot(before.Normalize(), after.Normalize());
                // 60 degrees or sharper is an intentional alignment point.
                if (cosine <= 0.5f) return false;
            }
            return true;
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
                bool normalized = NormalizeUserRoutes(file.Routes);
                foreach (Route route in file.Routes)
                    _routes.RemoveAll(x => x.Playfield == route.Playfield &&
                        string.Equals(x.Name, route.Name, StringComparison.OrdinalIgnoreCase));
                _routes.AddRange(file.Routes);
                if (normalized)
                {
                    Save();
                    _say("Normalized recorded navigation routes: duplicate canonical names were resolved and 'Fgrid Floor 10a' was removed.");
                }
            }
            catch (Exception ex) { _writable = false; _say("Navigation route file could not be read; existing file preserved: " + ex.Message); }
        }

        // A canonical route name identifies one user route per playfield. Keep the
        // newest recording deterministically; if timestamps tie, keep the later
        // file entry. Floor 10a was an accidental duplicate and is retired here so
        // existing installations are cleaned without touching the valid floor 10.
        private static bool NormalizeUserRoutes(List<Route> routes)
        {
            if (routes == null) return false;
            int originalCount = routes.Count;
            List<Route> normalized = routes
                .Select((route, index) => new { route, index })
                .Where(x => !string.Equals(x.route.Name?.Trim(), "Fgrid Floor 10a",
                    StringComparison.OrdinalIgnoreCase))
                .GroupBy(x => x.route.Playfield.ToString() + "\0" +
                    (x.route.Name ?? "").Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderByDescending(x => x.route.RecordedAtUtc)
                    .ThenByDescending(x => x.index).First())
                .OrderBy(x => x.index)
                .Select(x => x.route).ToList();
            bool changed = normalized.Count != originalCount;
            routes.Clear();
            routes.AddRange(normalized);
            return changed;
        }

        private void LoadDungeonTopology(string path)
        {
            try
            {
                if (!File.Exists(path)) return;
                foreach (Route route in ReadRoutes(path).Routes)
                {
                    // User routes retain authority over identical names. Dedicated
                    // topology assets never enter generic endpoint playback.
                    if (!route.DungeonTopologyOnly || _routes.Any(x =>
                        string.Equals(x.Name, route.Name, StringComparison.OrdinalIgnoreCase))) continue;
                    route.Bundled = true;
                    _routes.Add(route);
                }
            }
            catch (Exception ex) { _say("Dungeon topology assets were not loaded: " + ex.Message); }
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

        private static string RouteMode(Route route) => string.IsNullOrWhiteSpace(route?.MovementMode) ? "Foot" : route.MovementMode;
        private static string RouteAltitude(Route route) => string.IsNullOrWhiteSpace(route?.AltitudeProfile) ? "Ground" : route.AltitudeProfile;
        private static string RouteDirection(Route route) => string.IsNullOrWhiteSpace(route?.Directionality) ? "Bidirectional" : route.Directionality;
        private static bool RouteModeMatches(Route route, bool flying)
        {
            string mode = RouteMode(route);
            return mode == "Auto" || (flying ? mode == "Vehicle" : mode == "Foot");
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
