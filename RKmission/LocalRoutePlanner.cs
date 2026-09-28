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
            route.FlightApproach = OutsideEntrance(entrance, origin, 4);
            route.CruiseEnd = route.FlightApproach;
            route.CruiseEnd.Y = Math.Max(origin.Y, entrance.Y + 12);
            route.Cost = Vector3.Distance(origin, route.CruiseEnd) +
                Math.Abs(route.CruiseEnd.Y - entrance.Y) + 4;
            route.Reason = "direct elevated flight; descent and entrance approach in vehicle";
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

        // A short ray around an already resolved ground level avoids reintroducing a
        // roof/canopy from a high-altitude ray into the final doorway approach.
        public static bool TrySurfaceNearHeight(Vector3 point, float height, out Vector3 surface)
        {
            Vector3 top = point, bottom = point;
            top.Y = height + 3;
            bottom.Y = height - 6;
            return Playfield.Raycast(top, bottom, out surface, out Vector3 normal) &&
                AcceptedMissions.Finite(surface) && normal.Y >= 0.6f && Math.Abs(surface.Y - height) <= 3;
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

        // Combine the surface ray with the native scene line-of-sight query, using
        // several offset rays to allow room for the vehicle rather than only its centre.
        public static bool FlightCorridorClear(Vector3 origin, Vector3 destination, float radius, float endMargin = 0)
        {
            float distance = Vector3.Distance(origin, destination);
            if (distance <= endMargin + 0.3f) return true;
            Vector3 start = Toward(origin, destination, 0.3f);
            Vector3 end = Toward(origin, destination, distance - endMargin);
            float horizontal = HorizontalDistance(origin, destination);
            Vector3 side = horizontal < 0.1f ? new Vector3(radius, 0, 0) :
                new Vector3(-(destination.Z - origin.Z) * radius / horizontal, 0,
                    (destination.X - origin.X) * radius / horizontal);
            // Player position can be close to the floor during vehicle entry. Do not
            // start a lower offset inside terrain and falsely forbid every climb.
            Vector3[] offsets = { Vector3.Zero, side, side * -1, Vector3.Up * radius, Vector3.Up * (radius * 2) };
            foreach (Vector3 offset in offsets)
                if (!ClearSegment(start + offset, end + offset) ||
                    !Playfield.LineOfSight(start + offset, end + offset, 1, false)) return false;
            return true;
        }

        public static bool TryFlightBypass(Vector3 origin, Vector3 destination, float ceiling, float radius,
            int attempt, IList<Vector3> recent, out Vector3 step, out string kind)
        {
            step = origin;
            kind = "no clear local bypass";
            float best = float.PositiveInfinity;
            double heading = Math.Atan2(destination.Z - origin.Z, destination.X - origin.X);
            var candidates = new List<Vector3>();
            // A vertical first leg can climb beside a building instead of flying diagonally
            // through its wall. Raised forward/lateral legs then go over or around it.
            foreach (float rise in new float[] { 4, 8, 16, 24 })
                candidates.Add(new Vector3(origin.X, Math.Min(ceiling, origin.Y + rise), origin.Z));
            for (int i = 0; i < 8; i++)
            {
                double angle = heading + (i + attempt % 8) * Math.PI / 4;
                foreach (float length in new float[] { 6, 12 })
                {
                    foreach (float rise in new float[] { 0, 6, 12 })
                        candidates.Add(new Vector3(origin.X + (float)Math.Cos(angle) * length,
                            Math.Min(ceiling, origin.Y + rise), origin.Z + (float)Math.Sin(angle) * length));
                }
            }
            foreach (Vector3 point in candidates)
            {
                if (Vector3.Distance(origin, point) < 2 || !FlightCorridorClear(origin, point, radius)) continue;
                float score = Vector3.Distance(origin, point) * 0.35f + Vector3.Distance(point, destination);
                // Prefer a bypass with a usable continuation, rather than repeatedly moving
                // to a point immediately in front of the same tree/building.
                if (!FlightCorridorClear(point, Toward(point, destination, 12), radius)) score += 20;
                foreach (Vector3 previous in recent)
                    if (Vector3.Distance(previous, point) < 3) score += 18;
                if (score >= best) continue;
                best = score;
                step = point;
                kind = point.Y > origin.Y + 2 ? "higher elevation" : "lateral arc";
            }
            return !float.IsInfinity(best);
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
