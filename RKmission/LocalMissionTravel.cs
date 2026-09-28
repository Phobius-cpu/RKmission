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
        private enum Phase { Idle, GroundTravel, FlightClimb, FlightCruise, FlightLanding, Dismount, EntranceApproach, EnterDoor }
        private readonly Action<string> _say;
        private LocalRoute _route;
        private Phase _phase;
        private bool _ownsMovement;
        private Vector3 _lastPosition;
        private Vector3 _directStep, _groundTarget;
        private bool _forceDirect, _directActive, _alternateStep;
        private float _bestStepDistance, _bestFlightDistance;
        private DateTime _stepProgress, _flightProgress, _travelStarted;
        private int _groundRecoveries;
        private string _movementKind, _lastEvaluation;
        private DateTime _phaseAt, _lastProgress, _nextMove, _lastUse, _entryStarted;
        private int _doorAttempts;
        private Identity _door = Identity.None;
        public TravelMode Mode { get; set; } = TravelMode.Auto;
        public string Status => _phase.ToString();
        public bool IsFlightActive => _phase == Phase.FlightClimb || _phase == Phase.FlightCruise || _phase == Phase.FlightLanding;
        private static bool IsFlying => DynelManager.LocalPlayer.MovementState == MovementState.Fly;
        public bool NeedsReselection => Mode == TravelMode.Auto &&
            ((_phase == Phase.GroundTravel && IsFlying) ||
             ((_phase == Phase.FlightClimb || _phase == Phase.FlightCruise || _phase == Phase.FlightLanding) && !IsFlying));

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
                _say($"Local travel: playfield={Playfield.ModelIdentity.Instance}, position={DynelManager.LocalPlayer.Position}, " +
                    $"movement={DynelManager.LocalPlayer.MovementState}, outdoor mesh={SMovementController.NavAgent?.HasPathfinder == true}.");
                foreach (LocalRoute route in routes)
                    _say($"Mission {route.Mission.Id.Instance}: ground={CostText(route.GroundCost)} ({route.GroundReason}), " +
                        $"flying={CostText(route.FlyingCost)} ({route.FlyingReason}).");
                _lastEvaluation = evaluation;
            }
            _route = routes.Where(x => !float.IsInfinity(x.Cost(flying)))
                .OrderBy(x => x.Cost(flying)).ThenBy(x => x.Mission.Id.Instance).FirstOrDefault();
            if (_route == null) return null;
            // Take over only after an eligible local route exists; clear competing old paths.
            SMovementController.Halt();
            SMovementController.SetMovement(MovementAction.FullStop);
            _door = Identity.None;
            _doorAttempts = 0;
            _entryStarted = DateTime.MinValue;
            _travelStarted = DateTime.UtcNow;
            _groundRecoveries = 0;
            _forceDirect = !_route.GroundUsesMesh;
            Begin(flying ? Phase.FlightClimb : Phase.GroundTravel);
            _say($"Selected {_route.Mission.Id.Instance}: {_route.Mission.Name}; entrance={_route.Mission.Entrance}; " +
                $"{(flying ? _route.FlyingReason : _route.GroundReason)}, cost {_route.Cost(flying):F1} m.");
            return _route.Mission;
        }

        private static string CostText(float value) => float.IsInfinity(value) ? "unreachable" : $"{value:F1} m";

        public void Reset()
        {
            Halt();
            _route = null;
            _phase = Phase.Idle;
            _door = Identity.None;
            _lastEvaluation = null;
            _directActive = false;
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
            Halt();
            _phase = phase;
            _phaseAt = _lastProgress = DateTime.UtcNow;
            if (phase == Phase.EntranceApproach && _entryStarted == DateTime.MinValue)
                _entryStarted = _phaseAt;
            _lastPosition = DynelManager.LocalPlayer.Position;
            _nextMove = DateTime.MinValue;
            _directActive = _alternateStep = false;
            _movementKind = null;
            _bestFlightDistance = float.PositiveInfinity;
            _flightProgress = _phaseAt;
        }

        public bool Tick(AcceptedMission mission)
        {
            if (_route == null || _route.Mission.Id != mission.Id) return false;
            if ((_phase == Phase.EntranceApproach || _phase == Phase.EnterDoor) &&
                DateTime.UtcNow - _entryStarted > TimeSpan.FromSeconds(60))
                return Fail("Entrance approach/entry exceeded 60 seconds; mission handoff stopped.");
            if (DateTime.UtcNow - _travelStarted > TimeSpan.FromMinutes(15))
                return Fail($"Local travel exceeded 15 minutes for mission {mission.Id.Instance}; stopped.");
            Vector3 position = DynelManager.LocalPlayer.Position;
            if (Vector3.Distance(position, _lastPosition) >= 1)
            {
                _lastPosition = position;
                _lastProgress = DateTime.UtcNow;
            }
            bool groundPhase = _phase == Phase.GroundTravel || _phase == Phase.EntranceApproach;
            if (_phase != Phase.Dismount && _phase != Phase.EnterDoor &&
                (!groundPhase || _ownsMovement) &&
                DateTime.UtcNow - _lastProgress > TimeSpan.FromSeconds(20))
            {
                if (!groundPhase) return Fail($"{_phase} stalled for mission {mission.Id.Instance} at {position}.");
                if (!RecoverGround("movement stalled for 20 seconds")) return false;
            }
            switch (_phase)
            {
                case Phase.GroundTravel:
                    if (IsFlying) return Fail("Ground travel requires landing/dismount; use /rkm travel auto or flying.");
                    if (Vector3.Distance(position, mission.Entrance) <= 10) { Begin(Phase.EntranceApproach); return true; }
                    return GroundMove(mission.Entrance);
                case Phase.FlightClimb:
                    if (Vector3.Distance(position, _route.CruiseStart) <= 2) { Begin(Phase.FlightCruise); return true; }
                    return FlyMove(_route.CruiseStart);
                case Phase.FlightCruise:
                    if (Vector3.Distance(position, _route.CruiseEnd) <= 3)
                    {
                        // Distant terrain may not have been loaded during selection. Never descend
                        // onto an unverified point; refresh the surface now that the character is nearby.
                        Vector3 sample = _route.Landing;
                        sample.Y = mission.Entrance.Y;
                        if (!LocalRoutePlanner.TryLandingPoint(sample, out Vector3 landing, out bool verified) || !verified)
                            return Fail($"No suitable terrain at flight approach {sample}; descent withheld. Move nearby and /rkm start.");
                        _route.Landing = landing;
                        _route.LandingVerified = true;
                        Begin(Phase.FlightLanding);
                        _say($"Final flight approach: descending to {_route.Landing} before mission {mission.Id.Instance}.");
                        return true;
                    }
                    return FlyMove(_route.CruiseEnd);
                case Phase.FlightLanding:
                    Vector3 touchdown = _route.Landing + new Vector3(0, 1.5f, 0);
                    if (LocalRoutePlanner.HorizontalDistance(position, touchdown) <= 2 && Math.Abs(position.Y - touchdown.Y) <= 1.5f)
                    {
                        Begin(Phase.Dismount);
                        _say("At the landing point. Dismount your flying vehicle; precise ground/door approach resumes automatically.");
                        return true;
                    }
                    return FlyMove(touchdown);
                case Phase.Dismount:
                    Halt();
                    if (DateTime.UtcNow - _phaseAt > TimeSpan.FromMinutes(2))
                        return Fail("Dismount wait exceeded two minutes; /rkm start after landing/dismounting.");
                    if (!IsFlying && !DynelManager.LocalPlayer.IsFalling)
                    {
                        _forceDirect = false; // Reconsider an optional mesh from the actual grounded position.
                        Begin(Phase.EntranceApproach);
                        _say("Ground movement confirmed; approaching the mission door.");
                    }
                    return true;
                case Phase.EntranceApproach: return ApproachDoor(mission);
                case Phase.EnterDoor: return EnterDoor(mission);
                default: return false;
            }
        }

        private bool GroundMove(Vector3 destination)
        {
            Vector3 position = DynelManager.LocalPlayer.Position;
            if (_directActive && Vector3.Distance(destination, _groundTarget) > 1)
            {
                Halt();
                _directActive = false; // A newly resolved live door replaces the accepted coordinate target.
            }
            if (_directActive)
            {
                float remaining = LocalRoutePlanner.HorizontalDistance(position, _directStep);
                if (remaining + 0.5f < _bestStepDistance)
                {
                    _bestStepDistance = remaining;
                    _stepProgress = DateTime.UtcNow;
                }
                if (remaining <= 0.7f)
                {
                    Halt();
                    _directActive = false;
                }
                else if (DateTime.UtcNow - _stepProgress > TimeSpan.FromSeconds(12))
                {
                    if (!RecoverGround($"direct step made no progress for 12 seconds; remaining={remaining:F1} m")) return false;
                }
                else
                {
                    // Check the active short leg as geometry loads; do not keep pushing into a wall.
                    if (!LocalRoutePlanner.ClearSegment(position + Vector3.Up, _directStep + Vector3.Up))
                    {
                        if (!RecoverGround("direct step became obstructed")) return false;
                    }
                    else if (DateTime.UtcNow < _nextMove && SMovementController.IsNavigating()) return true;
                    else
                    {
                        _ownsMovement = true;
                        if (!SMovementController.SetDestination(_directStep)) return Fail("AO# rejected the direct waypoint.");
                        _nextMove = DateTime.UtcNow.AddSeconds(3);
                        return true;
                    }
                }
            }
            if (!_forceDirect && LocalRoutePlanner.TryGroundCost(position, destination, out _))
            {
                if (DateTime.UtcNow < _nextMove && SMovementController.IsNavigating()) return true;
                _ownsMovement = true;
                // The SDK bool confirms submission, not that a path was actually queued.
                if (SMovementController.SetNavDestination(destination) && SMovementController.IsNavigating())
                {
                    LogMovement("navmesh", destination);
                    _nextMove = DateTime.UtcNow.AddSeconds(3);
                    return true;
                }
                Halt();
                _say($"AO# queued no ground mesh path to {destination}; attempting a direct local approach.");
                _forceDirect = true;
            }
            if (!_forceDirect)
                _say($"Ground mesh no longer connects to {destination}; attempting a direct local approach.");
            _forceDirect = true;
            if (!LocalRoutePlanner.TryDirectGroundStep(position, destination, _alternateStep,
                _groundRecoveries, out Vector3 step, out bool detour))
                return Fail($"No clear local ground step from {position} toward {destination}; outdoor mesh={SMovementController.NavAgent?.HasPathfinder == true}. Move around the obstacle and /rkm start.");
            if (detour && !_alternateStep && !RecoverGround("straight local approach obstructed; trying a side step")) return false;
            _alternateStep = false;
            _directStep = step;
            _groundTarget = destination;
            _directActive = true;
            _bestStepDistance = LocalRoutePlanner.HorizontalDistance(position, step);
            _stepProgress = DateTime.UtcNow;
            _ownsMovement = true;
            if (!SMovementController.SetDestination(step)) return Fail("AO# rejected the direct ground waypoint.");
            LogMovement("direct local fallback", destination);
            _nextMove = DateTime.UtcNow.AddSeconds(3);
            return true;
        }

        private bool RecoverGround(string reason)
        {
            Halt();
            _directActive = false;
            _forceDirect = true;
            if (++_groundRecoveries > 3)
                return Fail($"Ground fallback exhausted three recovery attempts at {DynelManager.LocalPlayer.Position}: {reason}. Move to a clear approach and /rkm start.");
            _alternateStep = true;
            _lastProgress = DateTime.UtcNow;
            _nextMove = DateTime.MinValue;
            _say($"Ground recovery {_groundRecoveries}/3: {reason}; position={DynelManager.LocalPlayer.Position}.");
            return true;
        }

        private void LogMovement(string kind, Vector3 destination)
        {
            if (_movementKind == kind) return;
            _movementKind = kind;
            _say($"Ground movement: {kind}; target={destination}; short-step checks and stall limits active.");
        }

        private bool FlyMove(Vector3 destination)
        {
            if (!IsFlying) return Fail("Flying movement state was lost. Land, then restart local travel.");
            Vector3 position = DynelManager.LocalPlayer.Position;
            float remaining = Vector3.Distance(position, destination);
            if (remaining + 0.5f < _bestFlightDistance)
            {
                _bestFlightDistance = remaining;
                _flightProgress = DateTime.UtcNow;
            }
            if (DateTime.UtcNow - _flightProgress > TimeSpan.FromSeconds(20))
                return Fail($"{_phase} made no progress toward {destination} for 20 seconds; remaining={remaining:F1} m, position={position}.");
            if (!LocalRoutePlanner.ClearSegment(position, destination))
                return Fail($"{_phase} corridor obstructed from {position} toward {destination}. Move to a clear altitude/position and /rkm start.");
            // SharpNav's ordinary waypoint arrival uses X/Z only. Direct flight must include Y
            // in both steering and arrival so vertical climb/descent cannot finish prematurely.
            Vector3 direction = destination - position;
            // A vertical direction is parallel to Vector3.Up, so FromTo's default
            // LookRotation basis would be degenerate during climb or descent.
            Vector3 up = LocalRoutePlanner.HorizontalDistance(position, destination) < 0.1f
                ? new Vector3(0, 0, 1) : Vector3.Up;
            DynelManager.LocalPlayer.Rotation = Quaternion.LookRotation(direction, up);
            SMovementController.SetMovement(MovementAction.ForwardStart);
            SMovementController.SetMovement(MovementAction.Update);
            _ownsMovement = true;
            return true;
        }

        private Door ResolveDoor(AcceptedMission mission)
        {
            var doors = Playfield.Doors.Where(x => Vector3.Distance(x.Position, mission.Entrance) <= 6)
                .OrderBy(x => Vector3.Distance(x.Position, mission.Entrance)).ToList();
            if (doors.Count == 0) return null;
            if (_door != Identity.None)
                return doors.FirstOrDefault(x => x.Identity == _door); // Never retarget to another door mid-entry.
            if (doors.Count > 1 && Vector3.Distance(doors[1].Position, mission.Entrance) -
                Vector3.Distance(doors[0].Position, mission.Entrance) < 1)
                return null; // Ambiguous coordinates: do not use an arbitrary neighboring door.
            _door = doors[0].Identity;
            _say($"Mission {mission.Id.Instance}: entrance door {_door} at {doors[0].Position}.");
            return doors[0];
        }

        private bool ApproachDoor(AcceptedMission mission)
        {
            if (IsFlying) return Fail("Dismount before the precise entrance approach.");
            Door entrance = ResolveDoor(mission);
            if (entrance == null)
            {
                if (DateTime.UtcNow - _phaseAt > TimeSpan.FromSeconds(30))
                    return Fail("No unique mission door within 6 m of the accepted coordinates; entrance entry withheld.");
                if (Vector3.Distance(DynelManager.LocalPlayer.Position, mission.Entrance) > 4)
                    return GroundMove(mission.Entrance);
                Halt();
                return true;
            }
            Vector3 position = DynelManager.LocalPlayer.Position;
            float distance = Vector3.Distance(position, entrance.Position);
            if (distance <= 3 && Math.Abs(position.Y - entrance.Position.Y) <= 2.5f && !DynelManager.LocalPlayer.IsFalling)
            {
                Begin(Phase.EnterDoor);
                _lastUse = DateTime.MinValue;
                return true;
            }
            // The same bounded movement supports final approach with or without an outdoor mesh.
            return GroundMove(entrance.Position);
        }

        private bool EnterDoor(AcceptedMission mission)
        {
            if (IsFlying) return Fail("Flying state returned before entrance entry; dismount and restart.");
            Door entrance = ResolveDoor(mission);
            if (entrance == null) return Fail("The selected entrance door disappeared before zoning.");
            Vector3 position = DynelManager.LocalPlayer.Position;
            if (Vector3.Distance(position, entrance.Position) > 3.5f || Math.Abs(position.Y - entrance.Position.Y) > 2.5f)
            {
                Begin(Phase.EntranceApproach);
                return true;
            }
            if (DateTime.UtcNow - _phaseAt > TimeSpan.FromSeconds(20))
                return Fail("Mission door did not produce a verified dungeon transition after three use attempts.");
            if (_doorAttempts < 3 && DateTime.UtcNow - _lastUse >= TimeSpan.FromSeconds(4))
            {
                Halt();
                entrance.Use();
                _lastUse = DateTime.UtcNow;
                _doorAttempts++;
                _say($"Using entrance {_door}, attempt {_doorAttempts}/3; awaiting exact mission/dungeon verification.");
            }
            return true;
        }

        private bool Fail(string reason)
        {
            Halt();
            _say(reason);
            return false;
        }
    }
}
