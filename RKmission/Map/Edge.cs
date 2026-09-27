using AOSharp.Common.GameData;
using STriangle3 = SharpNav.Geometry.Triangle3;
using System.Collections.Generic;
using AOSharp.Pathfinding;
using System;
using System.Linq;

namespace MalisDungeonMap2
{

    public struct Edge : IEquatable<Edge>
    {
        public Vector3 V1, V2;
        public Vector3 Center;

        public Edge(Vector3 v1, Vector3 v2)
        {
            if (CompareVector3(v1, v2) < 0)
            {
                V1 = v1;
                V2 = v2;
            }
            else
            {
                V1 = v2;
                V2 = v1;
            }

            Center = (v1 + v2) / 2;
        }
        public Vector3 Direction => (V2 - V1).Normalize();

        private static int CompareVector3(Vector3 v1, Vector3 v2)
        {
            int compareX = v1.X.CompareTo(v2.X);
            if (compareX != 0) return compareX;

            int compareY = v1.Y.CompareTo(v2.Y);
            if (compareY != 0) return compareY;

            return v1.Z.CompareTo(v2.Z);
        }

        public bool Equals(Edge other)
        {
            return V1.Equals(other.V1) && V2.Equals(other.V2);
        }

        public override bool Equals(object obj)
        {
            return obj is Edge other && Equals(other);
        }

        public override int GetHashCode()
        {
            return CombineHashCodes(V1.GetHashCode(), V2.GetHashCode());
        }

        private int CombineHashCodes(int h1, int h2)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + h1;
                hash = hash * 31 + h2;
                return hash;
            }
        }
    }

    public static class Extensions
    {
        public static List<Edge> GetEdges(this STriangle3 tri)
        {
            return new List<Edge>
            {
                new Edge(tri.A.ToVector3(), tri.B.ToVector3()),
                new Edge(tri.B.ToVector3(), tri.C.ToVector3()),
                new Edge(tri.C.ToVector3(), tri.A.ToVector3())
            };
        }

        public static Vector3 GetCenter(this IEnumerable<Edge> edges)
        {
            Rect bounds = new Rect
            {
                MinX = float.MaxValue,
                MinY = float.MaxValue,
                MaxX = float.MinValue,
                MaxY = float.MinValue
            };

            foreach (var edge in edges)
            {
                UpdateBounds(ref bounds, edge.V1);
                UpdateBounds(ref bounds, edge.V2);
            }

            Vector3 center = new Vector3((bounds.MinX + bounds.MaxX) / 2, 0, (bounds.MinY + bounds.MaxY) / 2);

            return center;
        }

        private static void UpdateBounds(ref Rect bounds, Vector3 vertex)
        {
            bounds.MinX = Math.Min(bounds.MinX, vertex.X);
            bounds.MinY = Math.Min(bounds.MinY, vertex.Z);
            bounds.MaxX = Math.Max(bounds.MaxX, vertex.X);
            bounds.MaxY = Math.Max(bounds.MaxY, vertex.Z);
        }
    }
}

