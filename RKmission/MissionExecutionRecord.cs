using System;
using System.Collections.Generic;
using AOSharp.Common.GameData;

namespace RKmission
{
    // Diagnostic runtime knowledge. A restart never trusts these room or edge
    // observations as live clearance; exact mission verification runs again.
    internal sealed class MissionExecutionRecord
    {
        public int MissionId { get; set; }
        public int DungeonInstance { get; set; }
        public int EntryRoom { get; set; }
        public Vector3 EntryPosition { get; set; }
        public int CurrentRoom { get; set; }
        public int Floor { get; set; }
        public List<int> FloorsVisited { get; set; } = new List<int>();
        public Dictionary<int, string> RoomStates { get; set; } = new Dictionary<int, string>();
        public Dictionary<string, int> EdgeFailures { get; set; } = new Dictionary<string, int>();
        public List<int> ObjectiveRooms { get; set; } = new List<int>();
        public string ObjectiveSteps { get; set; }
        public string LootBlockers { get; set; }
        public string ExitDoor { get; set; }
        public string Phase { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
    }
}
