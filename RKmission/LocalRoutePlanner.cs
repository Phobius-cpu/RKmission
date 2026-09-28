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
        public int Playfield;
        public Vector3 Origin, Anchor;
        public float Cost;
        public bool GroundUsesMesh;
        public string Reason;
    }

    // Estimates and advisory geometry only. Execution never requires synthetic clearance.
    internal static class LocalRoutePlanner
    {
        public static LocalRoute Estimate(AcceptedMission mission, Vector3 origin, bool flying)
        {
            if (!AcceptedMissions.Finite(origin) || !AcceptedMissions.Finite(mission.Entrance)) return null;
            var route = new LocalRoute { Mission = mission, Playfield = mission.PlayfieldId, Origin = origin, Anchor = mission.Entrance,
                Cost = HorizontalDistance(origin, mission.Entrance) };
            // Quest Y is provisional. Project a local floor for optional mesh estimation;
            // never let a missing floor/mesh veto the direct horizontal estimate.
            Vector3 destination = mission.Entrance; destination.Y = origin.Y;
            if (!flying && TryEntranceSurface(destination, origin.Y, out float floor, out _)) destination.Y = floor;
            try
            {
                if (!flying && TryGroundCost(origin, destination, out float meshCost))
                { route.GroundUsesMesh = true; route.Cost = meshCost; }
            }
            catch { route.GroundUsesMesh = false; } // Optional mesh failures are advisory.
            route.Reason = flying ? "direct horizontal Fly estimate; local entry height unresolved" :
                route.GroundUsesMesh ? "complete optional Run navmesh estimate" : "direct horizontal Run estimate; mesh unavailable/disconnected";
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
            try { return SampleEntranceSurface(entrance, referenceHeight, out height, out support); }
            catch { height = referenceHeight; support = 0; return false; }
        }

        private static bool SampleEntranceSurface(Vector3 entrance, float referenceHeight, out float height, out int support)
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

        public static string Coordinates(Vector3 point) => string.Format(CultureInfo.InvariantCulture,
            "X={0:F2}, Z={1:F2}, height(Y)={2:F2}", point.X, point.Z, point.Y);

        public static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            float x = a.X - b.X, z = a.Z - b.Z;
            return (float)Math.Sqrt(x * x + z * z);
        }

        public static Vector3 Direction(double angle) => new Vector3((float)Math.Cos(angle), 0, (float)Math.Sin(angle));
        public static double Angle(Vector3 direction) => Math.Atan2(direction.Z, direction.X);
        public static Vector3 Toward(Vector3 origin, Vector3 destination, float length)
        {
            float distance = Vector3.Distance(origin, destination);
            return distance <= length || distance < 0.1f ? destination : origin + (destination - origin) * (length / distance);
        }

        public static Vector3 LocalElevation(Vector3 point, Vector3 player, bool flying,
            OutdoorNavigationSettings settings, out string source)
        {
            point.Y = player.Y;
            if (TryEntranceSurface(point, player.Y, out float height, out int support))
            {
                point.Y = height + (flying ? settings.FlightFloorClearance : 0);
                source = $"local supported floor ({support}/17 columns)";
            }
            else source = "current player elevation, provisional; no supported local floor";
            return point;
        }

        public static bool ClearSegment(Vector3 start, Vector3 end)
        {
            if (Vector3.Distance(start, end) <= 0.05f) return true;
            try { return !Playfield.Raycast(start, end, out _, out _); }
            catch { return true; } // Unavailable geometry is inconclusive, never a launch gate.
        }

        // AO surface rays are incomplete hints. Sample a small flight corridor
        // rather than just its centre; an observed movement block wins over rays.
        public static bool FlightCorridor(Vector3 start, Vector3 end, out Vector3 firstHit)
        {
            firstHit = end; float closest = float.PositiveInfinity; bool blocked = false;
            if (Vector3.Distance(start, end) <= 0.05f) return false;
            Vector3 delta = end - start; delta.Y = 0;
            Vector3 side = HorizontalDistance(delta, Vector3.Zero) > 0.1f ?
                new Vector3(-delta.Z, 0, delta.X).Normalize() * 0.6f : new Vector3(0.6f, 0, 0);
            foreach (Vector3 offset in new[] { Vector3.Zero, side, side * -1, Vector3.Up * 0.6f, Vector3.Up * -0.6f })
                try
                {
                    if (!Playfield.Raycast(start + offset, end + offset, out Vector3 hit, out _) || !AcceptedMissions.Finite(hit)) continue;
                    float distance = Vector3.Distance(start, hit);
                    if (distance < closest) { closest = distance; firstHit = hit; }
                    blocked = true;
                }
                catch { } // Missing scene evidence never vetoes motion.
            return blocked;
        }

        public static bool TryExteriorFloor(Vector3 exterior, float referenceHeight, out float height)
        {
            // First support beneath five nearby points. This identifies a
            // surface, not a doorway floor: it can still be a roof/platform.
            var heights = new List<float>(); height = referenceHeight;
            foreach (Vector3 offset in new[] { Vector3.Zero, new Vector3(0.8f, 0, 0), new Vector3(-0.8f, 0, 0),
                new Vector3(0, 0, 0.8f), new Vector3(0, 0, -0.8f) })
                try
                {
                    Vector3 top = exterior + offset, bottom = top;
                    top.Y = referenceHeight + 2; bottom.Y = referenceHeight - 96;
                    if (Playfield.Raycast(top, bottom, out Vector3 hit, out Vector3 normal) &&
                        AcceptedMissions.Finite(hit) && normal.Y >= 0.6f) heights.Add(hit.Y);
                }
                catch { }
            if (heights.Count < 3) return false;
            heights.Sort(); float median = heights[heights.Count / 2];
            if (heights.Count(x => Math.Abs(x - median) <= 1) < 3) return false;
            height = median; return true;
        }

        public static List<float> MissionEntrySupports(Vector3 anchor, float referenceHeight)
        {
            var samples = new List<SurfaceSample>();
            Vector3[] offsets = { Vector3.Zero, new Vector3(0.8f, 0, 0), new Vector3(-0.8f, 0, 0),
                new Vector3(0, 0, 0.8f), new Vector3(0, 0, -0.8f) };
            // Height belongs to the mission marker, never to a distant orbit
            // point. Keep lower anchor-local planes as hypotheses only; the
            // first supported plane is tried before any lower layer.
            for (int column = 0; column < offsets.Length; column++)
            {
                Vector3 top = anchor + offsets[column], bottom = top;
                top.Y = referenceHeight + 2; bottom.Y = referenceHeight - 96;
                for (int layer = 0; layer < 4; layer++)
                {
                    try
                    {
                        if (!Playfield.Raycast(top, bottom, out Vector3 hit, out Vector3 normal) ||
                            !AcceptedMissions.Finite(hit) || hit.Y >= top.Y) break;
                        if (normal.Y >= 0.6f) samples.Add(new SurfaceSample { Column = column, Height = hit.Y });
                        top.Y = hit.Y - 0.4f;
                        if (top.Y <= bottom.Y) break;
                    }
                    catch { break; }
                }
            }
            var heights = new List<float>();
            foreach (SurfaceSample centre in samples.Where(x => x.Column == 0).OrderByDescending(x => x.Height))
                if (samples.Where(x => Math.Abs(x.Height - centre.Height) <= 1).Select(x => x.Column).Distinct().Count() >= 3 &&
                    !heights.Any(x => Math.Abs(x - centre.Height) <= 1)) heights.Add(centre.Height);
            return heights;
        }

        public static float AdvisoryCruiseHeight(Vector3 anchor, Vector3 player, float ceiling, float clearance)
        {
            float desired = player.Y; bool sampled = false;
            Vector3 ahead = Toward(player, new Vector3(anchor.X, player.Y, anchor.Z), 20);
            foreach (Vector3 point in new[] { player, (player + ahead) * 0.5f, ahead, anchor })
                try
                {
                    Vector3 top = point, bottom = point; top.Y = ceiling; bottom.Y = player.Y - 96;
                    if (!Playfield.Raycast(top, bottom, out Vector3 hit, out _) || !AcceptedMissions.Finite(hit)) continue;
                    sampled = true;
                    // Include the normal transit arrival tolerance in the
                    // requested clearance; stop-short must not consume it.
                    desired = Math.Max(desired, hit.Y + clearance + 0.8f);
                }
                catch { }
            return Math.Min(ceiling, sampled ? desired : player.Y + 8);
        }

        public static float AdvisoryOverpassHeight(Vector3 anchor, Vector3 player, Vector3 exterior, float baseHeight)
        {
            float desired = Math.Max(baseHeight + 8, player.Y + 4);
            // Roof/terrain hits suggest a height, never certify or reject flight.
            // Even an unknown/taller obstacle gets a bounded observed attempt.
            foreach (Vector3 point in new[] { player, anchor, exterior, (player + anchor) * 0.5f })
            {
                Vector3 top = point, bottom = point; top.Y = baseHeight + 24; bottom.Y = baseHeight - 2;
                try
                {
                    if (Playfield.Raycast(top, bottom, out Vector3 hit, out _) && AcceptedMissions.Finite(hit))
                        desired = Math.Max(desired, hit.Y + 3);
                }
                catch { } // Missing geometry retains the modest climb fallback.
            }
            return Math.Min(baseHeight + 24, desired);
        }

        private static SVector ToSharp(Vector3 point) => new SVector(point.X, point.Y, point.Z);
        private static Vector3 ToAO(SVector point) => new Vector3(point.X, point.Y, point.Z);
    }
}
