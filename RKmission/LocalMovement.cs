using System;
using System.Collections.Generic;
using System.Linq;
using AOSharp.Common.GameData;
using AOSharp.Core;
using AOSharp.Pathfinding;

namespace RKmission
{
    internal enum MovementResult { Moving, Reached, Stalled }

    // One observed-movement executor for both coarse travel and entrance legs.
    // Rays score waypoints; only elapsed observed non-progress ends a leg.
    internal sealed class LocalMovement
    {
        private readonly OutdoorNavigationSettings _settings;
        private readonly Action<string> _say;
        private readonly List<Vector3> _failed = new List<Vector3>();
        private readonly List<Vector3> _visited = new List<Vector3>();
        private Vector3 _target;
        private int _stallLimit;
        private bool _active, _owns, _mesh, _flying, _precise, _steering;
        private DateTime _lastProgress, _nextSubmit, _lastSteer;
        public float BestDistance { get; private set; }
        public float StartDistance { get; private set; }
        public float Progress => Math.Max(0, StartDistance - BestDistance);
        public double StallSeconds => (DateTime.UtcNow - _lastProgress).TotalSeconds;
        public Vector3 Target => _target;
        public Vector3 StartPosition { get; private set; }

        public LocalMovement(OutdoorNavigationSettings settings, Action<string> say) { _settings = settings; _say = say; }

        public void Reset()
        { Halt(); _failed.Clear(); _visited.Clear(); }

        public void Halt()
        {
            if (_owns)
            { SMovementController.Halt(); SMovementController.SetMovement(MovementAction.FullStop); }
            _owns = _active = false;
        }

        public void Begin(Vector3 target, bool flying, bool precise, bool tryMesh = false, int? stallSeconds = null)
        {
            Halt(); _target = target; _flying = flying; _precise = precise; _mesh = _steering = false; _active = true;
            Vector3 player = DynelManager.LocalPlayer.Position;
            StartPosition = player;
            StartDistance = BestDistance = Distance(player, target, flying);
            _stallLimit = Math.Max(2, stallSeconds ?? _settings.LegStallSeconds);
            _lastProgress = _lastSteer = DateTime.UtcNow; _nextSubmit = DateTime.MinValue;
            if (!flying && tryMesh)
            {
                try { _mesh = LocalRoutePlanner.TryGroundCost(player, target, out _); }
                catch (Exception ex) { _say("Optional outdoor mesh unavailable; using direct movement: " + ex.Message); }
            }
            Vector3 lift = flying ? Vector3.Zero : Vector3.Up;
            _say($"Movement leg: mode={(flying ? "Fly" : "Run")}, target=({LocalRoutePlanner.Coordinates(target)}), " +
                $"distance={BestDistance:F2} m, executor={(_mesh ? "optional mesh" : "direct world movement")}, " +
                $"corridor={(LocalRoutePlanner.ClearSegment(player + lift, target + lift) ? "clear hint" : "hit hint; observed attempt allowed")}.");
        }

        public MovementResult Tick()
        {
            if (!_active) return MovementResult.Stalled;
            DateTime now = DateTime.UtcNow;
            Vector3 player = DynelManager.LocalPlayer.Position;
            float distance = Distance(player, _target, _flying);
            if (distance + 0.35f < BestDistance) { BestDistance = distance; _lastProgress = now; }
            float tolerance = _precise ? 0.8f : 1.5f;
            if (distance <= tolerance)
            {
                Remember(_visited, _target); Halt(); return MovementResult.Reached;
            }
            if (StallSeconds >= _stallLimit)
            {
                Remember(_failed, _target);
                _say($"Observed movement stall: target=({LocalRoutePlanner.Coordinates(_target)}), " +
                    $"position=({LocalRoutePlanner.Coordinates(player)}), progress delta={Progress:F2} m, " +
                    $"remaining={distance:F2} m, no-progress={StallSeconds:F1} s; this leg is blocked, mission not failed.");
                Halt(); return MovementResult.Stalled;
            }
            if (!_flying && DynelManager.LocalPlayer.IsFalling) { StopMotion(); return MovementResult.Moving; }
            // The direct waypoint controller may stop short at its own tolerance.
            // Use precise horizontal steering for the last metres on foot.
            if (!_flying && !(_precise && distance <= 3))
            {
                if (_steering) { StopMotion(); _steering = false; _nextSubmit = DateTime.MinValue; }
                if (now >= _nextSubmit)
                {
                    _owns = true;
                    bool accepted = _mesh ? SMovementController.SetNavDestination(_target) : SMovementController.SetDestination(_target);
                    if (!accepted && _mesh)
                    {
                        SMovementController.Halt(); _mesh = false;
                        accepted = SMovementController.SetDestination(_target);
                        _say("Optional mesh submission failed; direct waypoint submitted under the same progress clock.");
                    }
                    if (!accepted) _say("AO# waypoint submission declined; retaining observed no-progress deadline.");
                    _nextSubmit = now.AddSeconds(2);
                }
                return MovementResult.Moving;
            }
            if (!_steering)
            {
                // Stop the waypoint controller before taking precise steering ownership.
                // Otherwise its queued turn/forward updates can contradict this final leg.
                if (!_flying && _owns) StopMotion();
                _mesh = false; _steering = true;
            }
            float velocity = Math.Max(0, DynelManager.LocalPlayer.Velocity);
            double elapsed = Math.Max(0.025, Math.Min(0.2, (now - _lastSteer).TotalSeconds));
            _lastSteer = now;
            if (_precise && distance <= 6 && velocity > 0.5f && distance <= tolerance + velocity * elapsed)
            { StopMotion(); return MovementResult.Moving; }
            Vector3 delta = _target - player;
            if (!_flying) delta.Y = 0;
            Vector3 wanted = delta.Normalize();
            Vector3 current = DynelManager.LocalPlayer.Rotation.Forward;
            Vector3 direction = wanted;
            if (_flying && !_precise && AcceptedMissions.Finite(current) && Vector3.Distance(current, Vector3.Zero) > 0.1f)
            {
                current = current.Normalize();
                double angle = Math.Acos(Math.Max(-1, Math.Min(1, Vector3.Dot(current, wanted))));
                if (angle < Math.PI / 4) direction = Smooth(current, wanted, angle, elapsed * Math.PI * 2 / 3);
            }
            Vector3 up = Math.Abs(direction.X) + Math.Abs(direction.Z) < 0.05f ? new Vector3(0, 0, 1) : Vector3.Up;
            DynelManager.LocalPlayer.Rotation = Quaternion.LookRotation(direction, up);
            SMovementController.SetMovement(MovementAction.ForwardStart);
            SMovementController.SetMovement(MovementAction.Update);
            _owns = true;
            return MovementResult.Moving;
        }

        private void StopMotion()
        {
            if (!_owns) return;
            SMovementController.Halt(); SMovementController.SetMovement(MovementAction.FullStop); _owns = false;
        }

        public Vector3 CoarseStep(Vector3 destination, int recovery)
        {
            Vector3 player = DynelManager.LocalPlayer.Position;
            float distance = LocalRoutePlanner.HorizontalDistance(player, destination);
            double heading = LocalRoutePlanner.Angle(destination - player);
            // Start with a linear local estimate. Fan/radii are advisory recovery,
            // not a fixed number of attempts after which the mission is discarded.
            Vector3 direct = player + LocalRoutePlanner.Direction(heading) * Math.Min(12, distance);
            if (recovery == 0)
            {
                direct = LocalRoutePlanner.LocalElevation(direct, player, false, _settings, out _);
                return direct;
            }
            float best = float.PositiveInfinity;
            Vector3 chosen = direct;
            foreach (float radius in new[] { 12f, 8f, 4f })
                for (int sector = 0; sector < 16; sector++)
                {
                    double angle = heading + (sector + recovery % 2 * 0.5) * Math.PI / 8;
                    Vector3 candidate = player + LocalRoutePlanner.Direction(angle) * Math.Min(radius, Math.Max(2, distance));
                    candidate = LocalRoutePlanner.LocalElevation(candidate, player, false, _settings, out _);
                    float baseScore = LocalRoutePlanner.HorizontalDistance(candidate, destination) + radius * 0.25f;
                    float score = baseScore;
                    Vector3 lift = Vector3.Up;
                    if (!LocalRoutePlanner.ClearSegment(player + lift, candidate + lift)) score += 20;
                    score += _failed.Count(x => LocalRoutePlanner.HorizontalDistance(x, candidate) < 3) * 40;
                    score += _visited.Count(x => LocalRoutePlanner.HorizontalDistance(x, candidate) < 3) * 16;
                    if (score < best) { best = score; chosen = candidate; }
                }
            return chosen;
        }

        private static Vector3 Smooth(Vector3 current, Vector3 wanted, double angle, double maximum)
        {
            if (angle <= maximum) return wanted;
            Vector3 tangent = wanted - current * Vector3.Dot(current, wanted);
            return current * (float)Math.Cos(maximum) + tangent.Normalize() * (float)Math.Sin(maximum);
        }

        public static float Distance(Vector3 from, Vector3 to, bool flying) => flying ?
            Vector3.Distance(from, to) : LocalRoutePlanner.HorizontalDistance(from, to);
        private static void Remember(List<Vector3> points, Vector3 point)
        { points.Add(point); if (points.Count > 48) points.RemoveAt(0); }
    }
}
