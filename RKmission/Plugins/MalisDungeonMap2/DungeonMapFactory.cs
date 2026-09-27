using AOSharp.Common.GameData;
using AOSharp.Core;
using STriangle = SharpNav.Geometry.Triangle3;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using AOSharp.Common.Unmanaged.DbObjects;
using MalisDungeonMap2;

namespace MalisDungeonMap2
{
    public class DungeonMapFactory
    {
        private ReadOnlyDictionary<string, List<STriangle>> _meshData;

        public DungeonMapFactory()
        {
        }


        public DungeonData GetDungeonData()
        {
            if (!Playfield.IsDungeon)
                return null;

            EdgeDetection edgeDet = new EdgeDetection();
            Dictionary<int, Dictionary<int, List<STriangle>>> roomFloorMesh = new Dictionary<int, Dictionary<int, List<STriangle>>>();
            Dictionary<int, List<DoorTransform>> doorPos = new Dictionary<int, List<DoorTransform>>();
            DungeonRDBTilemap tilemap = Playfield.RDBTilemap as DungeonRDBTilemap;
            var entranceDoorPos = Playfield.Doors.FirstOrDefault()?.Position ?? Vector3.Zero;

            foreach (Room room in Playfield.Rooms)
            {
                if (!roomFloorMesh.ContainsKey(room.Floor))
                {
                    roomFloorMesh.Add(room.Floor, new Dictionary<int, List<STriangle>>());
                    doorPos.Add(room.Floor, new List<DoorTransform>());
                }
                for (int i = 0; i < room.NumDoors; i++)
                {
                    if (room.GetDoorConnectZone(i) == room.Instance)
                        continue;

                    room.GetDoorPosRot(i, out Vector3 pos, out Quaternion rot);

                    if (doorPos[room.Floor].Any(x => x.Pos == pos))
                        continue;

                    doorPos[room.Floor].Add(new DoorTransform(pos, rot, Vector3.Distance(entranceDoorPos, pos) < 0.1f));
                }
                roomFloorMesh[room.Floor].Add(room.Instance, DungeonTerrainHeight.CreateMesh(room, tilemap));
            }

            var keys = doorPos.Keys.ToList();
            foreach (var key in keys)
            {
                doorPos[key] = doorPos[key].OrderBy(x => x.IsEntrance).ToList();
            }

            DungeonData dungeonData = new DungeonData();
            dungeonData.Identity = Playfield.ModelIdentity.Instance;
            dungeonData.MeshData = new Dictionary<int, MeshData>();

            foreach (var floorTrisData in roomFloorMesh)
            {
                Dictionary<int, List<Edge>> floorEdgeData = new Dictionary<int, List<Edge>>();
                Dictionary<int, List<Edge>> recenteredfloorEdgeData = new Dictionary<int, List<Edge>>();

                foreach (var s in floorTrisData.Value)
                {
                    floorEdgeData.Add(s.Key, edgeDet.Start(s.Value));
                }

                var center = floorEdgeData.SelectMany(x=>x.Value).GetCenter();
                foreach (var s in floorEdgeData)
                {
                    List<Edge> edges = new List<Edge>();

                    foreach (var ed in s.Value)
                        edges.Add(new Edge(ed.V1 - center, ed.V2 - center));

                    recenteredfloorEdgeData.Add(s.Key, edges);
                }

                Dictionary<int, DoorTransform> doorData = new Dictionary<int, DoorTransform>();

                var doorTransforms = doorPos[floorTrisData.Key];

                int i = 0;

                foreach (var doorTransform in doorTransforms)
                {
                    doorData.Add(i, doorTransform);
                    var pos = doorTransform.Pos;
                    var rot = (float)doorTransform.Rot.Yaw * MathExtras.Rad2Deg;

                    float baseSize = 6;
                    float halfWidth = baseSize / 2;
                    float halfHeight = baseSize / 4;
                    Vector3 pivot = new Vector3(pos.X, pos.Y, pos.Z);
                    Vector3[] corners = new Vector3[4];

                    corners[0] = Vector3.Rotate(pivot, new Vector3(pos.X - halfWidth, pos.Y, pos.Z - halfHeight), rot); // Bottom-left
                    corners[1] = Vector3.Rotate(pivot, new Vector3(pos.X + halfWidth, pos.Y, pos.Z - halfHeight), rot); // Bottom-right
                    corners[2] = Vector3.Rotate(pivot, new Vector3(pos.X + halfWidth, pos.Y, pos.Z + halfHeight), rot); // Top-right
                    corners[3] = Vector3.Rotate(pivot, new Vector3(pos.X - halfWidth, pos.Y, pos.Z + halfHeight), rot); // Top-left

                    doorData[i].Edges.Add(new Edge(corners[0] - center, corners[1] - center));
                    doorData[i].Edges.Add(new Edge(corners[1] - center, corners[2] - center));
                    doorData[i].Edges.Add(new Edge(corners[2] - center, corners[3] - center));
                    doorData[i].Edges.Add(new Edge(corners[3] - center, corners[0] - center));

                    doorTransform.Pos = doorTransform.Pos - center;
                    i++;
                }

                dungeonData.MeshData.Add(floorTrisData.Key, new MeshData(recenteredfloorEdgeData, center, doorData));

            }

            return dungeonData;
        }
    }
}

public class DoorTransform
{
    public Vector3 Pos;
    public Quaternion Rot;
    public List<Edge> Edges;
    public bool IsEntrance;

    public DoorTransform(Vector3 pos, Quaternion rot, bool entrance)
    {
        Pos = pos;
        Rot = rot;
        Edges = new List<Edge>();
        IsEntrance = entrance;
    }
}

public class DungeonData
{
    public int Identity;
    public Dictionary<int, MeshData> MeshData;
}

public class MeshData
{
    public Dictionary<int, List<Edge>> Walls;
    public Vector3 Center;
    public Dictionary<int, DoorTransform> Doors;

    public MeshData(Dictionary<int, List<Edge>> wallEdgesPerRoom, Vector3 center, Dictionary<int, DoorTransform> doors)
    {
        Walls = wallEdgesPerRoom;
        Center = center;
        Doors = doors;
    }
}

public enum MissionMode
{
    Rubika,
    Shadowlands,
    UnicornHubShip
}
