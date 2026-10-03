using System;
using System.Collections.Generic;
using System.Linq;
using AOSharp.Common.GameData;
using AOSharp.Core;

namespace RKmission
{
    // Shared interaction-side selection. A candidate is issued only when the
    // loaded navmesh returns a complete corridor to it; straight-line range
    // across an interior wall is insufficient evidence of reachability.
    internal static class DungeonApproachPlanner
    {
        public static List<Vector3> Candidates(DungeonLayout layout, int roomId,
            Vector3 from, Vector3 target, float[] radii, float clearance)
        {
            Vector3 toward = from - target;
            toward.Y = 0;
            toward = toward.Magnitude > 0.1f ? toward.Normalize() : Vector3.Forward;
            Vector3 side = new Vector3(-toward.Z, 0, toward.X);
            var options = new List<Vector3>();
            foreach (float radius in radii)
                foreach (Vector3 direction in new[] { toward, side, -side, -toward })
                {
                    Vector3 point = target + direction * radius;
                    point.Y = from.Y;
                    if (layout.IsInside(roomId, point, clearance) &&
                        !options.Any(other => Vector3.Distance(other, point) < 0.8f))
                        options.Add(point);
                }
            if (layout.IsInside(roomId, target, clearance)) options.Add(target);
            return options.Select((point, index) => new
                {
                    Point = point, Index = index,
                    Cost = CompleteCost(from, point)
                })
                .Where(x => !float.IsInfinity(x.Cost))
                .OrderBy(x => x.Cost).ThenBy(x => x.Index)
                .Select(x => x.Point).ToList();
        }

        private static float CompleteCost(Vector3 from, Vector3 to)
        {
            try { return LocalRoutePlanner.TryDungeonGroundCost(from, to, out float cost) ? cost : float.PositiveInfinity; }
            catch { return float.PositiveInfinity; }
        }
    }
}
