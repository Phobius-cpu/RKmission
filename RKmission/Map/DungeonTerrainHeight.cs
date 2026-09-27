using AOSharp.Common.GameData;
using AOSharp.Core;
using STriangle = SharpNav.Geometry.Triangle3;
using System;
using System.Collections.Generic;
using System.Linq;
using AOSharp.Common.Unmanaged.DbObjects;
using Mesh = AOSharp.Common.GameData.Mesh;
using Vector3 = AOSharp.Common.GameData.Vector3;
using AOSharp.Pathfinding;

namespace MalisDungeonMap2
{
    public class DungeonTerrainHeight
    {
        public static List<STriangle> CreateMesh(Room room, DungeonRDBTilemap tilemap)
        {
            int numWidthTiles = (int)room.LocalRect.MaxX - (int)room.LocalRect.MinX;
            int numHeightTiles = (int)room.LocalRect.MaxY - (int)room.LocalRect.MinY;
            float width = numWidthTiles * tilemap.TileSize;
            float length = numHeightTiles * tilemap.TileSize;
            int hCount = numWidthTiles + 1;
            int vCount = numHeightTiles + 1;
            int numTriangles = numWidthTiles * numHeightTiles * 6;
            int numVertices = hCount * vCount;

            Vector3[] vertices = new Vector3[numVertices];
            List<int> indices = new List<int>();

            Vector3 anchorOffset = new Vector3(1, 0, 1);
            anchorOffset.X += (room.Center.X - (float)numWidthTiles / 2) * tilemap.TileSize;
            anchorOffset.Z += (room.Center.Z - (float)numHeightTiles / 2) * tilemap.TileSize;

            int idx = 0;
            for (int z = 0; z < vCount; z++)
            {
                for (int x = 0; x < hCount; x++)
                {
                    byte height = tilemap.Heightmap[x + (int)room.LocalRect.MinX - 1, z + (int)room.LocalRect.MinY - 1];

                    Vector3 vertex = new Vector3
                    {
                        X = x * tilemap.TileSize - width / 2f,
                        Y = (float)height * tilemap.HeightmapScale,
                        Z = z * tilemap.TileSize - length / 2f
                    };
                    vertex.X -= anchorOffset.X;
                    vertex.Z -= anchorOffset.Z;
                    vertices[idx] = vertex;
                    idx++;
                }
            }

            idx = 0;
            for (int z = 0; z < numHeightTiles; z++)
            {
                for (int x = 0; x < numWidthTiles; x++)
                {
                    byte tileCollisionData = tilemap.CollisionData[x + (int)room.LocalRect.MinX, z + (int)room.LocalRect.MinY];

                    if (tileCollisionData > 0 && tileCollisionData != 0x80)
                    {
                        indices.Add((z * hCount) + x);
                        indices.Add(((z + 1) * hCount) + x);
                        indices.Add((z * hCount) + x + 1);

                        indices.Add(((z + 1) * hCount) + x);
                        indices.Add(((z + 1) * hCount) + x + 1);
                        indices.Add((z * hCount) + x + 1);
                    }
                    idx += 6;
                }
            }


            List<Vector3> optimizedVerts = vertices.ToList();
            List<int> optimizedIndices = indices;

            int testVertex = 0;

            while (testVertex < optimizedVerts.Count)
            {
                if (optimizedIndices.Contains(testVertex))
                {
                    testVertex++;
                }
                else
                {
                    optimizedVerts.RemoveAt(testVertex);

                    for (int i = 0; i < optimizedIndices.Count; i++)
                    {
                        if (optimizedIndices[i] > testVertex)
                            optimizedIndices[i]--;
                    }
                }
            }

            Mesh mesh = new Mesh
            {
                Triangles = optimizedIndices,
                Vertices = optimizedVerts,
                Position = room.Position - new Vector3(0, room.YOffset, 0),
                Rotation = Quaternion.CreateFromAxisAngle(Vector3.Up, room.Rotation * (Math.PI / 180)),
                Scale = new Vector3(1, 1, 1)
            };

            List<STriangle> tris = new List<STriangle>();

            for (int j = 0; j < mesh.Triangles.Count() / 3; j++)
            {
                int tri = j * 3;
                int tri1 = mesh.Triangles[tri];
                int tri2 = mesh.Triangles[tri + 1];
                int tri3 = mesh.Triangles[tri + 2];

                tris.Add(new STriangle(
                    mesh.LocalToWorldMatrix.MultiplyPoint3x4(mesh.Vertices[tri1]).ToSharpNav(),
                    mesh.LocalToWorldMatrix.MultiplyPoint3x4(mesh.Vertices[tri2]).ToSharpNav(),
                    mesh.LocalToWorldMatrix.MultiplyPoint3x4(mesh.Vertices[tri3]).ToSharpNav()));
            }

            return tris;
        }
    }
}