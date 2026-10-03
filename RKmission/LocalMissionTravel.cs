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
        private enum Phase { Idle, CoarseTravel, FlyClearance, FlyToEntrance, FlyAvoidObstacle, FlyMatchEntryHeight, ProbeExterior, OrbitBypass, AlignElevation, FlyCloseApproach, FinalApproach, Interact, CrossThreshold, AwaitTransition }
        private const float FlightEntryRadius = 10;
        private const float FlightApproachStartDistance = 6;
        private const float FlightHeightTolerance = 0.35f;
        private const float FlightApproachMaxClearance = 2;
        // Another 20% longer than the previous 3.9 m final-approach steps.
        private const float FlightApproachLegLength = 4.68f;
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
        private bool _flyCruiseReady, _flyHeightMatched, _flyDescentRelocating;
        private float _flyMatchedHeight;
        private float _flyApproachStartHeight;
        private int _flyHeightRecoveries;
        private int _flyApproachHeightRecoveries;
        private Vector3 _flyDescentExterior;
        private int _flyRouteStalls;
        private double _flyStartBearing, _flyLastBearing;
        private float _flyAngularTravel;
        private Phase _phase;
        private bool _flying, _legActive, _pendingZoning;
        private int _pathIndex, _recoveries, _uses;
        private float _coarseBest;
        private DateTime _started, _coarseProgress, _nextLog, _phaseStarted, _lastUse;
        private DateTime _nextCruiseExtensionCheck;
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
                !x.Completed && x.PlayfieldId == Playfield.ModelIdentity.Instance && AcceptedMissions.Finite(x.Entrance)));
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
            _recoveries = 0; _nextLog = DateTime.MinValue; SetPhase(flying ? Phase.FlyClearance : Phase.CoarseTravel);
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
            _flyCruiseReady = _flyHeightMatched = _flyDescentRelocating = false; _flyHeightRecoveries = 0;
            _nextCruiseExtensionCheck = DateTime.MinValue;
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
                _flyCruiseReady = _flyHeightMatched = _flyDescentRelocating = false; _flyHeightRecoveries = 0;
                _nextCruiseExtensionCheck = DateTime.MinValue;
                _say($"Observed travel mode changed to {(flying ? "Fly" : "Run")}; selected mission, diagnostics and overall progress deadline retained.");
            }
            if (flying && !_flyCruiseReady) return CoarseTravel(player, now);
            if (_entrance == null && LocalRoutePlanner.HorizontalDistance(player, _route.Anchor) <=
                (flying ? FlightEntryRadius : _settings.MaxProbeRadius + 4))
            {
                _movement.Halt(); _legActive = false;
                _entrance = new EntranceAcquisition(mission, _accepted, _route.Anchor, _settings, _learning, _say);
                SetPhase(Phase.ProbeExterior);
                if (flying) _say($"Fly 10 m height trigger: actual distance={LocalRoutePlanner.HorizontalDistance(player, _route.Anchor):F2} m, " +
                    $"cruise height={player.Y:F2}; resolve entry height and prepare raised staging before side diagnostics.");
            }
            if (_entrance == null) return CoarseTravel(player, now);
            _entrance.Scan(player, flying);
            if (_entrance.PreferLiveDoor) Retry("associated live mission door loaded; replace inferred threshold", player, false);
            if (flying)
            {
                _entrance.ResolveFlightEntryHeight(player, _route.Origin.Y);
                if (_flyHeightMatched && Math.Abs(_flyMatchedHeight - _entrance.FlightEntryHeight) > 0.35f)
                {
                    Retry("mission entrance height changed with fresh associated Door geometry; re-match before side diagnostics", player, false);
                    _flyHeightMatched = false; _flyDescentRelocating = false; _flyHeightRecoveries = 0;
                }
                if (!_flyHeightMatched) return TickFlyEntryHeight(player, now);
            }
            if (flying && _entrance.Active == null && _entrance.FullSpaceCovered && !_entrance.HasUntriedLiveGeometry &&
                _entrance.TryNextFlyingHeight("all approach sectors reached at current mission entrance height without verified entry"))
            {
                _runRecord.FailedEntryHeights.Add(_flyMatchedHeight);
                _learning.Record(_runMemory, _runRecord);
                _flyHeightMatched = false; _flyDescentRelocating = false; _flyHeightRecoveries = 0;
                SetPhase(Phase.FlyMatchEntryHeight); return true;
            }
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
            if (flying && (_phase == Phase.FlyCloseApproach || _phase == Phase.FinalApproach ||
                _phase == Phase.Interact || _phase == Phase.CrossThreshold) &&
                (player.Y < attempt.Threshold.Y || player.Y > attempt.Threshold.Y + FlightApproachMaxClearance))
            {
                // Regain the entry-relative approach band before continuing inward.
                // Final precision may descend from staging to the doorway plane.
                if (++_flyApproachHeightRecoveries > 2)
                {
                    Retry("approach height left its allowed range after two recoveries; try another exterior side", player, false);
                    return true;
                }
                _flyApproachStartHeight = _entrance.FlightApproachHeight;
                Vector3 above = player; above.Y = _flyApproachStartHeight;
                SetPath(Phase.AlignElevation, new[] { above });
                _say($"Fly approach height guard: actual={player.Y:F2}, entry={attempt.Threshold.Y:F2}, " +
                    $"staging={_flyApproachStartHeight:F2}, recovery={_flyApproachHeightRecoveries}/2; halt inward motion and align height diagonally outside first.");
                return true;
            }
            if (_phase == Phase.Interact) return Interact(player, now);
            if (_phase == Phase.AwaitTransition)
            {
                if ((now - _phaseStarted).TotalSeconds >= 5)
                {
                    const string reason = "threshold reached/crossed but no zoning observed";
                    if (flying && !_entrance.FlightEntryHeightVerified && _entrance.TryNextFlyingHeight(reason))
                    {
                        Retry(reason + "; try next mission entrance height", player, false);
                        _flyHeightMatched = false; _flyDescentRelocating = false; _flyHeightRecoveries = 0;
                    }
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
                    // A blocked approach diagnoses the SIDE at the shared
                    // mission height. Do not repeat four low terrain heights
                    // before allowing another side to be considered.
                    Retry(reason, player, true);
                    return true;
                }
                if (result == MovementResult.Moving) return true;
                _legActive = false;
                if (!flying || _phase != Phase.FinalApproach ||
                    LocalMovement.Distance(player, _path[_pathIndex], true) <= FlightHeightTolerance) _pathIndex++;
            }
            if (_pathIndex < _path.Count)
            {
                Vector3 target = _path[_pathIndex];
                if (flying && _phase == Phase.AlignElevation && Math.Abs(target.Y - player.Y) > FlightHeightTolerance)
                {
                    if (!_flight.TryElevationLeg(player, target, _route.Anchor, _entrance.FlightRingRadius,
                        "align 1-2 m above entrance before inward approach", player.Y < attempt.Threshold.Y ? attempt.Normal : Vector3.Zero,
                        out Vector3 alignment))
                    {
                        Retry("no bounded diagonal route to raised staging on this side", player, false); return true;
                    }
                    target = alignment;
                }
                if (flying && _phase == Phase.FinalApproach)
                    target = LocalRoutePlanner.Toward(player, target, FlightApproachLegLength);
                _movement.Begin(target, flying, true, stallSeconds: flying ? 3 : (int?)null,
                    arrivalTolerance: flying && (_phase == Phase.AlignElevation || _phase == Phase.FlyCloseApproach || _phase == Phase.FinalApproach ||
                        _phase == Phase.CrossThreshold) ? FlightHeightTolerance : (float?)null); _legActive = true;
                _say($"Outdoor navigation target: phase={_phase}, sector={attempt.Sector}, source={attempt.Source}, " +
                    $"target=({LocalRoutePlanner.Coordinates(target)}), elevation={attempt.HeightSource}, door={attempt.DoorId}.");
                return true;
            }
            switch (_phase)
            {
                case Phase.AlignElevation:
                    if (flying && LocalRoutePlanner.HorizontalDistance(player, attempt.Threshold) > FlightApproachStartDistance + 1)
                    {
                        // The exterior side and raised staging height are confirmed.
                        // Move inward above the entry target before descending in final legs;
                        // never shrink the bypass ring used to reach another side.
                        Vector3 tangent = new Vector3(-attempt.Normal.Z, 0, attempt.Normal.X);
                        Vector3 close = attempt.Threshold + attempt.Normal * FlightApproachStartDistance + tangent * attempt.Lateral;
                        close.Y = _flyApproachStartHeight;
                        SetPath(Phase.FlyCloseApproach, new[] { close });
                        _say($"Fly close approach: selected sector={attempt.Sector}, target=({LocalRoutePlanner.Coordinates(close)}), " +
                            $"staging distance={FlightApproachStartDistance:F1} m, entry height={attempt.Threshold.Y:F2}; matched 1-2 m above entry, descend during final precision.");
                    }
                    else BeginFinalApproach(player);
                    break;
                case Phase.FlyCloseApproach:
                    BeginFinalApproach(player);
                    break;
                case Phase.FinalApproach:
                    if (attempt.DoorId != Identity.None) SetPhase(Phase.Interact);
                    else BeginCrossing(player, "no live mission Door; inferred normal/proximity threshold crossing");
                    break;
                case Phase.CrossThreshold: SetPhase(Phase.AwaitTransition); break;
            }
            return true;
        }

        private void BeginFinalApproach(Vector3 player)
        {
            EntranceAcquisition.Attempt attempt = _entrance.Active;
            List<Vector3> points = _entrance.FinalPoints(attempt);
            if (_flying)
                for (int i = 0; i < points.Count; i++)
                {
                    // The arrival sphere must not extend below the doorway plane.
                    Vector3 point = points[i]; point.Y += FlightHeightTolerance; points[i] = point;
                }
            SetPath(Phase.FinalApproach, points);
            if (_flying) _say($"Fly final approach: leg cap={FlightApproachLegLength:F2} m, " +
                $"another 20% longer, actual entrance distance={LocalRoutePlanner.HorizontalDistance(player, attempt.Threshold):F2} m; " +
                $"start height={player.Y:F2}, entry height={attempt.Threshold.Y:F2}; approach from above, retain precise threshold points.");
        }

        private bool TickFlyEntryHeight(Vector3 player, DateTime now)
        {
            // Match a fixed entry-relative height, including descent from a
            // higher bypass altitude, before the entrance approach moves inward.
            float height = _entrance.FlightApproachHeight;
            if (_legActive && Math.Abs(player.Y - height) > FlightHeightTolerance)
            {
                MovementResult result = _movement.Tick(); LogProgress(player, now);
                if (result == MovementResult.Moving) return true;
                RecordFlightLeg(_runRecord, player, result); _legActive = false;
                if (result == MovementResult.Stalled)
                {
                    _flight.Blocked(player, _movement.Target);
                    if (++_flyHeightRecoveries >= 6)
                        return Fail("raised entrance staging could not be reached after bounded elevation-corridor recovery; no approach side selected");
                    _flyDescentExterior = _flight.DescentExterior(player, _route.Anchor, height, _flyHeightRecoveries);
                    _flyDescentRelocating = true;
                }
                else _flight.Reached(player);
            }
            if (Math.Abs(player.Y - height) <= FlightHeightTolerance)
            {
                if (_legActive) RecordFlightLeg(_runRecord, player, MovementResult.Moving, "raised staging height available; horizontal leg ended");
                _movement.Halt(); _flyHeightMatched = true; _flyMatchedHeight = _entrance.FlightEntryHeight; _flyDescentRelocating = false;
                _flyApproachStartHeight = height;
                _entrance.FlightHeightMatched(player);
                Vector3 entry = _route.Anchor; entry.Y = _entrance.FlightEntryHeight;
                _runRecord.EntryPoint = NavigationPoint.From(entry);
                _runRecord.ElevationSource = _entrance.FlightEntryHeightSource;
                _runRecord.HeightMatchPoint = NavigationPoint.From(player); _runRecord.HeightMatchTriggerDistance = FlightEntryRadius;
                _learning.Record(_runMemory, _runRecord);
                _say($"Fly raised entrance approach ready: staging target={height:F2}, actual={player.Y:F2}, entry target={_entrance.FlightEntryHeight:F2}, " +
                    $"distance={LocalRoutePlanner.HorizontalDistance(player, _route.Anchor):F2} m, " +
                    $"source={_entrance.FlightEntryHeightSource}; begin approach-side diagnostics at the matched 1-2 m clearance, re-align after bypass.");
                SetPhase(Phase.ProbeExterior); return true;
            }
            Vector3 target;
            if (_flyDescentRelocating && LocalRoutePlanner.HorizontalDistance(player, _flyDescentExterior) > 1)
            {
                target = _flight.Next(player, _flyDescentExterior, _route.Anchor,
                    LocalRoutePlanner.HorizontalDistance(_flyDescentExterior, _route.Anchor));
            }
            else
            {
                _flyDescentRelocating = false;
                target = player; target.Y = height;
                if (!_flight.TryElevationLeg(player, target, _route.Anchor,
                    LocalRoutePlanner.HorizontalDistance(player, _route.Anchor), "raised entrance staging preparation",
                    player.Y < _entrance.FlightEntryHeight ? player - _route.Anchor : Vector3.Zero, out Vector3 elevation))
                    return Fail("no bounded diagonal route to raised entrance staging; approach not started");
                target = elevation;
            }
            SetPhase(Phase.FlyMatchEntryHeight);
            _movement.Begin(target, true, true, stallSeconds: 4, arrivalTolerance: _flyDescentRelocating ? 0.8f : FlightHeightTolerance);
            _legActive = true;
            _say($"Fly height-match target: ({LocalRoutePlanner.Coordinates(target)}), staging height={height:F2}, entry height={_entrance.FlightEntryHeight:F2}, " +
                $"relocating={_flyDescentRelocating}, source={_entrance.FlightEntryHeightSource}; approach side not selected.");
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
            _flyApproachHeightRecoveries = 0;
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
                _entrance.FlyingExteriorReached(player); _flyExteriorActive = false;
                _flyApproachStartHeight = _entrance.FlightApproachHeight;
                attempt.Exterior.Y = _flyApproachStartHeight;
                _say($"Fly approach from above: reached height={player.Y:F2}, staging height={_flyApproachStartHeight:F2}, " +
                    $"entry height={attempt.Threshold.Y:F2}; return diagonally to entry+1.5 m outside before the 6 m close approach.");
                SetPath(Phase.AlignElevation, new[] { attempt.Exterior }); return true;
            }
            Vector3 target = _flight.Next(player, attempt.Exterior, _route.Anchor,
                _entrance.FlightRingRadius, _entrance.PreferredFlightDirection);
            SetPhase(_flight.Strategy == "direct" ? Phase.ProbeExterior : Phase.FlyAvoidObstacle);
            _movement.Begin(target, true, true, stallSeconds: 4); _legActive = true;
            _say($"Fly exterior route: requested sector={attempt.Sector}, actual bearing={bearing * 180 / Math.PI:F1} deg, " +
                $"side change={sideChange:F1} deg, target=({LocalRoutePlanner.Coordinates(target)}), strategy={_flight.Strategy}; " +
                $"mission entry height remains {_entrance.FlightEntryHeight:F2}.");
            return true;
        }
        private static double AngleDelta(double angle)
        { while (angle > Math.PI) angle -= Math.PI * 2; while (angle < -Math.PI) angle += Math.PI * 2; return angle; }

        private bool CoarseTravel(Vector3 player, DateTime now)
        {
            float remaining = LocalRoutePlanner.HorizontalDistance(player, _route.Anchor);
            if (remaining + 0.75f < _coarseBest) { _coarseBest = remaining; _coarseProgress = now; }
            if (_entrance == null && (now - _coarseProgress).TotalSeconds >= _settings.NoProgressSeconds)
                return Fail($"coarse travel made no net horizontal improvement for {_settings.NoProgressSeconds} seconds; remaining={remaining:F2} m, recoveries={_recoveries}");
            if (_legActive)
            {
                ContinueFlyCruise(player, now, remaining);
                // Distant cruise waypoints are horizontal transit markers. The
                // flight client can lose height while covering a long level leg;
                // trying to finish its old Y at the reached X/Z creates a nearly
                // vertical tail and falsely records an obstacle after four seconds.
                // A clearance leg that finishes X/Z but misses Y also has no
                // evidence that its flown corridor was obstructed.
                bool horizontalEndpoint = _flying && _entrance == null &&
                    LocalRoutePlanner.HorizontalDistance(player, _movement.Target) <= 1.5f &&
                    Math.Abs(player.Y - _movement.Target.Y) > 0.8f;
                bool passedCruiseWaypoint = horizontalEndpoint && _phase == Phase.FlyToEntrance &&
                    remaining > FlightEntryRadius + 8;
                bool incompleteClearance = horizontalEndpoint && _phase == Phase.FlyClearance;
                if (passedCruiseWaypoint || incompleteClearance)
                {
                    Vector3 oldTarget = _movement.Target;
                    float heightGap = oldTarget.Y - player.Y;
                    RecordFlightLeg(_runRecord, player, MovementResult.Moving,
                        $"{(passedCruiseWaypoint ? "cruise waypoint passed horizontally" : "clearance horizontal leg ended before height match")}; height gap={heightGap:F2} m");
                    _movement.Halt(); _legActive = false;
                    _runRecord.StallSeconds = 0;
                    // Recheck the advisory cruise clearance from the observed
                    // height. A new diagonal leg supplies horizontal runway;
                    // the missed waypoint is not a failed flight corridor.
                    bool needsClearance = passedCruiseWaypoint && !float.IsNaN(_flight.CruiseHeight) &&
                        player.Y < _flight.CruiseHeight - 1.5f;
                    if (needsClearance) _flyCruiseReady = false;
                    // A clearance attempt which covered its horizontal run
                    // without gaining height should enter reactive transit,
                    // rather than repeat the same climb or invent a collision.
                    if (incompleteClearance) { _recoveries++; _flyCruiseReady = true; }
                    _say($"Fly {(passedCruiseWaypoint ? "cruise waypoint passed" : "clearance leg incomplete")} at X/Z: target=({LocalRoutePlanner.Coordinates(oldTarget)}), " +
                        $"actual=({LocalRoutePlanner.Coordinates(player)}), height gap={heightGap:F2} m, " +
                        $"clearance recheck={needsClearance}; continue from actual height without marking an obstruction.");
                }
                else
                {
                    MovementResult result = _movement.Tick(); LogProgress(player, now);
                    if (_flying && result != MovementResult.Moving) RecordFlightLeg(_runRecord, player, result);
                    _runRecord.StallSeconds = (float)_movement.StallSeconds;
                    if (result == MovementResult.Moving)
                    {
                        _runRecord.LastPosition = NavigationPoint.From(player);
                        _runRecord.LastTarget = NavigationPoint.From(_movement.Target);
                        _runRecord.ProgressMetres = Math.Max(0,
                            LocalRoutePlanner.HorizontalDistance(_route.Origin, _route.Anchor) - _coarseBest);
                        return true;
                    }
                    _legActive = false;
                    if (result == MovementResult.Stalled)
                    {
                        _recoveries++; if (_flying) _flight.Blocked(player, _movement.Target);
                        if (_phase == Phase.FlyClearance)
                        {
                            _flyCruiseReady = true;
                            _say("Fly initial clearance climb blocked; reactive over/around transit will seek clearance from actual position.");
                        }
                    }
                    else if (_flying) _flight.Reached(player);
                    if (_phase == Phase.FlyClearance && result == MovementResult.Reached) _flyCruiseReady = true;
                }
                _runRecord.LastPosition = NavigationPoint.From(player); _runRecord.LastTarget = NavigationPoint.From(_movement.Target);
                _runRecord.ProgressMetres = Math.Max(0, LocalRoutePlanner.HorizontalDistance(_route.Origin, _route.Anchor) - _coarseBest);
            }
            Vector3 destination = _route.Anchor; destination.Y = player.Y;
            if (_flying)
            {
                if (!_flyCruiseReady)
                {
                    float cruise = _flight.PrepareCruise(player, _route.Anchor); _runRecord.CruiseHeight = cruise;
                    if (player.Y < cruise - 0.8f)
                    {
                        float clearanceRing = remaining <= _settings.MaxFlightBypassRadius ? remaining : 0;
                        Vector3 up = _flight.ElevationLeg(player, new Vector3(_route.Anchor.X, cruise, _route.Anchor.Z),
                            _route.Anchor, clearanceRing, "initial cruise clearance");
                        SetPhase(Phase.FlyClearance); _movement.Begin(up, true, true, stallSeconds: 4);
                        _legActive = true; return true;
                    }
                    _flyCruiseReady = true;
                }
                // Stop inside the 10 m trigger without flying through the
                // marker before entrance height and side have been diagnosed.
                destination = FlyTransitGoal(player);
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

        private Vector3 FlyTransitGoal(Vector3 player)
        {
            Vector3 incoming = player - _route.Anchor; incoming.Y = 0;
            Vector3 goal = _route.Anchor;
            if (LocalRoutePlanner.HorizontalDistance(player, goal) > 0.1f)
                goal += incoming.Normalize() * (FlightEntryRadius - 1);
            goal.Y = player.Y; return goal;
        }

        private void ContinueFlyCruise(Vector3 player, DateTime now, float anchorDistance)
        {
            if (!_flying || !_flyCruiseReady || _entrance != null || _phase != Phase.FlyToEntrance ||
                anchorDistance <= FlightEntryRadius + 8 || now < _nextCruiseExtensionCheck) return;
            float remaining = Vector3.Distance(player, _movement.Target);
            if (remaining > 8) return;
            _nextCruiseExtensionCheck = now.AddMilliseconds(250);
            if (!_flight.TryCruiseContinuation(player, FlyTransitGoal(player), out Vector3 next) ||
                !_movement.CanExtendFlightTarget(next)) return;
            // This is a continued transit segment, not an arrival or a success.
            RecordFlightLeg(_runRecord, player, MovementResult.Moving, "continued without stopping");
            _movement.ExtendFlightTarget(next);
            _say($"Fly cruise continuation: previous remaining={remaining:F2} m, " +
                $"next=({LocalRoutePlanner.Coordinates(next)}), leg distance={_movement.StartDistance:F2} m, " +
                $"anchor distance={anchorDistance:F2} m; keep flying, no waypoint full stop.");
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
            if (_flying) point.Y += FlightHeightTolerance;
            _say($"Entrance crossing: mission={_route.Mission.Id.Instance}, sector={attempt.Sector}, door={attempt.DoorId}, " +
                $"target=({LocalRoutePlanner.Coordinates(point)}), reason={reason}; arrival cannot prove entry.");
            SetPath(Phase.CrossThreshold, new[] { point });
        }

        private static bool DoorWithinUseRange(Vector3 player, Door door) =>
            LocalRoutePlanner.HorizontalDistance(player, door.Position) <= 2 && Vector3.Distance(player, door.Position) <= 3.5f;

        private void RecordFlightLeg(EntranceAttemptRecord record, Vector3 player, MovementResult result, string outcome = null)
        {
            record.FlightLegs.Add(new FlightLegRecord { FinishedUtc = DateTime.UtcNow,
                Origin = NavigationPoint.From(_movement.StartPosition), Target = NavigationPoint.From(_movement.Target),
                Position = NavigationPoint.From(player), Stage = _phase.ToString(),
                Strategy = _flyExteriorActive || _entrance == null ? _flight.Strategy :
                    _phase == Phase.FlyMatchEntryHeight ? (_flyDescentRelocating ? _flight.Strategy : "raised entrance staging preparation") :
                    "entry alignment/approach", Result = outcome ?? result.ToString() });
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
