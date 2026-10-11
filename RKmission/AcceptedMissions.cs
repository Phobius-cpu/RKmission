using System;
using System.Collections.Generic;
using System.Linq;
using AOSharp.Common.GameData;
using AOSharp.Core;
using SmokeLounge.AOtomation.Messaging.Messages;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;

namespace RKmission
{
    internal enum MissionProgress { Accepted, InProgress, AwaitingHandIn, RemovedUnconfirmed, CompletedAutomatically, CompletedByUser }
    internal enum RkMissionKind { Unknown, FindItem, ReturnItem, Repair, FindPerson, KillPerson }

    // Managed snapshots only: AO# Mission pointers are refreshed, never retained across zoning/removal.
    internal sealed class AcceptedMission
    {
        public Identity Id;
        public string Name, Objectives, CompletionEvidence;
        public string QuestShortInfo, QuestLongInfo;
        public int PlayfieldId;
        // Quest world-position anchor (legacy field name), not a guaranteed physical door.
        // AOSharp Vector3 uses Y for altitude and X/Z for the outdoor plane.
        public Vector3 Entrance;
        public Identity DungeonInstance;
        public Identity Source = Identity.None;
        public Identity VerifiedIssuingTerminal = Identity.None;
        public RkMissionKind Kind;
        public Vector3? DungeonEntryPosition;
        public int DungeonEntryRoom;
        public bool DeletedByUser;
        public bool ReturnHandInPending;
        public List<MissionAction> Actions;
        public List<Identity> QuestActionIdentities = new List<Identity>();
        public Identity RepairToolIdentity = Identity.None;
        public int RepairToolLowId, RepairToolHighId;
        public Action<AcceptedMission> ObjectiveMetadataChanged;
        public bool Present, HandoffVerified, RoomsCleared;
        public MissionProgress State;
        public bool IsRubiKaDestination => AcceptedMissions.IsRubiKaPlayfield(PlayfieldId);
        public bool Completed => State == MissionProgress.CompletedAutomatically || State == MissionProgress.CompletedByUser;
    }

    internal sealed class AcceptedMissions
    {
        // Outdoor Rubi-Ka mission destinations from the embedded Mali roller's PlayfieldView.
        // This is geography, independent of the user's roller location/filter settings.
        private static readonly HashSet<int> RubiKaPlayfields = new HashSet<int>
        {
            760, 585, 655, 550, 545, 505, 605, 800, 665, 590, 670, 595, 620, 685,
            687, 717, 647, 791, 695, 625, 560, 696, 567, 566, 565, 540, 716, 705,
            700, 710, 570, 630, 735, 740, 730, 610, 615, 635, 790, 795, 640, 646,
            650, 600, 551, 586
        };
        private readonly Dictionary<Identity, AcceptedMission> _records = new Dictionary<Identity, AcceptedMission>();
        private readonly Dictionary<Identity, RkMissionKind> _kinds = new Dictionary<Identity, RkMissionKind>();
        private readonly Dictionary<Identity, QuestMetadata> _questMetadata = new Dictionary<Identity, QuestMetadata>();
        private readonly Dictionary<int, DateTime> _questUpdateAt = new Dictionary<int, DateTime>();
        private MissionCheckpoint _checkpoint;
        private Action<string> _say;
        private DateTime _nextRefresh;
        public IEnumerable<AcceptedMission> Records => _records.Values;
        public static bool IsRubiKaPlayfield(int id) => RubiKaPlayfields.Contains(id);
        public AcceptedMission Find(Identity id) => _records.TryGetValue(id, out AcceptedMission record) ? record : null;
        public bool ObservedQuestUpdate(int missionId, DateTime sinceUtc) =>
            _questUpdateAt.TryGetValue(missionId, out DateTime receivedAt) && receivedAt >= sinceUtc;
        public IEnumerable<int> NewQuestUpdateIdsSince(DateTime sinceUtc, HashSet<int> previousIds) =>
            _questUpdateAt.Where(x => x.Value >= sinceUtc && !previousIds.Contains(x.Key))
                .Select(x => x.Key);
        public IEnumerable<AcceptedMission> Eligible(int playfield) => Records.Where(x => x.Present && x.IsRubiKaDestination &&
            x.PlayfieldId == playfield && !x.Completed);

        public void AttachCheckpoint(MissionCheckpoint checkpoint, Action<string> say)
        {
            _checkpoint = checkpoint;
            _say = say;
            if (_checkpoint.ObjectiveMetadata == null)
                _checkpoint.ObjectiveMetadata = new List<MissionObjectiveBinding>();
        }

        // Offered metadata identifies all five types, but never makes an offer eligible.
        public void ObserveRoll(object sender, RollListChangedArgs args)
        {
            foreach (var offer in args.MissionDetails ?? Array.Empty<SmokeLounge.AOtomation.Messaging.GameData.MissionInfo>())
                _kinds[offer.MissionIdentity] = FromIcon(offer.MissionIcon);
        }

        public void ObserveQuest(object sender, N3Message message)
        {
            if (!(message is QuestFullUpdateMessage update)) return;
            foreach (var quest in update.Quests ?? Array.Empty<SmokeLounge.AOtomation.Messaging.GameData.Quest>())
            {
                _questUpdateAt[quest.QuestId.Instance] = DateTime.UtcNow;
                RkMissionKind kind = FromIcon(quest.MissionIconId);
                if (kind != RkMissionKind.Unknown) _kinds[quest.QuestId] = kind;
                var metadata = new QuestMetadata
                {
                    ShortInfo = quest.ShortInfo,
                    LongInfo = quest.LongInfo,
                    ActionIdentities = QuestIdentities(quest).Distinct().ToList()
                };
                _questMetadata[quest.QuestId] = metadata;
                AcceptedMission existing = Find(quest.QuestId);
                if (existing != null)
                {
                    ApplyQuestMetadata(existing, metadata);
                    if (kind != RkMissionKind.Unknown) existing.Kind = kind;
                    PersistObjectiveMetadata(existing);
                }
                else PersistQuestMetadata(quest.QuestId, kind, metadata);
            }
        }

        public void ObserveSent(object sender, N3Message message)
        {
            if (message is QuestMessage quest && quest.Action == QuestAction.Delete)
            {
                AcceptedMission record = Find(quest.Mission);
                if (record != null) record.DeletedByUser = true;
            }
        }

        private static RkMissionKind FromIcon(int icon)
        {
            // Same native icons already used by the embedded Mali roller.
            switch (icon)
            {
                case 11329: return RkMissionKind.ReturnItem;
                case 11330: return RkMissionKind.KillPerson;
                case 11335: return RkMissionKind.FindPerson;
                case 11337: return RkMissionKind.FindItem;
                case 11342: return RkMissionKind.Repair;
                default: return RkMissionKind.Unknown;
            }
        }

        public void Refresh(bool force = false)
        {
            if (Game.IsZoning || DynelManager.LocalPlayer == null || (!force && DateTime.UtcNow < _nextRefresh)) return;
            List<Mission> current = Mission.List;
            if (current == null) return; // An unavailable list is not evidence of removal/completion.
            _nextRefresh = DateTime.UtcNow.AddSeconds(1);
            var seen = new HashSet<Identity>();
            foreach (Mission mission in current)
            {
                Identity id = mission.Identity;
                seen.Add(id);
                AcceptedMission record = Find(id);
                MissionLocation location = mission.Location;
                bool outdoorDestination = location != null && RubiKaPlayfields.Contains(location.Playfield.Instance) &&
                    Finite(location.Pos);
                if (record == null)
                {
                    // Retain accepted identities/objectives even before their location resolves.
                    // Non-Rubi-Ka and unresolved destinations are visible, but never eligible for travel.
                    record = new AcceptedMission
                    {
                        Id = id,
                        State = MissionProgress.Accepted,
                        ObjectiveMetadataChanged = PersistObjectiveMetadata
                    };
                    _records.Add(id, record);
                }
                record.Present = true;
                if (record.State == MissionProgress.RemovedUnconfirmed)
                    record.State = record.HandoffVerified ? MissionProgress.InProgress : MissionProgress.Accepted;
                record.Name = mission.DisplayName;
                record.DungeonInstance = mission.PlayfieldInstance;
                record.Source = mission.Source;
                if (_kinds.TryGetValue(id, out RkMissionKind kind) && kind != RkMissionKind.Unknown)
                    record.Kind = kind;
                if (_questMetadata.TryGetValue(id, out QuestMetadata metadata))
                    ApplyQuestMetadata(record, metadata);
                RestoreObjectiveMetadata(record);
                if (location != null && Finite(location.Pos) && (outdoorDestination || !record.IsRubiKaDestination))
                {
                    record.PlayfieldId = location.Playfield.Instance;
                    record.Entrance = location.Pos;
                }
                var actions = mission.Actions;
                if (actions != null && (actions.Count > 0 || record.Actions == null))
                {
                    record.Actions = actions;
                    record.Objectives = actions.Count == 0 ? "unknown" :
                        string.Join(", ", actions.Select(Describe));
                }
                RestoreReturnItemFromOrigin(record);
            }
            foreach (AcceptedMission record in Records.Where(x => !seen.Contains(x.Id)))
            {
                record.Present = false;
                if (!record.Completed)
                    record.State = MissionProgress.RemovedUnconfirmed;
                // Removed/expired/deleted quests and rewards are indistinguishable in Mission.List.
                // Keep the bound identity and objective metadata, but never claim completion here.
            }
        }

        private sealed class QuestMetadata
        {
            public string ShortInfo, LongInfo;
            public List<Identity> ActionIdentities;
        }

        public void RemoveObjectiveMetadata(AcceptedMission mission)
        {
            Identity character = CurrentCharacter();
            if (_checkpoint == null || mission == null || character == Identity.None) return;
            int removed = _checkpoint.ObjectiveMetadata.RemoveAll(x => x.Matches(mission.Id, character));
            if (removed > 0) _checkpoint.Save(true, _say);
        }

        private void PersistQuestMetadata(Identity quest, RkMissionKind kind, QuestMetadata metadata)
        {
            Identity character = CurrentCharacter();
            if (_checkpoint == null || character == Identity.None) return;
            MissionObjectiveBinding binding = FindBinding(quest, character) ?? new MissionObjectiveBinding
            {
                QuestType = (int)quest.Type,
                QuestInstance = quest.Instance,
                CharacterInstance = character.Instance
            };
            if (!_checkpoint.ObjectiveMetadata.Contains(binding)) _checkpoint.ObjectiveMetadata.Add(binding);
            if (kind != RkMissionKind.Unknown) binding.Kind = kind;
            binding.QuestShortInfo = metadata.ShortInfo;
            binding.QuestLongInfo = metadata.LongInfo;
            binding.QuestActionIdentities = (metadata.ActionIdentities ?? new List<Identity>())
                .Distinct().Select(MissionObjectiveIdentity.From).ToList();
            binding.UpdatedAtUtc = DateTime.UtcNow;
            _checkpoint.Save(true, _say);
        }

        private void PersistObjectiveMetadata(AcceptedMission mission)
        {
            Identity character = CurrentCharacter();
            if (_checkpoint == null || mission == null || character == Identity.None) return;
            MissionObjectiveBinding binding = FindBinding(mission.Id, character) ?? new MissionObjectiveBinding
            {
                QuestType = (int)mission.Id.Type,
                QuestInstance = mission.Id.Instance,
                CharacterInstance = character.Instance
            };
            if (!_checkpoint.ObjectiveMetadata.Contains(binding)) _checkpoint.ObjectiveMetadata.Add(binding);
            if (mission.Kind != RkMissionKind.Unknown) binding.Kind = mission.Kind;
            binding.QuestShortInfo = mission.QuestShortInfo;
            binding.QuestLongInfo = mission.QuestLongInfo;
            binding.QuestActionIdentities = (mission.QuestActionIdentities ?? new List<Identity>())
                .Distinct().Select(MissionObjectiveIdentity.From).ToList();
            Identity tool = mission.RepairToolIdentity;
            binding.RepairToolType = tool != Identity.None ? (int)tool.Type : (int)IdentityType.None;
            binding.RepairToolInstance = tool != Identity.None ? tool.Instance : 0;
            binding.RepairToolLowId = mission.RepairToolLowId;
            binding.RepairToolHighId = mission.RepairToolHighId;
            binding.UpdatedAtUtc = DateTime.UtcNow;
            _checkpoint.Save(true, _say);
        }

        private void RestoreObjectiveMetadata(AcceptedMission mission)
        {
            Identity character = CurrentCharacter();
            MissionObjectiveBinding binding = FindBinding(mission.Id, character);
            if (binding == null) return;
            bool changed = mission.Kind == RkMissionKind.Unknown && binding.Kind != RkMissionKind.Unknown ||
                string.IsNullOrWhiteSpace(mission.QuestShortInfo) && !string.IsNullOrWhiteSpace(binding.QuestShortInfo) ||
                string.IsNullOrWhiteSpace(mission.QuestLongInfo) && !string.IsNullOrWhiteSpace(binding.QuestLongInfo) ||
                (mission.QuestActionIdentities == null || mission.QuestActionIdentities.Count == 0) &&
                    binding.QuestActionIdentities?.Count > 0 ||
                mission.RepairToolLowId == 0 && binding.RepairToolLowId != 0 ||
                mission.RepairToolHighId == 0 && binding.RepairToolHighId != 0;
            if (mission.Kind == RkMissionKind.Unknown) mission.Kind = binding.Kind;
            if (string.IsNullOrWhiteSpace(mission.QuestShortInfo)) mission.QuestShortInfo = binding.QuestShortInfo;
            if (string.IsNullOrWhiteSpace(mission.QuestLongInfo)) mission.QuestLongInfo = binding.QuestLongInfo;
            if (mission.QuestActionIdentities == null || mission.QuestActionIdentities.Count == 0)
                mission.QuestActionIdentities = (binding.QuestActionIdentities ?? new List<MissionObjectiveIdentity>())
                    .Select(x => x.ToIdentity()).ToList();
            if (mission.RepairToolIdentity == Identity.None)
                mission.RepairToolIdentity = binding.RepairToolIdentity;
            if (mission.RepairToolLowId == 0) mission.RepairToolLowId = binding.RepairToolLowId;
            if (mission.RepairToolHighId == 0) mission.RepairToolHighId = binding.RepairToolHighId;
            if (changed)
                _say?.Invoke($"Restored durable objective metadata for quest {mission.Id.Instance}: " +
                    $"kind={mission.Kind}, text={(!string.IsNullOrWhiteSpace(mission.QuestShortInfo) || !string.IsNullOrWhiteSpace(mission.QuestLongInfo) ? "yes" : "no")}, " +
                    $"actionIds={mission.QuestActionIdentities.Count}, repairTool template={mission.RepairToolLowId}/{mission.RepairToolHighId}.");
        }

        private void RestoreReturnItemFromOrigin(AcceptedMission mission)
        {
            Identity character = CurrentCharacter();
            if (_checkpoint?.OriginTerminals == null || character == Identity.None ||
                mission?.Actions == null ||
                (mission.Kind != RkMissionKind.Unknown && mission.Kind != RkMissionKind.ReturnItem)) return;

            MissionOriginBinding origin = _checkpoint.OriginTerminals.FirstOrDefault(x =>
                x.Matches(mission, character) && mission.Actions.OfType<UseItemOnItemAction>()
                    .Any(action => action.Destination == x.TerminalIdentity));
            if (origin == null) return;

            Identity terminal = origin.TerminalIdentity;
            mission.VerifiedIssuingTerminal = terminal;
            mission.Source = terminal;
            if (mission.Kind != RkMissionKind.Unknown) return;

            mission.Kind = RkMissionKind.ReturnItem;
            PersistObjectiveMetadata(mission);
            _say?.Invoke($"Restored ReturnItem semantics for quest {mission.Id.Instance} " +
                $"from verified issuing terminal {terminal}.");
        }

        private MissionObjectiveBinding FindBinding(Identity quest, Identity character)
        {
            if (_checkpoint?.ObjectiveMetadata == null || character == Identity.None) return null;
            return _checkpoint.ObjectiveMetadata.FirstOrDefault(x => x.Matches(quest, character));
        }

        private static Identity CurrentCharacter() => DynelManager.LocalPlayer?.Identity ?? Identity.None;

        private static IEnumerable<Identity> QuestIdentities(SmokeLounge.AOtomation.Messaging.GameData.Quest quest)
        {
            foreach (var action in quest.QuestActions ?? Array.Empty<SmokeLounge.AOtomation.Messaging.GameData.QuestActionInfo>())
            {
                yield return action.Action;
                yield return action.UnknownId1;
                yield return action.UnknownId2;
                yield return action.UnknownId3;
                yield return action.UnknownId4;
                yield return action.UnknownId5;
                yield return action.UnknownId6;
                yield return action.UnknownId7;
            }
        }

        private static void ApplyQuestMetadata(AcceptedMission record, QuestMetadata metadata)
        {
            record.QuestShortInfo = metadata.ShortInfo;
            record.QuestLongInfo = metadata.LongInfo;
            record.QuestActionIdentities = metadata.ActionIdentities ?? new List<Identity>();
        }

        private static string Describe(MissionAction action)
        {
            if (action is FindPersonAction person) return $"{action.Type} target={person.Target}";
            if (action is FindItemAction item) return $"{action.Type} target={item.Target}";
            if (action is KillPersonAction kill) return $"{action.Type} target={kill.Target}";
            if (action is UseItemOnItemAction use) return $"{action.Type} source={use.Source}, destination={use.Destination}";
            return action.Type.ToString();
        }

        internal static bool Finite(Vector3 point) =>
            !(float.IsNaN(point.X) || float.IsInfinity(point.X) || float.IsNaN(point.Y) ||
              float.IsInfinity(point.Y) || float.IsNaN(point.Z) || float.IsInfinity(point.Z));
    }
}
