using System;
using System.IO;
using Newtonsoft.Json;

namespace RKmission
{
    // User-editable runtime settings; association radius is deliberately not configurable.
    internal sealed class OutdoorNavigationSettings
    {
        public int Sectors { get; set; } = 16;
        public float ProbeRadius { get; set; } = 12;
        public float MaxProbeRadius { get; set; } = 20;
        public int LegStallSeconds { get; set; } = 8;
        public int NoProgressSeconds { get; set; } = 90;
        public int TravelLimitMinutes { get; set; } = 15;
        public float FlightFloorClearance { get; set; } = 1.5f;
        public float MaxFlightBypassRadius { get; set; } = 36;
        public float FlightClimbLimit { get; set; } = 48;
        public float FlightCruiseClearance { get; set; } = 6;
        public int MaxEntrances { get; set; } = 256;
        public int AttemptsPerEntrance { get; set; } = 96;

        public static OutdoorNavigationSettings Load(string directory, Action<string> say)
        {
            string path = Path.Combine(directory, "navigation-settings.json");
            var settings = new OutdoorNavigationSettings();
            try
            {
                Directory.CreateDirectory(directory);
                if (File.Exists(path)) settings = JsonConvert.DeserializeObject<OutdoorNavigationSettings>(File.ReadAllText(path)) ?? settings;
                else File.WriteAllText(path, JsonConvert.SerializeObject(settings, Formatting.Indented));
            }
            catch (Exception ex) { say("Outdoor navigation settings unavailable; using defaults: " + ex.Message); }
            settings.Sectors = Math.Max(8, Math.Min(32, settings.Sectors));
            // Multiples of eight retain cardinal/intercardinal sectors and opposite pairs.
            settings.Sectors = (settings.Sectors / 8) * 8;
            settings.ProbeRadius = Clamp(settings.ProbeRadius, 8, 20, 12);
            settings.MaxProbeRadius = Clamp(settings.MaxProbeRadius, settings.ProbeRadius, 28, 20);
            settings.FlightFloorClearance = Clamp(settings.FlightFloorClearance, 0.5f, 3, 1.5f);
            settings.MaxFlightBypassRadius = Clamp(settings.MaxFlightBypassRadius, settings.MaxProbeRadius, 60, 36);
            settings.FlightClimbLimit = Clamp(settings.FlightClimbLimit, 8, 96, 48);
            settings.FlightCruiseClearance = Clamp(settings.FlightCruiseClearance, 2, 16, 6);
            settings.LegStallSeconds = Math.Max(6, Math.Min(20, settings.LegStallSeconds));
            settings.NoProgressSeconds = Math.Max(60, Math.Min(300, settings.NoProgressSeconds));
            settings.TravelLimitMinutes = Math.Max(5, Math.Min(30, settings.TravelLimitMinutes));
            settings.MaxEntrances = Math.Max(16, Math.Min(512, settings.MaxEntrances));
            settings.AttemptsPerEntrance = Math.Max(16, Math.Min(192, settings.AttemptsPerEntrance));
            say($"Outdoor navigation settings: sectors={settings.Sectors}, movement rings=6/{settings.ProbeRadius}/{settings.MaxProbeRadius} m, " +
                $"Fly exterior bound={settings.MaxFlightBypassRadius} m, Fly climb bound={settings.FlightClimbLimit} m, " +
                $"Fly cruise clearance={settings.FlightCruiseClearance} m, entry height trigger=10 m, " +
                $"door association radius=6 m, leg stall={settings.LegStallSeconds} s, no-progress={settings.NoProgressSeconds} s, file={path}.");
            return settings;
        }

        private static float Clamp(float value, float minimum, float maximum, float fallback) =>
            float.IsNaN(value) || float.IsInfinity(value) ? fallback : Math.Max(minimum, Math.Min(maximum, value));
    }
}
