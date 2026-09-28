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
        private enum Phase { Idle, GroundTravel, FlightCruise, FlightLanding, Dismount, EntranceApproach, EnterDoor }
        private readonly Action<string> _say;
        private readonly List<Vector3> _recentGround = new List<Vector3>(), _recentFlight = new List<Vector3>();
        private LocalRoute _route;
        private Phase _phase;
        private bool _ownsMovement, _forceDirect, _directActive, _flightDetour, _landingFromDoor;
        private Vector3 _lastPosition, _directStep, _groundTarget, _flightStep, _flightGoal, _loggedTarget;
        private float _bestStepDistance, _bestGroundDistance, _bestFlightDistance, _bestFlightStepDistance;
        private DateTime _stepProgress, _groundProgress, _flightProgress, _flightStepProgress, _lastMotion;
        private DateTime _travelStarted, _phaseAt, _nextMove, _lastUse, _entryStarted, _nextProgressLog, _nextProbeLog;
        private int _groundRecoveries, _flightRecoveries, _preferredSide, _doorAttempts;
        private string _movementKind, _lastEvaluation;
        private Identity _door = Identity.None;
        public TravelMode Mode { get; set; } = TravelMode.Auto;
        public string Status => _phase.ToString();
        public bool IsFlightActive => _phase == Phase.FlightCruise || _phase == Phase.FlightLanding;
        private static bool IsFlying => DynelManager.LocalPlayer.MovementState == MovementState.Fly;
        public bool NeedsReselection => Mode == TravelMode.Auto &&
            ((_phase == Phase.GroundTravel && IsFlying) ||
             (IsFlightActive && !IsFlying && _route != null &&
              LocalRoutePlanner.HorizontalDistance(DynelManager.LocalPlayer.Position, _route.Mission.Entrance) > 16));

        public LocalMissionTravel(Action<string> say) { _say = say; }

        public AcceptedMission SelectBest(IEnumerable<AcceptedMission> missions)
        {
            bool flying = Mode == TravelMode.Flying || (Mode == TravelMode.Auto && IsFlying);
            if ((flying && !IsFlying) || (!flying && IsFlying))
            {
                string reason = flying ? "Equip your flying vehicle before using flying travel." : "Land and dismount before using ground travel.";
                if (_lastEvaluation != reason) _say(reason);
                _lastEvaluation = reason;
                return null;
            }
            var routes = missions.Select(LocalRoutePlanner.Evaluate).ToList();
            string evaluation = string.Join(" | ", routes.Select(route =>
                $"Mission {route.Mission.Id.Instance}: ground={CostText(route.GroundCost)} ({route.GroundReason}), " +
                $"flying={CostText(route.FlyingCost)} ({route.FlyingReason})."));
            if (_lastEvaluation != evaluation)
            {
                _say($"Local travel estimates: playfield={Playfield.ModelIdentity.Instance}, position={DynelManager.LocalPlayer.Position}, " +
                    $"movement={DynelManager.LocalPlayer.MovementState}, outdoor mesh={SMovementController.NavAgent?.HasPathfinder == true}.");
                foreach (LocalRoute route in routes)
                    _say($"Mission {route.Mission.Id.Instance}: ground={CostText(route.GroundCost)} ({route.GroundReason}), " +
                        $"flying={CostText(route.FlyingCost)} ({route.FlyingReason}).");
                _lastEvaluation = evaluation;
            }
            _route = routes.Where(x => !float.IsInfinity(x.Cost(flying)) && !float.IsNaN(x.Cost(flying)))
                .OrderBy(x => x.Cost(flying)).ThenBy(x => x.Mission.Id.Instance).FirstOrDefault();
            if (_route == null) return null;
            SMovementController.Halt();
            SMovementController.SetMovement(MovementAction.FullStop);
            _door = Identity.None;
            _doorAttempts = _groundRecoveries = _flightRecoveries = _preferredSide = 0;
            _entryStarted = DateTime.MinValue;
            _travelStarted = DateTime.UtcNow;
            _forceDirect = !_route.GroundUsesMesh;
            _recentGround.Clear(); _recentFlight.Clear();
            _landingFromDoor = false;
            Begin(flying ? Phase.FlightCruise : Phase.GroundTravel);
            _say($"Selected {_route.Mission.Id.Instance}: {_route.Mission.Name}; entrance={_route.Mission.Entrance}; " +
                $"{(flying ? _route.FlyingReason : _route.GroundReason)}, cost {_route.Cost(flying):F1} m; movement attempts enabled.");
            return _route.Mission;
        }

        private static string CostText(float value) => float.IsInfinity(value) || float.IsNaN(value)
            ? "invalid coordinates/no estimate" : $"{value:F1} m";

        public void Reset()
        {
            Halt(); _route = null; _phase = Phase.Idle; _door = Identity.None;
            _lastEvaluation = null; _directActive = _flightDetour = false;
            _recentGround.Clear(); _recentFlight.Clear();
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
            if (phase == Phase.EntranceApproach && _entryStarted == DateTime.MinValue) _entryStarted = _phaseAt;
            _lastPosition = DynelManager.LocalPlayer.Position;
            _nextMove = _nextProgressLog = _nextProbeLog = DateTime.MinValue;
            _directActive = _flightDetour = false; _movementKind = null;
            _bestGroundDistance = _bestFlightDistance = _bestFlightStepDistance = float.PositiveInfinity;
            _flightStepProgress = _phaseAt;
        }

        public bool Tick(AcceptedMission mission)
        {
            if (_route == null || _route.Mission.Id != mission.Id) return false;
            DateTime now = DateTime.UtcNow;
            if ((_phase == Phase.EntranceApproach || _phase == Phase.EnterDoor) && now - _entryStarted > TimeSpan.FromMinutes(3))
                return Fail("Entrance approach/entry exceeded three minutes; mission handoff stopped.");
            if (now - _travelStarted > TimeSpan.FromMinutes(15))
                return Fail($"Local travel exceeded 15 minutes for mission {mission.Id.Instance}.");
            Vector3 position = DynelManager.LocalPlayer.Position;
            if (!AcceptedMissions.Finite(position)) return Fail("Player world coordinates are invalid.");
            if (Vector3.Distance(position, _lastPosition) >= 0.5f)
            { _lastPosition = position; _lastMotion = now; }
            if (IsFlightActive && !IsFlying && LocalRoutePlanner.HorizontalDistance(position, mission.Entrance) <= 16)
            {
                Begin(Phase.Dismount);
                _say("Flight state cleared near entrance; waiting for ground/falling state before door approach.");
            }
            switch (_phase)
            {
                case Phase.GroundTravel:
                    if (IsFlying) return Fail("Ground travel requires landing/dismount; use /rkm travel auto or flying.");
                    if (LocalRoutePlanner.HorizontalDistance(position, mission.Entrance) <= 14)
                    {
                        Begin(Phase.EntranceApproach);
                        _say("Near mission entrance; resolving live door and precise approach height.");
                        return true;
                    }
                    return GroundMove(LocalRoutePlanner.ResolveEntranceHeight(mission.Entrance, position, out _));
                case Phase.FlightCruise:
                    // Direct elevated travel, without a mandatory vertical climb or corridor certificate.
                    if (LocalRoutePlanner.HorizontalDistance(position, mission.Entrance) <= 16)
                    { PrepareLanding(mission); return true; }
                    return FlyMove(_route.CruiseEnd);
                case Phase.FlightLanding:
                    // A door may become visible only after descent starts. Upgrade a terrain
                    // or provisional height once to its live approach, rather than landing on
                    // a roof solely because it was the first local terrain hit.
                    if (!_landingFromDoor && LocalRoutePlanner.HorizontalDistance(position, mission.Entrance) <= 8 &&
                        ResolveDoor(mission) != null)
                    { PrepareLanding(mission); return true; }
                    Vector3 touchdown = _route.Landing + new Vector3(0, 1.5f, 0);
                    if (LocalRoutePlanner.HorizontalDistance(position, touchdown) <= 2 && Math.Abs(position.Y - touchdown.Y) <= 1.5f)
                    {
                        Begin(Phase.Dismount);
                        _say(_route.LandingVerified
                            ? "At the final flight approach. Dismount your vehicle; precise ground/door approach resumes automatically."
                            : "At a provisional final flight approach; height is unresolved. Land/dismount here, then precise door approach resumes automatically.");
                        return true;
                    }
                    return FlyMove(touchdown);
                case Phase.Dismount:
                    Halt();
                    if (now - _phaseAt > TimeSpan.FromMinutes(2))
                        return Fail("Dismount wait exceeded two minutes; /rkm start after landing/dismounting.");
                    if (!IsFlying && !DynelManager.LocalPlayer.IsFalling)
                    {
                        _forceDirect = false; Begin(Phase.EntranceApproach);
                        _say("Ground movement confirmed; approaching the mission door.");
                    }
                    return true;
                case Phase.EntranceApproach: return ApproachDoor(mission);
                case Phase.EnterDoor: return EnterDoor(mission);
                default: return false;
            }
        }

        private void PrepareLanding(AcceptedMission mission)
        {
            Vector3 position = DynelManager.LocalPlayer.Position;
            Door entrance = ResolveDoor(mission);
            Vector3 anchor = entrance?.Position ?? LocalRoutePlanner.ResolveEntranceHeight(mission.Entrance, position, out _);
            Vector3 landing = LocalRoutePlanner.OutsideEntrance(anchor, position, 4);
            bool terrain = LocalRoutePlanner.TrySurface(landing, position.Y, out Vector3 surface);
            bool knownHeight = entrance != null || terrain || !LocalRoutePlanner.HeightMissing(mission.Entrance);
            // Use live door height if a local ray hit a roof. Missing height permits a small
            // provisional descent and user landing rather than a blind descent to map Y=0.
            if (terrain && (entrance == null || Math.Abs(surface.Y - anchor.Y) <= 5)) landing.Y = surface.Y;
            else if (entrance != null) landing.Y = entrance.Position.Y;
            else if (!knownHeight) landing.Y = position.Y - 4;
            _route.Landing = landing; _route.LandingVerified = entrance != null || terrain;
            _landingFromDoor = entrance != null;
            Begin(Phase.FlightLanding);
            _say($"Flight descent/final approach: target={landing + new Vector3(0, 1.5f, 0)}, " +
                $"height={(entrance != null ? "live door" : terrain ? "local terrain hint" : knownHeight ? "accepted coordinate, provisional" : "short provisional descent")}; " +
                $"position={position}; entrance={mission.Entrance}. Clearance probes are advisory.");
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
            if (Vector3.Distance(destination, _flightGoal) > 1)
            {
                _flightGoal = destination; _bestFlightDistance = _bestFlightStepDistance = float.PositiveInfinity;
                _flightProgress = _flightStepProgress = now; _flightDetour = false;
            }
            float remaining = Vector3.Distance(position, destination);
            if (remaining + 0.5f < _bestFlightDistance) { _bestFlightDistance = remaining; _flightProgress = now; }
            if (now - _flightProgress > TimeSpan.FromSeconds(90))
                return Fail($"Flight made no improvement toward {_phase} target for 90 seconds; target={destination}, " +
                    $"remaining={remaining:F1} m, best={_bestFlightDistance:F1} m, position={position}, recoveries={_flightRecoveries}.");
            LogProgress("Flight", position, destination, remaining, _flightProgress);
            if (_flightDetour && Vector3.Distance(position, _flightStep) <= 2)
            {
                Halt(); Remember(_recentFlight, position); _flightDetour = false;
                _bestFlightStepDistance = float.PositiveInfinity; _flightStepProgress = now;
            }
            Vector3 target = _flightDetour ? _flightStep : destination;
            float stepDistance = Vector3.Distance(position, target);
            if (stepDistance + 0.4f < _bestFlightStepDistance) { _bestFlightStepDistance = stepDistance; _flightStepProgress = now; }
            if (now - _flightStepProgress > TimeSpan.FromSeconds(8))
            {
                Remember(_recentFlight, target); Halt();
                _flightStep = LocalRoutePlanner.FlightRecoveryStep(position, destination, _route.CruiseEnd.Y + 40,
                    ++_flightRecoveries, _recentFlight);
                _flightDetour = true; target = _flightStep;
                _bestFlightStepDistance = Vector3.Distance(position, target); _flightStepProgress = now;
                _say($"Flight obstacle recovery {_flightRecoveries}: no waypoint progress for 8 seconds; " +
                    $"alternate target={target}, final target={destination}; no-progress clock continues.");
            }
            LogMovement(_flightDetour ? "Flight recovery" : _phase == Phase.FlightLanding ? "Flight descent/final approach" : "Direct elevated flight", target, destination);
            LogProbe(position, target, "Flight");
            Vector3 direction = target - position;
            // Ordinary waypoint arrival uses X/Z. Flight needs explicit altitude-aware steering
            // and a nondegenerate LookRotation basis for zero/vertical directions.
            if (Vector3.Distance(position, target) < 0.1f) { Halt(); return true; }
            Vector3 up = LocalRoutePlanner.HorizontalDistance(position, target) < 0.1f ? new Vector3(0, 0, 1) : Vector3.Up;
            DynelManager.LocalPlayer.Rotation = Quaternion.LookRotation(direction, up);
            SMovementController.SetMovement(MovementAction.ForwardStart);
            SMovementController.SetMovement(MovementAction.Update);
            _ownsMovement = true;
            return true;
        }

        private Door ResolveDoor(AcceptedMission mission)
        {
            // Map Y can be zero/stale. Match the horizontal neighborhood; live door height
            // governs entry. Preserve unique-door identity and exact dungeon handoff.
            var doors = Playfield.Doors.Where(x => AcceptedMissions.Finite(x.Position) &&
                    LocalRoutePlanner.HorizontalDistance(x.Position, mission.Entrance) <= 6)
                .OrderBy(x => LocalRoutePlanner.HorizontalDistance(x.Position, mission.Entrance)).ToList();
            if (doors.Count == 0) return null;
            if (_door != Identity.None) return doors.FirstOrDefault(x => x.Identity == _door);
            if (doors.Count > 1 && LocalRoutePlanner.HorizontalDistance(doors[1].Position, mission.Entrance) -
                LocalRoutePlanner.HorizontalDistance(doors[0].Position, mission.Entrance) < 1) return null;
            _door = doors[0].Identity;
            _say($"Mission {mission.Id.Instance}: entrance door {_door}, precise point={doors[0].Position}, accepted point={mission.Entrance}.");
            return doors[0];
        }

        private bool ApproachDoor(AcceptedMission mission)
        {
            if (IsFlying) return Fail("Dismount before the precise entrance approach.");
            if (DynelManager.LocalPlayer.IsFalling) { Halt(); return true; }
            Door entrance = ResolveDoor(mission);
            if (entrance == null)
            {
                if (DateTime.UtcNow - _phaseAt > TimeSpan.FromSeconds(45))
                    return Fail("No unique mission door within 6 horizontal metres of accepted coordinates after 45 seconds; entry withheld.");
                if (LocalRoutePlanner.HorizontalDistance(DynelManager.LocalPlayer.Position, mission.Entrance) > 2)
                    return GroundMove(LocalRoutePlanner.ResolveEntranceHeight(mission.Entrance, DynelManager.LocalPlayer.Position, out _));
                Halt(); return true;
            }
            Vector3 position = DynelManager.LocalPlayer.Position;
            if (Vector3.Distance(position, entrance.Position) <= 3 && Math.Abs(position.Y - entrance.Position.Y) <= 2.5f)
            {
                Begin(Phase.EnterDoor); _lastUse = DateTime.MinValue;
                _say($"Precise entrance approach reached at {position}; interacting with {_door}.");
                return true;
            }
            // If aligned horizontally but on another floor, step outside before approaching
            // again; the overall entry deadline still bounds a mismatched elevation.
            Vector3 target = LocalRoutePlanner.HorizontalDistance(position, entrance.Position) <= 0.7f &&
                Math.Abs(position.Y - entrance.Position.Y) > 2.5f
                ? LocalRoutePlanner.OutsideEntrance(entrance.Position, position, 4) : entrance.Position;
            return GroundMove(target);
        }

        private bool EnterDoor(AcceptedMission mission)
        {
            if (IsFlying) return Fail("Flying state returned before entrance entry; dismount and restart.");
            Door entrance = ResolveDoor(mission);
            if (entrance == null) return Fail("Selected entrance door disappeared before zoning.");
            Vector3 position = DynelManager.LocalPlayer.Position;
            if (DynelManager.LocalPlayer.IsFalling || Vector3.Distance(position, entrance.Position) > 3.5f || Math.Abs(position.Y - entrance.Position.Y) > 2.5f)
            { Begin(Phase.EntranceApproach); return true; }
            if (DateTime.UtcNow - _phaseAt > TimeSpan.FromSeconds(20))
                return Fail("Mission door did not produce a verified dungeon transition after three use attempts.");
            if (_doorAttempts < 3 && DateTime.UtcNow - _lastUse >= TimeSpan.FromSeconds(4))
            {
                Halt(); entrance.Use(); _lastUse = DateTime.UtcNow; _doorAttempts++;
                _say($"Using entrance {_door}, attempt {_doorAttempts}/3; awaiting exact mission/dungeon verification.");
            }
            return true;
        }

        private bool Fail(string reason)
        { Halt(); _say($"Local travel HARD FAILURE: {reason}"); return false; }
    }
}
