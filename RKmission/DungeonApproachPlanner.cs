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
            Vector3 from, Vector3 target, float[] radii, float clearance,
            bool combatFiringSide = false)
        {
            Vector3 toward = from - target;
            toward.Y = 0;
            toward = toward.Magnitude > 0.1f ? toward.Normalize() : Vector3.Forward;
            Vector3 side = new Vector3(-toward.Z, 0, toward.X);
            var options = new List<Vector3>();
            Vector3 diagonalA = (toward + side).Normalize();
            Vector3 diagonalB = (toward - side).Normalize();
            Vector3[] directions = combatFiringSide
                ? new[] { toward, diagonalA, side, -diagonalB,
                    -toward, -diagonalA, -side, diagonalB }
                : new[] { toward, side, -side, -toward };
            foreach (float radius in radii)
                foreach (Vector3 direction in directions)
                {
                    Vector3 point = target + direction * radius;
                    point.Y = from.Y;
                    if (layout.IsInside(roomId, point, clearance) &&
                        !options.Any(other => Vector3.Distance(other, point) < 0.8f))
                        options.Add(point);
                }
            if (layout.IsInside(roomId, target, clearance)) options.Add(target);
            var routed = options.Select((point, index) => new
                {
                    Point = point, Index = index,
                    Cost = CompleteCost(from, point, combatFiringSide),
                    Clear = !combatFiringSide || ClearTargetSegment(point, target)
                })
                .Where(x => !float.IsInfinity(x.Cost)).ToList();
            // The target can stand behind an internal wall in the same AO room.
            // Prefer a reachable firing side, but retain a fallback when AO's
            // surface ray data is incomplete.
            bool hasClearSide = routed.Any(x => x.Clear);
            return routed.Where(x => !combatFiringSide || x.Clear || !hasClearSide)
                .OrderBy(x => x.Cost).ThenBy(x => x.Index)
                .Take(combatFiringSide ? 20 : int.MaxValue)
                .Select(x => x.Point).ToList();
        }

        private static bool ClearTargetSegment(Vector3 point, Vector3 target)
        {
            Vector3 eye = point + new Vector3(0, 1.3f, 0);
            Vector3 aim = target + new Vector3(0, 1.3f, 0);
            return LocalRoutePlanner.ClearInteractionSegment(eye, aim);
        }

        private static float CompleteCost(Vector3 from, Vector3 to, bool combatFiringSide)
        {
            try
            {
                float cost;
                bool complete = combatFiringSide
                    ? LocalRoutePlanner.TryDungeonCombatCost(from, to, out cost)
                    : LocalRoutePlanner.TryDungeonGroundCost(from, to, out cost);
                return complete ? cost : float.PositiveInfinity;
            }
            catch { return float.PositiveInfinity; }
        }
    }
}
