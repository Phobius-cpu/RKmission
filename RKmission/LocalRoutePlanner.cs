using System;
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
        public float GroundCost = float.PositiveInfinity, FlyingCost = float.PositiveInfinity;
        public Vector3 Landing, CruiseStart, CruiseEnd;
        public float Cost(bool flying) => flying ? FlyingCost : GroundCost;
    }

    internal static class LocalRoutePlanner
    {
        public static LocalRoute Evaluate(AcceptedMission mission)
        {
            var route = new LocalRoute { Mission = mission };
            Vector3 origin = DynelManager.LocalPlayer.Position;
            if (TryGroundCost(origin, mission.Entrance, out float ground)) route.GroundCost = ground;
            // Prepare a flight alternative even when on foot; choosing it still requires actual flight state.
            for (int i = 0; i < 8; i++)
            {
                double angle = i * Math.PI / 4;
                Vector3 sample = mission.Entrance + new Vector3((float)Math.Cos(angle) * 12, 0, (float)Math.Sin(angle) * 12);
                if (!TryLandingPoint(sample, out Vector3 landing) ||
                    !TryGroundCost(landing, mission.Entrance, out float finalCost)) continue;
                if (!TryCruise(origin, landing, out Vector3 start, out Vector3 end)) continue;
                float cost = Vector3.Distance(origin, start) + Vector3.Distance(start, end) +
                    Vector3.Distance(end, landing) + finalCost;
                if (cost >= route.FlyingCost) continue;
                route.FlyingCost = cost;
                route.Landing = landing;
                route.CruiseStart = start;
                route.CruiseEnd = end;
            }
            return route;
        }

        // Arbitrary start positions are needed to cost the landing-to-door leg while still airborne.
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

        private static bool TryLandingPoint(Vector3 sample, out Vector3 landing)
        {
            landing = sample;
            if (SMovementController.NavAgent?.HasPathfinder != true) return false;
            if (!Playfield.Raycast(sample + new Vector3(0, 100, 0), sample - new Vector3(0, 100, 0),
                out Vector3 surface, out _)) return false;
            var query = new NavMeshQuery(SMovementController.NavAgent.NavMesh, 8192);
            SVector point = ToSharp(surface), extents = new SVector(3, 5, 3);
            if (!query.FindNearestPoly(ref point, ref extents, out NavPoint nearest) || nearest.Polygon == NavPolyId.Null) return false;
            landing = ToAO(nearest.Position);
            return Vector3.Distance(landing, surface) <= 3;
        }

        private static bool TryCruise(Vector3 origin, Vector3 landing, out Vector3 start, out Vector3 end)
        {
            float altitude = Math.Max(origin.Y, landing.Y + 20);
            float distance = HorizontalDistance(origin, landing);
            int steps = Math.Min(64, Math.Max(1, (int)Math.Ceiling(distance / 40)));
            // Terrain sampling sets broad-flight clearance; the complete horizontal segment is also raycast.
            for (int i = 0; i <= steps; i++)
            {
                float t = (float)i / steps;
                Vector3 point = origin + (landing - origin) * t;
                point.Y = altitude + 200;
                Vector3 below = point; below.Y = Math.Min(origin.Y, landing.Y) - 100;
                if (Playfield.Raycast(point, below, out Vector3 hit, out _)) altitude = Math.Max(altitude, hit.Y + 20);
            }
            start = new Vector3(origin.X, altitude, origin.Z);
            end = new Vector3(landing.X, altitude, landing.Z);
            for (int attempt = 0; attempt < 4; attempt++)
            {
                if (ClearSegment(start, end) && ClearSegment(origin, start) &&
                    ClearSegment(end, landing + new Vector3(0, 1.5f, 0))) return true;
                start.Y += 20; end.Y += 20;
            }
            return false;
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
