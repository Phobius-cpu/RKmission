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

    // Adapted from Neko KeyWarper. ACG IDs are candidates, never proof of entry.
    internal sealed class MissionEntranceResolver : IDisposable
    {
        private const uint WrongKey = 0x0FCA6FF9;
        private const uint KeyAccepted = 0x0BC6E104;
        private readonly Action<string> _say;
        private readonly Dictionary<string, List<uint>> _entrances;
        private Mission _mission;
        private Item _key;
        private List<uint> _candidates;
        private int _index;
        private bool _sent;
        private bool _accepted;
        private DateTime _sentAt;

        public MissionEntranceResolver(string pluginDir, Action<string> say)
        {
            _say = say;
            string path = Path.Combine(pluginDir, "Data", "ACGEntrances.json");
            _entrances = File.Exists(path)
                ? JsonConvert.DeserializeObject<Dictionary<string, List<uint>>>(File.ReadAllText(path))
                    ?? new Dictionary<string, List<uint>>()
                : new Dictionary<string, List<uint>>();
            Network.N3MessageReceived += OnN3Message;
        }

        public void Select(Mission mission)
        {
            if (_mission?.Identity == mission?.Identity) return;
            Reset();
            _mission = mission;
            if (mission == null) return;
            var keys = Inventory.Items.Where(x => x.UniqueIdentity.Type == IdentityType.MissionKey &&
                x.Name != null && x.Name.StartsWith("Mission key to ", StringComparison.OrdinalIgnoreCase)).ToList();
            if (keys.Count > 1)
            {
                _say("Several mission keys are present; ACG key selection is ambiguous. Using nearby entrance fallback.");
                return;
            }
            _key = keys.FirstOrDefault();
            if (_key == null) return;
            string name = _key.Name.Substring("Mission key to ".Length);
            _entrances.TryGetValue(name, out _candidates);
            if (_candidates != null && _candidates.Count > 0)
                _say($"Mission key maps to {_candidates.Count} ACG entrance candidate(s) for {name}.");
        }

        public EntranceResult Tick()
        {
            if (_mission == null) return EntranceResult.Failed;
            if (Playfield.IsDungeon)
                return VerifyCurrentDungeon(_mission, out _) ? EntranceResult.Verified : EntranceResult.Failed;
            if (_key == null || _candidates == null || _candidates.Count == 0)
                return EntranceResult.Fallback;
            if (_accepted)
            {
                if (DateTime.UtcNow - _sentAt < TimeSpan.FromSeconds(20)) return EntranceResult.Waiting;
                _say("Mission key was accepted, but the dungeon transition was not verified.");
                return EntranceResult.Failed;
            }
            if (_sent && DateTime.UtcNow - _sentAt < TimeSpan.FromSeconds(3))
                return EntranceResult.Waiting;
            if (_sent) { _index++; _sent = false; }
            if (_index >= _candidates.Count) return EntranceResult.Fallback;
            uint candidate = _candidates[_index];
            Item.UseItemOnItem(_key.Slot,
                new Identity(IdentityType.ACGEntrance, unchecked((int)candidate)));
            _sent = true;
            _sentAt = DateTime.UtcNow;
            return EntranceResult.Waiting;
        }

        public static bool VerifyCurrentDungeon(Mission expected, out Mission current)
        {
            current = null;
            if (!Playfield.IsDungeon || !Mission.FindMissionForCurrentDungeon(out current))
                return false;
            return expected == null || current.Identity == expected.Identity;
        }

        private void OnN3Message(object sender, N3Message message)
        {
            if (!_sent || message.Identity != DynelManager.LocalPlayer?.Identity ||
                !(message is FeedbackMessage feedback) || feedback.CategoryId != 0x6E)
                return;
            if (feedback.MessageId == WrongKey)
            {
                _index++;
                _sent = false;
                _say("Mission key rejected for this ACG entrance; trying the next candidate.");
            }
            else if (feedback.MessageId == KeyAccepted)
            {
                _accepted = true;
                _say("Mission key accepted; waiting for a verified dungeon transition.");
            }
        }

        public void Reset()
        {
            _mission = null;
            _key = null;
            _candidates = null;
            _index = 0;
            _sent = _accepted = false;
        }

        public void Dispose() => Network.N3MessageReceived -= OnN3Message;
    }
}
