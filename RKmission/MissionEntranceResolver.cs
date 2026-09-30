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

        private sealed class MissionKeyCandidate
        {
            public Item Key;
            public int DynelInstance;
            public string Name;
            public string Label;
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
            var live = Playfield.ModelIdentity.Instance == playfield
                ? DynelManager.AllDynels.Where(x => x.Identity.Type == IdentityType.ACGEntrance &&
                    HorizontalDistance(x.Position, anchor) <= 8f)
                    .OrderBy(x => HorizontalDistance(x.Position, anchor))
                    .Select(x => x.Identity.Instance).Distinct().ToList()
                : new List<int>();
            var keys = new List<MissionKeyCandidate>();
            int missionKeys = 0;
            string firstUnreadableName = null;
            List<Item> inventory = Inventory.Items;
            foreach (Item key in inventory)
            {
                // Neko reads the dynamic name through the key's dynel identity.
                Identity keyDynel = N3EngineClientAnarchy.TemplateIDToDynelID(key.Slot);
                bool templateMatches = TryMissionKeyLabel(key.Name, out string templateLabel);
                if (keyDynel.Type != IdentityType.MissionKey &&
                    key.UniqueIdentity.Type != IdentityType.MissionKey && !templateMatches)
                    continue;
                missionKeys++;
                string keyName = keyDynel.Type == IdentityType.MissionKey
                    ? N3EngineClientAnarchy.GetName(keyDynel) : null;
                if (!TryMissionKeyLabel(keyName, out string label))
                {
                    if (!templateMatches)
                    {
                        if (firstUnreadableName == null)
                            firstUnreadableName = $"dynel='{keyName}', template='{key.Name}'";
                        continue;
                    }
                    label = templateLabel;
                }
                keys.Add(new MissionKeyCandidate
                {
                    Key = key,
                    DynelInstance = keyDynel.Type == IdentityType.MissionKey
                        ? keyDynel.Instance : key.UniqueIdentity.Instance,
                    Name = keyName ?? key.Name,
                    Label = label
                });
            }

            // A nearby ACG entrance is tied to the mission anchor. Prefer only
            // keys whose label actually contains that entrance ID in Neko data.
            // With several keys and no live match, static brute force can open
            // another accepted mission's dungeon, so travel to the anchor.
            int matchingLiveKeys = live.Count == 0 ? 0 : keys.Count(key =>
                _entrances.TryGetValue(key.Label, out List<uint> known) &&
                live.Any(id => known.Contains(unchecked((uint)id))));
            foreach (MissionKeyCandidate key in keys)
            {
                var ids = new List<int>();
                bool hasKnownLabel = _entrances.TryGetValue(key.Label, out List<uint> known);
                if (live.Count > 0 && matchingLiveKeys <= 1)
                {
                    if (hasKnownLabel)
                        ids.AddRange(live.Where(id => known.Contains(unchecked((uint)id))));
                    else if (keys.Count == 1)
                        ids.AddRange(live);
                }
                else if (keys.Count == 1)
                {
                    if (_successfulKeys.TryGetValue(key.DynelInstance, out int cached))
                        ids.Add(cached);
                    if (hasKnownLabel && known.Count <= MaxUnlocatedEntrancesPerName)
                        ids.AddRange(known.Select(x => unchecked((int)x)));
                    else if (hasKnownLabel && known.Count > MaxUnlocatedEntrancesPerName)
                        _say($"ACG label '{key.Label}' has {known.Count} unlocated entrances, above the {MaxUnlocatedEntrancesPerName} candidate limit; using normal travel.");
                }
                foreach (int id in ids.Distinct())
                {
                    if (_attempts.Count >= MaxCandidateAttempts) break;
                    _attempts.Add(new KeyEntrance
                    {
                        Key = key.Key, KeyDynelInstance = key.DynelInstance,
                        KeyName = key.Name, Entrance = id
                    });
                }
            }
            if (_attempts.Count > 0)
                _say($"Selected mission has {_attempts.Count} Neko ACG key/entrance candidate pairs across " +
                    $"{_attempts.Select(x => x.KeyDynelInstance).Distinct().Count()} mission key(s), " +
                    $"mode={(live.Count > 0 ? "nearby entrance" : "single-key static")}; trying key warp before normal travel.");
            else
            {
                string reason = _entrances.Count == 0
                    ? "ACGEntrances.json was not loaded or has no entries"
                    : missionKeys == 0
                        ? "no mission-key dynel found in inventory"
                        : keys.Count == 0
                            ? $"mission-key dynels have no readable destination label ({firstUnreadableName})"
                            : live.Count == 0 && keys.Count > 1
                                ? "several mission keys and no nearby entrance to identify the selected mission"
                                : matchingLiveKeys > 1
                                    ? "several mission keys match nearby entrance IDs"
                                    : live.Count > 0
                                        ? "no mission-key label matches the nearby entrance IDs"
                                    : $"key label '{keys[0].Label}' has no bounded entrance IDs";
                if (reason != _lastNoCandidateReason ||
                    DateTime.UtcNow - _lastNoCandidateLogAt > TimeSpan.FromSeconds(30))
                {
                    _say($"No bounded Neko ACG key/entrance candidate: {reason}; " +
                        $"inventory items={inventory.Count}, keys={missionKeys}, named={keys.Count}, nearby entrances={live.Count}, entrance labels={_entrances.Count}. Using normal travel.");
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
            if (_attempts.Count <= 10 || _index == 0 || _index % 10 == 0)
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
