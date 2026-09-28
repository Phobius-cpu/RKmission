using System;
using System.Collections.Generic;
using System.Linq;
using AOSharp.Common.GameData;
using AOSharp.Core;

namespace RKmission
{
    // Outdoor only. Keep managed observations/identities, never a native Door pointer.
    // GetQuestWorldPos supplies a search anchor, with no exposed door/quest association.
    internal sealed class EntranceAcquisition
    {
        public const float SearchRadius = 40;
        public const float AcquisitionRadius = 48;
        private sealed class Candidate
        {
            public Identity Id;
            public Vector3 Position, Forward;
            public string Name;
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
        private readonly Vector3 _anchor, _searchOrigin;
        private readonly int _missionId;
        private DateTime _nextScan, _nextLog, _attemptProgress, _lastProgress;
        private int _mode = -1;
        public Attempt Active { get; private set; }
        public DateTime LastProgress => _lastProgress;
        public DateTime AttemptProgress => _attemptProgress;
        public bool Exhausted => Active == null && !_search.Any(x => !x.Tried) &&
            !_candidates.Values.Any(x => x.Visible && (x.Approaches == null || x.Approaches.Any(a => !a.Tried)));

        public EntranceAcquisition(AcceptedMission mission, Vector3 anchor, Vector3 origin, Action<string> say)
        {
            _missionId = mission.Id.Instance; _anchor = anchor; _searchOrigin = origin; _say = say;
            _lastProgress = DateTime.UtcNow;
            _say($"Entrance acquisition: mission={_missionId}, anchor=({LocalRoutePlanner.Coordinates(_anchor)}), " +
                $"scan radius={SearchRadius} m; quest height is provisional, live doors determine final targets.");
        }

        public void Scan(Vector3 player, bool flying)
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
            if (now < _nextScan) return;
            _nextScan = now.AddSeconds(2);
            foreach (Candidate candidate in _candidates.Values) candidate.Visible = false;
            // Playfield.Doors is AOSharp's live AllDynels filtered by IdentityType.Door.
            // Do not cast arbitrary scenery/items to Door or guess quest IDs from names.
            foreach (Door door in Playfield.Doors)
            {
                if (!door.IsValid || door.Identity.Type != IdentityType.Door) continue;
                Vector3 point = door.Position;
                if (!AcceptedMissions.Finite(point) || LocalRoutePlanner.HorizontalDistance(point, _anchor) > SearchRadius) continue;
                Identity id = door.Identity;
                bool added = !_candidates.TryGetValue(id, out Candidate candidate);
                if (added) { candidate = new Candidate { Id = id }; _candidates.Add(id, candidate); }
                // A changed live origin invalidates geometry, but not attempted sides/deadlines.
                if (!added && candidate.Approaches != null && Vector3.Distance(point, candidate.Position) > 0.5f)
                {
                    Vector3 delta = point - candidate.Position;
                    foreach (Attempt approach in candidate.Approaches)
                    { approach.Point += delta; approach.Threshold += delta; }
                    if (Active != null && Active.DoorId == id) Finish("live door position changed; refresh next approach");
                }
                candidate.Position = point; candidate.Forward = door.Rotation.Forward;
                candidate.Name = door.Name ?? ""; candidate.Locked = door.IsLocked; candidate.Open = door.IsOpen;
                candidate.Visible = true;
                float offset = LocalRoutePlanner.HorizontalDistance(point, _anchor);
                Vector3 end = LocalRoutePlanner.Toward(player + Vector3.Up, point + Vector3.Up,
                    Math.Max(0, Vector3.Distance(player, point) - 0.6f));
                candidate.Score = offset * 3 + LocalRoutePlanner.HorizontalDistance(player, point) * 0.2f +
                    Math.Min(20, Math.Abs(player.Y - point.Y)) * 0.2f +
                    (LocalRoutePlanner.ClearSegment(player + Vector3.Up, end) ? 0 : 8) +
                    (candidate.Locked && !candidate.Open ? 4 : 0) -
                    (candidate.Name.IndexOf("mission", StringComparison.OrdinalIgnoreCase) >= 0 ? 1 : 0);
                if (added)
                    _say($"Entrance candidate: mission={_missionId}, door={id}, name='{candidate.Name}', " +
                        $"position=({LocalRoutePlanner.Coordinates(point)}), anchor offset={offset:F2} m, " +
                        $"height delta from anchor={point.Y - _anchor.Y:F2} m, type=Door, room=outdoor/unavailable, " +
                        $"locked={candidate.Locked}, open={candidate.Open}, rank={candidate.Score:F1}; association is provisional.");
            }
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
            // Round-robin candidates before repeating sides of one building. A near
            // wrong/inaccessible door cannot monopolize every recovery attempt.
            foreach (Candidate candidate in _candidates.Values.Where(x => x.Visible)
                .OrderBy(x => x.Attempts).ThenBy(x => x.Score).ThenBy(x => x.Id.Instance))
            {
                if (candidate.Approaches == null) candidate.Approaches = BuildApproaches(candidate, player, flying);
                Attempt next = candidate.Approaches.Where(x => !x.Tried).OrderBy(x => x.Score).FirstOrDefault();
                if (next == null) continue;
                candidate.Attempts++; Start(next, player, flying);
                _say($"Entrance selected: mission={_missionId}, door={candidate.Id}, " +
                    $"position=({LocalRoutePlanner.Coordinates(candidate.Position)}), " +
                    $"anchor offset={LocalRoutePlanner.HorizontalDistance(candidate.Position, _anchor):F2} m, " +
                    $"approach=({LocalRoutePlanner.Coordinates(next.Point)}), height source={next.HeightSource}, " +
                    $"candidate attempt={candidate.Attempts}/{candidate.Approaches.Count}, probe score={next.Score:F1}.");
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

        public Door RefreshDoor()
        {
            if (Active == null || Active.DoorId == Identity.None) return null;
            return Playfield.Doors.FirstOrDefault(x => x.Identity == Active.DoorId && x.IsValid &&
                AcceptedMissions.Finite(x.Position) && LocalRoutePlanner.HorizontalDistance(x.Position, _anchor) <= SearchRadius);
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
            _search.Add(new Attempt { Point = centre });
            double heading = Math.Atan2(_searchOrigin.Z - centre.Z, _searchOrigin.X - centre.X);
            foreach (float radius in new[] { 8f, 16f, 28f, SearchRadius })
                for (int i = 0; i < 8; i++)
                {
                    double angle = heading + i * Math.PI / 4;
                    _search.Add(new Attempt { Point = centre + new Vector3((float)Math.Cos(angle) * radius, 0,
                        (float)Math.Sin(angle) * radius) });
                }
        }

        private static float Distance(Vector3 from, Vector3 to, bool flying) => flying
            ? Vector3.Distance(from, to) : LocalRoutePlanner.HorizontalDistance(from, to);
    }
}
