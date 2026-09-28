using System;
using System.Collections.Generic;
using System.Linq;
using AOSharp.Common.GameData;
using AOSharp.Core;

namespace RKmission
{
    internal enum TravelMode { Auto, Ground, Flying }

    internal sealed class LocalMissionTravel
    {
        private enum Phase { Idle, CoarseTravel, FlyToEntrance, FlyAvoidObstacle, ProbeExterior, OrbitBypass, AlignElevation, FinalApproach, Interact, CrossThreshold, AwaitTransition }
        private readonly Action<string> _say;
        private readonly OutdoorNavigationSettings _settings;
        private readonly EntranceLearning _learning;
        private readonly LocalMovement _movement;
        private readonly List<AcceptedMission> _accepted = new List<AcceptedMission>();
        private readonly List<Vector3> _path = new List<Vector3>();
        private LocalRoute _route;
        private EntranceMemory _runMemory;
        private EntranceAttemptRecord _runRecord;
        private EntranceAcquisition _entrance;
        private EntranceOrbit _orbit;
        private FlightPathPlanner _flight;
        private bool _flyExteriorActive, _flyNeedsSideChange;
        private int _flyRouteStalls;
        private double _flyStartBearing, _flyLastBearing;
        private float _flyAngularTravel;
        private Phase _phase;
        private bool _flying, _legActive, _pendingZoning;
        private int _pathIndex, _recoveries, _uses;
        private float _coarseBest;
        private DateTime _started, _coarseProgress, _nextLog, _phaseStarted, _lastUse;
        private string _lastEvaluation;
        public TravelMode Mode { get; set; } = TravelMode.Auto;
        public string Status => _phase.ToString();
        public Identity ActiveDoor => _entrance?.Active?.DoorId ?? Identity.None;
        public int UpdateIntervalMilliseconds => _route != null && IsFlying ? 25 : 250;
        private static bool IsFlying => DynelManager.LocalPlayer?.MovementState == MovementState.Fly;
        public bool MatchesAnchor(AcceptedMission mission) => _route != null && _route.Mission.Id == mission.Id &&
            _route.Playfield == mission.PlayfieldId && Vector3.Distance(_route.Anchor, mission.Entrance) <= 0.5f;

        public LocalMissionTravel(Action<string> say, string pluginDir)
        {
            _say = say;
            string directory = System.IO.Path.Combine(pluginDir, "RKMissionData");
            _settings = OutdoorNavigationSettings.Load(directory, say);
            _learning = new EntranceLearning(directory, _settings, say);
            _movement = new LocalMovement(_settings, say);
        }

        public AcceptedMission SelectNearest(IEnumerable<AcceptedMission> missions)
        {
            bool flying = Mode == TravelMode.Flying || (Mode == TravelMode.Auto && IsFlying);
            if (flying != IsFlying)
            {
                string reason = flying ? "Flying travel needs an active flying vehicle." : "Ground travel needs ground movement; land or use auto.";
                if (_lastEvaluation != reason) _say(reason); _lastEvaluation = reason; return null;
            }
            Vector3 origin = DynelManager.LocalPlayer.Position;
            if (!AcceptedMissions.Finite(origin)) return null;
            _accepted.Clear(); _accepted.AddRange(missions.Where(x => x.Present && x.IsRubiKaDestination &&
                x.State != MissionProgress.CompletedByUser && x.PlayfieldId == Playfield.ModelIdentity.Instance && AcceptedMissions.Finite(x.Entrance)));
            var estimates = _accepted.Select(x => LocalRoutePlanner.Estimate(x, origin, flying))
                .Where(x => x != null && !float.IsNaN(x.Cost) && !float.IsInfinity(x.Cost)).OrderBy(x => x.Cost).ThenBy(x => x.Mission.Id.Instance).ToList();
            foreach (LocalRoute estimate in estimates)
                _say($"Mission route estimate: mission={estimate.Mission.Id.Instance}, playfield={estimate.Mission.PlayfieldId}, origin=({LocalRoutePlanner.Coordinates(origin)}), " +
                    $"anchor=({LocalRoutePlanner.Coordinates(estimate.Anchor)}), mode={(flying ? "Fly" : "Run")}, cost={estimate.Cost:F1} m, source={estimate.Reason}.");
            LocalRoute nearest = estimates.FirstOrDefault();
            if (nearest == null) return null;
            Reset("new outdoor mission selected"); _route = nearest; _flying = flying;
            if (flying) _flight = new FlightPathPlanner(origin, _settings, _say);
            _runMemory = _learning.For(nearest.Mission.PlayfieldId, nearest.Anchor);
            _runRecord = new EntranceAttemptRecord { StartedUtc = DateTime.UtcNow, Playfield = nearest.Mission.PlayfieldId,
                MissionId = nearest.Mission.Id.Instance, Mode = flying ? "Fly" : "Run", Stage = "CoarseTravel", Source = nearest.Reason,
                Anchor = NavigationPoint.From(nearest.Anchor), Origin = NavigationPoint.From(origin), Result = "local run started" };
            _learning.Record(_runMemory, _runRecord);
            _started = _coarseProgress = DateTime.UtcNow; _coarseBest = LocalRoutePlanner.HorizontalDistance(origin, _route.Anchor);
            _recoveries = 0; _nextLog = DateTime.MinValue; SetPhase(flying ? Phase.FlyToEntrance : Phase.CoarseTravel);
            _say($"Selected accepted mission: {_route.Mission.Id.Instance}, '{_route.Mission.Name}', current playfield={_route.Mission.PlayfieldId}, " +
                $"origin=({LocalRoutePlanner.Coordinates(origin)}), anchor=({LocalRoutePlanner.Coordinates(_route.Anchor)}), mode={(flying ? "Fly" : "Run")}, " +
                $"estimate={nearest.Cost:F1} m; map/minimap upload handled by native accepted-mission API; final doorway unresolved.");
            return nearest.Mission;
        }

        public void Reset(string reason = "local automation reset/interrupted")
        {
            if (_entrance?.Active != null)
            {
                if (_pendingZoning) _entrance.CompleteHandoff(false, reason);
                else _entrance.Finish(reason, _entrance.Active.Record.LastPosition?.Vector ?? _route.Origin, false);
            }
            FinishRun("interrupted", reason);
            _movement.Reset(); _route = null; _entrance = null; _orbit = null; _flight = null; _flyExteriorActive = false; _path.Clear(); _accepted.Clear();
            _phase = Phase.Idle; _legActive = _pendingZoning = false; _lastEvaluation = null;
        }

        public void SuspendForZoning()
        {
            _movement.Halt(); _legActive = false; _flyExteriorActive = false; _orbit = null; _pendingZoning = _route != null;
            _phase = _route == null ? Phase.Idle : Phase.AwaitTransition;
            if (_entrance?.Active != null)
            {
                _entrance.Active.Record.Result = "zoning observed; exact mission verification pending";
                _entrance.Active.Record.ZoneObserved = true;
                _entrance.Active.Record.Reason = "No success vector is learned until verified dungeon handoff.";
                _say($"Entrance crossing result: mission={_route.Mission.Id.Instance}, door={ActiveDoor}, zoning observed; retain managed attempt for exact dungeon verification.");
            }
        }

        public void CompleteHandoff(bool success, string reason)
        {
            if (_pendingZoning) _entrance?.CompleteHandoff(success, reason);
            if (_pendingZoning) FinishRun(success ? "verified entry" : "failed transition", reason);
            _pendingZoning = false;
        }

        public bool Tick(AcceptedMission mission, IEnumerable<AcceptedMission> accepted)
        {
            if (_route == null || _route.Mission.Id != mission.Id) return false;
            _accepted.Clear(); _accepted.AddRange(accepted);
            if (!mission.Present || mission.PlayfieldId != Playfield.ModelIdentity.Instance || !MatchesAnchor(mission))
                return Fail("selected accepted mission/playfield/anchor changed");
            Vector3 player = DynelManager.LocalPlayer.Position;
            if (!AcceptedMissions.Finite(player)) return Fail("invalid player coordinates");
            DateTime now = DateTime.UtcNow;
            if ((now - _started).TotalMinutes >= _settings.TravelLimitMinutes) return Fail($"bounded local travel time {_settings.TravelLimitMinutes} minutes exhausted");
            bool flying = IsFlying;
            if (Mode != TravelMode.Auto && flying != (Mode == TravelMode.Flying))
                return Fail("actual movement state no longer matches requested travel mode; use /rkm travel auto or restore vehicle state");
            if (_flying != flying)
            {
                _flying = flying; _movement.Halt(); _legActive = false;
                _flight = flying ? new FlightPathPlanner(player, _settings, _say) : null; _flyExteriorActive = false; _orbit = null;
                _say($"Observed travel mode changed to {(flying ? "Fly" : "Run")}; selected mission, diagnostics and overall progress deadline retained.");
            }
            if (_entrance == null && LocalRoutePlanner.HorizontalDistance(player, _route.Anchor) <= _settings.MaxProbeRadius + 4)
            {
                _entrance = new EntranceAcquisition(mission, _accepted, _route.Anchor, _settings, _learning, _say);
                SetPhase(Phase.ProbeExterior);
            }
            if (_entrance == null) return CoarseTravel(player, now);
            _entrance.Scan(player, flying);
            if (_entrance.PreferLiveDoor) Retry("associated live mission door loaded; replace inferred threshold", player, false);
            if (_entrance.Active == null && _entrance.FullSpaceCovered && !_entrance.HasUntriedLiveGeometry &&
                (now - _entrance.LastProgress).TotalSeconds >= _settings.NoProgressSeconds)
                return Fail($"full directional candidate space explored ({_entrance.CoveredSectors}/{_settings.Sectors} sectors), " +
                    $"no new observed target-distance improvement for {_settings.NoProgressSeconds} seconds; no verified transition");
            if (_entrance.Active == null && _entrance.BypassExhausted &&
                (now - _entrance.LastProgress).TotalSeconds >= _settings.NoProgressSeconds)
                return Fail($"perimeter recovery made no new angular/target progress for {_settings.NoProgressSeconds} seconds; " +
                    "candidate sides not reached, entrance access unresolved");
            EntranceAcquisition.Attempt attempt = _entrance.Active;
            if (attempt == null)
            {
                attempt = _entrance.Select(player, flying);
                if (attempt == null) return true;
                _uses = 0; _lastUse = DateTime.MinValue;
                if (flying) BeginFlyExterior(player, _entrance.NeedsFlightSideChange(attempt));
                else
                {
                    _orbit = _entrance.GroundOrbit(attempt, player);
                    SetPhase(_entrance.BypassRequired ? Phase.OrbitBypass : Phase.ProbeExterior);
                }
            }
            if (_flyExteriorActive) return TickFlyExterior(player, now);
            if (_orbit != null) return TickOrbit(player, now);
            if (_phase == Phase.Interact) return Interact(player, now);
            if (_phase == Phase.AwaitTransition)
            {
                if ((now - _phaseStarted).TotalSeconds >= 5)
                {
                    const string reason = "threshold reached/crossed but no zoning observed";
                    if (flying && _entrance.TryNextFlyingHeight(reason)) BeginFlyExterior(player, false);
                    else Retry(reason, player, true);
                }
                return true;
            }
            if (_legActive)
            {
                MovementResult result = _movement.Tick();
                if (flying && result != MovementResult.Moving) RecordFlightLeg(attempt.Record, player, result);
                _entrance.Observe(player, _movement.Target, _phase.ToString(), flying, _movement.StartDistance, _movement.BestDistance, _movement.StallSeconds);
                LogProgress(player, now);
                if (result == MovementResult.Stalled)
                {
                    string reason = "observed leg no-progress at " + _phase;
                    if (flying) _flight.Blocked(player, _movement.Target);
                    if (flying && (_phase == Phase.AlignElevation || _phase == Phase.FinalApproach || _phase == Phase.CrossThreshold) &&
                        _entrance.TryNextFlyingHeight(reason)) BeginFlyExterior(player, false);
                    else Retry(reason, player, true);
                    return true;
                }
                if (result == MovementResult.Moving) return true;
                _legActive = false;
                if (!flying || _phase != Phase.FinalApproach ||
                    LocalMovement.Distance(player, _path[_pathIndex], true) <= 0.9f) _pathIndex++;
            }
            if (_pathIndex < _path.Count)
            {
                Vector3 target = _path[_pathIndex];
                if (flying && _phase == Phase.FinalApproach) target = LocalRoutePlanner.Toward(player, target, 3);
                _movement.Begin(target, flying, true, stallSeconds: flying ? 3 : (int?)null); _legActive = true;
                _say($"Outdoor navigation target: phase={_phase}, sector={attempt.Sector}, source={attempt.Source}, " +
                    $"target=({LocalRoutePlanner.Coordinates(target)}), elevation={attempt.HeightSource}, door={attempt.DoorId}.");
                return true;
            }
            switch (_phase)
            {
                case Phase.AlignElevation:
                    SetPath(Phase.FinalApproach, _entrance.FinalPoints(attempt));
                    break;
                case Phase.FinalApproach:
                    if (attempt.DoorId != Identity.None) SetPhase(Phase.Interact);
                    else BeginCrossing(player, "no live mission Door; inferred normal/proximity threshold crossing");
                    break;
                case Phase.CrossThreshold: SetPhase(Phase.AwaitTransition); break;
            }
            return true;
        }

        private bool TickOrbit(Vector3 player, DateTime now)
        {
            if (_legActive)
            {
                MovementResult result = _movement.Tick();
                _entrance.Observe(player, _movement.Target, _phase.ToString(), _flying,
                    _movement.StartDistance, _movement.BestDistance, _movement.StallSeconds);
                _entrance.ObserveOrbit(_orbit, player); LogProgress(player, now);
                if (result == MovementResult.Moving && !_orbit.ProbeExpired) return true;
                _movement.Halt(); _legActive = false;
                _orbit.LegEnded(player, result == MovementResult.Stalled);
                _entrance.ObserveOrbit(_orbit, player);
                if (result == MovementResult.Stalled) _entrance.OrbitBlocked(player, _orbit.Failed);
            }
            if (_orbit.Failed)
            {
                _entrance.OrbitBlocked(player, true);
                Retry("perimeter bypass exhausted before requested exterior was reached", player, true); return true;
            }
            if (_orbit.Next(player, out Vector3 target))
            {
                Phase phase = _entrance.BypassRequired ? Phase.OrbitBypass : Phase.ProbeExterior;
                SetPhase(phase); _movement.Begin(target, _flying, true); _legActive = true;
                _say($"Outdoor perimeter target: phase={phase}, requested sector={_entrance.Active.Sector}, " +
                    $"target=({LocalRoutePlanner.Coordinates(target)}); requested side remains unconfirmed.");
                return true;
            }
            if (_orbit.Finished)
            {
                _entrance.ExteriorReached(player, _orbit); _orbit = null;
                SetPath(Phase.AlignElevation, new[] { _entrance.Active.Exterior });
            }
            return true;
        }

        private void BeginFlyExterior(Vector3 player, bool requireNewSide)
        {
            _orbit = null; _flyExteriorActive = true; _flyNeedsSideChange = requireNewSide; _flyRouteStalls = 0;
            _flyStartBearing = _flyLastBearing = LocalRoutePlanner.Angle(player - _route.Anchor);
            _flyAngularTravel = _entrance.Active.Record.AngularSpanDegrees;
            SetPhase(Phase.ProbeExterior);
        }

        private bool TickFlyExterior(Vector3 player, DateTime now)
        {
            EntranceAcquisition.Attempt attempt = _entrance.Active;
            // If observed walls widened the clearance, update the ACTUAL goal,
            // not only the protected footprint. Otherwise arrival could be
            // requested inside a radius that the route correctly refuses to enter.
            Vector3 outside = _route.Anchor + attempt.Normal * Math.Max(attempt.Radius, _entrance.FlightRingRadius);
            attempt.Exterior.X = outside.X; attempt.Exterior.Z = outside.Z;
            double bearing = LocalRoutePlanner.Angle(player - _route.Anchor);
            _flyAngularTravel += (float)(Math.Abs(AngleDelta(bearing - _flyLastBearing)) * 180 / Math.PI); _flyLastBearing = bearing;
            _entrance.ObserveFlight(player, _flight, _flyAngularTravel);
            if (_legActive)
            {
                MovementResult result = _movement.Tick();
                _entrance.Observe(player, _movement.Target, _phase.ToString(), true,
                    _movement.StartDistance, _movement.BestDistance, _movement.StallSeconds); LogProgress(player, now);
                if (result == MovementResult.Moving) return true;
                RecordFlightLeg(attempt.Record, player, result);
                _legActive = false;
                if (result == MovementResult.Stalled)
                {
                    _flight.Blocked(player, _movement.Target); _entrance.OrbitBlocked(player, false);
                    if (++_flyRouteStalls >= 4)
                    {
                        _entrance.OrbitBlocked(player, true);
                        Retry("Fly over/around route repeatedly blocked before candidate arrival", player, true); return true;
                    }
                }
                else _flight.Reached(player);
            }
            float remaining = LocalRoutePlanner.HorizontalDistance(player, attempt.Exterior);
            float radius = LocalRoutePlanner.HorizontalDistance(player, _route.Anchor);
            double sideChange = Math.Abs(AngleDelta(bearing - _flyStartBearing)) * 180 / Math.PI;
            if (remaining <= 1 && radius >= _entrance.FlightRingRadius - 1 && (!_flyNeedsSideChange || sideChange >= 20))
            {
                if (_entrance.FlyingExteriorReached(player))
                {
                    _flyExteriorActive = false;
                    SetPath(Phase.AlignElevation, new[] { attempt.Exterior }); return true;
                }
                // A lower surface beyond a likely roof requires actual outward
                // relocation first; no descent at the old elevated support.
                outside = _route.Anchor + attempt.Normal * _entrance.FlightRingRadius;
                attempt.Exterior.X = outside.X; attempt.Exterior.Z = outside.Z;
            }
            Vector3 target = _flight.Next(player, attempt.Exterior, _route.Anchor,
                _entrance.FlightRingRadius, _entrance.PreferredFlightDirection,
                returningFromEntry: attempt.FlightHeightResolved && attempt.Record.ExteriorReached);
            SetPhase(_flight.Strategy == "direct" ? Phase.ProbeExterior : Phase.FlyAvoidObstacle);
            _movement.Begin(target, true, true, stallSeconds: 4); _legActive = true;
            _say($"Fly exterior route: requested sector={attempt.Sector}, actual bearing={bearing * 180 / Math.PI:F1} deg, " +
                $"side change={sideChange:F1} deg, target=({LocalRoutePlanner.Coordinates(target)}), strategy={_flight.Strategy}; entry height deferred.");
            return true;
        }
        private static double AngleDelta(double angle)
        { while (angle > Math.PI) angle -= Math.PI * 2; while (angle < -Math.PI) angle += Math.PI * 2; return angle; }

        private bool CoarseTravel(Vector3 player, DateTime now)
        {
            float remaining = LocalRoutePlanner.HorizontalDistance(player, _route.Anchor);
            if (remaining + 0.75f < _coarseBest) { _coarseBest = remaining; _coarseProgress = now; }
            if ((now - _coarseProgress).TotalSeconds >= _settings.NoProgressSeconds)
                return Fail($"coarse travel made no net horizontal improvement for {_settings.NoProgressSeconds} seconds; remaining={remaining:F2} m, recoveries={_recoveries}");
            if (_legActive)
            {
                MovementResult result = _movement.Tick(); LogProgress(player, now);
                if (_flying && result != MovementResult.Moving) RecordFlightLeg(_runRecord, player, result);
                _runRecord.LastPosition = NavigationPoint.From(player); _runRecord.LastTarget = NavigationPoint.From(_movement.Target);
                _runRecord.ProgressMetres = Math.Max(0, LocalRoutePlanner.HorizontalDistance(_route.Origin, _route.Anchor) - _coarseBest);
                _runRecord.StallSeconds = (float)_movement.StallSeconds;
                if (result == MovementResult.Moving) return true;
                _legActive = false;
                if (result == MovementResult.Stalled)
                { _recoveries++; if (_flying) _flight.Blocked(player, _movement.Target); }
                else if (_flying) _flight.Reached(player);
            }
            Vector3 destination = _route.Anchor; destination.Y = player.Y;
            if (_flying)
            {
                Vector3 step = _flight.Next(player, destination, _route.Anchor);
                SetPhase(_flight.Strategy == "direct" ? Phase.FlyToEntrance : Phase.FlyAvoidObstacle);
                _movement.Begin(step, true, true, stallSeconds: 4); _legActive = true; return true;
            }
            bool mesh = !_flying && _route.GroundUsesMesh && _recoveries == 0;
            if (mesh) destination = LocalRoutePlanner.LocalElevation(destination, player, false, _settings, out _);
            else destination = _movement.CoarseStep(destination, _recoveries);
            _movement.Begin(destination, _flying, false, mesh); _legActive = true;
            return true;
        }

        private bool Interact(Vector3 player, DateTime now)
        {
            EntranceAcquisition.Attempt attempt = _entrance.Active;
            Door door = _entrance.RefreshDoor(true);
            if (door == null || _entrance.Active != attempt)
            { Retry("live selected mission door no longer passes association/location checks before Use", player, false); return true; }
            if (!DoorWithinUseRange(player, door))
            { Retry("final side reached but live door is out of actual use range/elevation", player, true); return true; }
            Vector3 lift = _flying ? Vector3.Zero : Vector3.Up;
            Vector3 from = player + lift, to = door.Position + lift;
            Vector3 end = LocalRoutePlanner.Toward(from, to, Math.Max(0, Vector3.Distance(from, to) - 0.6f));
            // A short interaction corridor is an ownership/range safeguard. Synthetic
            // clearance never gates local travel or flight launch.
            if (!LocalRoutePlanner.ClearSegment(from, end))
            { Retry("short interaction corridor obstructed before door face; try another exterior side", player, true); return true; }
            if (_uses < 2 && (now - _lastUse).TotalSeconds >= 4)
            {
                try { door.Use(); }
                catch (Exception ex) { Retry("door Use failed: " + ex.Message, player, true); return true; }
                _uses++; _lastUse = now;
                attempt.Record.UseCommands = _uses;
                _say($"Entrance interaction: mission={_route.Mission.Id.Instance}, door={door.Identity}, position=({LocalRoutePlanner.Coordinates(door.Position)}), " +
                    $"rotation={door.Rotation}, horizontal range={LocalRoutePlanner.HorizontalDistance(player, door.Position):F2} m, " +
                    $"3D range={Vector3.Distance(player, door.Position):F2} m, locked={door.IsLocked}, open={door.IsOpen}, use={_uses}/2, " +
                    "result=command sent; awaiting zone/exact selected mission verification.");
            }
            if (_uses == 2 && (now - _lastUse).TotalSeconds >= 4) BeginCrossing(player, "two uses sent without zoning; short mission threshold crossing");
            return true;
        }

        private void BeginCrossing(Vector3 player, string reason)
        {
            EntranceAcquisition.Attempt attempt = _entrance.Active;
            attempt.Record.CrossCommands++;
            Vector3 point = attempt.Threshold - attempt.Normal * 0.8f;
            if (LocalRoutePlanner.HorizontalDistance(point, _route.Anchor) > EntranceAcquisition.SearchRadius) point = attempt.Threshold;
            _say($"Entrance crossing: mission={_route.Mission.Id.Instance}, sector={attempt.Sector}, door={attempt.DoorId}, " +
                $"target=({LocalRoutePlanner.Coordinates(point)}), reason={reason}; arrival cannot prove entry.");
            SetPath(Phase.CrossThreshold, new[] { point });
        }

        private static bool DoorWithinUseRange(Vector3 player, Door door) =>
            LocalRoutePlanner.HorizontalDistance(player, door.Position) <= 2 && Vector3.Distance(player, door.Position) <= 3.5f;

        private void RecordFlightLeg(EntranceAttemptRecord record, Vector3 player, MovementResult result)
        {
            record.FlightLegs.Add(new FlightLegRecord { FinishedUtc = DateTime.UtcNow,
                Origin = NavigationPoint.From(_movement.StartPosition), Target = NavigationPoint.From(_movement.Target),
                Position = NavigationPoint.From(player), Stage = _phase.ToString(),
                Strategy = _flyExteriorActive || _entrance == null ? _flight.Strategy : "entry alignment/approach", Result = result.ToString() });
            if (record.FlightLegs.Count > 12) record.FlightLegs.RemoveAt(0);
            if (_entrance?.Active?.Record == record) _entrance.SaveAttempt();
            else _learning.Record(_runMemory, record);
        }

        private void SetPhase(Phase phase)
        {
            _movement.Halt(); _legActive = false; _phase = phase; _phaseStarted = DateTime.UtcNow; _path.Clear(); _pathIndex = 0;
            if (_runRecord != null) { _runRecord.Stage = phase.ToString(); _runRecord.Mode = _flying ? "Fly" : "Run"; }
        }
        private void SetPath(Phase phase, IEnumerable<Vector3> points)
        { SetPhase(phase); _path.AddRange(points.Where(AcceptedMissions.Finite)); }
        private void Retry(string reason, Vector3 player, bool blocked)
        { _movement.Halt(); _legActive = false; _orbit = null; _flyExteriorActive = false; _entrance.Finish(reason, player, blocked); SetPhase(Phase.ProbeExterior); }

        private void LogProgress(Vector3 player, DateTime now)
        {
            if (now < _nextLog) return; _nextLog = now.AddSeconds(5);
            _say($"Outdoor progress: mission={_route.Mission.Id.Instance}, mode={(_flying ? "Fly" : "Run")}, phase={_phase}, " +
                $"position=({LocalRoutePlanner.Coordinates(player)}), target=({LocalRoutePlanner.Coordinates(_movement.Target)}), " +
                $"progress delta={_movement.Progress:F2} m, best remaining={_movement.BestDistance:F2} m, leg no-progress={_movement.StallSeconds:F1} s, " +
                $"overall no-progress={(now - (_entrance?.LastProgress ?? _coarseProgress)).TotalSeconds:F0}/{_settings.NoProgressSeconds} s.");
        }
        private bool Fail(string reason)
        {
            _entrance?.Finish("hard failure: " + reason, DynelManager.LocalPlayer.Position, true);
            _movement.Halt(); _legActive = false;
            FinishRun("hard failure", reason);
            _say($"Local outdoor navigation HARD FAILURE: mission={_route?.Mission.Id.Instance}, phase={_phase}, reason={reason}."); return false;
        }
        private void FinishRun(string result, string reason)
        {
            if (_runRecord == null) return;
            _runRecord.Result = result; _runRecord.Reason = reason; _runRecord.FinishedUtc = DateTime.UtcNow;
            _learning.Record(_runMemory, _runRecord); _runRecord = null; _runMemory = null;
        }
    }
}
