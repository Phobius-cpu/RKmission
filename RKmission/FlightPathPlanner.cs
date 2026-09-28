using System;
using System.Collections.Generic;
using System.Linq;
using AOSharp.Common.GameData;

namespace RKmission
{
    // One reactive 3D planner for Fly transit and exterior-side relocation.
    // LocalMovement remains the only executor. A 10 m height match precedes
    // side diagnosis; subsequent bypass flight retains altitude until rejoining
    // that same mission entry height, never the terrain at an orbit waypoint.
    internal sealed class FlightPathPlanner
    {
        private sealed class Choice
        {
            public string Kind;
            public Vector3 First;
            public float Score;
            public int Direction;
            public bool Obstructed;
        }
        private sealed class Failure { public Vector3 Position, Target; }
        private readonly List<Failure> _failed = new List<Failure>();
        private readonly List<Vector3> _visited = new List<Vector3>();
        private readonly Action<string> _say;
        private readonly float _ceiling;
        private readonly float _maximumRadius;
        private readonly float _minimumRadius;
        private readonly float _cruiseClearance;
        private int _sidePreference, _perimeterPreference;
        private int _committedDirection;
        private bool _committedPerimeter, _hasGoal;
        private Vector3 _goal;
        private float _overpassHeight = float.NaN;
        public string Strategy { get; private set; } = "direct";
        public int BypassDirection { get; private set; }
        public string OverpassResult { get; private set; }
        public float CruiseHeight { get; private set; } = float.NaN;

        public FlightPathPlanner(Vector3 origin, OutdoorNavigationSettings settings, Action<string> say)
        {
            _ceiling = origin.Y + settings.FlightClimbLimit; _maximumRadius = settings.MaxFlightBypassRadius;
            _minimumRadius = settings.ProbeRadius;
            _cruiseClearance = settings.FlightCruiseClearance; _say = say;
        }

        public float PrepareCruise(Vector3 player, Vector3 anchor)
        {
            if (!float.IsNaN(CruiseHeight)) return CruiseHeight;
            CruiseHeight = LocalRoutePlanner.AdvisoryCruiseHeight(anchor, player, _ceiling, _cruiseClearance);
            _overpassHeight = CruiseHeight; Strategy = "cruise clearance";
            _say($"Fly cruise clearance: current height={player.Y:F2}, target height={CruiseHeight:F2}, " +
                $"clearance={_cruiseClearance:F1} m, climb ceiling={_ceiling:F2}; gain elevation before transit, scene hints advisory.");
            return CruiseHeight;
        }

        public Vector3 DescentExterior(Vector3 player, Vector3 anchor, float entryHeight, int retry)
        {
            float radius = Math.Min(_maximumRadius, Math.Max(_minimumRadius, LocalRoutePlanner.HorizontalDistance(player, anchor) + 4));
            double bearing = LocalRoutePlanner.Angle(player - anchor);
            var choices = new List<Choice>();
            foreach (int offset in new[] { 0, -1, 1, -2, 2, -3, 3, 4 })
            {
                Vector3 point = anchor + LocalRoutePlanner.Direction(bearing + offset * Math.PI / 4) * radius;
                point.Y = player.Y;
                Vector3 below = point; below.Y = entryHeight;
                choices.Add(new Choice { First = point,
                    Score = Vector3.Distance(player, point) +
                        (LocalRoutePlanner.FlightCorridor(point, below, out _) ? 80 : 0) +
                        (RepeatsFailure(point, below) ? 120 : 0) });
            }
            Vector3 result = choices.OrderBy(x => x.Score).First().First;
            _say($"Fly height-match recovery: attempt={retry}, desired entry height={entryHeight:F2}, " +
                $"exterior=({LocalRoutePlanner.Coordinates(result)}), radius={radius:F1}; relocate before retrying vertical alignment, no new floor height.");
            return result;
        }

        public void Blocked(Vector3 position, Vector3 target)
        {
            _failed.Add(new Failure { Position = position, Target = target });
            if (_committedDirection != 0)
            {
                _say("Fly bypass direction released after actual stall; compare retained opposite direction and overpass.");
                _committedDirection = 0;
            }
            if (Strategy == "over") OverpassResult = "ascent blocked; compare around route next";
            if (_failed.Count > 48) _failed.RemoveAt(0);
            _say($"Fly obstruction observed: position=({LocalRoutePlanner.Coordinates(position)}), " +
                $"blocked target=({LocalRoutePlanner.Coordinates(target)}); compare over/around now, retain failed corridor.");
        }

        public void Reached(Vector3 point)
        {
            _visited.Add(point); if (_visited.Count > 48) _visited.RemoveAt(0);
            if (Strategy == "over" && point.Y >= _overpassHeight - 0.8f)
                OverpassResult = "ascent completed; maintain higher height through obstacle bypass";
        }

        public Vector3 Next(Vector3 player, Vector3 destination, Vector3 anchor,
            float perimeterRadius = 0, int preferredDirection = 0)
        {
            // Transit always retains the ACTUAL flight altitude, including a
            // successful climb. It cannot descend toward the old goal's Y.
            destination.Y = player.Y;
            bool perimeter = perimeterRadius > 0;
            if (!_hasGoal || LocalRoutePlanner.HorizontalDistance(destination, _goal) > 2 || _committedPerimeter != perimeter)
            { _committedDirection = 0; _goal = destination; _hasGoal = true; }
            _committedPerimeter = perimeter;
            Vector3 direct = LocalRoutePlanner.Toward(player, destination, 20);
            bool blocked = LocalRoutePlanner.FlightCorridor(player, direct, out Vector3 hit);
            bool observed = RepeatsFailure(player, direct);
            bool aboveBypass = !float.IsNaN(_overpassHeight) && player.Y >= _overpassHeight - 0.8f && !blocked;
            float radius = LocalRoutePlanner.HorizontalDistance(player, anchor);
            float closestRadius = ClosestRadius(player, direct, anchor);
            // The protected footprint applies to side-to-side chords, not an
            // escape radially OUT of it. A small rounding allowance also covers
            // the near-zero radius at the mission marker after crossing.
            bool outwardExit = perimeter && radius < perimeterRadius &&
                LocalRoutePlanner.HorizontalDistance(direct, anchor) > radius + 0.5f &&
                closestRadius >= radius - 0.1f;
            bool cutsStructure = perimeter && !aboveBypass && !outwardExit && closestRadius < perimeterRadius - 0.8f;
            if (!blocked && !observed && !cutsStructure)
            {
                if (outwardExit)
                    _say($"Fly outward return: radius={radius:F2} -> {LocalRoutePlanner.HorizontalDistance(direct, anchor):F2}, " +
                        $"height={player.Y:F2}; clear outward corridor, no automatic overpass.");
                _committedDirection = 0; Strategy = "direct"; return direct;
            }

            var choices = new List<Choice>();
            Vector3 forward = destination - player; forward.Y = 0;
            if (LocalRoutePlanner.HorizontalDistance(forward, Vector3.Zero) < 0.1f) forward = new Vector3(1, 0, 0);
            forward = forward.Normalize();
            Vector3 sideways = new Vector3(-forward.Z, 0, forward.X);
            if (perimeterRadius > 0)
            {
                // Around a mission structure, compare BOTH directions on a safe
                // ring. No chord through its footprint is an around candidate.
                float safeRadius = Math.Max(perimeterRadius, radius);
                double bearing = LocalRoutePlanner.Angle(player - anchor);
                if (radius < perimeterRadius - 0.8f)
                {
                    Vector3 outward = anchor + LocalRoutePlanner.Direction(bearing) * Math.Min(perimeterRadius, radius + 4);
                    outward.Y = player.Y; Add(choices, "around: outward clearance", player, outward, destination, 0, 0);
                    foreach (int direction in new[] { -1, 1 })
                    {
                        Vector3 escape = anchor + LocalRoutePlanner.Direction(bearing + direction * Math.PI / 12) *
                            Math.Min(perimeterRadius, radius + 4);
                        escape.Y = player.Y;
                        Add(choices, "around: outward " + EntranceOrbit.Name(direction), player, escape, destination, 1, direction);
                    }
                }
                else foreach (int direction in new[] { -1, 1 })
                {
                    double gap = Positive(direction * (LocalRoutePlanner.Angle(destination - anchor) - bearing));
                    // Do not step past a nearby target bearing and immediately
                    // reverse next tick. At the bearing, a blocked radial leg
                    // still permits a modest tangential search or an overpass.
                    double step = gap > Math.PI / 180 ? Math.Min(Math.PI / 12, gap) : Math.PI / 12;
                    Vector3 next = anchor + LocalRoutePlanner.Direction(bearing + direction * step) *
                        Math.Min(_maximumRadius + 0.5f, safeRadius / (float)Math.Cos(step / 2));
                    next.Y = player.Y;
                    int preference = _perimeterPreference != 0 ? _perimeterPreference : preferredDirection;
                    Add(choices, "around: " + EntranceOrbit.Name(direction), player, next, destination,
                        (float)(gap * safeRadius) - Vector3.Distance(next, destination) +
                        (preference != 0 && preference != direction ? 2 : 0), direction, true);
                }
            }
            else foreach (int direction in new[] { -1, 1 })
                foreach (float width in new[] { 4f, 8f, 16f })
                {
                    // Retreat slightly from the blocked face before moving past
                    // its edge. Rays score both legs, actual motion decides.
                    Vector3 next = player + sideways * (direction * width) - forward * 2;
                    Add(choices, "around: " + (direction < 0 ? "right" : "left"), player, next, destination,
                        _sidePreference != 0 && _sidePreference != direction ? 2 : 0, direction);
                }

            // Over and around compete immediately. Climbing is not postponed
            // until a predetermined number of perimeter failures.
            float roof = LocalRoutePlanner.AdvisoryOverpassHeight(anchor, player,
                blocked ? hit : destination, player.Y);
            foreach (float rise in new[] { 6f, 12f, 24f })
            {
                Vector3 up = player; up.Y = Math.Min(_ceiling, Math.Max(player.Y + rise, roof));
                if (up.Y <= player.Y + 1) continue;
                Vector3 across = destination; across.Y = up.Y;
                float cost = Vector3.Distance(player, up) + Vector3.Distance(up, across);
                int hits = (LocalRoutePlanner.FlightCorridor(player, up, out _) ? 1 : 0) +
                    (LocalRoutePlanner.FlightCorridor(up, across, out _) ? 1 : 0);
                choices.Add(new Choice { Kind = "over", First = up,
                    Score = cost + hits * 60 + (RepeatsFailure(player, up) ? 120 : 0) });
            }
            if (_committedDirection != 0)
            {
                Choice continuation = choices.Where(x => x.Direction == _committedDirection && !x.Obstructed)
                    .OrderBy(x => x.Score).FirstOrDefault();
                if (continuation != null)
                {
                    // Keep a direction that still makes a clear next leg.
                    // Overpass and outward correction remain available; the
                    // other tangential direction is retained until blockage.
                    choices.RemoveAll(x => x.Direction != 0 && x.Direction != _committedDirection);
                }
                else
                {
                    _say("Fly committed bypass corridor now obstructed; release direction and compare opposite/over routes.");
                    _committedDirection = 0;
                }
            }
            Choice best = choices.OrderBy(x => x.Score).First();
            Strategy = best.Kind;
            if (best.Kind == "over")
            { _committedDirection = 0; _overpassHeight = best.First.Y; OverpassResult = "ascending to advisory bypass height"; }
            if (best.Direction != 0)
            {
                _committedDirection = best.Direction;
                if (perimeterRadius > 0) _perimeterPreference = BypassDirection = best.Direction;
                else _sidePreference = best.Direction;
            }
            _say($"Fly route choice: {best.Kind}, over cost={BestCost(choices, "over")}, around cost={BestCost(choices, "around")}, " +
                $"current height={player.Y:F2}, next=({LocalRoutePlanner.Coordinates(best.First)}), " +
                $"ray hit={blocked}, observed block={observed}, footprint crossing={cutsStructure}, " +
                $"committed direction={_committedDirection}; no floor descent during bypass.");
            return best.First;
        }

        private void Add(List<Choice> choices, string kind, Vector3 player, Vector3 next, Vector3 goal, float bias, int direction, bool ring = false)
        {
            next.Y = player.Y;
            // The continuation ray scores the solution, but execute just the
            // first leg and re-evaluate from the next actual position.
            float score = Vector3.Distance(player, next) + Vector3.Distance(next, goal) + bias;
            bool obstructed = LocalRoutePlanner.FlightCorridor(player, next, out _) || RepeatsFailure(player, next);
            if (obstructed) score += 80;
            if (!ring && LocalRoutePlanner.FlightCorridor(next, goal, out _)) score += 30;
            if (RepeatsFailure(player, next)) score += 120;
            // Short final angular legs naturally lie near the current reached
            // point; penalize returning to earlier positions, not advancing
            // from the position where this leg begins.
            score += _visited.Count(x => Vector3.Distance(x, next) < 2 && Vector3.Distance(x, player) > 2) * 25;
            choices.Add(new Choice { Kind = kind, First = next, Score = score, Direction = direction, Obstructed = obstructed });
        }
        private bool RepeatsFailure(Vector3 player, Vector3 next)
        {
            Vector3 proposed = next - player;
            if (Vector3.Distance(proposed, Vector3.Zero) < 0.1f) return false;
            return _failed.Any(x => Vector3.Distance(x.Position, player) < 5 &&
                Vector3.Distance(x.Target - x.Position, Vector3.Zero) > 0.1f &&
                Vector3.Dot((x.Target - x.Position).Normalize(), proposed.Normalize()) > 0.8f);
        }
        private static string BestCost(List<Choice> choices, string kind)
        {
            Choice best = choices.Where(x => x.Kind.StartsWith(kind, StringComparison.Ordinal)).OrderBy(x => x.Score).FirstOrDefault();
            return best == null ? "unavailable" : best.Score.ToString("F1");
        }
        private static float ClosestRadius(Vector3 from, Vector3 to, Vector3 anchor)
        {
            Vector3 delta = to - from; delta.Y = 0;
            float lengthSquared = Vector3.Dot(delta, delta);
            float fraction = lengthSquared < 0.01f ? 0 : Math.Max(0, Math.Min(1, Vector3.Dot(anchor - from, delta) / lengthSquared));
            return LocalRoutePlanner.HorizontalDistance(from + delta * fraction, anchor);
        }
        private static double Positive(double angle) => (angle % (Math.PI * 2) + Math.PI * 2) % (Math.PI * 2);
    }
}
