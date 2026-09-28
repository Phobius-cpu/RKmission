using System;
using System.Collections.Generic;
using System.Linq;
using AOSharp.Common.GameData;
using AOSharp.Core;
using AOSharp.Pathfinding;

namespace RKmission
{
    internal enum TravelMode { Auto, Ground, Flying }

    internal sealed class LocalMissionTravel
    {
        private enum Phase { Idle, GroundTravel, FlightCruise, EntrancePosition, EntranceHeight, EntranceApproach, EnterDoor }
        private readonly Action<string> _say;
        private readonly List<Vector3> _recentGround = new List<Vector3>();
        private LocalRoute _route;
        private EntranceAcquisition _entrance;
        private List<AcceptedMission> _acceptedContext;
        private Phase _phase;
        private bool _ownsMovement, _forceDirect, _directActive, _doorCrossLogged;
        private bool _flightHolding, _entranceHeightReady, _flightPathComplete, _flightPathAdvisory, _entrancePointSet;
        private readonly List<Vector3> _flightPath = new List<Vector3>();
        private readonly List<LocalRoutePlanner.FlightBlockedLeg> _blockedFlightLegs = new List<LocalRoutePlanner.FlightBlockedLeg>();
        private Vector3 _lastPosition, _directStep, _groundTarget, _flightGoal, _loggedTarget, _heightAnchor, _flightPathOrigin;
        private float _bestStepDistance, _bestGroundDistance, _bestFlightDistance, _bestFlightStepDistance;
        private float _positionHeight;
        private DateTime _stepProgress, _groundProgress, _flightProgress, _flightStepProgress, _lastMotion;
        private DateTime _travelStarted, _nextMove, _lastUse, _nextProgressLog, _nextProbeLog;
        private DateTime _nextFlightProbe, _lastFlightSteer, _nextFlightPlan, _flightBlockedAt;
        private int _groundRecoveries, _flightPlans, _preferredSide, _doorAttempts;
        private int _flightPathIndex;
        private int _flightSearchRetries;
        private string _movementKind, _lastEvaluation;
        private Identity _door = Identity.None;
        public TravelMode Mode { get; set; } = TravelMode.Auto;
        public string Status => _phase.ToString();
        public Identity ActiveDoor => _door;
        public bool MatchesAnchor(AcceptedMission mission) => _route != null && _route.Mission.Id == mission.Id &&
            Vector3.Distance(_route.Entrance, mission.Entrance) <= 0.5f;
        public bool IsFlightActive => IsFlying && (_phase == Phase.FlightCruise || _phase == Phase.EntrancePosition ||
            _phase == Phase.EntranceHeight || _phase == Phase.EntranceApproach);
        public int UpdateIntervalMilliseconds => !IsFlightActive ? 250 :
            _phase != Phase.FlightCruise && _flightPath.Count > 0 &&
            Vector3.Distance(DynelManager.LocalPlayer.Position, _flightPath[_flightPathIndex]) <= 6 ? 25 : 100;
        private static bool IsFlying => DynelManager.LocalPlayer.MovementState == MovementState.Fly;

        public LocalMissionTravel(Action<string> say) { _say = say; }

        public AcceptedMission SelectNearest(IEnumerable<AcceptedMission> missions)
        {
            bool flying = Mode == TravelMode.Flying || (Mode == TravelMode.Auto && IsFlying);
            if ((flying && !IsFlying) || (!flying && IsFlying))
            {
                string reason = flying ? "Equip your flying vehicle before using flying travel." : "Land and dismount before using ground travel.";
                if (_lastEvaluation != reason) _say(reason);
                _lastEvaluation = reason;
                return null;
            }
            Vector3 origin = DynelManager.LocalPlayer.Position;
            if (!AcceptedMissions.Finite(origin))
            {
                if (_lastEvaluation != "invalid origin") _say("Cannot select an entrance: player origin coordinates are invalid.");
                _lastEvaluation = "invalid origin";
                return null;
            }
            _acceptedContext = missions.Where(x => x.Present && x.IsRubiKaDestination &&
                x.State != MissionProgress.CompletedByUser && x.PlayfieldId == Playfield.ModelIdentity.Instance &&
                AcceptedMissions.Finite(x.Entrance)).ToList();
            var estimates = _acceptedContext.Select(x => LocalRoutePlanner.Estimate(x, origin, flying))
                .Where(x => x != null && !float.IsNaN(x.Cost) && !float.IsInfinity(x.Cost)).ToList();
            foreach (LocalRoute estimate in estimates.OrderBy(x => x.Cost).ThenBy(x => x.Mission.Id.Instance))
                _say($"Mission route estimate: mission={estimate.Mission.Id.Instance}, playfield={estimate.Mission.PlayfieldId}, " +
                    $"origin=({LocalRoutePlanner.Coordinates(origin)}), anchor=({LocalRoutePlanner.Coordinates(estimate.Entrance)}), " +
                    $"mode={(flying ? "flight" : "ground")}, route cost={estimate.Cost:F1} m, source={estimate.Reason}.");
            LocalRoute nearest = estimates.OrderBy(x => x.Cost).ThenBy(x => x.Mission.Id.Instance).FirstOrDefault();
            if (nearest == null) return null;
            _route = LocalRoutePlanner.Plan(nearest, flying);
            if (_route == null) return null;
            _entrance = null;
            SMovementController.Halt();
            SMovementController.SetMovement(MovementAction.FullStop);
            _door = Identity.None;
            _doorAttempts = _groundRecoveries = _flightPlans = _preferredSide = 0;
            _travelStarted = DateTime.UtcNow;
            _forceDirect = !_route.GroundUsesMesh;
            _recentGround.Clear();
            _blockedFlightLegs.Clear(); _flightSearchRetries = 0;
            _entranceHeightReady = _entrancePointSet = false;
            Begin(flying ? Phase.FlightCruise : Phase.GroundTravel);
            _say($"Nearest entrance selected: {_route.Mission.Id.Instance}, {_route.Mission.Name}; " +
                $"origin=({LocalRoutePlanner.Coordinates(_route.Origin)}), " +
                $"accepted marker=({LocalRoutePlanner.Coordinates(_route.Entrance)}), estimated distance={_route.EntranceDistance:F1} m. " +
                $"Selected {(flying ? "flying" : "ground")} route: {_route.Reason}, route cost={_route.Cost:F1} m; " +
                $"movement={DynelManager.LocalPlayer.MovementState}, outdoor mesh={SMovementController.NavAgent?.HasPathfinder == true}, " +
                $"search hint=({_route.Mission.PlayfieldId}: {LocalRoutePlanner.Coordinates(_route.EntrancePoint)}), " +
                $"source={_route.HeightSource}, terrain floor={_route.EntranceIsFloor}, " +
                "final door/threshold pending live acquisition.");
            return _route.Mission;
        }

        public void Reset()
        {
            Halt(); _route = null; _entrance = null; _acceptedContext = null; _phase = Phase.Idle; _door = Identity.None;
            _lastEvaluation = null; _directActive = _flightHolding = _entranceHeightReady = false;
            _entrancePointSet = _flightPathAdvisory = false;
            _flightPath.Clear();
            _recentGround.Clear();
            _blockedFlightLegs.Clear(); _flightSearchRetries = 0;
        }

        private void Halt()
        {
            if (!_ownsMovement) return;
            SMovementController.Halt();
            SMovementController.SetMovement(MovementAction.FullStop);
            _ownsMovement = false;
        }

        private void Begin(Phase phase)
        {
            Halt(); _phase = phase;
            DateTime now = DateTime.UtcNow;
            _groundProgress = _flightProgress = _lastMotion = now;
            _lastPosition = DynelManager.LocalPlayer.Position;
            _nextMove = _nextProgressLog = _nextProbeLog = DateTime.MinValue;
            _nextFlightProbe = DateTime.MinValue;
            _directActive = _flightHolding = _flightPathAdvisory = false; _movementKind = null;
            _flightPath.Clear(); _flightPathIndex = 0;
            _nextFlightPlan = _flightBlockedAt = DateTime.MinValue; _lastFlightSteer = now;
            _bestGroundDistance = _bestFlightDistance = _bestFlightStepDistance = float.PositiveInfinity;
            _flightStepProgress = now;
        }

        public bool Tick(AcceptedMission mission, IEnumerable<AcceptedMission> accepted)
        {
            if (_route == null || _route.Mission.Id != mission.Id) return false;
            // Shared managed list also sees missions accepted/removed during this route.
            _acceptedContext.Clear(); _acceptedContext.AddRange(accepted);
            if (!mission.Present || mission.PlayfieldId != Playfield.ModelIdentity.Instance)
                return Fail($"Selected mission {mission.Id.Instance} is no longer accepted in this playfield.");
            DateTime now = DateTime.UtcNow;
            if (now - _travelStarted > TimeSpan.FromMinutes(15))
                return Fail($"Local travel exceeded 15 minutes for mission {mission.Id.Instance}.");
            Vector3 position = DynelManager.LocalPlayer.Position;
            if (!AcceptedMissions.Finite(position)) return Fail("Player world coordinates are invalid.");
            if (Vector3.Distance(position, _lastPosition) >= 0.5f)
            { _lastPosition = position; _lastMotion = now; }
            if (_entrance != null) return AcquireEntrance(mission);
            if (LocalRoutePlanner.HorizontalDistance(position, _route.Entrance) <= EntranceAcquisition.AcquisitionRadius)
            {
                _entrance = new EntranceAcquisition(mission, _acceptedContext, _route.Entrance, _route.Origin, _say);
                Begin(Phase.EntranceApproach);
                return AcquireEntrance(mission);
            }
            switch (_phase)
            {
                case Phase.GroundTravel:
                    if (IsFlying) return Fail("Ground travel requires landing/dismount; use /rkm travel auto or flying.");
                    return GroundMove(_route.EntrancePoint);
                case Phase.FlightCruise:
                {
                    Vector3 point = _route.EntrancePoint;
                    Vector3 near = LocalRoutePlanner.OutsideEntrance(point, _route.Origin, 1.5f);
                    _route.CruiseEnd.X = near.X; _route.CruiseEnd.Z = near.Z;
                    // Coarse hint only; final coordinates/height come from acquisition.
                    _route.CruiseEnd.Y = _route.Origin.Y;
                    return FlyMove(_route.CruiseEnd);
                }
                default: return false;
            }
        }

        private bool GroundMove(Vector3 destination)
        {
            Vector3 position = DynelManager.LocalPlayer.Position;
            DateTime now = DateTime.UtcNow;
            if (!AcceptedMissions.Finite(destination)) return Fail("Ground target coordinates are invalid.");
            if (LocalRoutePlanner.HorizontalDistance(destination, _groundTarget) > 1)
            {
                Halt(); _directActive = false; _bestGroundDistance = float.PositiveInfinity;
                _groundProgress = now;
            }
            _groundTarget = destination;
            float distance = LocalRoutePlanner.HorizontalDistance(position, destination);
            if (distance + 0.75f < _bestGroundDistance)
            { _bestGroundDistance = distance; _groundProgress = now; }
            if (now - _groundProgress > TimeSpan.FromSeconds(90))
            {
                if (_entrance != null) { RetryEntrance("ground executor made no improvement for 90 seconds"); return true; }
                return Fail($"Ground made no improvement toward entrance for 90 seconds; target={destination}, " +
                    $"remaining={distance:F1} m, best={_bestGroundDistance:F1} m, position={position}, recoveries={_groundRecoveries}.");
            }
            LogProgress("Ground", position, destination, distance, _groundProgress);
            if (distance <= 0.7f) { Halt(); _directActive = false; return true; }
            if (_ownsMovement && now - _lastMotion > TimeSpan.FromSeconds(15)) RecoverGround("movement stalled for 15 seconds");
            if (_directActive)
            {
                float remaining = LocalRoutePlanner.HorizontalDistance(position, _directStep);
                if (remaining + 0.4f < _bestStepDistance) { _bestStepDistance = remaining; _stepProgress = now; }
                // The SDK can finish a waypoint inside its own arrival radius.
                if (remaining <= 1.2f || (!SMovementController.IsNavigating() && remaining <= 2))
                { Halt(); _directActive = false; Remember(_recentGround, position); _nextMove = DateTime.MinValue; }
                else if (now - _stepProgress > TimeSpan.FromSeconds(8))
                    RecoverGround($"waypoint made no progress for 8 seconds; target={_directStep}, remaining={remaining:F1} m");
                else
                {
                    LogProbe(position + Vector3.Up, _directStep + Vector3.Up, "Ground");
                    if (now < _nextMove) return true;
                    _ownsMovement = true;
                    if (!SMovementController.SetDestination(_directStep))
                    { RecoverGround("AO# did not accept direct waypoint; resampling"); _nextMove = now.AddSeconds(1); return true; }
                    _nextMove = now.AddSeconds(3); return true;
                }
            }
            if (now < _nextMove) return true;
            if (!_forceDirect && LocalRoutePlanner.TryGroundCost(position, destination, out _))
            {
                _ownsMovement = true;
                if (SMovementController.SetNavDestination(destination) && SMovementController.IsNavigating())
                { LogMovement("Ground navmesh", destination, destination); _nextMove = now.AddSeconds(3); return true; }
                RecoverGround("AO# queued no complete mesh path; using local waypoint sampling");
            }
            _forceDirect = true;
            if (!LocalRoutePlanner.TryDirectGroundStep(position, destination, _preferredSide,
                _groundRecoveries, _recentGround, out Vector3 step, out string hint, out int side))
            { RecoverGround("no finite local waypoint this pass; will resample"); _nextMove = now.AddSeconds(1); return true; }
            if (side != 0) _preferredSide = side;
            _directStep = step; _directActive = true;
            _bestStepDistance = LocalRoutePlanner.HorizontalDistance(position, step); _stepProgress = now;
            _ownsMovement = true;
            if (!SMovementController.SetDestination(step))
            { RecoverGround("AO# did not accept sampled waypoint; will retry"); _nextMove = now.AddSeconds(1); return true; }
            LogMovement("Ground local fallback: " + hint, step, destination);
            _nextMove = now.AddSeconds(3); return true;
        }

        private void RecoverGround(string reason)
        {
            if (_directActive) Remember(_recentGround, _directStep);
            Halt(); _directActive = false; _forceDirect = true; _groundRecoveries++;
            if (_groundRecoveries % 4 == 0) _preferredSide = -_preferredSide;
            _lastMotion = DateTime.UtcNow; _nextMove = DateTime.MinValue;
            // Recovery never resets entrance progress. Wandering cannot extend a stalled run.
            _say($"Ground obstacle recovery {_groundRecoveries}: {reason}; position={DynelManager.LocalPlayer.Position}, " +
                $"entrance target={_groundTarget}; sampling multiple radii/arcs, no-progress={(DateTime.UtcNow - _groundProgress).TotalSeconds:F0}/90 s.");
        }

        private static void Remember(List<Vector3> recent, Vector3 point)
        { recent.Add(point); if (recent.Count > 24) recent.RemoveAt(0); }

        private void LogMovement(string kind, Vector3 activeTarget, Vector3 finalTarget)
        {
            if (_movementKind == kind && Vector3.Distance(activeTarget, _loggedTarget) < 1) return;
            _movementKind = kind; _loggedTarget = activeTarget;
            _say($"Active movement: {kind}; waypoint=({LocalRoutePlanner.Coordinates(activeTarget)}); " +
                $"stage target=({LocalRoutePlanner.Coordinates(finalTarget)}); " +
                $"mission entrance=({LocalRoutePlanner.Coordinates(_route.EntrancePoint)}).");
        }

        private void LogProgress(string kind, Vector3 position, Vector3 target, float distance, DateTime progress)
        {
            if (DateTime.UtcNow < _nextProgressLog) return;
            _nextProgressLog = DateTime.UtcNow.AddSeconds(5);
            _say($"{kind} progress: phase={_phase}, position=({LocalRoutePlanner.Coordinates(position)}), " +
                $"stage target=({LocalRoutePlanner.Coordinates(target)}), " +
                $"entrance=({LocalRoutePlanner.Coordinates(_route.EntrancePoint)}), remaining={distance:F1} m, " +
                (kind == "Flight" ? $"vertical gap={position.Y - target.Y:F1} m, velocity={DynelManager.LocalPlayer.Velocity:F1}, " : "") +
                $"no improvement for {(DateTime.UtcNow - progress).TotalSeconds:F0}/90 s.");
        }

        private void LogProbe(Vector3 position, Vector3 target, string kind)
        {
            if (DateTime.UtcNow < _nextProbeLog) return;
            _nextProbeLog = DateTime.UtcNow.AddSeconds(8);
            if (!LocalRoutePlanner.ClearSegment(position, target))
                _say($"{kind} obstacle hint: probe hit toward {target}; continuing observed movement, resampling on a stall.");
        }

        private bool FlightMovementSegmentClear(Vector3 from, Vector3 to, float radius)
            => !LocalRoutePlanner.ObservedFlightSegmentBlocked(from, to, radius, _blockedFlightLegs) &&
                (_flightPathAdvisory || LocalRoutePlanner.ClearSegment(from, to));

        private bool FlyMove(Vector3 destination)
        {
            if (!IsFlying) return Fail("Flying movement state was lost away from entrance. Land, then restart local travel.");
            if (!AcceptedMissions.Finite(destination)) return Fail("Flight target coordinates are invalid.");
            Vector3 position = DynelManager.LocalPlayer.Position;
            DateTime now = DateTime.UtcNow;
            if (Vector3.Distance(destination, _flightGoal) > 0.05f)
            {
                _bestFlightDistance = Vector3.Distance(position, destination);
                if (LocalRoutePlanner.HorizontalDistance(destination, _flightGoal) > 2 ||
                    Math.Abs(destination.Y - _flightGoal.Y) > 1) _flightPath.Clear();
                else if (_flightPathComplete && _flightPath.Count > 0) _flightPath[_flightPath.Count - 1] = destination;
                // Changed height is not observed progress; retain the final deadline.
                _flightGoal = destination;
            }
            float remaining = Vector3.Distance(position, destination);
            if (remaining + 0.5f < _bestFlightDistance) { _bestFlightDistance = remaining; _flightProgress = now; }
            if (now - _flightProgress > TimeSpan.FromSeconds(90))
            {
                if (_entrance != null) { RetryEntrance("flight executor made no improvement for 90 seconds"); return true; }
                return Fail($"Flight made no improvement toward {_phase} target for 90 seconds; target={destination}, " +
                    $"remaining={remaining:F1} m, position={position}, committed plans={_flightPlans}.");
            }
            LogProgress("Flight", position, destination, remaining, _flightProgress);
            float bodyRadius = DynelManager.LocalPlayer.Radius;
            if (float.IsNaN(bodyRadius) || float.IsInfinity(bodyRadius)) bodyRadius = 0.6f;
            float radius = Math.Max(0.6f, Math.Min(1.2f, bodyRadius + 0.25f));
            float velocity = DynelManager.LocalPlayer.Velocity;
            if (float.IsNaN(velocity) || float.IsInfinity(velocity)) velocity = 0;
            if (_flightPath.Count == 0)
            {
                if (now >= _nextFlightPlan) CommitFlightPath(position, destination, radius, "initial/changed destination or expanded search retry");
                // Final stages can attempt a finite estimate under the same deadline
                // when surface clearance is inconclusive. Observed failures still
                // exclude attempted directions; a truly exhausted search holds.
                if (_flightPath.Count == 0) { Halt(); return true; }
                remaining = Vector3.Distance(position, destination);
            }
            float arrival = Math.Max(1.2f, Math.Min(2.5f, velocity * 0.12f));
            if (!_flightPathComplete && _flightPathIndex == _flightPath.Count - 1 &&
                FlightWaypointReached(position, _flightPathIndex, arrival) &&
                FlightMovementSegmentClear(LocalRoutePlanner.Toward(position, _flightPath[_flightPathIndex], 0.3f),
                    _flightPath[_flightPathIndex], radius))
            {
                // Successful prefix arrival is a continuation, not an obstruction
                // or permission to treat its endpoint as the actual mission entrance.
                _flightSearchRetries = 0;
                CommitFlightPath(position, destination, radius, "validated prefix reached; continue from new position");
                if (_flightPath.Count == 0) { Halt(); return true; }
                remaining = Vector3.Distance(position, destination);
            }
            while (_flightPathIndex < _flightPath.Count - 1 &&
                FlightWaypointReached(position, _flightPathIndex, arrival))
            {
                Vector3 next = _flightPath[_flightPathIndex + 1];
                Vector3 nextEnd = _flightPathComplete && _phase == Phase.EntranceApproach && _flightPathIndex + 1 == _flightPath.Count - 1
                    ? LocalRoutePlanner.Toward(position, next, Math.Max(0, Vector3.Distance(position, next) - 1)) : next;
                // Do not cut a planned corner early through a roof/wall. Continue
                // toward this waypoint until the next leg is clear from the real position.
                if (!FlightMovementSegmentClear(LocalRoutePlanner.Toward(position, nextEnd, 0.3f), nextEnd, radius)) break;
                _flightPathIndex++;
                _bestFlightStepDistance = float.PositiveInfinity; _flightStepProgress = now;
                _nextFlightProbe = DateTime.MinValue;
            }
            Vector3 target = _flightPath[_flightPathIndex];
            float stepDistance = Vector3.Distance(position, target);
            if (stepDistance + 0.4f < _bestFlightStepDistance)
            { _bestFlightStepDistance = stepDistance; _flightStepProgress = now; }
            bool stalled = now - _flightStepProgress > TimeSpan.FromSeconds(8);
            if (now >= _nextFlightProbe)
            {
                _nextFlightProbe = now.AddMilliseconds(400);
                float probeLength = Math.Min(stepDistance, Math.Max(2, Math.Min(5, velocity * 0.3f + 1)));
                if (_flightPathComplete && _phase == Phase.EntranceApproach && remaining <= probeLength + 1)
                    probeLength = Math.Max(0, probeLength - 1);
                Vector3 end = LocalRoutePlanner.Toward(position, target, probeLength);
                Vector3 start = LocalRoutePlanner.Toward(position, end, 0.3f);
                // A nearby surface hit can pause movement. Body-offset hints rank
                // routes; search budget exhaustion alone cannot stop usable travel.
                _flightHolding = probeLength > 0.5f && !FlightMovementSegmentClear(start, end, radius);
                if (_flightPathAdvisory && probeLength > 0.5f) LogProbe(start, end, "Entrance movement estimate");
                if (!_flightHolding) _flightBlockedAt = DateTime.MinValue;
                else if (_flightBlockedAt == DateTime.MinValue) _flightBlockedAt = now;
            }
            bool obstructed = _flightHolding && now - _flightBlockedAt >= TimeSpan.FromSeconds(1);
            if ((stalled || obstructed) && now >= _nextFlightPlan)
            {
                // A probe-only pause is not an observed movement failure. Otherwise
                // stale surface data could poison the later advisory attempt too.
                if (stalled && _ownsMovement && !_flightHolding)
                {
                    RememberBlockedFlightLeg(position, target, true, radius);
                    if (_entrance != null)
                    {
                        RetryEntrance("observed flight waypoint stall");
                        return true;
                    }
                }
                CommitFlightPath(position, destination, radius, stalled ? "8 seconds without waypoint progress" : "sustained nearby surface obstruction");
                if (_flightPath.Count == 0) { Halt(); return true; }
                remaining = Vector3.Distance(position, destination);
                target = _flightPath[0];
                stepDistance = Vector3.Distance(position, target);
                float probeLength = Math.Min(2, stepDistance);
                if (_flightPathComplete && _phase == Phase.EntranceApproach && _flightPath.Count == 1 && stepDistance <= probeLength + 1)
                    probeLength = Math.Max(0, probeLength - 1);
                Vector3 end = LocalRoutePlanner.Toward(position, target, probeLength);
                _flightHolding = !FlightMovementSegmentClear(LocalRoutePlanner.Toward(position, end, 0.3f), end, radius);
                _flightBlockedAt = _flightHolding ? now : DateTime.MinValue;
            }
            if (_flightHolding) { Halt(); return true; }
            Vector3 legStart = _flightPathIndex == 0 ? _flightPathOrigin : _flightPath[_flightPathIndex - 1];
            string purpose = _phase == Phase.EntrancePosition ? "travel to selected live-door approach side" : _phase == Phase.EntranceHeight
                ? target.Y < legStart.Y - 0.75f ? "descending to approach height" :
                    target.Y > legStart.Y + 0.75f ? "climbing to approach height" :
                    LocalRoutePlanner.HorizontalDistance(target, _route.EntrancePoint) > 2
                        ? "outside alignment for descent" : "entrance height alignment"
                : _phase == Phase.EntranceApproach ? "entrance approach in vehicle" : "travel to within 2 m";
            LogMovement($"Flight committed {(_flightPathAdvisory ? "attempt" : _flightPathComplete ? "path" : "prefix")} leg {_flightPathIndex + 1}/{_flightPath.Count}: " +
                purpose, target, destination);
            bool precise = _phase == Phase.EntrancePosition || _phase == Phase.EntranceHeight || _phase == Phase.EntranceApproach;
            if (stepDistance <= (precise ? 0.45f : 0.1f)) { Halt(); return true; }
            if (precise && stepDistance <= 6 && velocity > 0.5f)
            {
                // Release forward before the next movement update carries us over
                // a precise waypoint. Resume below this speed if it is still outside
                // arrival tolerance; never change game speed/position or the target.
                float reaction = (float)Math.Max(0.05, Math.Min(0.2, (now - _lastFlightSteer).TotalSeconds));
                if (stepDistance <= 0.45f + velocity * reaction) { Halt(); return true; }
            }
            Vector3 wanted = (target - position).Normalize();
            Vector3 current = DynelManager.LocalPlayer.Rotation.Forward;
            if (!AcceptedMissions.Finite(current) || Vector3.Distance(current, Vector3.Zero) < 0.1f) current = wanted;
            else current = current.Normalize();
            double angle = Math.Acos(Math.Max(-1, Math.Min(1, Vector3.Dot(current, wanted))));
            double elapsed = Math.Max(0.02, Math.Min(0.2, (now - _lastFlightSteer).TotalSeconds));
            _lastFlightSteer = now;
            Vector3 steering = (precise && stepDistance <= 6) || angle > Math.PI / 4 ? wanted :
                SmoothFlightDirection(current, wanted, angle, elapsed * Math.PI * 2 / 3);
            // Moving turns stay continuous; face the committed leg directly if a
            // smoothed heading would cut a corner into known geometry.
            float turnProbe = Math.Min(stepDistance, 2);
            if (angle > Math.PI / 180 && !FlightMovementSegmentClear(position + steering * 0.3f,
                position + steering * turnProbe, radius)) steering = wanted;
            Vector3 up = Math.Abs(steering.X) + Math.Abs(steering.Z) < 0.05f ? new Vector3(0, 0, 1) : Vector3.Up;
            DynelManager.LocalPlayer.Rotation = Quaternion.LookRotation(steering, up);
            SMovementController.SetMovement(MovementAction.ForwardStart);
            SMovementController.SetMovement(MovementAction.Update);
            _ownsMovement = true;
            return true;
        }

        private bool FlightWaypointReached(Vector3 position, int index, float arrival)
        {
            Vector3 point = _flightPath[index];
            if (_phase == Phase.EntrancePosition || _phase == Phase.EntranceHeight)
            {
                // Reach the chosen outside column before turning downward. The
                // cruise arrival radius would otherwise cut up to 2.5 m back over
                // the roof. Finish lowering before the sideways return, too.
                if (index + 1 < _flightPath.Count && _flightPath[index + 1].Y < point.Y - 1.5f)
                    arrival = Math.Min(arrival, 0.75f);
                if (index > 0 && point.Y < _flightPath[index - 1].Y - 1.5f && Math.Abs(position.Y - point.Y) > 0.75f)
                    return false;
            }
            return Vector3.Distance(position, point) <= arrival;
        }

        private void RememberBlockedFlightLeg(Vector3 position, Vector3 target, bool stalled, float radius)
        {
            if (Vector3.Distance(position, target) < 0.75f) return;
            Vector3 end = LocalRoutePlanner.Toward(position, target, 4);
            bool descent = stalled && _ownsMovement && !_flightHolding && _phase == Phase.EntranceHeight &&
                position.Y - target.Y > 1.5f &&
                LocalRoutePlanner.HorizontalDistance(position, target) <= 1.5f &&
                LocalRoutePlanner.HorizontalDistance(position, _route.EntrancePoint) <= 24;
            if (descent)
            {
                float footprint = Math.Max(4, radius * 3);
                int nearby = _blockedFlightLegs.FindIndex(x => x.DescentRadius > 0 &&
                    Math.Abs(x.From.Y - position.Y) <= 1 &&
                    LocalRoutePlanner.HorizontalDistance(x.From, position) <= x.DescentRadius);
                if (nearby >= 0)
                {
                    var obstruction = _blockedFlightLegs[nearby];
                    obstruction.DescentRadius = Math.Min(16, Math.Max(obstruction.DescentRadius + 4,
                        LocalRoutePlanner.HorizontalDistance(obstruction.From, position) + footprint));
                    _blockedFlightLegs[nearby] = obstruction;
                    _say($"Observed descent obstruction expanded: centre={obstruction.From}, " +
                        $"avoid crossing within {obstruction.DescentRadius:F1} m; held height={position.Y:F2}, " +
                        $"selected entry height={_route.EntrancePoint.Y:F2} retained; seek a farther outside descent.");
                    return;
                }
                _blockedFlightLegs.Add(new LocalRoutePlanner.FlightBlockedLeg
                    { From = position, To = end, DescentRadius = footprint });
                if (_blockedFlightLegs.Count > 16) _blockedFlightLegs.RemoveAt(0);
                _say($"Observed descent obstruction recorded: centre={position}, avoid crossing within {footprint:F1} m; " +
                    $"requested height={target.Y:F2}, selected entry height={_route.EntrancePoint.Y:F2} retained. " +
                    "A stopped descent is not a door-height measurement; seek an outside drop and return below the obstruction.");
                return;
            }
            if (_blockedFlightLegs.Any(x => Vector3.Distance(x.From, position) < 1 && Vector3.Distance(x.To, end) < 1)) return;
            _blockedFlightLegs.Add(new LocalRoutePlanner.FlightBlockedLeg { From = position, To = end });
            if (_blockedFlightLegs.Count > 16) _blockedFlightLegs.RemoveAt(0);
            _say($"Flight blocked leg recorded: {position} -> {end}; searching around/above the obstruction, retaining this entrance.");
        }

        private void CommitFlightPath(Vector3 position, Vector3 destination, float radius, string trigger)
        {
            float ceiling = Math.Max(_route.CruiseEnd.Y, _route.EntrancePoint.Y + 12) + 40;
            float extent = Math.Min(80, 32 + _flightSearchRetries * 16);
            bool entranceStage = _phase == Phase.EntrancePosition || _phase == Phase.EntranceHeight || _phase == Phase.EntranceApproach;
            // Plan only the active acquisition attempt. Another approach is selected
            // by the shared acquisition routine after observed failure/no progress.
            List<Vector3> path = LocalRoutePlanner.PlanFlightPath(position, destination, ceiling, radius,
                _blockedFlightLegs, extent, entranceStage, _phase == Phase.EntranceApproach, out _flightPathComplete, out string reason);
            _flightPathAdvisory = false;
            if (path.Count == 0 && entranceStage && _entrancePointSet)
            {
                path = LocalRoutePlanner.PlanEntranceAttempt(position, destination, radius, _blockedFlightLegs, out string attemptReason);
                reason = $"bounded surface search: {reason}; {attemptReason}";
                if (path.Count > 0) _flightPathAdvisory = _flightPathComplete = true;
            }
            _flightPath.Clear(); _flightPath.AddRange(path); _flightPathIndex = 0;
            _flightPathOrigin = position;
            _bestFlightStepDistance = float.PositiveInfinity; _flightStepProgress = DateTime.UtcNow;
            _nextFlightPlan = DateTime.UtcNow.AddSeconds(3); _nextFlightProbe = DateTime.MinValue;
            _flightHolding = false; _flightBlockedAt = DateTime.MinValue;
            if (path.Count == 0)
            {
                _flightSearchRetries++;
                _say($"Flight obstacle search waiting: {trigger}; {reason}; final target={destination}; progress deadline retained.");
                return;
            }
            // Expand following failed execution too, without resetting observed progress.
            _flightSearchRetries++;
            _say($"Flight path committed {++_flightPlans}: {trigger}; {reason}; " +
                $"waypoints={string.Join(" -> ", path)}; final target={destination}; complete={_flightPathComplete}; " +
                $"advisory={_flightPathAdvisory}; observed descent areas={_blockedFlightLegs.Count(x => x.DescentRadius > 0)}; " +
                $"fixed approach point={(_entrancePointSet ? _heightAnchor.ToString() : "pending")}; progress deadline retained.");
        }

        private static Vector3 SmoothFlightDirection(Vector3 current, Vector3 wanted, double angle, double maxTurn)
        {
            if (angle <= maxTurn) return wanted;
            Vector3 tangent = wanted - current * Vector3.Dot(current, wanted);
            if (Vector3.Distance(tangent, Vector3.Zero) < 0.001f)
            {
                // A 180-degree turn needs a deterministic perpendicular direction.
                tangent = new Vector3(-current.Z, 0, current.X);
                if (Vector3.Distance(tangent, Vector3.Zero) < 0.001f) tangent = new Vector3(1, 0, 0);
            }
            return current * (float)Math.Cos(maxTurn) + tangent.Normalize() * (float)Math.Sin(maxTurn);
        }

        private bool AcquireEntrance(AcceptedMission mission)
        {
            DateTime now = DateTime.UtcNow;
            Vector3 position = DynelManager.LocalPlayer.Position;
            bool flying = IsFlying;
            _entrance.Scan(position, flying);
            if (_entrance.PreferNewDoor) RetryEntrance("live door loaded during anchor search");
            if (_entrance.Select(position, flying))
            {
                EntranceAcquisition.Attempt selected = _entrance.Active;
                _door = selected.DoorId; _doorAttempts = 0; _doorCrossLogged = false; _lastUse = DateTime.MinValue;
                _route.EntrancePoint = selected.Threshold;
                _route.HeightSource = selected.HeightSource;
                _route.EntranceIsFloor = false; // Attempt height already includes any floor clearance.
                _heightAnchor = selected.Point;
                _entrancePointSet = true; _entranceHeightReady = false;
                _positionHeight = LocalRoutePlanner.ClearSegment(position, _heightAnchor)
                    ? selected.Point.Y : Math.Max(position.Y, selected.Point.Y);
                // Final mode follows the actual player state, even after manual landing.
                // No movement-state writes or automatic equipment changes are needed.
                _forceDirect = SMovementController.NavAgent?.HasPathfinder != true;
                Begin(flying ? Phase.EntrancePosition : Phase.EntranceApproach);
            }
            EntranceAcquisition.Attempt attempt = _entrance.Active;
            if (attempt == null)
            {
                Halt();
                if (_entrance.Exhausted && now - _entrance.LastProgress > TimeSpan.FromSeconds(90))
                    return Fail($"Mission {mission.Id.Instance}: all observed door approaches and anchor-search waypoints exhausted " +
                        "with 90 seconds of no new best approach distance; no verified transition.");
                return true; // Give later-loaded/replaced live objects another scan under the same clock.
            }
            _entrance.Observe(position, flying);
            Door door = _entrance.RefreshDoor();
            if (attempt.DoorId != Identity.None && door == null)
            { RetryEntrance("selected live door disappeared; rescan identities"); return true; }
            if (!flying && DynelManager.LocalPlayer.IsFalling) { Halt(); return true; }
            if (now - _entrance.AttemptProgress > TimeSpan.FromSeconds(door == null ? 8 : 18))
            { RetryEntrance("no observed progress at this candidate/approach"); return true; }

            // Use the freshly resolved object's real range, independent of marker range
            // or a mandatory 1-2 m annulus. Model origin and threshold heights can differ.
            if (door != null && DoorWithinUseRange(position, door) &&
                (!flying || Math.Abs(position.Y - attempt.Threshold.Y) <= 1))
            {
                Vector3 from = position + (flying ? Vector3.Zero : Vector3.Up);
                Vector3 to = door.Position + (flying ? Vector3.Zero : Vector3.Up);
                // Exclude the door face itself; a closed door should be usable. A wall
                // before the face requires a different side rather than a through-wall use.
                Vector3 end = LocalRoutePlanner.Toward(from, to, Math.Max(0, Vector3.Distance(from, to) - 0.6f));
                if (LocalRoutePlanner.ClearSegment(from, end))
                {
                    _entranceHeightReady = true;
                    if (!_doorCrossLogged && _phase != Phase.EnterDoor) Begin(Phase.EnterDoor);
                    if (_doorAttempts >= 2 && now - _lastUse >= TimeSpan.FromSeconds(8))
                    { RetryEntrance("two use commands sent, no dungeon transition observed; try another side/door"); return true; }
                    if (_doorAttempts < 2 && now - _lastUse >= TimeSpan.FromSeconds(4))
                    {
                        // Resolve again immediately before sending, never use a cached native pointer.
                        Door current = _entrance.RefreshDoor(true);
                        if (current == null || !DoorWithinUseRange(position, current))
                        { RetryEntrance("door identity/range changed before interaction"); return true; }
                        try { current.Use(); }
                        catch (Exception ex)
                        { RetryEntrance("door use threw " + ex.Message); return true; }
                        _lastUse = now; _doorAttempts++;
                        _say($"Entrance interaction: mission={mission.Id.Instance}, door={current.Identity}, " +
                            $"position=({LocalRoutePlanner.Coordinates(current.Position)}), " +
                            $"anchor offset={LocalRoutePlanner.HorizontalDistance(current.Position, _route.Entrance):F2} m, " +
                            $"approach=({LocalRoutePlanner.Coordinates(attempt.Point)}), height source={attempt.HeightSource}, " +
                            $"horizontal range={LocalRoutePlanner.HorizontalDistance(position, current.Position):F2} m, " +
                            $"3D range={Vector3.Distance(position, current.Position):F2} m, height gap={position.Y - current.Position.Y:F2} m, " +
                            $"locked={current.IsLocked}, open={current.IsOpen}, mode={(flying ? "flight" : "ground")}, " +
                            $"attempt={_doorAttempts}/2, result=command sent (Use has no acknowledgement); awaiting exact dungeon verification.");
                    }
                    if (_doorAttempts < 2 || now - _lastUse < TimeSpan.FromSeconds(4))
                    { Halt(); return true; }
                    if (!_doorCrossLogged)
                    {
                        _doorCrossLogged = true;
                        _say($"Entrance interaction: mission={mission.Id.Instance}, door={attempt.DoorId}, result=no zoning observed after use; " +
                            "trying a short physical threshold crossing before another candidate.");
                    }
                }
                LogProbe(from, end, "Entrance interaction corridor");
            }
            if (door == null)
            {
                // Moving through a proximity trigger may zone, but arriving at the
                // mission marker alone is never reported as acquired/entered.
                if (LocalRoutePlanner.HorizontalDistance(position, attempt.Point) <= 0.9f &&
                    (!flying || Math.Abs(position.Y - attempt.Point.Y) <= 0.9f))
                { RetryEntrance("search waypoint reached without a live door/transition"); return true; }
                return flying ? FlyMove(attempt.Point) : GroundMove(attempt.Point);
            }
            if (flying)
            {
                if (_phase == Phase.EntrancePosition)
                {
                    if (LocalRoutePlanner.HorizontalDistance(position, attempt.Point) <= 0.9f)
                        Begin(Phase.EntranceHeight);
                    else
                    {
                        Vector3 stage = attempt.Point; stage.Y = _positionHeight;
                        return FlyMove(stage);
                    }
                }
                if (_phase == Phase.EntranceHeight)
                {
                    if (Vector3.Distance(position, attempt.Point) > 0.9f) return FlyMove(attempt.Point);
                    _entranceHeightReady = true; Begin(Phase.EntranceApproach);
                    _say($"Entrance aligned: door={attempt.DoorId}, approach=({LocalRoutePlanner.Coordinates(attempt.Point)}), " +
                        $"height source={attempt.HeightSource}; proceeding toward live threshold.");
                }
                if (_phase == Phase.EnterDoor) Begin(Phase.EntranceApproach);
                return FlyMove(ThresholdTarget(attempt));
            }
            if (!_entranceHeightReady)
            {
                if (LocalRoutePlanner.HorizontalDistance(position, attempt.Point) > 0.9f)
                    return GroundMove(attempt.Point);
                _entranceHeightReady = true;
            }
            // Ground follows local terrain. Reaching the correct horizontal point
            // on the wrong floor does not satisfy the live object's 3D use range.
            return GroundMove(ThresholdTarget(attempt));
        }

        private Vector3 ThresholdTarget(EntranceAcquisition.Attempt attempt)
        {
            if (!_doorCrossLogged) return attempt.Threshold;
            Vector3 direction = attempt.Threshold - attempt.Point; direction.Y = 0;
            Vector3 target = Vector3.Distance(direction, Vector3.Zero) > 0.1f
                ? attempt.Threshold + direction.Normalize() * 0.8f : attempt.Threshold;
            return LocalRoutePlanner.HorizontalDistance(target, _route.Entrance) <= EntranceAcquisition.SearchRadius
                ? target : attempt.Threshold;
        }

        private static bool DoorWithinUseRange(Vector3 position, Door entrance) =>
            LocalRoutePlanner.HorizontalDistance(position, entrance.Position) <= 2 &&
            Vector3.Distance(position, entrance.Position) <= 3.5f && Math.Abs(position.Y - entrance.Position.Y) <= 3.5f;

        private void RetryEntrance(string reason)
        {
            Halt(); _entrance.Finish(reason); _door = Identity.None;
            _entrancePointSet = _entranceHeightReady = false;
            _flightPath.Clear(); _directActive = false;
            // Obstacle memory persists across candidates; phase clocks cannot extend
            // the acquisition clock, which records actual per-attempt minima only.
        }

        private bool Fail(string reason)
        { Halt(); _say($"Entrance interaction result: mission={_route?.Mission.Id.Instance}, door={_door}, result=travel failed; {reason}");
            _say($"Local travel HARD FAILURE: {reason}"); return false; }
    }
}
