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
        private enum Phase { Idle, GroundTravel, FlightCruise, EntranceHeight, EntranceApproach, EnterDoor }
        private readonly Action<string> _say;
        private readonly List<Vector3> _recentGround = new List<Vector3>();
        private LocalRoute _route;
        private Phase _phase;
        private bool _ownsMovement, _forceDirect, _directActive, _approachFromDoor, _entryProbeLogged;
        private bool _flightHolding, _entranceHeightReady, _flightPathComplete;
        private readonly List<Vector3> _flightPath = new List<Vector3>();
        private readonly List<LocalRoutePlanner.FlightBlockedLeg> _blockedFlightLegs = new List<LocalRoutePlanner.FlightBlockedLeg>();
        private Vector3 _lastPosition, _directStep, _groundTarget, _flightGoal, _loggedTarget, _heightAnchor;
        private float _bestStepDistance, _bestGroundDistance, _bestFlightDistance, _bestFlightStepDistance;
        private float _entryHeight;
        private DateTime _stepProgress, _groundProgress, _flightProgress, _flightStepProgress, _lastMotion;
        private DateTime _travelStarted, _phaseAt, _nextMove, _lastUse, _entryStarted, _nextProgressLog, _nextProbeLog;
        private DateTime _nextHeightCheck, _nextFlightProbe, _lastFlightSteer, _nextFlightPlan, _flightBlockedAt;
        private int _groundRecoveries, _flightPlans, _preferredSide, _doorAttempts;
        private int _flightPathIndex;
        private int _flightSearchRetries;
        private string _movementKind, _lastEvaluation;
        private Identity _door = Identity.None;
        public TravelMode Mode { get; set; } = TravelMode.Auto;
        public string Status => _phase.ToString();
        public bool IsFlightActive => _phase == Phase.FlightCruise || _phase == Phase.EntranceHeight ||
            (_phase == Phase.EntranceApproach && _route?.Flying == true && IsFlying);
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
            // Compare distances only, using one captured origin. Other missions receive no
            // navmesh query, terrain probe or flight plan. Keep the chosen identity/point.
            AcceptedMission nearest = missions.Where(x => AcceptedMissions.Finite(x.Entrance))
                .OrderBy(x => LocalRoutePlanner.HorizontalDistance(origin, x.Entrance))
                .ThenBy(x => x.Id.Instance).FirstOrDefault();
            if (nearest == null) return null;
            _route = LocalRoutePlanner.Plan(nearest, origin, flying);
            if (_route == null) return null;
            SMovementController.Halt();
            SMovementController.SetMovement(MovementAction.FullStop);
            _door = Identity.None;
            _doorAttempts = _groundRecoveries = _flightPlans = _preferredSide = 0;
            _entryStarted = DateTime.MinValue;
            _travelStarted = DateTime.UtcNow;
            _forceDirect = !_route.GroundUsesMesh;
            _recentGround.Clear();
            _blockedFlightLegs.Clear(); _flightSearchRetries = 0;
            _approachFromDoor = false;
            _entranceHeightReady = false;
            Begin(flying ? Phase.FlightCruise : Phase.GroundTravel);
            _say($"Nearest entrance selected: {_route.Mission.Id.Instance}, {_route.Mission.Name}; origin={_route.Origin}, " +
                $"entrance={_route.Entrance}, estimated distance={_route.EntranceDistance:F1} m. " +
                $"Single {(flying ? "flying" : "ground")} route: {_route.Reason}, path cost={_route.Cost:F1} m; " +
                $"movement={DynelManager.LocalPlayer.MovementState}, outdoor mesh={SMovementController.NavAgent?.HasPathfinder == true}, " +
                $"entrance height={_route.EntrancePoint.Y:F2} ({_route.HeightSource}).");
            return _route.Mission;
        }

        public void Reset()
        {
            Halt(); _route = null; _phase = Phase.Idle; _door = Identity.None;
            _lastEvaluation = null; _directActive = _flightHolding = _entranceHeightReady = false;
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
            _phaseAt = _groundProgress = _flightProgress = _lastMotion = DateTime.UtcNow;
            if ((phase == Phase.EntranceHeight || phase == Phase.EntranceApproach) && _entryStarted == DateTime.MinValue)
                _entryStarted = _phaseAt;
            _lastPosition = DynelManager.LocalPlayer.Position;
            _nextMove = _nextProgressLog = _nextProbeLog = DateTime.MinValue;
            _nextHeightCheck = _nextFlightProbe = DateTime.MinValue;
            _directActive = _flightHolding = false; _movementKind = null;
            _flightPath.Clear(); _flightPathIndex = 0;
            _nextFlightPlan = _flightBlockedAt = DateTime.MinValue; _lastFlightSteer = _phaseAt;
            _entryProbeLogged = false;
            _bestGroundDistance = _bestFlightDistance = _bestFlightStepDistance = float.PositiveInfinity;
            _flightStepProgress = _phaseAt;
        }

        public bool Tick(AcceptedMission mission)
        {
            if (_route == null || _route.Mission.Id != mission.Id) return false;
            DateTime now = DateTime.UtcNow;
            if ((_phase == Phase.EntranceHeight || _phase == Phase.EntranceApproach || _phase == Phase.EnterDoor) &&
                now - _entryStarted > TimeSpan.FromMinutes(3))
                return Fail("Entrance approach/entry exceeded three minutes; mission handoff stopped.");
            if (now - _travelStarted > TimeSpan.FromMinutes(15))
                return Fail($"Local travel exceeded 15 minutes for mission {mission.Id.Instance}.");
            Vector3 position = DynelManager.LocalPlayer.Position;
            if (!AcceptedMissions.Finite(position)) return Fail("Player world coordinates are invalid.");
            if (Vector3.Distance(position, _lastPosition) >= 0.5f)
            { _lastPosition = position; _lastMotion = now; }
            if ((_phase == Phase.FlightCruise || _phase == Phase.EntranceHeight) && !IsFlying &&
                LocalRoutePlanner.HorizontalDistance(position, _route.Entrance) <= 16)
            {
                _forceDirect = false;
                Begin(Phase.EntranceApproach);
                _say("Flight state cleared near the chosen entrance; continuing this mission's approach without reselection.");
            }
            switch (_phase)
            {
                case Phase.GroundTravel:
                    if (IsFlying) return Fail("Ground travel requires landing/dismount; use /rkm travel auto or flying.");
                    if (LocalRoutePlanner.HorizontalDistance(position, _route.Entrance) <= 14)
                    {
                        Begin(Phase.EntranceApproach);
                        _say("Near mission entrance; resolving live door and precise approach height.");
                        return true;
                    }
                    return GroundMove(_route.EntrancePoint);
                case Phase.FlightCruise:
                {
                    Door travelDoor = ResolveDoor(mission);
                    // A distant zero-height entrance starts provisional. Refine the
                    // cruise clearance as local data loads, before trying to fly into
                    // an endpoint below the actual ground/building height.
                    RefreshEntranceHeight(travelDoor);
                    Vector3 point = travelDoor?.Position ?? _route.EntrancePoint;
                    Vector3 near = LocalRoutePlanner.OutsideEntrance(point, _route.Origin, 1.5f);
                    _route.CruiseEnd.X = near.X; _route.CruiseEnd.Z = near.Z;
                    _route.CruiseEnd.Y = Math.Max(_route.CruiseEnd.Y, point.Y + 12);
                    if (LocalRoutePlanner.HorizontalDistance(position, point) <= 2)
                    { BeginEntranceHeight(mission, travelDoor); return true; }
                    return FlyMove(_route.CruiseEnd);
                }
                case Phase.EntranceHeight: return AlignEntranceHeight(mission);
                case Phase.EntranceApproach: return ApproachDoor(mission);
                case Phase.EnterDoor: return EnterDoor(mission);
                default: return false;
            }
        }

        private void BeginEntranceHeight(AcceptedMission mission, Door entrance)
        {
            Vector3 position = DynelManager.LocalPlayer.Position;
            Begin(Phase.EntranceHeight);
            _entranceHeightReady = false;
            RefreshEntranceHeight(entrance, true);
            _entryHeight = _route.EntrancePoint.Y + 1;
            _heightAnchor = position;
            if (LocalRoutePlanner.HorizontalDistance(position, _route.EntrancePoint) < 1)
                _heightAnchor = LocalRoutePlanner.OutsideEntrance(_route.EntrancePoint, _route.Origin, 1.5f);
            _heightAnchor.Y = _entryHeight;
            _say($"Entrance height selected within 2 m: mission={mission.Id.Instance}, floor={_route.EntrancePoint.Y:F2}, " +
                $"source={_route.HeightSource}, alignment target={_heightAnchor}; align height first, then enter in vehicle.");
        }

        private bool AlignEntranceHeight(AcceptedMission mission)
        {
            Door entrance = ResolveDoor(mission);
            RefreshEntranceHeight(entrance);
            _entryHeight = _route.EntrancePoint.Y + 1;
            if (LocalRoutePlanner.HorizontalDistance(_heightAnchor, _route.EntrancePoint) > 2)
                _heightAnchor = LocalRoutePlanner.OutsideEntrance(_route.EntrancePoint, _route.Origin, 1.5f);
            _heightAnchor.Y = _entryHeight;
            Vector3 position = DynelManager.LocalPlayer.Position;
            if (LocalRoutePlanner.HorizontalDistance(position, _route.EntrancePoint) <= 2 &&
                Math.Abs(position.Y - _entryHeight) <= 0.75f &&
                LocalRoutePlanner.ClearSegment(LocalRoutePlanner.Toward(position, _heightAnchor, 0.3f), _heightAnchor))
            {
                _entranceHeightReady = true;
                Begin(Phase.EntranceApproach);
                _say($"Entrance height aligned: position={position}, floor={_route.EntrancePoint.Y:F2}, " +
                    $"entry height={_entryHeight:F2}; proceeding to the chosen entrance in vehicle.");
                return true;
            }
            return FlyMove(_heightAnchor);
        }

        private void RefreshEntranceHeight(Door door = null, bool force = false)
        {
            DateTime now = DateTime.UtcNow;
            Vector3 position = DynelManager.LocalPlayer.Position;
            if (LocalRoutePlanner.HorizontalDistance(position, _route.Entrance) > 24 ||
                (!force && now < _nextHeightCheck && door == null)) return;
            Vector3 point = _route.EntrancePoint;
            string source;
            if (door != null)
            {
                point = door.Position;
                source = "live door";
                _approachFromDoor = true;
            }
            else
            {
                if (_approachFromDoor || (!force && now < _nextHeightCheck)) return;
                _nextHeightCheck = now.AddSeconds(2);
                if (!LocalRoutePlanner.TryEntranceSurface(_route.Entrance, position.Y, out float height, out int support)) return;
                point.Y = height;
                source = $"local surface consensus ({support}/17 columns)";
            }
            _nextHeightCheck = now.AddSeconds(2);
            if (_route.EntranceHeightVerified && Math.Abs(point.Y - _route.EntrancePoint.Y) <= 0.5f &&
                LocalRoutePlanner.HorizontalDistance(point, _route.EntrancePoint) <= 0.5f &&
                (_route.HeightSource == source || (source.StartsWith("local surface") && _route.HeightSource.StartsWith("local surface")))) return;
            float previous = _route.EntrancePoint.Y;
            _route.EntrancePoint = point;
            _route.EntranceHeightVerified = true;
            _route.HeightSource = source;
            _say($"Entrance height refined: mission={_route.Mission.Id.Instance}, floor={previous:F2} -> {point.Y:F2}, " +
                $"source={source}, entrance point={point}; entry target={point + new Vector3(0, 1, 0)}.");
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
                return Fail($"Ground made no improvement toward entrance for 90 seconds; target={destination}, " +
                    $"remaining={distance:F1} m, best={_bestGroundDistance:F1} m, position={position}, recoveries={_groundRecoveries}.");
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
            _say($"Active movement: {kind}; target={activeTarget}; entrance/final target={finalTarget}.");
        }

        private void LogProgress(string kind, Vector3 position, Vector3 target, float distance, DateTime progress)
        {
            if (DateTime.UtcNow < _nextProgressLog) return;
            _nextProgressLog = DateTime.UtcNow.AddSeconds(5);
            _say($"{kind} progress: phase={_phase}, position={position}, final target={target}, remaining={distance:F1} m, " +
                (kind == "Flight" ? $"vertical gap={position.Y - target.Y:F1} m, " : "") +
                $"no improvement for {(DateTime.UtcNow - progress).TotalSeconds:F0}/90 s.");
        }

        private void LogProbe(Vector3 position, Vector3 target, string kind)
        {
            if (DateTime.UtcNow < _nextProbeLog) return;
            _nextProbeLog = DateTime.UtcNow.AddSeconds(8);
            if (!LocalRoutePlanner.ClearSegment(position, target))
                _say($"{kind} obstacle hint: probe hit toward {target}; continuing observed movement, resampling on a stall.");
        }

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
                return Fail($"Flight made no improvement toward {_phase} target for 90 seconds; target={destination}, " +
                    $"remaining={remaining:F1} m, position={position}, committed plans={_flightPlans}.");
            LogProgress("Flight", position, destination, remaining, _flightProgress);
            float bodyRadius = DynelManager.LocalPlayer.Radius;
            if (float.IsNaN(bodyRadius) || float.IsInfinity(bodyRadius)) bodyRadius = 0.6f;
            float radius = Math.Max(0.6f, Math.Min(1.2f, bodyRadius + 0.25f));
            float velocity = DynelManager.LocalPlayer.Velocity;
            if (float.IsNaN(velocity) || float.IsInfinity(velocity)) velocity = 0;
            if (_flightPath.Count == 0)
            {
                if (now >= _nextFlightPlan) CommitFlightPath(position, ref destination, radius, "initial/changed destination or expanded search retry");
                // A blocked edge is not a usable one-leg estimate. Keep the selected
                // mission, stop and retry the search under the original deadline.
                if (_flightPath.Count == 0) { Halt(); return true; }
                remaining = Vector3.Distance(position, destination);
            }
            float arrival = Math.Max(1.2f, Math.Min(2.5f, velocity * 0.12f));
            if (!_flightPathComplete && _flightPathIndex == _flightPath.Count - 1 &&
                Vector3.Distance(position, _flightPath[_flightPathIndex]) <= arrival &&
                LocalRoutePlanner.ClearSegment(LocalRoutePlanner.Toward(position, _flightPath[_flightPathIndex], 0.3f), _flightPath[_flightPathIndex]))
            {
                // Successful prefix arrival is a continuation, not an obstruction
                // or permission to treat its endpoint as the actual mission entrance.
                _flightSearchRetries = 0;
                CommitFlightPath(position, ref destination, radius, "validated prefix reached; continue from new position");
                if (_flightPath.Count == 0) { Halt(); return true; }
                remaining = Vector3.Distance(position, destination);
            }
            while (_flightPathIndex < _flightPath.Count - 1 &&
                Vector3.Distance(position, _flightPath[_flightPathIndex]) <= arrival)
            {
                Vector3 next = _flightPath[_flightPathIndex + 1];
                Vector3 nextEnd = _flightPathComplete && _phase == Phase.EntranceApproach && _flightPathIndex + 1 == _flightPath.Count - 1
                    ? LocalRoutePlanner.Toward(position, next, Math.Max(0, Vector3.Distance(position, next) - 1)) : next;
                // Do not cut a planned corner early through a roof/wall. Continue
                // toward this waypoint until the next leg is clear from the real position.
                if (!LocalRoutePlanner.ClearSegment(LocalRoutePlanner.Toward(position, nextEnd, 0.3f), nextEnd)) break;
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
                _flightHolding = probeLength > 0.5f && !LocalRoutePlanner.ClearSegment(start, end);
                if (!_flightHolding) _flightBlockedAt = DateTime.MinValue;
                else if (_flightBlockedAt == DateTime.MinValue) _flightBlockedAt = now;
            }
            bool obstructed = _flightHolding && now - _flightBlockedAt >= TimeSpan.FromSeconds(1);
            if ((stalled || obstructed) && now >= _nextFlightPlan)
            {
                RememberBlockedFlightLeg(position, target);
                CommitFlightPath(position, ref destination, radius, stalled ? "8 seconds without waypoint progress" : "sustained nearby surface obstruction");
                if (_flightPath.Count == 0) { Halt(); return true; }
                remaining = Vector3.Distance(position, destination);
                target = _flightPath[0];
                stepDistance = Vector3.Distance(position, target);
                float probeLength = Math.Min(2, stepDistance);
                if (_flightPathComplete && _phase == Phase.EntranceApproach && _flightPath.Count == 1 && stepDistance <= probeLength + 1)
                    probeLength = Math.Max(0, probeLength - 1);
                Vector3 end = LocalRoutePlanner.Toward(position, target, probeLength);
                _flightHolding = !LocalRoutePlanner.ClearSegment(LocalRoutePlanner.Toward(position, end, 0.3f), end);
                _flightBlockedAt = _flightHolding ? now : DateTime.MinValue;
            }
            if (_flightHolding) { Halt(); return true; }
            string purpose = _phase == Phase.EntranceHeight
                ? target.Y < position.Y - 1 ? "lowering beside entrance" :
                    LocalRoutePlanner.HorizontalDistance(target, _route.EntrancePoint) > 2
                        ? "outside alignment for descent" : "entrance height alignment"
                : _phase == Phase.EntranceApproach ? "entrance approach in vehicle" : "travel to within 2 m";
            LogMovement($"Flight committed {(_flightPathComplete ? "path" : "prefix")} leg {_flightPathIndex + 1}/{_flightPath.Count}: " +
                purpose, target, destination);
            if (stepDistance < 0.1f) { Halt(); return true; }
            Vector3 wanted = (target - position).Normalize();
            Vector3 current = DynelManager.LocalPlayer.Rotation.Forward;
            if (!AcceptedMissions.Finite(current) || Vector3.Distance(current, Vector3.Zero) < 0.1f) current = wanted;
            else current = current.Normalize();
            double angle = Math.Acos(Math.Max(-1, Math.Min(1, Vector3.Dot(current, wanted))));
            double elapsed = Math.Max(0.02, Math.Min(0.2, (now - _lastFlightSteer).TotalSeconds));
            _lastFlightSteer = now;
            Vector3 steering = angle > Math.PI / 4 ? wanted :
                SmoothFlightDirection(current, wanted, angle, elapsed * Math.PI * 2 / 3);
            // Moving turns stay continuous; face the committed leg directly if a
            // smoothed heading would cut a corner into known geometry.
            float turnProbe = Math.Min(stepDistance, 2);
            if (angle > Math.PI / 180 && !LocalRoutePlanner.ClearSegment(position + steering * 0.3f,
                position + steering * turnProbe)) steering = wanted;
            Vector3 up = Math.Abs(steering.X) + Math.Abs(steering.Z) < 0.05f ? new Vector3(0, 0, 1) : Vector3.Up;
            DynelManager.LocalPlayer.Rotation = Quaternion.LookRotation(steering, up);
            SMovementController.SetMovement(MovementAction.ForwardStart);
            SMovementController.SetMovement(MovementAction.Update);
            _ownsMovement = true;
            return true;
        }

        private void RememberBlockedFlightLeg(Vector3 position, Vector3 target)
        {
            if (Vector3.Distance(position, target) < 0.75f) return;
            Vector3 end = LocalRoutePlanner.Toward(position, target, 4);
            if (_blockedFlightLegs.Any(x => Vector3.Distance(x.From, position) < 1 && Vector3.Distance(x.To, end) < 1)) return;
            _blockedFlightLegs.Add(new LocalRoutePlanner.FlightBlockedLeg { From = position, To = end });
            if (_blockedFlightLegs.Count > 16) _blockedFlightLegs.RemoveAt(0);
            _say($"Flight blocked leg recorded: {position} -> {end}; searching around/above the obstruction, retaining this entrance.");
        }

        private void CommitFlightPath(Vector3 position, ref Vector3 destination, float radius, string trigger)
        {
            float ceiling = Math.Max(_route.CruiseEnd.Y, _route.EntrancePoint.Y + 12) + 40;
            float extent = Math.Min(80, 32 + _flightSearchRetries * 16);
            Vector3? entranceCentre = _phase == Phase.EntranceHeight ? _route.EntrancePoint + new Vector3(0, 1, 0) : (Vector3?)null;
            List<Vector3> path = LocalRoutePlanner.PlanFlightPath(position, destination, ceiling, radius,
                _blockedFlightLegs, extent, entranceCentre, _phase == Phase.EntranceApproach, out _flightPathComplete, out string reason);
            _flightPath.Clear(); _flightPath.AddRange(path); _flightPathIndex = 0;
            _bestFlightStepDistance = float.PositiveInfinity; _flightStepProgress = DateTime.UtcNow;
            _nextFlightPlan = DateTime.UtcNow.AddSeconds(3); _nextFlightProbe = DateTime.MinValue;
            _flightHolding = false; _flightBlockedAt = DateTime.MinValue;
            if (path.Count == 0)
            {
                _flightSearchRetries++;
                _say($"Flight obstacle search waiting: {trigger}; {reason}; final target={destination}; progress deadline retained.");
                return;
            }
            if (_flightPathComplete && Vector3.Distance(path[path.Count - 1], destination) > 0.1f && _phase == Phase.EntranceHeight)
            {
                _heightAnchor = destination = path[path.Count - 1];
                _flightGoal = destination;
                _bestFlightDistance = Vector3.Distance(position, destination);
                _say($"Entrance alignment side changed: target={destination}; same entrance={_route.EntrancePoint}, selected height retained.");
            }
            // Expand following failed execution too, without resetting observed progress.
            _flightSearchRetries++;
            _say($"Flight path committed {++_flightPlans}: {trigger}; {reason}; " +
                $"waypoints={string.Join(" -> ", path)}; final target={destination}; complete={_flightPathComplete}; progress deadline retained.");
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

        private Door ResolveDoor(AcceptedMission mission)
        {
            // Map Y can be zero/stale. Match the horizontal neighborhood; live door height
            // governs entry. Preserve unique-door identity and exact dungeon handoff.
            var doors = Playfield.Doors.Where(x => AcceptedMissions.Finite(x.Position) &&
                    LocalRoutePlanner.HorizontalDistance(x.Position, _route.Entrance) <= 6)
                .OrderBy(x => LocalRoutePlanner.HorizontalDistance(x.Position, _route.Entrance)).ToList();
            if (doors.Count == 0) return null;
            if (_door != Identity.None) return doors.FirstOrDefault(x => x.Identity == _door);
            if (doors.Count > 1 && LocalRoutePlanner.HorizontalDistance(doors[1].Position, _route.Entrance) -
                LocalRoutePlanner.HorizontalDistance(doors[0].Position, _route.Entrance) < 1) return null;
            _door = doors[0].Identity;
            _say($"Mission {mission.Id.Instance}: entrance door {_door}, precise point={doors[0].Position}, chosen point={_route.Entrance}.");
            return doors[0];
        }

        private bool ApproachDoor(AcceptedMission mission)
        {
            if (IsFlying && !_route.Flying) return Fail("Ground route changed to flying state; select /rkm travel auto or flying to restart from this origin.");
            if (!IsFlying && DynelManager.LocalPlayer.IsFalling) { Halt(); return true; }
            Door entrance = ResolveDoor(mission);
            RefreshEntranceHeight(entrance);
            Vector3 position = DynelManager.LocalPlayer.Position;
            if (entrance == null && DateTime.UtcNow - _phaseAt > TimeSpan.FromSeconds(45))
                return Fail("No unique mission door within 6 horizontal metres of accepted coordinates after 45 seconds; entry withheld.");
            if (IsFlying && (!_entranceHeightReady || Math.Abs(position.Y - (_route.EntrancePoint.Y + 1)) > 0.9f))
            {
                _entranceHeightReady = false;
                if (LocalRoutePlanner.HorizontalDistance(position, _route.EntrancePoint) <= 2)
                { BeginEntranceHeight(mission, entrance); return true; }
                Vector3 near = LocalRoutePlanner.OutsideEntrance(_route.EntrancePoint, _route.Origin, 1.5f);
                near.Y = Math.Max(position.Y, _route.EntrancePoint.Y + 1);
                return FlyMove(near);
            }
            if (entrance == null)
            {
                // Allow proximity entry even if the client exposes no live Door yet.
                // Continue to the selected entrance itself, rather than stopping 2 m short
                // or handing a flight route to ground detours. Exact dungeon verification
                // remains the coordinator's gate after any resulting zone transition.
                Vector3 target = _route.EntrancePoint + (IsFlying ? new Vector3(0, 1, 0) : Vector3.Zero);
                if (!_entryProbeLogged)
                {
                    _entryProbeLogged = true;
                    _say($"No unique live door yet; approaching chosen entrance trigger point {target} " +
                        $"{(IsFlying ? "in vehicle" : "on ground")}; awaiting door visibility or zoning.");
                }
                if (IsFlying && Vector3.Distance(DynelManager.LocalPlayer.Position, target) > 0.7f)
                    return FlyMove(target);
                if (!IsFlying && LocalRoutePlanner.HorizontalDistance(DynelManager.LocalPlayer.Position, target) > 0.7f)
                    return GroundMove(target);
                Halt(); return true;
            }
            if (LocalRoutePlanner.HorizontalDistance(position, entrance.Position) <= 2 &&
                Vector3.Distance(position, entrance.Position) <= 3 && Math.Abs(position.Y - entrance.Position.Y) <= 2.5f)
            {
                Begin(Phase.EnterDoor); _lastUse = DateTime.MinValue;
                _say($"Precise entrance approach reached at {position}; interacting with {_door} " +
                    $"{(IsFlying ? "in vehicle" : "on ground")}.");
                return true;
            }
            if (IsFlying) return FlyMove(entrance.Position + new Vector3(0, 1, 0));
            // A grounded character aligned horizontally on another floor must step outside
            // before approaching again; the overall entry deadline still bounds the attempt.
            Vector3 groundTarget = LocalRoutePlanner.HorizontalDistance(position, entrance.Position) <= 0.7f &&
                Math.Abs(position.Y - entrance.Position.Y) > 2.5f
                ? LocalRoutePlanner.OutsideEntrance(entrance.Position, position, 4) : entrance.Position;
            return GroundMove(groundTarget);
        }

        private bool EnterDoor(AcceptedMission mission)
        {
            if (IsFlying && !_route.Flying) return Fail("Ground route changed to flying state before entry; restart with the appropriate travel mode.");
            Door entrance = ResolveDoor(mission);
            if (entrance == null) return Fail("Selected entrance door disappeared before zoning.");
            Vector3 position = DynelManager.LocalPlayer.Position;
            if ((!IsFlying && DynelManager.LocalPlayer.IsFalling) || Vector3.Distance(position, entrance.Position) > 3.5f ||
                Math.Abs(position.Y - entrance.Position.Y) > 2.5f ||
                (IsFlying && (!_entranceHeightReady || Math.Abs(position.Y - (entrance.Position.Y + 1)) > 0.9f)))
            { Begin(Phase.EntranceApproach); return true; }
            if (DateTime.UtcNow - _phaseAt > TimeSpan.FromSeconds(20))
                return Fail("Mission door did not produce a verified dungeon transition after three use attempts.");
            if (_doorAttempts < 3 && DateTime.UtcNow - _lastUse >= TimeSpan.FromSeconds(4))
            {
                Halt(); entrance.Use(); _lastUse = DateTime.UtcNow; _doorAttempts++;
                _say($"Using entrance {_door} {(IsFlying ? "in vehicle" : "on ground")}, attempt {_doorAttempts}/3; awaiting exact mission/dungeon verification.");
            }
            return true;
        }

        private bool Fail(string reason)
        { Halt(); _say($"Local travel HARD FAILURE: {reason}"); return false; }
    }
}
