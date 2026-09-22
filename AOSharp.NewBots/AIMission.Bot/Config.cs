using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;

namespace AIMission.Bot
{
    internal class Config
    {
        [JsonIgnore]
        private string _configPath;

        public MissionDifficulty MissionDifficulty { get; set; } = MissionDifficulty.Easy;
        public bool ClearCoccoons { get; set; } = true;
        public Dictionary<string, bool> Bosses { get; set; } = new Dictionary<string, bool>
        {
            { "Master of PsyMod", true },
            { "Master of Time and Space", true },
            { "Master of Biological Metamorphoses", true },
            { "Master of Silence", true },
            { "Master of Nanovoid", true },
            { "Coccoon Attendant - Cha'Heru", true }
        };

        public static Config Load(string path)
        {
            Config config;

            try
            {
                if (!File.Exists(path))
                {
                    config = new Config();
                }
                else
                {
                    string json = File.ReadAllText(path);
                    config = JsonConvert.DeserializeObject<Config>(json) ?? new Config();
                }
            }
            catch
            {
                config = new Config();
            }

            config._configPath = path;
            return config;
        }

        public void Save()
        {
            if (string.IsNullOrEmpty(_configPath))
                throw new InvalidOperationException("Config path not set. Load the config before saving.");

            string json = JsonConvert.SerializeObject(this, Formatting.Indented);
            File.WriteAllText(_configPath, json);
        }


        public void Reload()
        {
            if (string.IsNullOrEmpty(_configPath))
                throw new InvalidOperationException("Config path not set. Load the config before reloading.");

            if (File.Exists(_configPath))
            {
                string json = File.ReadAllText(_configPath);
                var newConfig = JsonConvert.DeserializeObject<Config>(json);

                MissionDifficulty = newConfig.MissionDifficulty;
                ClearCoccoons = newConfig.ClearCoccoons;
                Bosses = newConfig.Bosses;
            }
        }
    }
}
