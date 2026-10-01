using System;
using System.IO;
using AOSharp.Common.GameData;
using Newtonsoft.Json;

namespace RKmission
{
    // Durable intent only. No path, live dynel, room-clearance or movement command
    // is restored from disk. The coordinator reconciles identity against AO# first.
    internal sealed class MissionCheckpoint
    {
        public bool Armed { get; set; }
        public bool AutoCycle { get; set; }
        public int AutoMissionLimit { get; set; } // Zero means no acceptance limit.
        public int AutoAcceptedCount { get; set; }
        public string Phase { get; set; } = "Idle";
        public int MissionType { get; set; }
        public int MissionInstance { get; set; }
        public int DestinationPlayfield { get; set; }
        public Vector3 Destination { get; set; }
        public int EntranceIdentity { get; set; }
        public int DungeonInstance { get; set; }
        public int EntranceRoom { get; set; }
        public Vector3? EntrancePosition { get; set; }
        public int Floor { get; set; }
        public string TravelProvider { get; set; }
        public string ObjectiveState { get; set; }

        private string _path;
        [JsonIgnore] private DateTime _nextWrite;

        public MissionCheckpoint() { }
        private MissionCheckpoint(string path) { _path = path; }

        public static MissionCheckpoint Load(string pluginDir, Action<string> say)
        {
            string path = Path.Combine(pluginDir, "RKMissionData", "checkpoint.json");
            try
            {
                if (File.Exists(path))
                {
                    MissionCheckpoint saved = JsonConvert.DeserializeObject<MissionCheckpoint>(File.ReadAllText(path));
                    if (saved != null)
                    {
                        saved._path = path;
                        return saved;
                    }
                }
            }
            catch (Exception ex) { say("Checkpoint could not be read: " + ex.Message); }
            return new MissionCheckpoint(path);
        }

        public void Save(bool force, Action<string> say)
        {
            if (!force && DateTime.UtcNow < _nextWrite) return;
            _nextWrite = DateTime.UtcNow.AddSeconds(5);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                string temp = _path + ".new";
                File.WriteAllText(temp, JsonConvert.SerializeObject(this, Formatting.Indented));
                if (File.Exists(_path)) File.Replace(temp, _path, null);
                else File.Move(temp, _path);
            }
            catch (Exception ex) { say("Checkpoint write failed: " + ex.Message); }
        }
    }
}
