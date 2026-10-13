using System;
using System.Collections.Generic;
using System.IO;
using AOSharp.Common.GameData;
using Newtonsoft.Json;

namespace RKmission
{
    internal sealed class MissionObjectiveIdentity
    {
        public int Type { get; set; }
        public int Instance { get; set; }

        public static MissionObjectiveIdentity From(Identity identity) => new MissionObjectiveIdentity
        {
            Type = (int)identity.Type,
            Instance = identity.Instance
        };

        public Identity ToIdentity() => new Identity((IdentityType)Type, Instance);
    }

    internal sealed class MissionObjectiveBinding
    {
        public int QuestType { get; set; }
        public int QuestInstance { get; set; }
        public int CharacterInstance { get; set; }
        public RkMissionKind Kind { get; set; }
        public string QuestShortInfo { get; set; }
        public string QuestLongInfo { get; set; }
        public List<MissionObjectiveIdentity> QuestActionIdentities { get; set; } =
            new List<MissionObjectiveIdentity>();
        public int RepairToolType { get; set; }
        public int RepairToolInstance { get; set; }
        public int RepairToolLowId { get; set; }
        public int RepairToolHighId { get; set; }
        public DateTime UpdatedAtUtc { get; set; }

        public bool Matches(Identity quest, Identity character) =>
            QuestType == (int)quest.Type && QuestInstance == quest.Instance &&
            CharacterInstance > 0 && CharacterInstance == character.Instance;

        public Identity RepairToolIdentity => RepairToolType == (int)IdentityType.None || RepairToolInstance == 0
            ? Identity.None
            : new Identity((IdentityType)RepairToolType, RepairToolInstance);
    }

    internal sealed class MissionOriginBinding
    {
        public int QuestInstance { get; set; }
        public int CharacterInstance { get; set; }
        public int DestinationPlayfield { get; set; }
        public Vector3 Destination { get; set; }
        public int TerminalInstance { get; set; }
        public int TerminalPlayfield { get; set; }
        public Vector3 TerminalPosition { get; set; }
        public DateTime AcceptedAtUtc { get; set; }

        public bool MatchesQuestCharacter(Identity quest, Identity character) =>
            QuestInstance > 0 && QuestInstance == quest.Instance &&
            CharacterInstance > 0 && CharacterInstance == character.Instance &&
            TerminalInstance != 0;

        public bool Matches(AcceptedMission mission, Identity character) =>
            TryMatch(mission, character, out _);

        public bool TryMatch(AcceptedMission mission, Identity character, out string diagnostics)
        {
            bool hasMission = mission != null;
            bool questMatches = hasMission && QuestInstance == mission.Id.Instance;
            bool characterMatches = CharacterInstance > 0 && CharacterInstance == character.Instance;
            bool playfieldMatches = hasMission && DestinationPlayfield == mission.PlayfieldId;
            bool destinationFinite = AcceptedMissions.Finite(Destination);
            bool entranceFinite = hasMission && AcceptedMissions.Finite(mission.Entrance);
            float entranceDistance = destinationFinite && entranceFinite
                ? Vector3.Distance(Destination, mission.Entrance)
                : float.NaN;
            bool entranceMatches = entranceDistance <= 12f;
            bool terminalInstanceValid = TerminalInstance != 0;
            bool terminalPlayfieldValid = TerminalPlayfield > 0;
            bool terminalPositionFinite = AcceptedMissions.Finite(TerminalPosition);

            diagnostics =
                $"quest stored={QuestInstance}, current={(hasMission ? mission.Id.Instance : 0)}, match={questMatches}; " +
                $"character stored={CharacterInstance}, current={character.Instance}, match={characterMatches}; " +
                $"destination playfield stored={DestinationPlayfield}, current={(hasMission ? mission.PlayfieldId : 0)}, match={playfieldMatches}; " +
                $"destination finite={destinationFinite}, entrance finite={entranceFinite}, " +
                $"entrance distance={entranceDistance:0.00}m, within 12m={entranceMatches}; " +
                $"terminal instance={TerminalInstance} (0x{unchecked((uint)TerminalInstance):X8}), nonzero={terminalInstanceValid}; " +
                $"terminal playfield={TerminalPlayfield}, valid={terminalPlayfieldValid}; " +
                $"terminal position finite={terminalPositionFinite}";

            return hasMission && questMatches && characterMatches && playfieldMatches &&
                destinationFinite && entranceFinite && entranceMatches && terminalInstanceValid &&
                terminalPlayfieldValid && terminalPositionFinite;
        }

        public Identity TerminalIdentity => new Identity(IdentityType.MissionTerminal, TerminalInstance);
    }

    // Durable intent only. No path, live dynel, room-clearance or movement command
    // is restored from disk. The coordinator reconciles identity against AO# first.
    internal sealed class MissionCheckpoint
    {
        public bool Armed { get; set; }
        public bool AutoCycle { get; set; }
        public int AutoMissionLimit { get; set; } // Zero means no acceptance limit.
        public int AutoAcceptedCount { get; set; }
        public bool HasRollTerminal { get; set; }
        public int RollTerminalPlayfield { get; set; }
        public Vector3 RollTerminalPosition { get; set; }
        public string RollLogisticsSite { get; set; }
        public string RollLogisticsTerminalIdentity { get; set; }
        // Per-quest provenance. The global roller location can change among sites.
        public List<MissionOriginBinding> OriginTerminals { get; set; } = new List<MissionOriginBinding>();
        // Exact accepted-objective evidence, scoped to quest identity and character.
        public List<MissionObjectiveBinding> ObjectiveMetadata { get; set; } =
            new List<MissionObjectiveBinding>();
        public string Phase { get; set; } = "Idle";
        public int MissionType { get; set; }
        public int MissionInstance { get; set; }
        public int DestinationPlayfield { get; set; }
        public Vector3 Destination { get; set; }
        public int EntranceIdentity { get; set; }
        public int DungeonInstance { get; set; }
        public int EntranceRoom { get; set; }
        public Vector3? EntrancePosition { get; set; }
        public int Floor { get; set; }
        public string TravelProvider { get; set; }
        public string ObjectiveState { get; set; }
        // Set only after observed zoning out of the exact completed dungeon.
        public bool PendingHandInExitVerified { get; set; }
        // Durable cycle ledger. These are intent/evidence markers only; every
        // action is reconciled against live AO state before it can be repeated.
        public bool HandInPending { get; set; }
        public bool RewardSettlementPending { get; set; }
        public bool InventorySettlementPending { get; set; }
        public bool LogisticsPending { get; set; }
        public List<int> AcceptedMissionIds { get; set; } = new List<int>();
        public string LastVerifiedPhase { get; set; } = "Idle";
        public string StopInvariant { get; set; }
        public MissionExecutionRecord Execution { get; set; }

        private string _path;
        [JsonIgnore] private DateTime _nextWrite;

        public MissionCheckpoint() { }
        private MissionCheckpoint(string path) { _path = path; }

        public static MissionCheckpoint Load(string pluginDir, Action<string> say)
        {
            string path = Path.Combine(pluginDir, "RKMissionData", "checkpoint.json");
            try
            {
                if (File.Exists(path))
                {
                    MissionCheckpoint saved = JsonConvert.DeserializeObject<MissionCheckpoint>(File.ReadAllText(path));
                    if (saved != null)
                    {
                        saved._path = path;
                        return saved;
                    }
                }
            }
            catch (Exception ex) { say("Checkpoint could not be read: " + ex.Message); }
            return new MissionCheckpoint(path);
        }

        public void Save(bool force, Action<string> say)
        {
            if (!force && DateTime.UtcNow < _nextWrite) return;
            _nextWrite = DateTime.UtcNow.AddSeconds(5);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                string temp = _path + ".new";
                File.WriteAllText(temp, JsonConvert.SerializeObject(this, Formatting.Indented));
                if (File.Exists(_path)) File.Replace(temp, _path, null);
                else File.Move(temp, _path);
            }
            catch (Exception ex) { say("Checkpoint write failed: " + ex.Message); }
        }
    }
}
