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
        public bool GroundUsesMesh, LandingVerified;
        public string GroundReason, FlyingReason;
        public float Cost(bool flying) => flying ? FlyingCost : GroundCost;
    }

    internal static class LocalRoutePlanner
    {
        public static LocalRoute Evaluate(AcceptedMission mission)
        {
            var route = new LocalRoute { Mission = mission };
            Vector3 origin = DynelManager.LocalPlayer.Position;
            if (!AcceptedMissions.Finite(origin) || !AcceptedMissions.Finite(mission.Entrance))
            {
                route.GroundReason = route.FlyingReason = "invalid world coordinates";
                return route;
            }
            route.GroundUsesMesh = TryGroundCost(origin, mission.Entrance, out float ground);
            route.GroundCost = route.GroundUsesMesh ? ground : Vector3.Distance(origin, mission.Entrance);
            route.GroundReason = route.GroundUsesMesh ? "complete navmesh path" :
                SMovementController.NavAgent?.HasPathfinder == true ? "mesh does not connect endpoints; direct estimate" :
                "no outdoor mesh; direct estimate";
            // Prepare a flight alternative even when on foot; choosing it still requires actual flight state.
            route.FlyingReason = "no clear climb/cruise/descent to a suitable approach point";
            for (int i = 0; i < 8; i++)
            {
                double angle = i * Math.PI / 4;
                Vector3 sample = mission.Entrance + new Vector3((float)Math.Cos(angle) * 12, 0, (float)Math.Sin(angle) * 12);
                if (!TryLandingPoint(sample, out Vector3 landing, out bool verified)) continue;
                // Landing/final approach must not depend on a ground navmesh. A missing distant
                // terrain hit is provisional until the client loads that area; recheck before descent.
                float finalCost = Vector3.Distance(landing, mission.Entrance);
                if (!TryCruise(origin, landing, out Vector3 start, out Vector3 end)) continue;
                float cost = Vector3.Distance(origin, start) + Vector3.Distance(start, end) +
                    Vector3.Distance(end, landing) + finalCost;
                if (route.LandingVerified && !verified) continue;
                if (route.LandingVerified == verified && cost >= route.FlyingCost) continue;
                route.FlyingCost = cost;
                route.LandingVerified = verified;
                route.FlyingReason = verified ? "direct flight; terrain approach" : "direct flight; terrain check on arrival";
                route.Landing = landing;
                route.CruiseStart = start;
                route.CruiseEnd = end;
            }
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

        public static bool TryLandingPoint(Vector3 sample, out Vector3 landing, out bool verified)
        {
            landing = sample;
            verified = false;
            if (!Playfield.Raycast(sample + new Vector3(0, 50, 0), sample - new Vector3(0, 60, 0),
                out Vector3 surface, out Vector3 normal)) return true;
            // Reject steep faces and high roofs/large drops relative to the accepted entrance.
            if (!AcceptedMissions.Finite(surface) || normal.Y < 0.65f || Math.Abs(surface.Y - sample.Y) > 8) return false;
            landing = surface;
            verified = true;
            return true;
        }

        // A local, geometry-checked waypoint, not a claim of a complete outdoor path.
        public static bool TryDirectGroundStep(Vector3 origin, Vector3 destination, bool alternate,
            int attempt, out Vector3 step, out bool detour)
        {
            step = origin;
            detour = false;
            float distance = HorizontalDistance(origin, destination);
            if (distance < 0.6f) return false;
            double heading = Math.Atan2(destination.Z - origin.Z, destination.X - origin.X);
            float length = Math.Min(12, distance);
            for (int i = alternate ? 1 : 0; i < 5; i++)
            {
                double offset = i == 0 ? 0 : ((i + 1) / 2) * Math.PI / 4 * (i % 2 == 1 ? 1 : -1);
                if (attempt % 2 == 1) offset = -offset;
                Vector3 candidate = origin + new Vector3((float)Math.Cos(heading + offset) * length, 0,
                    (float)Math.Sin(heading + offset) * length);
                if (Playfield.Raycast(candidate + new Vector3(0, 4, 0), candidate - new Vector3(0, 8, 0),
                    out Vector3 surface, out Vector3 normal))
                {
                    if (!AcceptedMissions.Finite(surface) || normal.Y < 0.65f || Math.Abs(surface.Y - origin.Y) > 4) continue;
                    candidate = surface;
                }
                if (!ClearSegment(origin + Vector3.Up, candidate + Vector3.Up)) continue;
                step = candidate;
                detour = i != 0;
                return true;
            }
            return false;
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
