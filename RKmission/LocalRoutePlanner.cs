using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AOSharp.Common.GameData;
using AOSharp.Core;
using AOSharp.Pathfinding;
using SharpNav;
using SharpNav.Pathfinding;
using SVector = SharpNav.Geometry.Vector3;

namespace RKmission
{
    internal sealed class LocalRoute
    {
        public AcceptedMission Mission;
        public Vector3 Origin, Entrance, EntrancePoint, FlightApproach, CruiseEnd;
        public float EntranceDistance, Cost;
        public bool GroundUsesMesh, EntranceIsFloor;
        public string Reason, HeightSource;
    }

    internal static class LocalRoutePlanner
    {
        // Compare accepted anchors in the active mode; defer terrain/flight planning
        // until selection. Use complete ground mesh costs when available.
        public static LocalRoute Estimate(AcceptedMission mission, Vector3 origin, bool flying)
        {
            if (!AcceptedMissions.Finite(origin) || !AcceptedMissions.Finite(mission.Entrance)) return null;
            float distance = HorizontalDistance(origin, mission.Entrance);
            var route = new LocalRoute { Mission = mission, Origin = origin, Entrance = mission.Entrance,
                EntrancePoint = mission.Entrance, EntranceDistance = distance, Cost = distance };
            if (flying)
                route.Reason = "horizontal flight estimate to mission anchor; final live height pending";
            else
            {
                route.GroundUsesMesh = TryGroundCost(origin, mission.Entrance, out float ground);
                route.Cost = route.GroundUsesMesh ? ground : distance;
                route.Reason = route.GroundUsesMesh ? "complete ground navmesh path to mission anchor" :
                    "direct ground estimate; outdoor mesh absent or endpoints disconnected";
            }
            return route;
        }

        // Resolve only the selected mission's coarse geometry. Preserve the cost
        // used to rank its original quest anchor, independent of measured hints.
        public static LocalRoute Plan(LocalRoute route, bool flying)
        {
            AcceptedMission mission = route.Mission;
            Vector3 origin = route.Origin;
            if (!AcceptedMissions.Finite(origin) || !AcceptedMissions.Finite(mission.Entrance))
                return null;
            bool measured = TryMeasuredEntrance(mission, out Vector3 entrance);
            bool heightVerified = measured || !HeightMissing(mission.Entrance);
            bool floor = false;
            string source = measured ? "user-measured search hint" : heightVerified ? "quest anchor height, provisional" : "player height, provisional";
            if (!measured)
            {
                entrance = ResolveEntranceHeight(mission.Entrance, origin, out heightVerified);
                floor = heightVerified && HeightMissing(mission.Entrance);
                if (floor) source = "local surface estimate";
                else if (heightVerified && HorizontalDistance(entrance, origin) <= 24)
                    floor = IsFloorCoordinate(entrance, origin.Y);
            }
            route.EntrancePoint = entrance;
            route.EntranceIsFloor = floor;
            route.HeightSource = source;
            if (!flying)
            {
                return route;
            }
            // This is an estimate, not a clearance certificate. Never require a synthetic
            // climb/cruise/descent corridor to select a finite world-space destination.
            route.FlightApproach = OutsideEntrance(entrance, origin, 1.5f);
            route.CruiseEnd = route.FlightApproach;
            // Quest height, even nonzero, is only a coarse hint. Live acquisition
            // takes over before final descent; do not steer into an assumed doorway.
            route.CruiseEnd.Y = origin.Y;
            return route;
        }

        // Optional complete-mesh cost. A failure here does not invalidate a direct local attempt.
        // Reject incomplete polygon paths: FindPath can succeed without reaching the destination polygon.
        public static bool TryGroundCost(Vector3 origin, Vector3 destination, out float cost)
        {
            cost = float.PositiveInfinity;
            if (!AcceptedMissions.Finite(origin) || !AcceptedMissions.Finite(destination) ||
                SMovementController.NavAgent?.HasPathfinder != true) return false;
            var query = new NavMeshQuery(SMovementController.NavAgent.NavMesh, 8192);
            // Match the pinned controller's default search extents for executed ground routes.
            SVector from = ToSharp(origin), to = ToSharp(destination), extents = new SVector(3, 3, 3);
            if (!query.FindNearestPoly(ref from, ref extents, out NavPoint first) ||
                !query.FindNearestPoly(ref to, ref extents, out NavPoint last) ||
                first.Polygon == NavPolyId.Null || last.Polygon == NavPolyId.Null ||
                Vector3.Distance(origin, ToAO(first.Position)) > 5 ||
                Vector3.Distance(destination, ToAO(last.Position)) > 4) return false;
            var corridor = new SharpNav.Pathfinding.Path();
            if (!query.FindPath(ref first, ref last, new NavQueryFilter(), corridor) ||
                corridor.Count == 0 || corridor[corridor.Count - 1] != last.Polygon) return false;
            var straight = new StraightPath();
            if (!query.FindStraightPath(first.Position, last.Position, corridor, straight, PathBuildFlags.AllCrossingVertices) ||
                straight.Verts.Count == 0) return false;
            Vector3 previous = origin;
            float length = 0;
            foreach (var vertex in straight.Verts)
            {
                Vector3 point = vertex.Position;
                length += Vector3.Distance(previous, point);
                previous = point;
            }
            if (Vector3.Distance(previous, destination) > 4) return false;
            cost = length + Vector3.Distance(previous, destination);
            return !float.IsNaN(cost) && !float.IsInfinity(cost);
        }

        public static bool HeightMissing(Vector3 point) => Math.Abs(point.Y) < 0.01f;

        // AO /pos reports X, Z, then height Y; Vector3 stores X, Y, Z. This
        // measured doorway is a local correction, not a replacement for every
        // mission in Broken Shores. Only markers within 2 m can use it.
        public static bool TryMeasuredEntrance(AcceptedMission mission, out Vector3 entrance)
        {
            entrance = new Vector3(553.2f, 18.1f, 1475.0f);
            return mission.PlayfieldId == 665 && HorizontalDistance(mission.Entrance, entrance) <= 2;
        }

        public static string Coordinates(Vector3 point) => string.Format(CultureInfo.InvariantCulture,
            "X={0:F2}, Z={1:F2}, height(Y)={2:F2}", point.X, point.Z, point.Y);

        // Some door/quest origins sit on the floor, others already carry an entry
        // height. Establish that distinction without replacing their coordinates.
        public static bool IsFloorCoordinate(Vector3 point, float referenceHeight) =>
            TryEntranceSurface(point, referenceHeight, out float floor, out _) && Math.Abs(point.Y - floor) <= 0.5f;

        public static Vector3 ResolveEntranceHeight(Vector3 entrance, Vector3 position, out bool verified)
        {
            float height = entrance.Y;
            // Retain nonzero quest height only as a coarse travel hint. The shared
            // acquisition routine replaces it with live origin/threshold alternatives.
            verified = !HeightMissing(entrance);
            if (verified) return entrance;
            verified = HorizontalDistance(entrance, position) <= 24 &&
                TryEntranceSurface(entrance, position.Y, out height, out _);
            if (verified) entrance.Y = height;
            else if (HeightMissing(entrance)) entrance.Y = position.Y;
            return entrance;
        }

        private struct SurfaceSample
        {
            public int Column;
            public float Height;
        }

        // A single high downward hit can be a roof/canopy. Sample independent columns
        // around the doorway, including lower layers beneath the first hit, and select
        // the best-supported walkable height. Each column contributes at most one vote.
        public static bool TryEntranceSurface(Vector3 entrance, float referenceHeight, out float height, out int support)
        {
            height = entrance.Y;
            support = 0;
            var samples = new List<SurfaceSample>();
            for (int column = 0; column < 17; column++)
            {
                Vector3 point = entrance;
                if (column > 0)
                {
                    float radius = column <= 8 ? 2 : 6;
                    double angle = ((column - 1) % 8) * Math.PI / 4;
                    point += new Vector3((float)Math.Cos(angle) * radius, 0, (float)Math.Sin(angle) * radius);
                }
                Vector3 top = point, bottom = point;
                top.Y = referenceHeight + 24;
                bottom.Y = referenceHeight - 240;
                for (int layer = 0; layer < 4; layer++)
                {
                    if (!Playfield.Raycast(top, bottom, out Vector3 hit, out Vector3 normal) ||
                        !AcceptedMissions.Finite(hit) || hit.Y >= top.Y + 0.1f) break;
                    if (normal.Y >= 0.6f) samples.Add(new SurfaceSample { Column = column, Height = hit.Y });
                    top.Y = hit.Y - 0.4f;
                    if (top.Y <= bottom.Y) break;
                }
            }
            // Sort low-to-high so equally supported lower ground wins over a roof.
            // Require support near the entrance itself, then use the centre/inner ring
            // for its height instead of averaging distant ground into a sloped doorway.
            samples.Sort((a, b) => a.Height.CompareTo(b.Height));
            foreach (SurfaceSample candidate in samples)
            {
                var columns = new HashSet<int>();
                float innerTotal = 0, centreHeight = 0;
                int innerCount = 0;
                bool hasCentre = false;
                foreach (SurfaceSample sample in samples)
                {
                    if (Math.Abs(sample.Height - candidate.Height) > 1.5f || !columns.Add(sample.Column)) continue;
                    if (sample.Column == 0) { centreHeight = sample.Height; hasCentre = true; }
                    else if (sample.Column <= 8) { innerTotal += sample.Height; innerCount++; }
                }
                if ((!hasCentre && innerCount < 2) || columns.Count <= support) continue;
                support = columns.Count;
                height = hasCentre ? centreHeight : innerTotal / innerCount;
            }
            return support >= 3;
        }

        // A local terrain hint, independent of the accepted entrance's possibly stale height.
        // A missed/steep ray is inconclusive; callers retain a provisional point.
        public static bool TrySurface(Vector3 sample, float referenceHeight, out Vector3 surface)
        {
            Vector3 top = sample, bottom = sample;
            top.Y = referenceHeight + 40;
            bottom.Y = referenceHeight - 160;
            return Playfield.Raycast(top, bottom, out surface, out Vector3 normal) &&
                AcceptedMissions.Finite(surface) && normal.Y >= 0.5f;
        }

        public static Vector3 OutsideEntrance(Vector3 entrance, Vector3 from, float radius)
        {
            float distance = HorizontalDistance(entrance, from);
            if (distance < 0.1f) return entrance + new Vector3(radius, 0, 0);
            return entrance + new Vector3((from.X - entrance.X) * radius / distance, 0,
                (from.Z - entrance.Z) * radius / distance);
        }

        // Sample a full fan at several radii, including tangents/backtracking around a wall.
        // Probes affect scores only. Even if every probe hits, submit a short cautious attempt
        // and let observed movement decide. Recent attempts discourage local oscillation.
        public static bool TryDirectGroundStep(Vector3 origin, Vector3 destination, int preferredSide,
            int attempt, IList<Vector3> recent, out Vector3 step, out string hint, out int side)
        {
            step = origin;
            hint = "no finite local waypoint";
            side = 0;
            float distance = HorizontalDistance(origin, destination);
            if (distance < 0.6f) return false;
            Vector3 levelDestination = destination; levelDestination.Y = origin.Y;
            Vector3 forward = Toward(origin, levelDestination, Math.Min(12, distance));
            bool forwardSurface = TrySurface(forward, origin.Y, out Vector3 forwardFloor);
            if (forwardSurface && Math.Abs(forwardFloor.Y - origin.Y) <= 5) forward.Y = forwardFloor.Y;
            if ((!forwardSurface || Math.Abs(forwardFloor.Y - origin.Y) <= 5) &&
                ClearSegment(origin + Vector3.Up, forward + Vector3.Up) &&
                !recent.Any(x => HorizontalDistance(forward, x) < 3))
            {
                step = forward;
                hint = "straight approach; probe clear" + (forwardSurface ? "; terrain hint" : "; provisional height");
                return true;
            }
            double heading = Math.Atan2(destination.Z - origin.Z, destination.X - origin.X);
            float best = float.PositiveInfinity;
            float[] radii = { 12, 8, 4, 2 };
            foreach (float radius in radii)
            {
                for (int i = 0; i < 16; i++)
                {
                    int direction = i == 0 ? 0 : (i % 2 == 1 ? 1 : -1);
                    double offset = ((i + 1) / 2) * Math.PI / 8 * direction;
                    if (preferredSide == -1 || (preferredSide == 0 && attempt % 2 == 1)) offset = -offset;
                    float length = i == 0 ? Math.Min(radius, distance) : radius;
                    Vector3 candidate = origin + new Vector3((float)Math.Cos(heading + offset) * length, 0,
                        (float)Math.Sin(heading + offset) * length);
                    float terrainPenalty = 0;
                    bool surfaceKnown = TrySurface(candidate, origin.Y, out Vector3 surface);
                    if (surfaceKnown && Math.Abs(surface.Y - origin.Y) <= 5) candidate.Y = surface.Y;
                    else if (surfaceKnown) terrainPenalty = 20; // Roof/drop hint; retain current height.
                    if (!AcceptedMissions.Finite(candidate)) continue;
                    bool clear = ClearSegment(origin + Vector3.Up, candidate + Vector3.Up);
                    float score = HorizontalDistance(candidate, destination) + length * 0.35f + terrainPenalty;
                    // A hit prefers a shorter exploratory leg, rather than forbidding execution.
                    if (!clear) score += 30 + length * 2;
                    if (!surfaceKnown) score += 1;
                    int candidateSide = Math.Sign(offset);
                    if (preferredSide != 0 && candidateSide != 0 && candidateSide != preferredSide) score += 5;
                    for (int j = 0; j < recent.Count; j++)
                        if (HorizontalDistance(candidate, recent[j]) < 3) score += 18;
                    if (score >= best) continue;
                    best = score;
                    step = candidate;
                    side = candidateSide;
                    hint = (i == 0 ? "forward" : "obstacle arc") +
                        (clear ? "; probe clear" : "; probe hit, cautious attempt") +
                        (surfaceKnown ? "; terrain hint" : "; provisional height");
                }
            }
            return !float.IsInfinity(best);
        }

        public static Vector3 Toward(Vector3 origin, Vector3 destination, float length)
        {
            float distance = Vector3.Distance(origin, destination);
            return distance <= length || distance < 0.1f ? destination : origin + (destination - origin) * (length / distance);
        }

        private sealed class FlightNode
        {
            public int X, Z, Level, Parent;
            public Vector3 Point;
            public float Cost;
            public bool Closed;
        }

        private const int FlightProbeLimit = 6000, FlightSearchProbeLimit = 4000;

        // Observed failed movement remembers a short direction or descent area,
        // not an unreachable mission. Scene objects may be absent from surface rays.
        public struct FlightBlockedLeg
        {
            public Vector3 From, To;
            // Only an actual stalled, nearly vertical descent sets this footprint.
            // A surface-only ray can miss the roof/platform that stops the vehicle.
            public float DescentRadius;
        }

        private static bool RepeatsBlockedLeg(Vector3 from, Vector3 to, float radius, IList<FlightBlockedLeg> blocked)
        {
            Vector3 delta = to - from;
            float length = Vector3.Distance(from, to);
            if (length < 0.1f) return false;
            foreach (FlightBlockedLeg leg in blocked)
            {
                if (leg.DescentRadius > 0 && Math.Abs(delta.Y) > 0.1f)
                {
                    // Remember a crossing plane, not a solid column down to the
                    // terrain estimate: flight below a roof can still reach the door.
                    // Horizontal escape at the observed stopping height stays usable.
                    float plane = leg.From.Y - 0.5f;
                    float crossing = (plane - from.Y) / delta.Y;
                    if (crossing >= 0 && crossing <= 1 &&
                        HorizontalDistance(from + delta * crossing, leg.From) <= leg.DescentRadius + radius)
                        return true;
                }
                if (Vector3.Distance(leg.From, leg.To) < 0.1f) continue;
                Vector3 direction = (leg.To - leg.From).Normalize();
                if (Vector3.Dot(delta / length, direction) < 0.8f) continue;
                Vector3 witness = Toward(leg.From, leg.To, 2);
                float along = Vector3.Dot(witness - from, delta) / (length * length);
                if (along < 0 || along > 1) continue;
                if (Vector3.Distance(witness, from + delta * along) < Math.Max(0.7f, radius)) return true;
            }
            return false;
        }

        // Execution and corner cutting must respect observed geometry too; a new
        // clear terrain ray cannot erase a failed scene-object descent.
        public static bool FlightSegmentClear(Vector3 from, Vector3 to, float radius, IList<FlightBlockedLeg> blocked)
            => !RepeatsBlockedLeg(from, to, radius, blocked) && ClearSegment(from, to);

        public static bool ObservedFlightSegmentBlocked(Vector3 from, Vector3 to, float radius, IList<FlightBlockedLeg> blocked)
            => RepeatsBlockedLeg(from, to, radius, blocked);

        // Near the entrance, an exhausted/inconclusive surface search must not
        // require a clearance certificate before trying the fixed approach point.
        // Rank whole direct/arc/climb-drop-return estimates by surface hints, but
        // exclude actual failed movement. Execution retains collision/stall bounds.
        public static List<Vector3> PlanEntranceAttempt(Vector3 origin, Vector3 destination, float radius,
            IList<FlightBlockedLeg> blocked, out string reason)
        {
            const int probeLimit = 512;
            int probes = 0, bestHints = 0;
            float bestCost = float.PositiveInfinity;
            List<Vector3> best = null;
            Action<List<Vector3>> consider = candidate =>
            {
                Vector3 previous = origin;
                float cost = 0;
                int hints = 0;
                var legs = new List<Vector3>();
                foreach (Vector3 point in candidate)
                {
                    float length = Vector3.Distance(previous, point);
                    if (length < 0.05f) continue;
                    if (RepeatsBlockedLeg(previous, point, radius, blocked)) return;
                    if (probes >= probeLimit) return;
                    probes++;
                    if (!ClearSegment(Toward(previous, point, 0.3f), point)) hints++;
                    cost += length; legs.Add(point); previous = point;
                }
                cost += hints * 32;
                if (legs.Count == 0 || cost >= bestCost) return;
                best = legs; bestCost = cost; bestHints = hints;
            };
            consider(new List<Vector3> { destination });
            double heading = Math.Atan2(origin.Z - destination.Z, origin.X - destination.X);
            foreach (float distance in new float[] { 4, 8, 12, 20 })
                for (int direction = 0; direction < 16 && probes < probeLimit; direction++)
                    foreach (float rise in new float[] { 0, 4 })
                    {
                        double angle = heading + direction * Math.PI / 8;
                        Vector3 outside = destination + new Vector3((float)Math.Cos(angle) * distance, 0,
                            (float)Math.Sin(angle) * distance);
                        outside.Y = Math.Max(origin.Y, destination.Y) + rise;
                        Vector3 lowered = outside; lowered.Y = destination.Y;
                        consider(new List<Vector3> { outside, lowered, destination });
                    }
            reason = best == null
                ? $"no entrance estimate avoids observed failed movement; {probes}/{probeLimit} advisory probes"
                : $"fixed approach movement estimate; {best.Count} legs, {bestHints} advisory surface hints; " +
                    $"{probes}/{probeLimit} probes; actual progress/stall governs recovery";
            return best ?? new List<Vector3>();
        }

        // Only confirmed surface hits/observed failed legs exclude a search edge.
        // Offset probes rank body clearance; missing native LOS is not a rejection.
        // Limit all probes, including shortcutting, per complete planning attempt.
        private static float FlightEdge(Vector3 from, Vector3 to, float radius,
            IList<FlightBlockedLeg> blocked, bool portal, ref int probes, out int hints)
        {
            hints = 0;
            float length = Vector3.Distance(from, to);
            if (length < 0.1f) return 0;
            // Budget exhaustion is not a physical obstruction.
            if (probes >= FlightProbeLimit) return float.PositiveInfinity;
            if (RepeatsBlockedLeg(from, to, radius, blocked)) return -1;
            Vector3 end = portal ? Toward(from, to, Math.Max(0, length - 1)) : to;
            Vector3 start = Toward(from, end, 0.3f);
            probes++;
            if (!ClearSegment(start, end)) return -1;
            float horizontal = HorizontalDistance(from, to);
            Vector3 side = horizontal < 0.1f ? new Vector3(radius, 0, 0) :
                new Vector3(-(to.Z - from.Z) * radius / horizontal, 0, (to.X - from.X) * radius / horizontal);
            foreach (Vector3 offset in new[] { side, side * -1, Vector3.Up * radius })
            {
                if (probes >= FlightProbeLimit) { hints++; continue; }
                probes++;
                if (!ClearSegment(start + offset, end + offset)) hints++;
            }
            return length + hints * 4;
        }

        // Search descent columns before the generic grid. A roof can require moving
        // away from the doorway first; certify the sideways and downward legs as one
        // section instead of demanding that sideways movement already gets closer.
        private static List<Vector3> PlanOutsideDescent(Vector3 origin, Vector3 destination, Vector3 centre,
            IList<Vector3> goals, float radius, IList<FlightBlockedLeg> blocked, float extent, bool portal,
            ref int probes, out bool complete, out string reason)
        {
            complete = false;
            int limit = Math.Min(FlightSearchProbeLimit, probes + 2000), columns = 0, clearDrops = 0;
            string hitHint = "direct drop obstructed or inconclusive";
            probes++;
            if (Playfield.Raycast(Toward(origin, destination, 0.3f), destination, out Vector3 hit, out _) &&
                AcceptedMissions.Finite(hit)) hitHint = $"direct drop surface at Y={hit.Y:F1}";
            var levels = new List<float> { destination.Y };
            foreach (float drop in new float[] { 6, 12, 18 })
            {
                float height = Math.Max(destination.Y, origin.Y - drop);
                if (origin.Y - height >= 3 && !levels.Exists(y => Math.Abs(y - height) < 0.1f)) levels.Add(height);
            }
            List<Vector3> bestPath = null, bestSection = null;
            float bestPathCost = float.PositiveInfinity, bestSectionCost = float.PositiveInfinity;
            int bestHints = int.MaxValue;
            double heading = Math.Atan2(origin.Z - centre.Z, origin.X - centre.X);
            foreach (float distance in new float[] { 4, 8, 12, 20, 28, 40, 56, 72 })
            {
                if (distance > extent || probes >= limit) break;
                for (int direction = 0; direction < 16 && probes < limit; direction++)
                {
                    double angle = heading + direction * Math.PI / 8;
                    Vector3 top = centre + new Vector3((float)Math.Cos(angle) * distance, 0,
                        (float)Math.Sin(angle) * distance);
                    top.Y = origin.Y;
                    columns++;
                    float across = FlightEdge(origin, top, radius, blocked, false, ref probes, out int acrossHints);
                    if (across < 0 || float.IsInfinity(across)) continue;
                    foreach (float height in levels)
                    {
                        if (probes >= limit) break;
                        Vector3 bottom = top; bottom.Y = height;
                        float down = FlightEdge(top, bottom, radius, blocked, false, ref probes, out int downHints);
                        if (down < 0 || float.IsInfinity(down)) continue;
                        clearDrops++;
                        var section = new List<Vector3>();
                        if (Vector3.Distance(origin, top) > 0.05f) section.Add(top);
                        section.Add(bottom);
                        float sectionCost = Vector3.Distance(bottom, destination) + (across + down) * 0.025f;
                        // A useful drop can end further away horizontally. Keep it
                        // under the original final-target deadline, without moving
                        // the selected entrance or overwriting its floor with this column.
                        if (origin.Y - bottom.Y >= Math.Min(6, origin.Y - destination.Y) && sectionCost < bestSectionCost)
                        { bestSectionCost = sectionCost; bestSection = section; }
                        // Intermediate lowering planes are prefixes. At entry height,
                        // check the return to the same entrance, including its clear side.
                        if (Math.Abs(height - destination.Y) > 0.1f) continue;
                        foreach (Vector3 goal in goals)
                        {
                            if (probes >= limit) break;
                            float approach = FlightEdge(bottom, goal, radius, blocked, portal, ref probes, out int approachHints);
                            if (approach < 0 || float.IsInfinity(approach)) continue;
                            int hints = acrossHints + downHints + approachHints;
                            float cost = across + down + approach + Vector3.Distance(goal, destination) * 0.25f;
                            bool better = bestPath == null || (hints == 0 && bestHints != 0) ||
                                ((hints == 0) == (bestHints == 0) && cost < bestPathCost);
                            if (!better) continue;
                            bestPath = new List<Vector3>(section); bestPath.Add(goal);
                            bestPathCost = cost; bestHints = hints;
                        }
                    }
                }
            }
            string stats = $"{columns} outside columns, {clearDrops} clear drops, {probes} probes; {hitHint}";
            if (bestPath != null)
            {
                complete = true;
                reason = $"outside descent and entrance return; {stats}; selected entry height={destination.Y:F1}";
                return bestPath;
            }
            if (bestSection != null)
            {
                reason = $"outside descent prefix; {stats}; lowering target={bestSection[bestSection.Count - 1]}; " +
                    "final doorway leg pending, continue from the lowered position";
                return bestSection;
            }
            reason = $"outside descent search found no useful drop; {stats}";
            return null;
        }

        // Bounded A* links local grid cells at entrance/current/raised heights.
        // Unlike fixed rectangles, connected arcs can turn around several building
        // faces or reach a cave opening. Commit/simplify the whole path, not each cell.
        public static List<Vector3> PlanFlightPath(Vector3 origin, Vector3 destination,
            float ceiling, float radius, IList<FlightBlockedLeg> blocked, float extent,
            bool entranceStage, bool portal, out bool complete, out string reason)
        {
            complete = true;
            var goals = new List<Vector3> { destination };
            int probes = 0, directHints = 0;
            float direct = FlightEdge(origin, destination, radius, blocked, portal, ref probes, out directHints);
            if (direct >= 0 && directHints == 0)
            { reason = "direct complete flight path; surface probes clear"; return new List<Vector3> { destination }; }

            bool descending = origin.Y > destination.Y + (entranceStage ? 1 : 3);
            string descentHint = null;
            if (descending)
            {
                List<Vector3> descent = PlanOutsideDescent(origin, destination, destination,
                    goals, radius, blocked, extent, portal, ref probes, out bool descentComplete, out descentHint);
                if (descent != null)
                {
                    complete = descentComplete;
                    List<Vector3> section = SimplifyFlightPath(origin, descent, radius, blocked,
                        portal && complete, ref probes);
                    reason = $"{descentHint}; {section.Count} committed legs, complete={complete}; probes={probes}/{FlightProbeLimit}";
                    return section;
                }
            }

            // Cheap whole-route candidates avoid spending the local grid budget on
            // hundreds of metres of open travel. Retain clear prefixes even if the
            // provisional final height/last descent cannot yet be reached.
            List<Vector3> bestComplete = direct >= 0 ? new List<Vector3> { destination } : null;
            float bestCompleteCost = direct >= 0 ? direct : float.PositiveInfinity;
            int bestCompleteHints = directHints;
            List<Vector3> prefix = null;
            float prefixScore = float.PositiveInfinity;
            Func<Vector3, float, float> progressScore = (point, cost) =>
                Vector3.Distance(point, destination) + cost * 0.025f;
            float initialScore = progressScore(origin, 0);
            bool launching = !entranceStage && !portal && HorizontalDistance(origin, destination) > 24;
            Action<List<Vector3>, float> retainPrefix = (points, cost) =>
            {
                if (points.Count == 0) return;
                Vector3 end = points[points.Count - 1];
                // Useful multi-leg travel/climb, not repeated two-unit micro-hops.
                if (HorizontalDistance(origin, end) < 8 && Math.Abs(origin.Y - end.Y) < 6) return;
                float score = progressScore(end, cost);
                // Leaving a canopy or reaching an outside descent column can first
                // increase final distance. Keep the same observed-progress deadline.
                bool necessaryDrop = descending && end.Y <= origin.Y - 6;
                if (score >= initialScore - 0.5f && !necessaryDrop && !(launching && end.Y >= origin.Y + 6)) return;
                if (score >= prefixScore) return;
                prefixScore = score; prefix = new List<Vector3>(points);
            };
            foreach (float rise in new float[] { 0, 12, 24, 40 })
            {
                float height = Math.Min(ceiling, Math.Max(origin.Y, destination.Y) + rise);
                Vector3 climb = origin, cruise = destination;
                climb.Y = cruise.Y = height;
                var candidate = new List<Vector3>();
                Vector3 previous = origin;
                float cost = 0;
                int hints = 0;
                bool reachesGoal = true;
                foreach (Vector3 point in new[] { climb, cruise, destination })
                {
                    if (Vector3.Distance(previous, point) < 0.05f) continue;
                    float edge = FlightEdge(previous, point, radius, blocked,
                        portal && Vector3.Distance(point, destination) < 0.05f, ref probes, out int edgeHints);
                    if (edge < 0 || float.IsInfinity(edge)) { reachesGoal = false; break; }
                    cost += edge; hints += edgeHints; candidate.Add(point); previous = point;
                }
                if (!reachesGoal) { retainPrefix(candidate, cost); continue; }
                bool better = bestComplete == null || (hints == 0 && bestCompleteHints != 0) ||
                    ((hints == 0) == (bestCompleteHints == 0) && cost < bestCompleteCost);
                if (!better) continue;
                bestComplete = candidate; bestCompleteCost = cost; bestCompleteHints = hints;
            }
            if (bestComplete != null && bestCompleteHints == 0)
            {
                reason = $"complete climb/cruise/approach flight path; {bestComplete.Count} legs, surface probes clear";
                return bestComplete;
            }
            if (prefix != null && launching &&
                HorizontalDistance(origin, prefix[prefix.Count - 1]) >= 24)
            {
                // Cruise can start on a clear long section while an unverified final
                // elevation is refined nearer the entrance. Do not preflight-veto it.
                complete = false;
                List<Vector3> section = SimplifyFlightPath(origin, prefix, radius, blocked, false, ref probes);
                reason = $"validated climb/cruise prefix; {section.Count} legs, endpoint={section[section.Count - 1]}; " +
                    $"final approach pending local height/obstacle refinement; probes={probes}/{FlightProbeLimit}";
                return section;
            }

            var heights = new List<float> { origin.Y };
            foreach (float height in new[] { destination.Y, Math.Min(ceiling, Math.Max(origin.Y, destination.Y) + 12),
                Math.Min(ceiling, Math.Max(origin.Y, destination.Y) + 24), ceiling })
                if (!heights.Exists(h => Math.Abs(h - height) < 0.1f)) heights.Add(height);
            if (descending)
                foreach (float drop in new float[] { 6, 12, 18 })
                {
                    float height = Math.Max(destination.Y, origin.Y - drop);
                    if (!heights.Exists(h => Math.Abs(h - height) < 0.1f)) heights.Add(height);
                }
            const float spacing = 4;
            float minX = Math.Min(origin.X, destination.X) - extent, maxX = Math.Max(origin.X, destination.X) + extent;
            float minZ = Math.Min(origin.Z, destination.Z) - extent, maxZ = Math.Max(origin.Z, destination.Z) + extent;
            var nodes = new List<FlightNode>
            { new FlightNode { Point = origin, Parent = -1, Cost = 0 } };
            var indices = new Dictionary<Tuple<int, int, int>, int> { { Tuple.Create(0, 0, 0), 0 } };
            int expanded = 0, surfaceBlocks = 0, found = -1;
            float foundCost = float.PositiveInfinity;
            Vector3 foundGoal = destination;
            Func<Vector3, float> heuristic = point =>
            {
                float best = float.PositiveInfinity;
                foreach (Vector3 goal in goals) best = Math.Min(best, Vector3.Distance(point, goal));
                return best;
            };
            // Reserve probes for smoothing the result instead of returning a raw
            // cell-by-cell path when the search budget runs out.
            while (expanded < 450 && probes < FlightSearchProbeLimit)
            {
                int current = -1;
                float priority = float.PositiveInfinity;
                for (int i = 0; i < nodes.Count; i++)
                {
                    if (nodes[i].Closed) continue;
                    float score = nodes[i].Cost + heuristic(nodes[i].Point);
                    if (score < priority) { priority = score; current = i; }
                }
                if (current < 0) break;
                if (found >= 0 && priority >= foundCost) break;
                FlightNode node = nodes[current];
                node.Closed = true; expanded++;
                // Connect only to the committed stage target. No new approach side
                // or altitude can silently replace the entrance alignment point.
                foreach (Vector3 goal in goals)
                {
                    float cost = FlightEdge(node.Point, goal, radius, blocked, portal, ref probes, out _);
                    if (cost < 0) { surfaceBlocks++; continue; }
                    cost += node.Cost + Vector3.Distance(goal, destination) * 0.25f;
                    if (cost < foundCost) { foundCost = cost; foundGoal = goal; found = current; }
                }
                for (int level = 0; level < heights.Count; level++)
                    for (int dx = -1; dx <= 1; dx++)
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            // Eight horizontal neighbours and vertical columns.
                            if (level == node.Level && dx == 0 && dz == 0) continue;
                            if (level != node.Level && (dx != 0 || dz != 0)) continue;
                            int x = node.X + dx, z = node.Z + dz;
                            Vector3 point = new Vector3(origin.X + x * spacing, heights[level], origin.Z + z * spacing);
                            if (point.X < minX || point.X > maxX || point.Z < minZ || point.Z > maxZ) continue;
                            var key = Tuple.Create(x, z, level);
                            if (!indices.TryGetValue(key, out int next))
                            {
                                next = nodes.Count; indices.Add(key, next);
                                nodes.Add(new FlightNode { X = x, Z = z, Level = level, Point = point,
                                    Parent = -1, Cost = float.PositiveInfinity });
                            }
                            if (nodes[next].Closed) continue;
                            float cost = FlightEdge(node.Point, point, radius, blocked, false, ref probes, out _);
                            if (cost < 0) surfaceBlocks++;
                            if (cost < 0 || node.Cost + cost >= nodes[next].Cost) continue;
                            nodes[next].Cost = node.Cost + cost; nodes[next].Parent = current;
                        }
            }
            if (found < 0)
            {
                // Unknown/body-offset data still permits a complete estimate.
                if (bestComplete != null)
                {
                    reason = $"complete flight estimate; {bestComplete.Count} legs, {bestCompleteHints} body-clearance hints; contour search bounded";
                    return bestComplete;
                }
                // Every finite-cost node has a validated chain from the actual
                // origin. Keep the best useful frontier when full access is unknown.
                for (int n = 1; n < nodes.Count; n++)
                {
                    if (float.IsInfinity(nodes[n].Cost)) continue;
                    float score = progressScore(nodes[n].Point, nodes[n].Cost);
                    if (score >= prefixScore) continue;
                    var candidate = new List<Vector3>();
                    for (int i = n; i > 0; i = nodes[i].Parent) candidate.Add(nodes[i].Point);
                    candidate.Reverse(); retainPrefix(candidate, nodes[n].Cost);
                }
                complete = false;
                string stop = probes >= FlightSearchProbeLimit ? "search probe budget" : expanded >= 450 ? "cell budget" : "local frontier exhausted";
                string stats = $"{stop}; {expanded} cells, {probes} probes, {surfaceBlocks} physical/observed blocked edges; margin={extent:F0} m";
                if (prefix == null)
                {
                    reason = $"no useful clear flight section; {stats}; " +
                        (descentHint == null ? "" : descentHint + "; ") +
                        $"{blocked.Count} observed failed legs";
                    return new List<Vector3>();
                }
                List<Vector3> section = SimplifyFlightPath(origin, prefix, radius, blocked, false, ref probes);
                reason = $"validated flight prefix; {section.Count} legs, endpoint={section[section.Count - 1]}; " +
                    $"final approach still pending; {stats}; total probes={probes}/{FlightProbeLimit}; continue planning after observed arrival";
                return section;
            }
            var raw = new List<Vector3> { foundGoal };
            for (int i = found; i > 0; i = nodes[i].Parent) raw.Add(nodes[i].Point);
            raw.Reverse();
            List<Vector3> path = SimplifyFlightPath(origin, raw, radius, blocked, portal, ref probes);
            reason = $"connected obstacle route; {path.Count} legs, {expanded} cells, {surfaceBlocks} blocked edges; " +
                $"probes={probes}/{FlightProbeLimit}, search margin={extent:F0} m, endpoint={foundGoal}";
            return path;
        }

        private static List<Vector3> SimplifyFlightPath(Vector3 origin, List<Vector3> raw, float radius,
            IList<FlightBlockedLeg> blocked, bool portal, ref int probes)
        {
            // Use longer certified stretches while keeping proven adjacent edges
            // if the remaining budget cannot certify a further shortcut.
            var path = new List<Vector3>();
            Vector3 previous = origin;
            for (int i = 0; i < raw.Count;)
            {
                int chosen = i;
                for (int j = raw.Count - 1; j > i && probes < FlightProbeLimit; j--)
                {
                    float cost = FlightEdge(previous, raw[j], radius, blocked,
                        portal && j == raw.Count - 1, ref probes, out int hints);
                    if (cost >= 0 && !float.IsInfinity(cost) && hints == 0) { chosen = j; break; }
                }
                if (Vector3.Distance(previous, raw[chosen]) > 0.05f) path.Add(raw[chosen]);
                previous = raw[chosen]; i = chosen + 1;
            }
            if (path.Count == 0) path.Add(raw[raw.Count - 1]);
            return path;
        }

        public static bool ClearSegment(Vector3 start, Vector3 end) =>
            Vector3.Distance(start, end) < 0.5f || !Playfield.Raycast(start, end, out _, out _);
        public static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            float x = a.X - b.X, z = a.Z - b.Z;
            return (float)Math.Sqrt(x * x + z * z);
        }
        private static SVector ToSharp(Vector3 point) => new SVector(point.X, point.Y, point.Z);
        private static Vector3 ToAO(SVector point) => new Vector3(point.X, point.Y, point.Z);
    }
}
