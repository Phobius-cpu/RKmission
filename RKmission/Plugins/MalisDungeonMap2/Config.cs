using AOSharp.Common.GameData;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;

namespace MalisDungeonMap2
{
    public class EntrySettings
    {
        public bool Show;
        public bool Outline;

        public EntrySettings(bool show, bool outline)
        {
            Show = show;
            Outline = outline;
        }
    }

    public class MapConfig
    {
        public Vector2 Offset;
        public float Scale;
        public float ViewDistance;
        public float OutlineOffset;
        public bool ShowConfigOnStartup;

        public Dictionary<Entry, EntrySettings> LineEntries;
        public Dictionary<Entry, EntrySettings> ShapeEntries;
        public Dictionary<Entry, Vector3> Colors;

        [JsonIgnore]
        public List<int> BlacklistIds;

        public string BlacklistIdsRaw;

        public void ParseBlacklist()
        {
            BlacklistIds = new List<int>();

            foreach (var line in BlacklistIdsRaw.Split('\n'))
            {
                foreach (var rawNumber in line.Split(' '))
                {
                    if (!int.TryParse(rawNumber, out int id))
                        break;

                    BlacklistIds.Add(id);
                    break;
                }
            }
        }

        public MapConfig()
        {
            BlacklistIds = new List<int> { 152, 4107 };
            BlacklistIdsRaw = "152 - Grid\n4107 - Fixer Grid";
            Offset = Vector2.Zero;
            Scale = 0.005f;
            ViewDistance = 1000;
            ShowConfigOnStartup = true;
            OutlineOffset = 1;
            Colors = new Dictionary<Entry, Vector3>
            {
                { Entry.Wall, Utils.HexToVector3("FFDB00") },
                { Entry.ActiveRoom, Utils.HexToVector3("00FF52")},
                { Entry.Door, Utils.HexToVector3("F4CD7A")  },
                { Entry.LocalPlayer, Utils.HexToVector3("44FFAA") },
                { Entry.MissionObjective, Utils.HexToVector3("FFFFFF") },
                { Entry.Player, Utils.HexToVector3("00FFFF") },
                { Entry.Npc, Utils.HexToVector3("FF002C") },
                { Entry.Terminal, Utils.HexToVector3("FF00EA") },
                { Entry.UnopenedChest, Utils.HexToVector3("FAA701") },
                { Entry.OpenedChest, Utils.HexToVector3("CCCCCC") },
                { Entry.EntranceDoor, Utils.HexToVector3("00E6CD") },
            };

            ShapeEntries = new Dictionary<Entry, EntrySettings>
            {
                { Entry.Door, new EntrySettings(true, false) },
                { Entry.EntranceDoor, new EntrySettings(true, false) },
                { Entry.Wall, new EntrySettings(true, false) },
                { Entry.Character, new EntrySettings(true, false) },
                { Entry.Terminal, new EntrySettings(true, false) },
                { Entry.MissionObjective, new EntrySettings(true, false) },
                { Entry.UnopenedChest, new EntrySettings(true, false) },
                { Entry.OpenedChest, new EntrySettings(true, false) },
            };

            LineEntries = new Dictionary<Entry, EntrySettings>
            {
                { Entry.EntranceDoor, new EntrySettings(false, false) },
                { Entry.Terminal, new EntrySettings(true, true) },
                { Entry.MissionObjective, new EntrySettings(true, true) },
                { Entry.UnopenedChest, new EntrySettings(false, false) },
                { Entry.OpenedChest, new EntrySettings(false, false) },
            };
        }
    }

    public class Config
    {
        public MapConfig MapConfig;
        private string _filePath;

        public Config(string filePath)
        {
            MapConfig = new MapConfig();
            _filePath = filePath;
        }

        public static Config TryLoad(string filePath)
        {
            try
            {
                Config config = JsonConvert.DeserializeObject<Config>(File.ReadAllText(filePath));
                config._filePath = filePath;
                return config;
            }
            catch (Exception ex)
            {
                var config = new Config(filePath);
                config.Save();
                return config;
            }
        }

        public void Save() => File.WriteAllText(_filePath, JsonConvert.SerializeObject(this,Formatting.Indented));
    }
}

public enum Entry
{
    None,
    [Description("Visited Room")]
    ActiveRoom,
    [Description("Room")]
    Wall,
    [Description("Local Player")]
    LocalPlayer,
    [Description("Other Player")]
    Player,
    Character,
    [Description("Monster")]
    Npc,
    Terminal,
    [Description("Looted Chest")]
    OpenedChest,
    [Description("Unlooted Chest")]
    UnopenedChest,
    [Description("Mission Objective")]
    MissionObjective,
    Door,
    [Description("Entrance Door")]
    EntranceDoor
}