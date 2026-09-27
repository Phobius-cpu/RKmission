using System;
using System.Collections.Generic;
using System.Linq;
using AOSharp.Common.GameData;
using AOSharp.Core;
using MalisDungeonMap2;

namespace RKmission
{
    // Mali's original room outlines supply world-space interior destinations.
    internal sealed class DungeonLayout
    {
        internal sealed class Connection
        {
            public int Source, Target;
            public Vector3 Threshold, Interior, DeepInterior;
        }

        private readonly Dictionary<int, Room> _rooms;
        private readonly Dictionary<int, List<Edge>> _worldWalls = new Dictionary<int, List<Edge>>();
        private readonly Dictionary<int, List<int>> _neighbors = new Dictionary<int, List<int>>();
        private readonly Dictionary<string, Connection> _connections = new Dictionary<string, Connection>();
        private readonly DungeonData _maliData;
        public int MissingConnections { get; private set; }

        public DungeonLayout()
        {
            _maliData = new DungeonMapFactory().GetDungeonData();
            _rooms = Playfield.Rooms.ToDictionary(room => room.Instance);
            foreach (int id in _rooms.Keys)
                _neighbors[id] = new List<int>();
            foreach (Room room in _rooms.Values)
            {
                for (int i = 0; i < room.NumDoors; i++)
                {
                    int adjacent = room.GetDoorConnectZone(i);
                    if (adjacent == room.Instance || !_rooms.ContainsKey(adjacent)) continue;
                    room.GetDoorPosRot(i, out Vector3 threshold, out Quaternion rotation);
                    if (!TryInterior(adjacent, threshold, out Vector3 interior,
                        out Vector3 deepInterior))
                    {
                        MissingConnections++;
                        continue;
                    }
                    _connections[Key(room.Instance, adjacent)] = new Connection
                    {
                        Source = room.Instance, Target = adjacent,
                        Threshold = threshold,
                        Interior = interior, DeepInterior = deepInterior
                    };
                    if (!_neighbors[room.Instance].Contains(adjacent)) _neighbors[room.Instance].Add(adjacent);
                }
            }
        }

        public Room Room(int id) => _rooms.TryGetValue(id, out Room room) ? room : null;
        public IEnumerable<int> Neighbors(int id) => _neighbors.TryGetValue(id, out List<int> neighbors)
            ? neighbors : Enumerable.Empty<int>();

        public Connection Edge(int source, int target) =>
            _connections.TryGetValue(Key(source, target), out Connection edge) ? edge : null;

        public Door DoorAt(Connection edge) => Playfield.Doors
            .Where(door => Vector3.Distance(door.Position, edge.Threshold) <= 3f)
            .OrderByDescending(door =>
                (door.RoomLink1?.Instance == edge.Source && door.RoomLink2?.Instance == edge.Target) ||
                (door.RoomLink2?.Instance == edge.Source && door.RoomLink1?.Instance == edge.Target))
            .ThenBy(door => Vector3.Distance(door.Position, edge.Threshold))
            .FirstOrDefault();

        public bool IsInside(int roomId, Vector3 point, float clearance = 0f)
        {
            List<Edge> walls = Walls(roomId);
            return walls != null && walls.Count > 0 && Inside(walls, point, clearance);
        }

        // The original Mali renderer discovers entities from AllDynels. Use
        // its room outlines to associate those live entities with a room.
        public bool ContainsDynel(int roomId, Dynel dynel)
        {
            Room room = Room(roomId);
            return room != null &&
                (dynel.Room == null || dynel.Room.Floor == room.Floor) &&
                (IsInside(roomId, dynel.Position) || dynel.Room?.Instance == roomId);
        }

        public IEnumerable<Dynel> VisibleRoomDynels(int roomId) =>
            DynelManager.AllDynels.Where(dynel => ContainsDynel(roomId, dynel));

        private List<Edge> Walls(int roomId)
        {
            if (_worldWalls.TryGetValue(roomId, out List<Edge> cached)) return cached;
            Room room = Room(roomId);
            if (room == null || _maliData?.MeshData == null ||
                !_maliData.MeshData.TryGetValue(room.Floor, out MeshData floor) ||
                !floor.Walls.TryGetValue(roomId, out List<Edge> centered))
                return _worldWalls[roomId] = null;
            return _worldWalls[roomId] = centered
                .Select(x => new Edge(x.V1 + floor.Center, x.V2 + floor.Center)).ToList();
        }

        private bool TryInterior(int roomId, Vector3 threshold, out Vector3 interior,
            out Vector3 deepInterior)
        {
            interior = deepInterior = Vector3.Zero;
            List<Edge> walls = Walls(roomId);
            if (walls == null || walls.Count == 0) return false;
            float minX = walls.Min(x => Math.Min(x.V1.X, x.V2.X));
            float maxX = walls.Max(x => Math.Max(x.V1.X, x.V2.X));
            float minZ = walls.Min(x => Math.Min(x.V1.Z, x.V2.Z));
            float maxZ = walls.Max(x => Math.Max(x.V1.Z, x.V2.Z));
            float nearScore = float.MaxValue, deepScore = float.MinValue;
            bool found = false;
            for (int x = 1; x < 20; x++)
            for (int z = 1; z < 20; z++)
            {
                var point = new Vector3(minX + (maxX - minX) * x / 20f,
                    walls[0].V1.Y, minZ + (maxZ - minZ) * z / 20f);
                if (!Inside(walls, point, 0.8f)) continue;
                float distance = HorizontalDistance(point, threshold);
                if (distance < 2.5f) continue;
                found = true;
                float margin = walls.Min(edge => SegmentDistance(point, edge));
                float candidateNear = distance - margin * 0.25f;
                if (candidateNear < nearScore) { nearScore = candidateNear; interior = point; }
                float candidateDeep = Math.Min(distance, 12f) + margin * 0.25f;
                if (distance <= 14f && candidateDeep > deepScore)
                { deepScore = candidateDeep; deepInterior = point; }
            }
            if (found && deepScore == float.MinValue) deepInterior = interior;
            return found;
        }

        private static bool Inside(List<Edge> walls, Vector3 point, float clearance)
        {
            bool inside = false;
            foreach (Edge edge in walls)
            {
                if (clearance > 0f && SegmentDistance(point, edge) < clearance) return false;
                Vector3 a = edge.V1, b = edge.V2;
                if ((a.Z > point.Z) != (b.Z > point.Z) &&
                    point.X < (b.X - a.X) * (point.Z - a.Z) / (b.Z - a.Z) + a.X)
                    inside = !inside;
            }
            return inside;
        }

        private static float SegmentDistance(Vector3 point, Edge edge)
        {
            float dx = edge.V2.X - edge.V1.X, dz = edge.V2.Z - edge.V1.Z;
            float length = dx * dx + dz * dz;
            float t = length > 0f ? Math.Max(0f, Math.Min(1f,
                ((point.X - edge.V1.X) * dx + (point.Z - edge.V1.Z) * dz) / length)) : 0f;
            float x = edge.V1.X + t * dx - point.X, z = edge.V1.Z + t * dz - point.Z;
            return (float)Math.Sqrt(x * x + z * z);
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            float x = a.X - b.X, z = a.Z - b.Z;
            return (float)Math.Sqrt(x * x + z * z);
        }

        private static string Key(int source, int target) => source + ":" + target;
    }
}
