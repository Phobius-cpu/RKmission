using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace RKmission
{
    internal sealed class MissionReadinessSettings
    {
        public int HealthPercent { get; set; } = 95;
        public int NanoPercent { get; set; } = 95;
        public int HandlerStartSeconds { get; set; } = 3;
        public int QuietSeconds { get; set; } = 2;
        public int NoProgressSeconds { get; set; } = 45;
        public int TimeoutSeconds { get; set; } = 180;
        // Empty by default: the user's combat handler selects profession buffs.
        public int[] BuffNanoIds { get; set; } = new int[0];

        public static MissionReadinessSettings Load(string pluginDirectory, Action<string> say)
        {
            var settings = new MissionReadinessSettings();
            string path = Path.Combine(pluginDirectory, "RKMissionData", "readiness-settings.json");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                if (File.Exists(path))
                    settings = JsonConvert.DeserializeObject<MissionReadinessSettings>(File.ReadAllText(path)) ?? settings;
                else
                    File.WriteAllText(path, JsonConvert.SerializeObject(settings, Formatting.Indented));
            }
            catch (Exception ex) { say("Readiness settings unavailable; using defaults: " + ex.Message); }
            settings.HealthPercent = Math.Max(1, Math.Min(100, settings.HealthPercent));
            settings.NanoPercent = Math.Max(1, Math.Min(100, settings.NanoPercent));
            settings.HandlerStartSeconds = Math.Max(1, Math.Min(10, settings.HandlerStartSeconds));
            settings.QuietSeconds = Math.Max(1, Math.Min(10, settings.QuietSeconds));
            settings.NoProgressSeconds = Math.Max(15, Math.Min(120, settings.NoProgressSeconds));
            settings.TimeoutSeconds = Math.Max(settings.NoProgressSeconds, Math.Min(600, settings.TimeoutSeconds));
            settings.BuffNanoIds = (settings.BuffNanoIds ?? new int[0]).Where(x => x > 0).Distinct().ToArray();
            say($"Mission readiness: HP/nano targets={settings.HealthPercent}/{settings.NanoPercent}%, " +
                $"configured buffs={settings.BuffNanoIds.Length}, file={path}.");
            return settings;
        }
    }
}
