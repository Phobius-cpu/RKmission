using System;
using System.Collections.Generic;
using System.Linq;
using AOSharp.Common.GameData;
using AOSharp.Core;
using AOSharp.Core.UI;
using MalisDungeonMap2;

namespace RKmission
{
    // Mali's Dungeon Map 2.0 supplies the room wall mesh and door positions.
    // The same room/door topology drives the route planner and the display.
    internal sealed class DungeonLayout : IDisposable
    {
        private readonly Dictionary<int, Room> _rooms;
        private readonly Dictionary<int, List<int>> _neighbors = new Dictionary<int, List<int>>();
        private readonly DungeonData _map;
        private readonly HashSet<int> _visited;
        private Func<int> _targetRoom;

        public DungeonLayout(HashSet<int> visited)
        {
            _visited = visited;
            _rooms = Playfield.Rooms.ToDictionary(room => room.Instance);
            foreach (int id in _rooms.Keys)
                _neighbors[id] = new List<int>();
            foreach (Room room in _rooms.Values)
            {
                for (int i = 0; i < room.NumDoors; i++)
                {
                    int id = room.GetDoorConnectZone(i);
                    if (id == room.Instance || !_rooms.ContainsKey(id))
                        continue;
                    if (!_neighbors[room.Instance].Contains(id))
                        _neighbors[room.Instance].Add(id);
                    if (!_neighbors[id].Contains(room.Instance))
                        _neighbors[id].Add(room.Instance);
                }
            }
            _map = new DungeonMapFactory().GetDungeonData();
        }

        public Room Room(int id) => _rooms.TryGetValue(id, out Room room) ? room : null;

        public IEnumerable<int> Neighbors(int id) =>
            _neighbors.TryGetValue(id, out List<int> neighbors) ? neighbors : Enumerable.Empty<int>();

        public void Show(Func<int> targetRoom)
        {
            _targetRoom = targetRoom;
            Game.OnUpdate += Draw;
        }

        public void Dispose()
        {
            Game.OnUpdate -= Draw;
        }

        private void Draw(object sender, float elapsed)
        {
            Room current = DynelManager.LocalPlayer?.Room;
            if (!Playfield.IsDungeon || current == null || _map == null ||
                !_map.MeshData.TryGetValue(current.Floor, out MeshData floor))
                return;

            // MDebug uses the same centered coordinates as Mali's renderer.
            Vector3 center = floor.Center;
            foreach (var roomWalls in floor.Walls)
            {
                Vector3 color = roomWalls.Key == current.Instance ? new Vector3(0f, 1f, 0.3f) :
                    _visited.Contains(roomWalls.Key) ? new Vector3(0.25f, 0.7f, 0.5f) :
                    new Vector3(1f, 0.85f, 0f);
                foreach (Edge edge in roomWalls.Value)
                    MDebug.DrawLine(edge.V1, edge.V2, color);
            }

            foreach (DoorTransform door in floor.Doors.Values)
                foreach (Edge edge in door.Edges)
                    MDebug.DrawLine(edge.V1, edge.V2,
                        door.IsEntrance ? new Vector3(0f, 0.9f, 0.8f) : new Vector3(1f, 0.7f, 0.2f));

            int target = _targetRoom?.Invoke() ?? -1;
            Room next = Room(target);
            if (next != null && next.Floor == current.Floor)
                MDebug.DrawLine(DynelManager.LocalPlayer.Position - center,
                    next.Center - center, new Vector3(0f, 0.8f, 1f));
        }
    }
}
