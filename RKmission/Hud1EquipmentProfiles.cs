using System;
using System.Collections.Generic;
using System.IO;
using AOSharp.Core.Inventory;
using Newtonsoft.Json;

namespace RKmission
{
    internal sealed class Hud1EquipmentModel
    {
        public int Id { get; set; }
        public int LowId { get; set; }
        public int HighId { get; set; }
        public int QualityLevel { get; set; }
        public string Name { get; set; } = string.Empty;

        public static Hud1EquipmentModel From(Item item) => item == null ? null : new Hud1EquipmentModel
        {
            Id = item.Id, LowId = item.LowId, HighId = item.HighId,
            QualityLevel = item.QualityLevel, Name = item.Name ?? string.Empty
        };

        public bool Matches(Item item) => item != null && item.Id == Id && item.LowId == LowId &&
            item.HighId == HighId && item.QualityLevel == QualityLevel &&
            string.Equals(item.Name ?? string.Empty, Name ?? string.Empty, StringComparison.Ordinal);
    }

    internal sealed class Hud1EquipmentProfile
    {
        public int CharacterInstance { get; set; }
        public string CharacterName { get; set; } = string.Empty;
        public Hud1EquipmentModel Vehicle { get; set; }
        public Hud1EquipmentModel Indoor { get; set; }
    }

    internal sealed class Hud1EquipmentProfiles
    {
        private sealed class ProfileFile
        { public List<Hud1EquipmentProfile> Characters { get; set; } = new List<Hud1EquipmentProfile>(); }

        private readonly string _path;
        private readonly Action<string> _say;
        private ProfileFile _data = new ProfileFile();

        public Hud1EquipmentProfiles(string pluginDir, Action<string> say)
        {
            _path = Path.Combine(pluginDir, "RKMissionData", "hud1-equipment.json");
            _say = say;
            Load();
        }

        public Hud1EquipmentProfile ForCharacter(int instance, string name)
        {
            string safeName = name ?? string.Empty;
            Hud1EquipmentProfile profile = _data.Characters.Find(x => x.CharacterInstance == instance &&
                string.Equals(x.CharacterName ?? string.Empty, safeName, StringComparison.OrdinalIgnoreCase));
            if (profile != null) return profile;
            profile = new Hud1EquipmentProfile { CharacterInstance = instance, CharacterName = safeName };
            _data.Characters.Add(profile);
            return profile;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                string temporary = _path + ".tmp";
                File.WriteAllText(temporary, JsonConvert.SerializeObject(_data, Formatting.Indented));
                if (File.Exists(_path)) File.Replace(temporary, _path, null);
                else File.Move(temporary, _path);
            }
            catch (Exception ex) { _say("HUD1 equipment profile save failed: " + ex.GetType().Name + "."); }
        }

        private void Load()
        {
            try
            {
                if (File.Exists(_path))
                    _data = JsonConvert.DeserializeObject<ProfileFile>(File.ReadAllText(_path)) ?? new ProfileFile();
                if (_data.Characters == null) _data.Characters = new List<Hud1EquipmentProfile>();
            }
            catch (Exception ex)
            {
                _data = new ProfileFile();
                _say("HUD1 equipment profile load failed; dynamic discovery remains available: " + ex.GetType().Name + ".");
            }
        }
    }
}
