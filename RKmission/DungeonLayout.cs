using System.Collections.Generic;
using System.Linq;
using AOSharp.Core;

namespace RKmission
{
    // Route topology from AO# rooms. Mali's original renderer supplies the UI.
    internal sealed class DungeonLayout
    {
        private readonly Dictionary<int, Room> _rooms;
        private readonly Dictionary<int, List<int>> _neighbors = new Dictionary<int, List<int>>();

        public DungeonLayout()
        {
            _rooms = Playfield.Rooms.ToDictionary(room => room.Instance);
            foreach (int id in _rooms.Keys)
                _neighbors[id] = new List<int>();
            foreach (Room room in _rooms.Values)
            {
                for (int i = 0; i < room.NumDoors; i++)
                {
                    int adjacent = room.GetDoorConnectZone(i);
                    if (adjacent == room.Instance || !_rooms.ContainsKey(adjacent)) continue;
                    if (!_neighbors[room.Instance].Contains(adjacent)) _neighbors[room.Instance].Add(adjacent);
                    if (!_neighbors[adjacent].Contains(room.Instance)) _neighbors[adjacent].Add(room.Instance);
                }
            }
        }

        public Room Room(int id) => _rooms.TryGetValue(id, out Room room) ? room : null;
        public IEnumerable<int> Neighbors(int id) => _neighbors.TryGetValue(id, out List<int> neighbors)
            ? neighbors : Enumerable.Empty<int>();
    }
}
