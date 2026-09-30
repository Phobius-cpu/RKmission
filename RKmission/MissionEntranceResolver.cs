using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AOSharp.Common.GameData;
using AOSharp.Common.Unmanaged.Interfaces;
using AOSharp.Core;
using AOSharp.Core.Inventory;
using Newtonsoft.Json;
using SmokeLounge.AOtomation.Messaging.Messages;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;

namespace RKmission
{
    internal enum EntranceResult { Waiting, Fallback, Verified, Failed }

    // Neko's ACG candidate/feedback mechanism, bound to one exact accepted mission.
    internal sealed class MissionEntranceResolver : IDisposable
    {
        private const int MaxCandidateAttempts = 64;
        private const int MaxUnlocatedEntrancesPerName = 64;
        private const uint WrongKey = 0x0FCA6FF9;
        private const uint KeyAccepted = 0x0BC6E104;
        private readonly Action<string> _say;
        private readonly Dictionary<string, List<uint>> _entrances;
        private readonly Dictionary<int, int> _successfulKeys;
        private readonly string _cachePath;
        private readonly List<KeyEntrance> _attempts = new List<KeyEntrance>();
        private Mission _mission;
        private int _index;
        private bool _sent, _accepted;
        private DateTime _sentAt, _lastScanAt, _lastNoCandidateLogAt;
        private string _lastNoCandidateReason;
        public int ActiveEntranceIdentity => _index < _attempts.Count ? _attempts[_index].Entrance : 0;

        private sealed class KeyEntrance
        {
            public Item Key;
            public int KeyDynelInstance;
            public string KeyName;
            public int Entrance;
        }

        public MissionEntranceResolver(string pluginDir, Action<string> say)
        {
            _say = say;
            string path = Path.Combine(pluginDir, "Data", "ACGEntrances.json");
            var loadedEntrances = File.Exists(path)
                ? JsonConvert.DeserializeObject<Dictionary<string, List<uint>>>(File.ReadAllText(path))
                : null;
            _entrances = new Dictionary<string, List<uint>>(StringComparer.OrdinalIgnoreCase);
            if (loadedEntrances != null)
                foreach (var entry in loadedEntrances)
                {
                    if (_entrances.TryGetValue(entry.Key, out List<uint> existing))
                        existing.AddRange(entry.Value);
                    else
                        _entrances[entry.Key] = new List<uint>(entry.Value);
                }
            _cachePath = Path.Combine(pluginDir, "RKMissionData", "successful-entrances.json");
            try
            {
                _successfulKeys = File.Exists(_cachePath)
                    ? JsonConvert.DeserializeObject<Dictionary<int, int>>(File.ReadAllText(_cachePath))
                        ?? new Dictionary<int, int>()
                    : new Dictionary<int, int>();
            }
            catch (Exception ex)
            {
                _successfulKeys = new Dictionary<int, int>();
                _say("ACG entrance cache could not be read: " + ex.Message);
            }
            Network.N3MessageReceived += OnN3Message;
        }

        public void Select(Mission mission)
        {
            bool sameMission = _mission != null && mission != null && _mission.Identity == mission.Identity;
            if (sameMission && (_attempts.Count > 0 || _sent || _accepted ||
                DateTime.UtcNow - _lastScanAt < TimeSpan.FromSeconds(2)))
                return;
            if (!sameMission) _lastNoCandidateReason = null;
            Reset();
            _mission = mission;
            _lastScanAt = DateTime.UtcNow;
            if (mission?.Location == null) return;
            Vector3 anchor = mission.Location.Pos;
            int playfield = mission.Location.Playfield.Instance;
            // Live entrances near the anchor take priority over static Neko IDs.
            var live = Playfield.ModelIdentity.Instance == playfield
                ? DynelManager.AllDynels.Where(x => x.Identity.Type == IdentityType.ACGEntrance &&
                    HorizontalDistance(x.Position, anchor) <= 8f)
                    .OrderBy(x => HorizontalDistance(x.Position, anchor))
                    .Select(x => x.Identity.Instance).Distinct().ToList()
                : new List<int>();
            int missionKeys = 0, namedKeys = 0, mappedKeys = 0;
            string firstUnmappedLabel = null;
            List<Item> inventory = Inventory.Items;
            string firstUnreadableName = null;
            foreach (Item key in inventory)
            {
                // Neko reads the name from the key's dynel identity. Item.Name
                // is a template name and can omit the mission's dynamic label.
                Identity keyDynel = N3EngineClientAnarchy.TemplateIDToDynelID(key.Slot);
                bool templateMatches = TryMissionKeyLabel(key.Name, out string templateLabel);
                if (keyDynel.Type != IdentityType.MissionKey &&
                    key.UniqueIdentity.Type != IdentityType.MissionKey && !templateMatches)
                    continue;
                missionKeys++;
                string keyName = keyDynel.Type == IdentityType.MissionKey
                    ? N3EngineClientAnarchy.GetName(keyDynel) : null;
                if (!TryMissionKeyLabel(keyName, out string name))
                {
                    if (!templateMatches)
                    {
                        if (firstUnreadableName == null)
                            firstUnreadableName = $"dynel='{keyName}', template='{key.Name}'";
                        continue;
                    }
                    name = templateLabel;
                }
                namedKeys++;
                var ids = new List<int>();
                int keyDynelInstance = keyDynel.Type == IdentityType.MissionKey
                    ? keyDynel.Instance : key.UniqueIdentity.Instance;
                if (_successfulKeys.TryGetValue(keyDynelInstance, out int cached)) ids.Add(cached);
                ids.AddRange(live);
                if (_entrances.TryGetValue(name, out List<uint> known))
                {
                    if (known.Count <= MaxUnlocatedEntrancesPerName)
                    {
                        mappedKeys++;
                        ids.AddRange(known.Select(x => unchecked((int)x)));
                    }
                    else if (ids.Count == 0)
                        _say($"ACG label '{name}' has {known.Count} unlocated entrances, above the {MaxUnlocatedEntrancesPerName} candidate limit; using normal travel.");
                }
                else if (firstUnmappedLabel == null) firstUnmappedLabel = name;
                foreach (int id in ids.Distinct())
                {
                    if (_attempts.Count >= MaxCandidateAttempts) break;
                    _attempts.Add(new KeyEntrance { Key = key, KeyDynelInstance = keyDynelInstance, KeyName = keyName ?? key.Name, Entrance = id });
                }
            }
            if (_attempts.Count > 0)
                _say($"Selected mission has {_attempts.Count} Neko ACG key/entrance candidate pairs across " +
                    $"{_attempts.Select(x => x.KeyDynelInstance).Distinct().Count()} mission key(s); trying key warp before normal travel.");
            else
            {
                string reason = _entrances.Count == 0
                    ? "ACGEntrances.json was not loaded or has no entries"
                    : missionKeys == 0
                        ? "no mission-key dynel found in inventory"
                        : namedKeys == 0
                            ? $"mission-key dynels have no readable destination label ({firstUnreadableName})"
                            : mappedKeys == 0 && firstUnmappedLabel != null
                                ? $"key label '{firstUnmappedLabel}' is not in ACGEntrances.json"
                                : "no bounded entrance IDs for the available key";
                if (reason != _lastNoCandidateReason ||
                    DateTime.UtcNow - _lastNoCandidateLogAt > TimeSpan.FromSeconds(30))
                {
                    _say($"No bounded Neko ACG key/entrance candidate: {reason}; " +
                        $"inventory items={inventory.Count}, keys={missionKeys}, named={namedKeys}, entrance labels={_entrances.Count}. Using normal travel.");
                    _lastNoCandidateReason = reason;
                    _lastNoCandidateLogAt = DateTime.UtcNow;
                }
            }
        }

        public EntranceResult Tick()
        {
            if (_mission == null) return EntranceResult.Failed;
            if (Playfield.IsDungeon)
            {
                if (!VerifyCurrentDungeon(_mission, out _)) return EntranceResult.Failed;
                if ((_sent || _accepted) && _index < _attempts.Count) CacheSuccess(_attempts[_index]);
                return EntranceResult.Verified;
            }
            MissionLocation location = _mission.Location;
            if (location == null || DynelManager.LocalPlayer == null)
                return EntranceResult.Fallback;
            if (_attempts.Count == 0) return EntranceResult.Fallback;
            if (_accepted)
            {
                if (DateTime.UtcNow - _sentAt < TimeSpan.FromSeconds(20)) return EntranceResult.Waiting;
                _say("ACG key was accepted, but exact dungeon zoning did not follow.");
                return EntranceResult.Failed;
            }
            if (_sent && DateTime.UtcNow - _sentAt < TimeSpan.FromSeconds(2))
                return EntranceResult.Waiting;
            if (_sent) Advance();
            if (_index >= _attempts.Count) return EntranceResult.Fallback;
            KeyEntrance attempt = _attempts[_index];
            if (!Inventory.Items.Any(x => x.UniqueIdentity == attempt.Key.UniqueIdentity))
            { Advance(); return EntranceResult.Waiting; }
            if (_index == 0 || _index % 10 == 0)
                _say($"Neko ACG key warp: candidate {_index + 1}/{_attempts.Count}, key='{attempt.KeyName}', " +
                    $"entrance={unchecked((uint)attempt.Entrance)}, current playfield={Playfield.ModelIdentity.Instance}.");
            Item.UseItemOnItem(attempt.Key.Slot,
                new Identity(IdentityType.ACGEntrance, attempt.Entrance));
            _sent = true;
            _sentAt = DateTime.UtcNow;
            return EntranceResult.Waiting;
        }

        public static bool VerifyCurrentDungeon(Mission expected, out Mission current)
        {
            current = null;
            if (!Playfield.IsDungeon || !Mission.FindMissionForCurrentDungeon(out current))
                return false;
            return expected != null && current.Identity == expected.Identity;
        }

        public void RecordVerified(Mission current)
        {
            if ((_sent || _accepted) && _mission != null && current != null &&
                current.Identity == _mission.Identity && _index < _attempts.Count)
                CacheSuccess(_attempts[_index]);
        }

        private void OnN3Message(object sender, N3Message message)
        {
            if (!_sent || DynelManager.LocalPlayer == null ||
                message.Identity != DynelManager.LocalPlayer.Identity ||
                !(message is FeedbackMessage feedback) || feedback.CategoryId != 0x6E)
                return;
            if (feedback.MessageId == WrongKey)
            {
                Advance();
                _say("ACG entrance rejected this key; trying the next key/entrance pair.");
            }
            else if (feedback.MessageId == KeyAccepted)
            {
                _accepted = true;
                _say("ACG key accepted; waiting for exact mission-dungeon verification.");
            }
        }

        private void Advance()
        {
            _index++;
            _sent = false;
            if (_index == _attempts.Count)
                _say("Neko ACG key warp candidates were exhausted; using normal travel.");
        }
        private static bool TryMissionKeyLabel(string itemName, out string label)
        {
            label = null;
            if (string.IsNullOrWhiteSpace(itemName)) return false;
            string name = itemName.Trim();
            const string temporary = "Temporary:";
            if (name.StartsWith(temporary, StringComparison.OrdinalIgnoreCase))
                name = name.Substring(temporary.Length).TrimStart();
            const string prefix = "Mission key to ";
            if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
            label = name.Substring(prefix.Length).Trim();
            return label.Length > 0;
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            float x = a.X - b.X, z = a.Z - b.Z;
            return (float)Math.Sqrt(x * x + z * z);
        }

        private void CacheSuccess(KeyEntrance attempt)
        {
            _successfulKeys[attempt.KeyDynelInstance] = attempt.Entrance;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_cachePath));
                File.WriteAllText(_cachePath, JsonConvert.SerializeObject(_successfulKeys, Formatting.Indented));
            }
            catch (Exception ex) { _say("ACG association verified but cache write failed: " + ex.Message); }
            _sent = _accepted = false;
        }

        public void Reset()
        {
            _mission = null;
            _attempts.Clear();
            _index = 0;
            _sent = _accepted = false;
        }

        public void Dispose() => Network.N3MessageReceived -= OnN3Message;
    }
}
