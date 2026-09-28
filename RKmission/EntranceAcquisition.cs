using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using AOSharp.Common.GameData;
using AOSharp.Core;

namespace RKmission
{
    // Outdoor only. Keep managed observations/identities, never a native Door pointer.
    // GetQuestWorldPos supplies the selected accepted mission's search anchor.
    // QuestInstance is an optional identity hint, not a documented ownership API.
    internal sealed class EntranceAcquisition
    {
        public const float SearchRadius = 6;
        public const float AcquisitionRadius = 12;
        private sealed class Candidate
        {
            public Identity Id;
            public Vector3 Position, Forward;
            public string Name, Association;
            public bool QuestLinked;
            public bool Visible, Locked, Open;
            public float Score;
            public int Attempts;
            public List<Attempt> Approaches;
        }

        internal sealed class Attempt
        {
            public Identity DoorId = Identity.None;
            public Vector3 Point, Threshold;
            public string HeightSource;
            public bool Floor, Tried;
            public float Score, BestDistance = float.PositiveInfinity;
        }

        private readonly Dictionary<Identity, Candidate> _candidates = new Dictionary<Identity, Candidate>();
        private readonly List<Attempt> _search = new List<Attempt>();
        private readonly Action<string> _say;
        private readonly AcceptedMission _mission;
        private readonly List<AcceptedMission> _accepted;
        private readonly Dictionary<Identity, string> _decisions = new Dictionary<Identity, string>();
        private readonly Vector3 _anchor, _searchOrigin;
        private readonly int _missionId;
        private bool _fallbackBound;
        private Vector3 _fallbackThreshold;
        private DateTime _nextScan, _nextLog, _attemptProgress, _lastProgress;
        private int _mode = -1;
        public Attempt Active { get; private set; }
        public DateTime LastProgress => _lastProgress;
        public DateTime AttemptProgress => _attemptProgress;
        public bool Exhausted => Active == null && !_search.Any(x => !x.Tried) &&
            !_candidates.Values.Any(x => x.Visible && (x.Approaches == null || x.Approaches.Any(a => !a.Tried)));

        public EntranceAcquisition(AcceptedMission mission, List<AcceptedMission> accepted,
            Vector3 anchor, Vector3 origin, Action<string> say)
        {
            _mission = mission; _accepted = accepted;
            _missionId = mission.Id.Instance; _anchor = anchor; _searchOrigin = origin; _say = say;
            _lastProgress = DateTime.UtcNow;
            _say($"Entrance acquisition: mission={_missionId}, anchor=({LocalRoutePlanner.Coordinates(_anchor)}), " +
                $"playfield={mission.PlayfieldId}, scan radius={SearchRadius} m; " +
                "selected accepted mission only; no radius expansion; live mission entrance determines final height.");
        }

        public void Scan(Vector3 player, bool flying, bool force = false)
        {
            DateTime now = DateTime.UtcNow;
            int mode = flying ? 1 : 0;
            if (_mode != mode)
            {
                // Equipment/state changes retain the mission and the overall progress clock.
                Finish("movement mode changed; regenerate mode-specific approaches");
                _mode = mode;
                foreach (Candidate candidate in _candidates.Values) candidate.Approaches = null;
                _search.Clear(); BuildSearch();
                _nextScan = DateTime.MinValue;
            }
            if (!force && now < _nextScan) return;
            _nextScan = now.AddSeconds(2);
            foreach (Candidate candidate in _candidates.Values) candidate.Visible = false;
            // Playfield.Doors is AOSharp's live AllDynels filtered by IdentityType.Door.
            // Do not cast arbitrary scenery/items to Door or guess quest IDs from names.
            foreach (Door door in Playfield.Doors)
            {
                if (!Associated(door, out string association, out bool questLinked)) continue;
                Vector3 point = door.Position;
                Identity id = door.Identity;
                bool added = !_candidates.TryGetValue(id, out Candidate candidate);
                if (added) { candidate = new Candidate { Id = id }; _candidates.Add(id, candidate); }
                // A changed live origin invalidates geometry; the next side must still
                // fit inside the captured mission radius. Retain the overall deadline.
                if (!added && candidate.Approaches != null && Vector3.Distance(point, candidate.Position) > 0.5f)
                {
                    candidate.Approaches = null;
                    if (Active != null && Active.DoorId == id) Finish("live door position changed; refresh next approach");
                }
                candidate.Position = point; candidate.Forward = door.Rotation.Forward;
                candidate.Name = door.Name ?? ""; candidate.Locked = door.IsLocked; candidate.Open = door.IsOpen;
                candidate.Association = association; candidate.QuestLinked = questLinked;
                candidate.Visible = true;
                float offset = LocalRoutePlanner.HorizontalDistance(point, _anchor);
                Vector3 end = LocalRoutePlanner.Toward(player + Vector3.Up, point + Vector3.Up,
                    Math.Max(0, Vector3.Distance(player, point) - 0.6f));
                candidate.Score = offset * 3 + LocalRoutePlanner.HorizontalDistance(player, point) * 0.2f +
                    Math.Min(20, Math.Abs(player.Y - point.Y)) * 0.2f +
                    (LocalRoutePlanner.ClearSegment(player + Vector3.Up, end) ? 0 : 8) +
                    (candidate.Locked && !candidate.Open ? 4 : 0);
            }
            // Location fallback must distinguish a doorway. Never tour equally plausible
            // unlinked doors. Explicit quest hints take precedence over location fallback.
            var visible = _candidates.Values.Where(x => x.Visible).ToList();
            var linked = visible.Where(x => x.QuestLinked).ToList();
            var fallback = visible.Where(x => !x.QuestLinked)
                .OrderBy(x => LocalRoutePlanner.HorizontalDistance(x.Position, _anchor)).ThenBy(x => x.Id.Instance).ToList();
            Candidate closest = fallback.FirstOrDefault();
            bool ambiguous = closest != null && fallback.Any(x => x.Id != closest.Id &&
                LocalRoutePlanner.HorizontalDistance(x.Position, _anchor) <=
                    LocalRoutePlanner.HorizontalDistance(closest.Position, _anchor) + 1 &&
                LocalRoutePlanner.HorizontalDistance(x.Position, closest.Position) > 0.5f);
            foreach (Candidate candidate in visible.Where(x => !x.QuestLinked))
            {
                bool sameThreshold = closest != null &&
                    LocalRoutePlanner.HorizontalDistance(candidate.Position, closest.Position) <= 0.5f;
                bool boundThreshold = !_fallbackBound ||
                    LocalRoutePlanner.HorizontalDistance(candidate.Position, _fallbackThreshold) <= 0.5f;
                if (linked.Count == 0 && !ambiguous && sameThreshold && boundThreshold) continue;
                candidate.Visible = false;
                Decision(candidate.Id, candidate.Position, candidate.Name, false,
                    linked.Count > 0 ? "selected mission has a quest-linked door; unlinked fallback excluded" :
                    !boundThreshold ? "different unlinked threshold from the one bound to selected anchor; do not chase another building" :
                    ambiguous ? "ambiguous anchor association: another unlinked door has a similar offset" :
                    "another door is more tightly associated with selected anchor; do not broaden after a failed use");
            }
            foreach (Candidate candidate in _candidates.Values.Where(x => x.Visible))
                Decision(candidate.Id, candidate.Position, candidate.Name, true, candidate.Association);
            if (Active != null && Active.DoorId != Identity.None &&
                (!_candidates.TryGetValue(Active.DoorId, out Candidate active) || !active.Visible))
                Finish("selected door lost its accepted mission association");
            if (now >= _nextLog)
            {
                _nextLog = now.AddSeconds(10);
                _say($"Entrance scan: mission={_missionId}, live candidates={_candidates.Values.Count(x => x.Visible)}, " +
                    $"observed identities={_candidates.Count}, active={Active?.DoorId.ToString() ?? "none"}, " +
                    $"no new best approach distance for {(now - _lastProgress).TotalSeconds:F0} s.");
            }
        }

        public bool Select(Vector3 player, bool flying)
        {
            if (Active != null) return false;
            // Alternate only among mission-constrained candidates, never nearby buildings.
            foreach (Candidate candidate in _candidates.Values.Where(x => x.Visible)
                .OrderBy(x => x.Attempts).ThenBy(x => x.Score).ThenBy(x => x.Id.Instance))
            {
                if (candidate.Approaches == null) candidate.Approaches = BuildApproaches(candidate, player, flying);
                Attempt next = candidate.Approaches.Where(x => !x.Tried).OrderBy(x => x.Score).FirstOrDefault();
                if (next == null) continue;
                if (!candidate.QuestLinked && !_fallbackBound)
                { _fallbackBound = true; _fallbackThreshold = candidate.Position; }
                candidate.Attempts++; Start(next, player, flying);
                _say($"Entrance selected: mission={_missionId}, door={candidate.Id}, " +
                    $"position=({LocalRoutePlanner.Coordinates(candidate.Position)}), " +
                    $"anchor offset={LocalRoutePlanner.HorizontalDistance(candidate.Position, _anchor):F2} m, " +
                    $"approach=({LocalRoutePlanner.Coordinates(next.Point)}), height source={next.HeightSource}, " +
                    $"association={candidate.Association}, candidate attempt={candidate.Attempts}/{candidate.Approaches.Count}, probe score={next.Score:F1}.");
                return true;
            }
            Attempt search = _search.FirstOrDefault(x => !x.Tried);
            if (search == null) return false;
            // Evaluate each anchor-search waypoint locally, never chase stale quest Y.
            Vector3 point = search.Point;
            search.Floor = LocalRoutePlanner.TryEntranceSurface(point, player.Y, out float height, out _);
            point.Y = search.Floor ? height + (flying ? 1.5f : 0) : player.Y;
            search.Point = search.Threshold = point;
            search.HeightSource = search.Floor ? "search local floor" : "search player height, provisional";
            Start(search, player, flying);
            _say($"Entrance search waypoint: mission={_missionId}, point=({LocalRoutePlanner.Coordinates(point)}), " +
                $"height source={search.HeightSource}; scanning live doors, no assumed marker door.");
            return true;
        }

        private void Start(Attempt attempt, Vector3 player, bool flying)
        {
            Active = attempt; attempt.Tried = true;
            // Establishing a different target is not progress. Only a new observed
            // distance minimum for this finite attempt can extend the session clock.
            attempt.BestDistance = Distance(player, attempt.Point, flying);
            _attemptProgress = DateTime.UtcNow;
        }

        public void Observe(Vector3 player, bool flying)
        {
            if (Active == null) return;
            float remaining = Distance(player, Active.Point, flying);
            if (remaining + 0.5f < Active.BestDistance)
            { Active.BestDistance = remaining; _attemptProgress = _lastProgress = DateTime.UtcNow; }
        }

        public void Finish(string reason)
        {
            if (Active != null)
                _say($"Entrance alternate attempt: mission={_missionId}, door={Active.DoorId}, " +
                    $"approach=({LocalRoutePlanner.Coordinates(Active.Point)}), reason={reason}; overall progress clock retained.");
            Active = null;
        }

        public bool PreferNewDoor => Active?.DoorId == Identity.None && _candidates.Values.Any(x => x.Visible &&
            (x.Approaches == null || x.Approaches.Any(a => !a.Tried)));

        public Door RefreshDoor(bool rescan = false)
        {
            // Before Use, include newly loaded competing doors and changed context,
            // even when the regular two-second scan is not due yet.
            if (rescan) Scan(DynelManager.LocalPlayer.Position,
                DynelManager.LocalPlayer.MovementState == MovementState.Fly, true);
            if (Active == null || Active.DoorId == Identity.None) return null;
            if (!_candidates.TryGetValue(Active.DoorId, out Candidate candidate) || !candidate.Visible) return null;
            if (rescan && Mission.List?.Any(x => x.Identity == _mission.Id) != true)
            {
                Decision(candidate.Id, candidate.Position, candidate.Name, false, "selected mission no longer present in live accepted list");
                return null;
            }
            Door door = Playfield.Doors.FirstOrDefault(x => x.Identity == Active.DoorId);
            if (door == null || !Associated(door, out _, out _)) return null;
            // A moving native origin needs rebuilt geometry before movement/use.
            if (Vector3.Distance(door.Position, candidate.Position) > 0.5f) return null;
            return door;
        }

        private bool Associated(Door door, out string association, out bool questLinked)
        {
            association = null; questLinked = false;
            if (door == null || !door.IsValid) return false;
            Identity id = door.Identity;
            Vector3 point = door.Position;
            string name = door.Name ?? "";
            string reason = null;
            float offset = LocalRoutePlanner.HorizontalDistance(point, _anchor);
            if (!_mission.Present || _mission.State == MissionProgress.CompletedByUser ||
                Playfield.IsDungeon || Playfield.ModelIdentity.Instance != _mission.PlayfieldId)
                reason = "selected mission is not accepted in this outdoor playfield";
            else if (id.Type != IdentityType.Door) reason = "object type is not Door";
            else if (!AcceptedMissions.Finite(point)) reason = "invalid live coordinates";
            else if (offset > SearchRadius) reason = $"outside selected mission anchor radius ({SearchRadius} m)";
            else
            {
                try
                {
                    int quest = door.GetStat(Stat.QuestInstance);
                    int buildingType = door.GetStat(Stat.BuildingType);
                    int building = door.GetStat(Stat.BuildingInstance);
                    questLinked = quest > 0 && quest == _mission.Id.Instance;
                    string context = $"QuestInstance={quest}, BuildingType={buildingType}, BuildingInstance={building}";
                    // The SDK exposes these stats but does not document all building type
                    // values. Reject unlinked building context instead of inventing codes.
                    if (quest > 0 && !questLinked) reason = "quest identity differs from selected mission; " + context;
                    else if (Regex.IsMatch(name, @"\b(shop|store|supermarket|apartment|bar|club|bank|building|grid|whompa|whom-pah|backyard|headquarters)\b",
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                        reason = "ordinary shop/building/transport name context; " + context;
                    else if (!questLinked && (buildingType != 0 || building != 0))
                        reason = "unlinked building context; " + context;
                    else if (!questLinked && _accepted.Any(x => x.Id != _mission.Id && x.Present &&
                        x.State != MissionProgress.CompletedByUser && x.PlayfieldId == _mission.PlayfieldId &&
                        AcceptedMissions.Finite(x.Entrance) && LocalRoutePlanner.HorizontalDistance(x.Entrance, _anchor) > 1 &&
                        LocalRoutePlanner.HorizontalDistance(point, x.Entrance) <= offset + 0.5f))
                        reason = "door is closer to or ambiguous with another accepted mission anchor; " + context;
                    else association = (questLinked ? "matching quest identity hint plus bounded anchor" :
                        "bounded selected mission anchor, neutral door context (ownership unconfirmed)") + "; " + context;
                }
                catch (Exception ex) { reason = "unable to inspect mission/building context: " + ex.Message; }
            }
            if (reason != null) Decision(id, point, name, false, reason);
            return reason == null;
        }

        private void Decision(Identity id, Vector3 point, string name, bool accepted, string reason)
        {
            string decision = $"{accepted}:{reason}";
            if (_decisions.TryGetValue(id, out string previous) && previous == decision) return;
            _decisions[id] = decision;
            _say($"Entrance door {(accepted ? "accepted" : "rejected")}: mission={_missionId}, playfield={_mission.PlayfieldId}, " +
                $"door={id}, type={id.Type}, name='{name}', position=({LocalRoutePlanner.Coordinates(point)}), " +
                $"anchor offset={LocalRoutePlanner.HorizontalDistance(point, _anchor):F2} m, reason={reason}.");
        }

        private List<Attempt> BuildApproaches(Candidate door, Vector3 player, bool flying)
        {
            var result = new List<Attempt>();
            var heights = new List<Tuple<float, bool, string>> { Tuple.Create(door.Position.Y, false, "live door origin") };
            if (LocalRoutePlanner.TryEntranceSurface(door.Position, player.Y, out float floor, out int support))
            {
                float height = floor + (flying ? 1.5f : 0);
                if (Math.Abs(height - door.Position.Y) > 0.5f)
                    heights.Add(Tuple.Create(height, true, $"local threshold floor ({support}/17 columns)"));
            }
            if (!flying && LocalRoutePlanner.HorizontalDistance(player, door.Position) <= 8 &&
                heights.All(x => Math.Abs(x.Item1 - player.Y) > 0.5f))
                heights.Add(Tuple.Create(player.Y, false, "nearby grounded player"));
            Vector3 normal = door.Forward; normal.Y = 0;
            if (!AcceptedMissions.Finite(normal) || Vector3.Distance(normal, Vector3.Zero) < 0.1f)
                normal = LocalRoutePlanner.OutsideEntrance(door.Position, player, 1) - door.Position;
            double heading = Math.Atan2(normal.Z, normal.X);
            foreach (var height in heights)
                for (int i = 0; i < 12; i++)
                {
                    double angle = heading + (i < 8 ? i * Math.PI / 4 : (i - 8) * Math.PI / 2);
                    float radius = i < 8 ? 1.5f : 3;
                    Vector3 point = door.Position + new Vector3((float)Math.Cos(angle) * radius, 0, (float)Math.Sin(angle) * radius);
                    if (LocalRoutePlanner.HorizontalDistance(point, _anchor) > SearchRadius) continue;
                    point.Y = height.Item1;
                    Vector3 threshold = door.Position; threshold.Y = point.Y;
                    Vector3 from = flying ? player : player + Vector3.Up;
                    Vector3 to = flying ? point : point + Vector3.Up;
                    Vector3 end = flying ? threshold : threshold + Vector3.Up;
                    end = LocalRoutePlanner.Toward(to, end, Math.Max(0, Vector3.Distance(to, end) - 0.6f));
                    float score = Vector3.Distance(player, point) + (i >= 8 ? 3 : 0) +
                        (LocalRoutePlanner.ClearSegment(from, to) ? 0 : 8) +
                        (LocalRoutePlanner.ClearSegment(to, end) ? 0 : 16);
                    result.Add(new Attempt { DoorId = door.Id, Point = point, Threshold = threshold,
                        Floor = height.Item2, HeightSource = height.Item3, Score = score });
                }
            return result;
        }

        private void BuildSearch()
        {
            Vector3 centre = _anchor;
            // Retain the prior user's measured point only as a local search hint.
            var mission = new AcceptedMission { Entrance = _anchor, PlayfieldId = Playfield.ModelIdentity.Instance };
            if (LocalRoutePlanner.TryMeasuredEntrance(mission, out Vector3 measured)) centre = measured;
            _search.Add(new Attempt { Point = _anchor });
            if (LocalRoutePlanner.HorizontalDistance(centre, _anchor) > 0.5f)
                _search.Add(new Attempt { Point = centre });
            double heading = Math.Atan2(_searchOrigin.Z - centre.Z, _searchOrigin.X - centre.X);
            foreach (float radius in new[] { 2f, 4f, SearchRadius })
                for (int i = 0; i < 8; i++)
                {
                    double angle = heading + i * Math.PI / 4;
                    Vector3 point = centre + new Vector3((float)Math.Cos(angle) * radius, 0,
                        (float)Math.Sin(angle) * radius);
                    if (LocalRoutePlanner.HorizontalDistance(point, _anchor) <= SearchRadius)
                        _search.Add(new Attempt { Point = point });
                }
        }

        private static float Distance(Vector3 from, Vector3 to, bool flying) => flying
            ? Vector3.Distance(from, to) : LocalRoutePlanner.HorizontalDistance(from, to);
    }
}
