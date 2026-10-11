using System;
using System.Collections.Generic;
using AOSharp.Common.GameData;

namespace RKmission
{
    // Deterministic geometry for the repeated circular floors in PF 4107.
    // The constants are the least-squares common fit of the user-recorded
    // "Fgrid Floor 1" through "Fgrid Floor 10" routes (2026-10-11).
    internal static class FGridGeometry
    {
        internal const float CenterX = 307.003f;
        internal const float CenterZ = 67.037f;
        internal const float RingRadius = 11.694f;
        internal const float FloorYOffset = 1.435f;
        internal const float ArcWaypointSpacing = 2.5f;
        internal const float AnchorAcceptanceRadius = 0.75f;
        internal const float RingCrossTrackTolerance = 1.35f;
        internal const float RadialCrossTrackTolerance = 1.25f;

        internal static bool IsModeledFloor(int floor) => floor >= 1 && floor <= 10;

        internal static float FloorY(int floor) => floor * 10f + FloorYOffset;

        internal static Vector3 RingAnchor(Vector3 radialTarget, int floor)
        {
            float dx = radialTarget.X - CenterX;
            float dz = radialTarget.Z - CenterZ;
            float length = (float)Math.Sqrt(dx * dx + dz * dz);
            if (length < 0.01f)
                throw new ArgumentException("FGrid radial target is at the ring center.", nameof(radialTarget));
            float scale = RingRadius / length;
            return new Vector3(CenterX + dx * scale, FloorY(floor), CenterZ + dz * scale);
        }

        internal static Vector3 ProjectToRing(Vector3 position, int floor) =>
            RingAnchor(position, floor);

        internal static float RingCrossTrack(Vector3 position)
        {
            float dx = position.X - CenterX;
            float dz = position.Z - CenterZ;
            return Math.Abs((float)Math.Sqrt(dx * dx + dz * dz) - RingRadius);
        }

        internal static float RadialCrossTrack(Vector3 position, Vector3 anchor,
            Vector3 target)
        {
            float dx = target.X - anchor.X;
            float dz = target.Z - anchor.Z;
            float length = (float)Math.Sqrt(dx * dx + dz * dz);
            if (length < 0.01f) return 0f;
            return Math.Abs((position.X - anchor.X) * dz -
                (position.Z - anchor.Z) * dx) / length;
        }

        internal static bool IsPlausibleRadialTarget(Vector3 target)
        {
            float dx = target.X - CenterX;
            float dz = target.Z - CenterZ;
            float radius = (float)Math.Sqrt(dx * dx + dz * dz);
            // Known lifts are near 6.8 m and surveyed portals near 16.1 m.
            return radius >= 5f && radius <= 18f;
        }

        internal static List<Vector3> ShortestArc(Vector3 from, Vector3 to, int floor)
        {
            double start = Math.Atan2(from.Z - CenterZ, from.X - CenterX);
            double end = Math.Atan2(to.Z - CenterZ, to.X - CenterX);
            double delta = end - start;
            while (delta > Math.PI) delta -= Math.PI * 2.0;
            while (delta < -Math.PI) delta += Math.PI * 2.0;
            int legs = Math.Max(1, (int)Math.Ceiling(Math.Abs(delta) * RingRadius /
                ArcWaypointSpacing));
            var result = new List<Vector3>(legs);
            for (int i = 1; i <= legs; i++)
            {
                double angle = start + delta * i / legs;
                result.Add(new Vector3(
                    CenterX + RingRadius * (float)Math.Cos(angle),
                    FloorY(floor),
                    CenterZ + RingRadius * (float)Math.Sin(angle)));
            }
            // Preserve the exact calculated destination anchor instead of the
            // small trigonometric round-trip error at the final waypoint.
            result[result.Count - 1] = RingAnchor(to, floor);
            return result;
        }
    }
}
