using System;
using System.Collections.Generic;
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
        public bool Flying, GroundUsesMesh, EntranceHeightVerified;
        public string Reason, HeightSource;
    }

    internal static class LocalRoutePlanner
    {
        // Called once, after selecting the nearest entrance by a cheap distance estimate.
        // Build only this mission's path and only for the selected movement mode.
        public static LocalRoute Plan(AcceptedMission mission, Vector3 origin, bool flying)
        {
            if (!AcceptedMissions.Finite(origin) || !AcceptedMissions.Finite(mission.Entrance))
                return null;
            Vector3 entrance = ResolveEntranceHeight(mission.Entrance, origin, out bool heightVerified);
            var route = new LocalRoute
            {
                Mission = mission, Origin = origin, Entrance = mission.Entrance,
                EntrancePoint = entrance, Flying = flying, EntranceHeightVerified = heightVerified,
                EntranceDistance = HorizontalDistance(origin, mission.Entrance),
                HeightSource = heightVerified ? "local surface consensus" : HeightMissing(mission.Entrance)
                    ? "player height, provisional" : "accepted height, provisional"
            };
            if (!flying)
            {
                route.GroundUsesMesh = TryGroundCost(origin, entrance, out float ground);
                route.Cost = route.GroundUsesMesh ? ground : route.EntranceDistance;
                route.Reason = route.GroundUsesMesh ? "complete navmesh path" :
                    SMovementController.NavAgent?.HasPathfinder == true ? "mesh does not connect endpoints; direct estimate" :
                    "no outdoor mesh; direct estimate";
                return route;
            }
            // This is an estimate, not a clearance certificate. Never require a synthetic
            // climb/cruise/descent corridor to select a finite world-space destination.
            route.FlightApproach = OutsideEntrance(entrance, origin, 1.5f);
            route.CruiseEnd = route.FlightApproach;
            route.CruiseEnd.Y = Math.Max(origin.Y, entrance.Y + 12);
            route.Cost = Vector3.Distance(origin, route.CruiseEnd) +
                Math.Abs(route.CruiseEnd.Y - entrance.Y) + 1.5f;
            route.Reason = "committed flight path; select entrance height within 2 m, align, then enter in vehicle";
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

        public static Vector3 ResolveEntranceHeight(Vector3 entrance, Vector3 position, out bool verified)
        {
            float height = entrance.Y;
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

        // Observed failed movement blocks a short direction, not the mission or its
        // entrance. This also handles scene objects absent from the surface ray data.
        public struct FlightBlockedLeg
        {
            public Vector3 From, To;
        }

        private static bool RepeatsBlockedLeg(Vector3 from, Vector3 to, float radius, IList<FlightBlockedLeg> blocked)
        {
            Vector3 delta = to - from;
            float length = Vector3.Distance(from, to);
            if (length < 0.1f) return false;
            foreach (FlightBlockedLeg leg in blocked)
            {
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

        // Only confirmed surface hits/observed failed legs exclude a search edge.
        // Offset probes rank body clearance; missing native LOS is not a rejection.
        // Limit all probes, including shortcutting, per complete planning attempt.
        private static float FlightEdge(Vector3 from, Vector3 to, float radius,
            IList<FlightBlockedLeg> blocked, bool portal, ref int probes, out int hints)
        {
            hints = 0;
            float length = Vector3.Distance(from, to);
            if (length < 0.1f) return 0;
            if (RepeatsBlockedLeg(from, to, radius, blocked) || probes >= 6000) return -1;
            Vector3 end = portal ? Toward(from, to, Math.Max(0, length - 1)) : to;
            Vector3 start = Toward(from, end, 0.3f);
            probes++;
            if (!ClearSegment(start, end)) return -1;
            float horizontal = HorizontalDistance(from, to);
            Vector3 side = horizontal < 0.1f ? new Vector3(radius, 0, 0) :
                new Vector3(-(to.Z - from.Z) * radius / horizontal, 0, (to.X - from.X) * radius / horizontal);
            foreach (Vector3 offset in new[] { side, side * -1, Vector3.Up * radius })
            {
                if (probes >= 6000) { hints++; continue; }
                probes++;
                if (!ClearSegment(start + offset, end + offset)) hints++;
            }
            return length + hints * 4;
        }

        // Bounded A* links local grid cells at entrance/current/raised heights.
        // Unlike fixed rectangles, connected arcs can turn around several building
        // faces or reach a cave opening. Commit/simplify the whole path, not each cell.
        public static List<Vector3> PlanFlightPath(Vector3 origin, Vector3 destination,
            float ceiling, float radius, IList<FlightBlockedLeg> blocked, float extent,
            Vector3? entranceCentre, bool portal, out string reason)
        {
            var goals = new List<Vector3> { destination };
            if (entranceCentre.HasValue)
            {
                Vector3 centre = entranceCentre.Value;
                for (int i = 0; i < 16; i++)
                {
                    double angle = i * Math.PI / 8;
                    goals.Add(centre + new Vector3((float)Math.Cos(angle) * 1.5f, 0,
                        (float)Math.Sin(angle) * 1.5f));
                }
            }
            int probes = 0, directHints = 0;
            float direct = FlightEdge(origin, destination, radius, blocked, portal, ref probes, out directHints);
            if (direct >= 0 && directHints == 0)
            { reason = "direct complete flight path; surface probes clear"; return new List<Vector3> { destination }; }

            var heights = new List<float> { origin.Y };
            foreach (float height in new[] { destination.Y, Math.Min(ceiling, Math.Max(origin.Y, destination.Y) + 12),
                Math.Min(ceiling, Math.Max(origin.Y, destination.Y) + 24), ceiling })
                if (!heights.Exists(h => Math.Abs(h - height) < 0.1f)) heights.Add(height);
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
            while (expanded < 450 && probes < 6000)
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
                // Try actual destinations from this cell. Alternate endpoints are
                // confined to 1.5 m around this same entrance at selected entry height.
                foreach (Vector3 goal in goals)
                {
                    if (Vector3.Distance(node.Point, goal) > 12) continue;
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
                // Unknown/body-offset data still permits an estimate; a known blocked
                // centre leg never becomes the same one-leg "estimate" repeatedly.
                if (direct >= 0)
                {
                    reason = $"direct flight estimate; {directHints} body-clearance hints; bounded contour search inconclusive";
                    return new List<Vector3> { destination };
                }
                reason = $"no connected clear flight path in {extent:F0} m search margin; {expanded} cells, " +
                    $"{surfaceBlocks} blocked edges, {blocked.Count} observed failed legs; holding for expanded retry";
                return new List<Vector3>();
            }
            var raw = new List<Vector3> { foundGoal };
            for (int i = found; i > 0; i = nodes[i].Parent) raw.Add(nodes[i].Point);
            raw.Reverse();
            // Collapse clear straight portions so a long arc is a coherent set of
            // corners, rather than visible 4 m grid hops. Keep proven adjacent edges
            // if the budget cannot certify a further shortcut.
            var path = new List<Vector3>();
            Vector3 previous = origin;
            for (int i = 0; i < raw.Count;)
            {
                int chosen = i;
                for (int j = raw.Count - 1; j > i && probes < 6000; j--)
                {
                    float cost = FlightEdge(previous, raw[j], radius, blocked,
                        portal && j == raw.Count - 1, ref probes, out int hints);
                    if (cost >= 0 && hints == 0) { chosen = j; break; }
                }
                if (Vector3.Distance(previous, raw[chosen]) > 0.05f) path.Add(raw[chosen]);
                previous = raw[chosen]; i = chosen + 1;
            }
            if (path.Count == 0) path.Add(foundGoal);
            reason = $"connected obstacle route; {path.Count} legs, {expanded} cells, {surfaceBlocks} blocked edges; " +
                $"search margin={extent:F0} m, endpoint={foundGoal}" +
                (Vector3.Distance(foundGoal, destination) > 0.1f ? "; clear side of the same entrance" : "");
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
