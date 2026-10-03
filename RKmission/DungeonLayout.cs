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
            public Vector3 Threshold, DoorCenter, SourceApproach, TargetCenterline, Interior, DeepInterior;
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
                    Vector3 sourceApproach = DoorwayPoint(room.Instance, threshold, 1.35f);
                    Vector3 axis = interior - sourceApproach;
                    axis.Y = 0;
                    Vector3 targetCenterline = AlignedDoorwayPoint(adjacent, threshold, axis,
                        1.8f, DoorwayPoint(adjacent, threshold, 1.8f));
                    sourceApproach = AlignedDoorwayPoint(room.Instance, threshold, -axis,
                        1.35f, sourceApproach);
                    _connections[Key(room.Instance, adjacent)] = new Connection
                    {
                        Source = room.Instance, Target = adjacent,
                        Threshold = threshold, DoorCenter = threshold,
                        SourceApproach = sourceApproach,
                        TargetCenterline = targetCenterline,
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

        public bool TryExit(int entryRoom, Vector3 arrival, out Vector3 threshold,
            out Vector3 across, out Identity doorId)
        {
            threshold = across = Vector3.Zero;
            doorId = Identity.None;
            Room room = Room(entryRoom);
            if (room == null) return false;
            var candidates = new List<Vector3>();
            for (int i = 0; i < room.NumDoors; i++)
            {
                int adjacent = room.GetDoorConnectZone(i);
                if (_rooms.ContainsKey(adjacent)) continue; // Includes unavailable interior connections.
                room.GetDoorPosRot(i, out Vector3 point, out Quaternion rotation);
                candidates.Add(point);
            }
            // A live external door may exist without an external room-table entry.
            foreach (Door door in Playfield.Doors.Where(x =>
                (x.RoomLink1?.Instance == entryRoom && x.RoomLink2 == null) ||
                (x.RoomLink2?.Instance == entryRoom && x.RoomLink1 == null) ||
                (x.RoomLink1 == null && x.RoomLink2 == null &&
                    (x.Room == null || x.Room.Floor == room.Floor) &&
                    Walls(entryRoom)?.Any(wall => SegmentDistance(x.Position, wall) <= 1.5f) == true &&
                    !_connections.Values.Any(edge => Vector3.Distance(edge.Threshold, x.Position) <= 3f))))
                candidates.Add(door.Position);
            if (candidates.Count == 0) return false;
            threshold = candidates.OrderBy(x => Vector3.Distance(x, arrival)).First();
            Vector3 outward = threshold - arrival; outward.Y = 0;
            if (HorizontalDistance(outward, Vector3.Zero) < 0.5f) outward = -DynelManager.LocalPlayer.Rotation.Forward;
            outward.Y = 0;
            if (HorizontalDistance(outward, Vector3.Zero) < 0.1f) outward = new Vector3(1, 0, 0);
            outward = outward.Normalize();
            Vector3 exitPoint = threshold;
            var directions = new[] { outward, -outward, new Vector3(-outward.Z, 0, outward.X), new Vector3(outward.Z, 0, -outward.X) };
            Vector3? chosen = directions.Select(x => (Vector3?)(exitPoint + x * 3f))
                .FirstOrDefault(x => !IsInside(entryRoom, x.Value));
            if (!chosen.HasValue) return false;
            across = chosen.Value;
            doorId = Playfield.Doors.Where(x => Vector3.Distance(x.Position, exitPoint) <= 3f)
                .OrderBy(x => Vector3.Distance(x.Position, exitPoint)).FirstOrDefault()?.Identity ?? Identity.None;
            return true;
        }

        public bool TryEntryFromInside(Vector3 position, out int roomId, out Vector3 approach)
        {
            roomId = -1;
            approach = Vector3.Zero;
            float best = float.MaxValue;
            foreach (Room room in _rooms.Values)
            {
                if (!TryExit(room.Instance, position, out Vector3 threshold, out _, out _) ||
                    !TryInterior(room.Instance, threshold, out Vector3 interior, out _)) continue;
                float distance = Vector3.Distance(position, threshold);
                if (distance >= best) continue;
                best = distance; roomId = room.Instance; approach = interior;
            }
            return roomId >= 0;
        }

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

        // Choose a point on the room side of the doorway. The threshold itself
        // is a boundary and is never a normal navigation destination.
        private Vector3 DoorwayPoint(int roomId, Vector3 threshold, float preferredDistance)
        {
            if (!TryInterior(roomId, threshold, out Vector3 interior, out _))
                return threshold;
            Vector3 direction = interior - threshold;
            direction.Y = 0;
            if (direction.Magnitude < 0.1f) return interior;
            direction = direction.Normalize();
            for (float distance = preferredDistance; distance <= 2.5f; distance += 0.25f)
            {
                Vector3 candidate = threshold + direction * distance;
                candidate.Y = interior.Y;
                if (IsInside(roomId, candidate, 0.25f)) return candidate;
            }
            return interior;
        }

        private Vector3 AlignedDoorwayPoint(int roomId, Vector3 threshold,
            Vector3 direction, float preferredDistance, Vector3 fallback)
        {
            if (direction.Magnitude < 0.1f) return fallback;
            direction = direction.Normalize();
            for (float distance = preferredDistance; distance <= 2.5f; distance += 0.25f)
            {
                Vector3 candidate = threshold + direction * distance;
                candidate.Y = fallback.Y;
                if (IsInside(roomId, candidate, 0.25f)) return candidate;
            }
            return fallback;
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
