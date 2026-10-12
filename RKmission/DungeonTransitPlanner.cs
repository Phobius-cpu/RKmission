using System;
using System.Collections.Generic;
using System.Linq;
using AOSharp.Common.GameData;

namespace RKmission
{
    // Shared same-room transit. It uses the ordinary complete dungeon navmesh
    // corridor so stairs and ramps remain valid. Final sight/use checks belong
    // to the combat and loot consumers.
    internal static class DungeonTransitPlanner
    {
        internal sealed class Route
        {
            public Vector3 Destination;
            public float Cost;
            public float VerticalProgress;
            public bool CorridorDerived;
            public bool TargetLevel;
            public List<Vector3> Waypoints = new List<Vector3>();
        }

        public static bool TryCost(DungeonLayout layout, int roomId, Vector3 from,
            Vector3 destination, out float cost)
        {
            cost = float.PositiveInfinity;
            if (layout == null || !layout.IsInside(roomId, from, 0.1f) ||
                !layout.IsInside(roomId, destination, 0.3f)) return false;
            try { return LocalRoutePlanner.TryDungeonGroundCost(from, destination, out cost); }
            catch { cost = float.PositiveInfinity; return false; }
        }

        public static List<Route> Candidates(DungeonLayout layout, int roomId,
            Vector3 from, Vector3 target, float[] radii, float clearance)
        {
            float verticalGap = Math.Abs(target.Y - from.Y);
            Vector3 toward = from - target;
            toward.Y = 0;
            toward = toward.Magnitude > 0.1f ? toward.Normalize() : Vector3.Forward;
            Vector3 side = new Vector3(-toward.Z, 0, toward.X);
            Vector3[] directions = { toward, (toward + side).Normalize(), side,
                (-toward + side).Normalize(), -toward, (-toward - side).Normalize(),
                -side, (toward - side).Normalize() };
            float[] levels = Math.Abs(target.Y - from.Y) > 1f
                ? new[] { target.Y, (target.Y + from.Y) * 0.5f, from.Y }
                : new[] { target.Y };
            var points = new List<Vector3>();
            foreach (float radius in radii)
                foreach (Vector3 direction in directions)
                    foreach (float level in levels)
                    {
                        Vector3 point = target + direction * radius;
                        point.Y = level;
                        if (layout.IsInside(roomId, point, clearance) &&
                            !points.Any(other => Vector3.Distance(other, point) < 0.8f))
                            points.Add(point);
                    }
            var routes = points.Select(point => BuildRoute(layout, roomId, from, target, point))
                .Where(route => !float.IsInfinity(route.Cost))
                .ToList();

            // Ask the mesh for the actual corridor toward the target surface.
            // Its crossing vertices reveal ramps and stairs even when radial
            // samples miss a narrow gallery. Use progressive vertices only;
            // final interaction range/visibility remains the consumer's job.
            try
            {
                if (LocalRoutePlanner.TryDungeonGroundPath(from, target,
                    out float corridorCost, out List<Vector3> corridor))
                {
                    float traversed = 0;
                    Vector3 previous = from;
                    foreach (Vector3 point in corridor)
                    {
                        traversed += Vector3.Distance(previous, point);
                        previous = point;
                        float progress = Progress(from, target, point);
                        if (progress < 0.75f || !layout.IsInside(roomId, point, clearance) ||
                            routes.Any(route => Vector3.Distance(route.Destination, point) < 0.8f)) continue;
                        routes.Add(new Route
                        {
                            Destination = point,
                            Cost = Math.Min(traversed, corridorCost),
                            VerticalProgress = progress,
                            CorridorDerived = true,
                            TargetLevel = Math.Abs(point.Y - target.Y) <= 1.5f,
                            Waypoints = ProgressiveWaypoints(layout, roomId, from,
                                corridor.TakeWhile(vertex => Vector3.Distance(vertex, point) > 0.25f)
                                    .Concat(new[] { point }))
                        });
                    }
                }
            }
            catch { }

            bool separated = verticalGap >= 1.5f;
            bool hasProgressive = separated && routes.Any(route =>
                route.TargetLevel || route.VerticalProgress >= Math.Min(2f, verticalGap * 0.25f));
            return routes
                .OrderBy(route => hasProgressive && !(route.TargetLevel ||
                    route.VerticalProgress >= Math.Min(2f, verticalGap * 0.25f)))
                .ThenByDescending(route => separated ? route.VerticalProgress : 0)
                .ThenBy(route => Math.Abs(route.Destination.Y - target.Y))
                .ThenBy(route => route.Cost)
                .ToList();
        }

        private static Route BuildRoute(DungeonLayout layout, int roomId, Vector3 from,
            Vector3 target, Vector3 destination)
        {
            try
            {
                float cost;
                List<Vector3> corridor;
                if (!LocalRoutePlanner.TryDungeonGroundPath(from, destination, out cost, out corridor))
                    return new Route { Destination = destination, Cost = float.PositiveInfinity };
                return new Route
                {
                    Destination = destination,
                    Cost = cost,
                    VerticalProgress = Progress(from, target, destination),
                    TargetLevel = Math.Abs(destination.Y - target.Y) <= 1.5f,
                    Waypoints = ProgressiveWaypoints(layout, roomId, from,
                        corridor.Concat(new[] { destination }))
                };
            }
            catch { return new Route { Destination = destination, Cost = float.PositiveInfinity }; }
        }

        // Keep a bounded, strictly forward representation of the already-complete
        // navmesh corridor. Each issued leg is still routed by the movement controller.
        private static List<Vector3> ProgressiveWaypoints(DungeonLayout layout, int roomId,
            Vector3 from, IEnumerable<Vector3> corridor)
        {
            const int maximum = 12;
            var useful = new List<Vector3>();
            Vector3 previous = from;
            foreach (Vector3 point in corridor)
            {
                if (!layout.IsInside(roomId, point, 0.3f) ||
                    Vector3.Distance(previous, point) < 1.25f) continue;
                if (useful.Count < maximum - 1) useful.Add(point);
                else if (useful.Count == maximum - 1) useful.Add(point);
                else useful[maximum - 1] = point;
                previous = point;
            }
            return useful;
        }

        private static float Progress(Vector3 from, Vector3 target, Vector3 point)
            => Math.Max(0, Math.Abs(target.Y - from.Y) - Math.Abs(target.Y - point.Y));
    }
}
