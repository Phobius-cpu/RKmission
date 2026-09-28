using System;
using AOSharp.Common.GameData;

namespace RKmission
{
    // Ground perimeter planner only. LocalMissionTravel owns execution through
    // LocalMovement. Replan each short leg from the observed position, never
    // advance a precomputed arc after scraping a wall.
    internal sealed class EntranceOrbit
    {
        private enum Leg { Outward, Probe, Tangent }
        private readonly Vector3 _anchor, _exterior;
        private readonly OutdoorNavigationSettings _settings;
        private readonly Action<string> _say;
        private readonly bool _newSideRequired;
        private readonly double _startBearing, _targetBearing, _requiredChange;
        private readonly float _maximumRadius;
        private Leg _leg;
        private Vector3 _legOrigin;
        private double _lastBearing, _unwrappedBearing, _angularTravel;
        private readonly double[] _bestAngularProgress = new double[2];
        private double _probeStartBearing, _probeBestProgress, _firstProbeProgress;
        private float _probeStartRadius, _firstProbeScore;
        private int _probeDirection, _firstProbeDirection, _stableLegs, _stalls;
        private bool _probeBoth, _secondProbe, _fallbackUsed, _outwardBlocked;
        private DateTime _legStarted, _nextLog;
        public int Direction { get; private set; }
        public float Radius { get; private set; }
        public float AngularSpanDegrees => (float)(_angularTravel * 180 / Math.PI);
        public bool Finished { get; private set; }
        public bool Failed { get; private set; }
        public bool MadeNewProgress { get; private set; }
        public bool ProbeExpired => _leg == Leg.Probe && (DateTime.UtcNow - _legStarted).TotalSeconds >= 3;
        private float MinimumRadius => Radius - 1;

        public EntranceOrbit(Vector3 anchor, Vector3 exterior, Vector3 player,
            bool newSideRequired, float wallRadius, float rememberedRadius, int preferredDirection,
            OutdoorNavigationSettings settings, Action<string> say)
        {
            _anchor = anchor; _exterior = exterior; _settings = settings; _say = say;
            _newSideRequired = newSideRequired; _maximumRadius = settings.MaxProbeRadius;
            _startBearing = _lastBearing = _unwrappedBearing = Bearing(player);
            _targetBearing = Bearing(exterior);
            double gap = Math.Abs(Delta(_targetBearing - _startBearing));
            _requiredChange = Math.Min(Math.PI / 6, gap);
            Radius = Math.Min(_maximumRadius, Math.Max(LocalRoutePlanner.HorizontalDistance(exterior, anchor),
                Math.Max(rememberedRadius, newSideRequired ? wallRadius + 4 : 0)));
            // -1 decreases atan2(X/Z) bearing (CW); +1 increases it (CCW).
            _firstProbeDirection = preferredDirection == -1 || preferredDirection == 1 ? preferredDirection :
                Delta(_targetBearing - _startBearing) >= 0 ? 1 : -1;
            _say($"{(newSideRequired ? "Entering OrbitBypass" : "Perimeter acquisition")}: bearing={Degrees(_startBearing):F1} deg, " +
                $"target bearing={Degrees(_targetBearing):F1} deg, radius={Radius:F1} m, band={MinimumRadius:F1}-{Radius + 1:F1} m; " +
                "outward clearance precedes tangential direction selection.");
        }

        public void Observe(Vector3 player)
        {
            double bearing = Bearing(player);
            double change = Delta(bearing - _lastBearing);
            _angularTravel += Math.Abs(change);
            _unwrappedBearing += change; _lastBearing = bearing;
            float radius = LocalRoutePlanner.HorizontalDistance(player, _anchor);
            double directed = Direction == 0 ? 0 : (_unwrappedBearing - _startBearing) * Direction;
            int directionIndex = Direction < 0 ? 0 : 1;
            MadeNewProgress = radius >= MinimumRadius && directed > _bestAngularProgress[directionIndex] + Math.PI / 90;
            if (MadeNewProgress) _bestAngularProgress[directionIndex] = directed;
            if (_leg == Leg.Probe && radius >= MinimumRadius)
                _probeBestProgress = Math.Max(_probeBestProgress, (_unwrappedBearing - _probeStartBearing) * _probeDirection);
            if (DateTime.UtcNow < _nextLog) return;
            _nextLog = DateTime.UtcNow.AddSeconds(3);
            _say($"Orbit progress: bearing={Degrees(bearing):F1} deg, target bearing={Degrees(_targetBearing):F1} deg, " +
                $"radius={radius:F2}/{Radius:F1} m, direction={Name(Direction)}, angular progress={AngularSpanDegrees:F1} deg, " +
                $"net side change={Math.Abs(Delta(bearing - _startBearing)) * 180 / Math.PI:F1} deg, stable legs={_stableLegs}.");
        }

        public bool Next(Vector3 player, out Vector3 target)
        {
            target = player;
            if (Failed || Finished) return false;
            float radius = LocalRoutePlanner.HorizontalDistance(player, _anchor);
            double bearing = Bearing(player), gap = Delta(_targetBearing - bearing);
            // Match the executor's 0.8 m arrival tolerance, including the small
            // diagnostic ring, so a sub-tolerance final arc cannot become a loop.
            double arrivalAngle = Math.Max(Math.PI / 45, Math.Asin(Math.Min(1, 0.9 / Math.Max(1, radius))));
            // Outward is radial away from the structure. A small angular step is
            // used only if that outward corridor itself stalled; it cannot cut inward.
            if (radius < MinimumRadius || radius > Radius + 1)
            {
                _leg = Leg.Outward;
                double escape = _outwardBlocked ? (Direction == 0 ? _firstProbeDirection : Direction) * Math.PI / 24 : 0;
                float stepRadius = radius < MinimumRadius ? Math.Min(Radius, radius + 3) : Radius;
                target = Point(bearing + escape, stepRadius, player);
                _say($"Orbit outward correction: radius={radius:F2} m, safe ring={Radius:F1} m, " +
                    $"target=({LocalRoutePlanner.Coordinates(target)}), escape angle={escape * 180 / Math.PI:F1} deg.");
                return StartLeg(player, target);
            }
            if (!_newSideRequired && Math.Abs(gap) <= arrivalAngle)
            { ConfirmSide(player); return false; }
            // Arrival is actual bearing + ring clearance + successful movement,
            // never a requested sector label or an exhausted waypoint list.
            bool changed = !_newSideRequired || (_requiredChange >= Math.PI / 12 &&
                Math.Abs(Delta(bearing - _startBearing)) >= _requiredChange - arrivalAngle && _stableLegs >= 2);
            if (Math.Abs(gap) <= arrivalAngle && changed &&
                LocalRoutePlanner.HorizontalDistance(player, _exterior) <= Math.Abs(Radius - LocalRoutePlanner.HorizontalDistance(_exterior, _anchor)) + 1.5f)
            {
                ConfirmSide(player); return false;
            }
            if (Direction == 0)
            {
                if (_probeDirection == 0) ChooseProbes(player);
                _leg = Leg.Probe; _probeStartBearing = _unwrappedBearing; _probeBestProgress = 0; _probeStartRadius = radius;
                target = Tangent(player, _probeDirection, Math.PI / 12);
                _say($"Orbit direction probe: {Name(_probeDirection)}, corridor={(Corridor(player, target) ? "clear hint" : "uncertain/hit hint")}; observe up to 3 s.");
                return StartLeg(player, target);
            }
            _leg = Leg.Tangent;
            double remaining = Direction > 0 ? Positive(_targetBearing - bearing) : Positive(bearing - _targetBearing);
            if (Math.Abs(gap) <= arrivalAngle && !changed) remaining = Math.PI / 12;
            // Straight segments connecting ring points sag inward. Inflate their
            // endpoints so the segment's closest radius stays outside the band.
            target = Tangent(player, Direction, Math.Min(Math.PI / 12, Math.Max(Math.PI / 90, remaining)));
            return StartLeg(player, target);
        }

        public void LegEnded(Vector3 player, bool stalled)
        {
            Observe(player);
            if (_leg == Leg.Probe)
            {
                float radialGain = LocalRoutePlanner.HorizontalDistance(player, _anchor) - _probeStartRadius;
                float score = (float)(_probeBestProgress * Radius) + Math.Max(-2, Math.Min(2, radialGain)) * 0.25f - (stalled ? 2 : 0);
                _say($"Orbit probe result: direction={Name(_probeDirection)}, angular progress={_probeBestProgress * 180 / Math.PI:F1} deg, " +
                    $"radial progress={radialGain:F2} m, score={score:F2}, stalled={stalled}.");
                if (_probeBoth && !_secondProbe)
                { _firstProbeScore = score; _firstProbeProgress = _probeBestProgress; _secondProbe = true; _probeDirection = -_firstProbeDirection; return; }
                Direction = _probeBoth && _firstProbeScore >= score ? _firstProbeDirection : _probeDirection;
                _stableLegs += (_probeBestProgress >= Math.PI / 180 ? 1 : 0) +
                    (_probeBoth && _firstProbeProgress >= Math.PI / 180 ? 1 : 0);
                bool neitherProgressed = _probeBoth ? Math.Max(_firstProbeScore, score) < 0.5f : score < 0.5f;
                _say($"Orbit CW/CCW choice: {Name(Direction)}; preserve {Name(-Direction)} as fallback; measured probes take precedence over hints.");
                if (neitherProgressed) Recover(player, "both tangential directions made insufficient progress");
                return;
            }
            if (stalled)
            {
                _stableLegs = 0;
                if (_leg == Leg.Outward) _outwardBlocked = true;
                Recover(player, "observed " + _leg + " stall"); return;
            }
            if (_leg == Leg.Outward) _outwardBlocked = false;
            if ((_leg == Leg.Tangent || _leg == Leg.Outward) && LocalRoutePlanner.HorizontalDistance(player, _anchor) >= MinimumRadius &&
                Delta(Bearing(player) - Bearing(_legOrigin)) * (Direction == 0 ? _firstProbeDirection : Direction) >= Math.PI / 180) _stableLegs++;
        }

        private void Recover(Vector3 player, string reason)
        {
            _stalls++;
            float radius = LocalRoutePlanner.HorizontalDistance(player, _anchor);
            if (Radius < _maximumRadius - 0.1f)
            {
                Radius = Math.Min(_maximumRadius, Math.Max(Radius + 3, radius + 4));
                _say($"Orbit outward correction: {reason}; widen safe ring to {Radius:F1} m before continuing {Name(Direction)}.");
                // A failed escape should compare the opposite escape, not repeat it.
                if (_outwardBlocked)
                {
                    if (Direction == 0) _firstProbeDirection = -_firstProbeDirection;
                    else Direction = -Direction;
                }
                return;
            }
            if (!_fallbackUsed)
            {
                _fallbackUsed = true;
                if (Direction == 0) _firstProbeDirection = -_firstProbeDirection;
                else Direction = -Direction;
                _say($"Orbit fallback: {reason}; try preserved {Name(Direction == 0 ? _firstProbeDirection : Direction)} at radius={Radius:F1} m."); return;
            }
            Failed = true;
            _say($"OrbitBypass exhausted: {reason}, radius={Radius:F1} m, recovery stalls={_stalls}; requested side was not reached.");
        }

        private void ChooseProbes(Vector3 player)
        {
            bool preferredClear = Corridor(player, Tangent(player, _firstProbeDirection, Math.PI / 12));
            bool otherClear = Corridor(player, Tangent(player, -_firstProbeDirection, Math.PI / 12));
            _probeBoth = preferredClear == otherClear;
            if (!preferredClear && otherClear) _firstProbeDirection = -_firstProbeDirection;
            _probeDirection = _firstProbeDirection;
        }
        private Vector3 Tangent(Vector3 player, int direction, double step)
        {
            // Endpoints use the observed radius if it is already safely outside.
            // Steps <=15 degrees bound inward chord sag to <0.2 m on default rings.
            float radius = Math.Max(Radius, LocalRoutePlanner.HorizontalDistance(player, _anchor));
            return Point(Bearing(player) + direction * step, Math.Min(_maximumRadius + 0.5f, radius / (float)Math.Cos(step / 2)), player);
        }
        private Vector3 Point(double bearing, float radius, Vector3 player)
        {
            Vector3 point = _anchor + LocalRoutePlanner.Direction(bearing) * radius;
            point = LocalRoutePlanner.LocalElevation(point, player, false, _settings, out _);
            return point;
        }
        private bool Corridor(Vector3 player, Vector3 target) => LocalRoutePlanner.ClearSegment(
            player + Vector3.Up, target + Vector3.Up);
        private bool StartLeg(Vector3 player, Vector3 target)
        { _legOrigin = player; _legStarted = DateTime.UtcNow; return AcceptedMissions.Finite(target); }
        private void ConfirmSide(Vector3 player)
        {
            Finished = true;
            _say($"Reached-new-side confirmation: bearing={Degrees(Bearing(player)):F1} deg, target={Degrees(_targetBearing):F1} deg, " +
                $"radius={LocalRoutePlanner.HorizontalDistance(player, _anchor):F2} m, direction={Name(Direction)}, angular span={AngularSpanDegrees:F1} deg, " +
                $"stable movement legs={_stableLegs}; resume FinalApproach from this observed exterior side.");
        }
        private double Bearing(Vector3 player) => LocalRoutePlanner.Angle(player - _anchor);
        private static double Delta(double angle)
        { while (angle > Math.PI) angle -= Math.PI * 2; while (angle < -Math.PI) angle += Math.PI * 2; return angle; }
        private static double Positive(double angle) => (angle % (Math.PI * 2) + Math.PI * 2) % (Math.PI * 2);
        private static double Degrees(double angle) => Positive(angle) * 180 / Math.PI;
        public static string Name(int direction) => direction < 0 ? "CW" : direction > 0 ? "CCW" : "probing";
    }
}
