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
            return points.Select(point =>
                {
                    float cost;
                    return new Route { Destination = point,
                        Cost = TryCost(layout, roomId, from, point, out cost)
                            ? cost : float.PositiveInfinity };
                })
                .Where(route => !float.IsInfinity(route.Cost))
                .OrderBy(route => Math.Abs(route.Destination.Y - target.Y))
                .ThenBy(route => route.Cost)
                .ToList();
        }
    }
}
