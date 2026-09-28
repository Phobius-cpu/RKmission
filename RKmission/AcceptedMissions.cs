using System;
using System.Collections.Generic;
using System.Linq;
using AOSharp.Common.GameData;
using AOSharp.Core;

namespace RKmission
{
    internal enum MissionProgress { Accepted, InProgress, RemovedUnconfirmed, CompletedByUser }

    // Managed snapshots only: AO# Mission pointers are refreshed, never retained across zoning/removal.
    internal sealed class AcceptedMission
    {
        public Identity Id;
        public string Name, Objectives, CompletionEvidence;
        public int PlayfieldId;
        public Vector3 Entrance;
        public Identity DungeonInstance;
        public List<MissionAction> Actions;
        public bool Present, HandoffVerified, RoomsCleared;
        public MissionProgress State;
        public bool IsRubiKaDestination => AcceptedMissions.IsRubiKaPlayfield(PlayfieldId);
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
        private DateTime _nextRefresh;
        public IEnumerable<AcceptedMission> Records => _records.Values;
        public static bool IsRubiKaPlayfield(int id) => RubiKaPlayfields.Contains(id);
        public AcceptedMission Find(Identity id) => _records.TryGetValue(id, out AcceptedMission record) ? record : null;
        public IEnumerable<AcceptedMission> Eligible(int playfield) => Records.Where(x => x.Present && x.IsRubiKaDestination &&
            x.PlayfieldId == playfield && x.State != MissionProgress.CompletedByUser);

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
                    record = new AcceptedMission { Id = id, State = MissionProgress.Accepted };
                    _records.Add(id, record);
                }
                record.Present = true;
                if (record.State == MissionProgress.RemovedUnconfirmed)
                    record.State = record.HandoffVerified ? MissionProgress.InProgress : MissionProgress.Accepted;
                record.Name = mission.DisplayName;
                record.DungeonInstance = mission.PlayfieldInstance;
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
            }
            foreach (AcceptedMission record in Records.Where(x => !seen.Contains(x.Id)))
            {
                record.Present = false;
                if (record.State != MissionProgress.CompletedByUser)
                    record.State = MissionProgress.RemovedUnconfirmed;
                // Removed/expired/deleted quests and rewards are indistinguishable in Mission.List.
                // Keep the bound identity and objective metadata, but never claim completion here.
            }
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
