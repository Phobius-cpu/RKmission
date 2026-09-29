using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AOSharp.Common.GameData;
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
        private const int MaxCandidateAttempts = 16;
        private const int MaxUnlocatedEntrancesPerName = 16;
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
        private bool _loadedNear;
        private DateTime _sentAt;
        public int ActiveEntranceIdentity => _index < _attempts.Count ? _attempts[_index].Entrance : 0;

        private sealed class KeyEntrance
        {
            public Item Key;
            public int Entrance;
        }

        public MissionEntranceResolver(string pluginDir, Action<string> say)
        {
            _say = say;
            string path = Path.Combine(pluginDir, "Data", "ACGEntrances.json");
            _entrances = File.Exists(path)
                ? JsonConvert.DeserializeObject<Dictionary<string, List<uint>>>(File.ReadAllText(path))
                    ?? new Dictionary<string, List<uint>>()
                : new Dictionary<string, List<uint>>();
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
            if (_mission?.Identity == mission?.Identity &&
                (_loadedNear || _sent || mission?.Location == null ||
                 Playfield.ModelIdentity.Instance != mission.Location.Playfield.Instance ||
                 DynelManager.LocalPlayer == null ||
                 HorizontalDistance(DynelManager.LocalPlayer.Position, mission.Location.Pos) > 8f))
                return;
            Reset();
            _mission = mission;
            if (mission?.Location == null) return;
            Vector3 anchor = mission.Location.Pos;
            int playfield = mission.Location.Playfield.Instance;
            _loadedNear = Playfield.ModelIdentity.Instance == playfield &&
                DynelManager.LocalPlayer != null &&
                HorizontalDistance(DynelManager.LocalPlayer.Position, anchor) <= 8f;
            // Some ACG entrances are exposed as dynels; static Neko IDs remain fallback.
            var live = Playfield.ModelIdentity.Instance == playfield
                ? DynelManager.AllDynels.Where(x => x.Identity.Type == IdentityType.ACGEntrance &&
                    HorizontalDistance(x.Position, anchor) <= 8f)
                    .OrderBy(x => HorizontalDistance(x.Position, anchor))
                    .Select(x => x.Identity.Instance).Distinct().ToList()
                : new List<int>();
            foreach (Item key in Inventory.Items.Where(x => x.UniqueIdentity.Type == IdentityType.MissionKey &&
                x.Name != null && x.Name.StartsWith("Mission key to ", StringComparison.OrdinalIgnoreCase)))
            {
                string name = key.Name.Substring("Mission key to ".Length);
                var ids = new List<int>();
                if (_successfulKeys.TryGetValue(key.UniqueIdentity.Instance, out int cached)) ids.Add(cached);
                ids.AddRange(live);
                if (_entrances.TryGetValue(name, out List<uint> known))
                {
                    // Common labels such as "a house" map to hundreds of unrelated
                    // entrances. Without a live entrance or a verified cache entry,
                    // guessing through them can hold the character indefinitely.
                    if (known.Count <= MaxUnlocatedEntrancesPerName)
                        ids.AddRange(known.Select(x => unchecked((int)x)));
                    else if (ids.Count == 0)
                        _say($"ACG label '{name}' has {known.Count} unlocated entrances; using the local mission-door approach.");
                }
                foreach (int id in ids.Distinct())
                {
                    if (_attempts.Count >= MaxCandidateAttempts) break;
                    _attempts.Add(new KeyEntrance { Key = key, Entrance = id });
                }
            }
            if (_attempts.Count > 0)
                _say($"Selected mission has {_attempts.Count} ACG key/entrance candidate pairs across " +
                    $"{_attempts.Select(x => x.Key.UniqueIdentity).Distinct().Count()} mission key(s).");
            else if (_loadedNear)
                _say("No nearby or bounded ACG key/entrance candidate; using the local mission-door approach.");
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
            if (location == null || Playfield.ModelIdentity.Instance != location.Playfield.Instance ||
                DynelManager.LocalPlayer == null ||
                HorizontalDistance(DynelManager.LocalPlayer.Position, location.Pos) > 8f)
                return EntranceResult.Fallback; // LocalMissionTravel approaches first.
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

        private void Advance() { _index++; _sent = false; }
        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            float x = a.X - b.X, z = a.Z - b.Z;
            return (float)Math.Sqrt(x * x + z * z);
        }

        private void CacheSuccess(KeyEntrance attempt)
        {
            _successfulKeys[attempt.Key.UniqueIdentity.Instance] = attempt.Entrance;
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
            _loadedNear = false;
        }

        public void Dispose() => Network.N3MessageReceived -= OnN3Message;
    }
}
