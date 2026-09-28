using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using AOSharp.Common.GameData;
using AOSharp.Core;

namespace RKmission
{
    // Outdoor association, radial candidates and managed observations. LocalMissionTravel
    // owns the only travel/entry state machine. Never retain native Door objects.
    internal sealed class EntranceAcquisition
    {
        public const float SearchRadius = 6; // Live door association never expands.
        private sealed class Candidate
        {
            public Identity Id;
            public Vector3 Position, Forward;
            public Quaternion Rotation;
            public string Name, Association;
            public bool QuestLinked, Visible, Oriented;
        }
        internal sealed class Attempt
        {
            public string Key, Source, HeightSource, DoorGeometry;
            public Identity DoorId = Identity.None;
            public Vector3 Exterior, Normal, Threshold, DoorOrigin;
            public int Sector, Pass;
            public float Radius, Lateral, Score;
            public bool KnownGood, AlternateHeight;
            public EntranceAttemptRecord Record;
        }
        private readonly Dictionary<Identity, Candidate> _candidates = new Dictionary<Identity, Candidate>();
        private readonly Dictionary<Identity, string> _decisions = new Dictionary<Identity, string>();
        private readonly Dictionary<string, float> _minima = new Dictionary<string, float>();
        private readonly List<Attempt> _plans = new List<Attempt>();
        private readonly Dictionary<string, int> _visits = new Dictionary<string, int>();
        private readonly int[] _sectorVisits;
        private readonly float[] _sectorScores, _blockedRadii;
        private readonly HashSet<int> _covered = new HashSet<int>();
        private readonly HashSet<string> _coveredRings = new HashSet<string>();
        private readonly Action<string> _say;
        private readonly AcceptedMission _mission;
        private readonly List<AcceptedMission> _accepted;
        private readonly Vector3 _anchor;
        private readonly int _missionId;
        private readonly OutdoorNavigationSettings _settings;
        private readonly EntranceLearning _learning;
        private readonly EntranceMemory _memory;
        private bool _fallbackBound;
        private Vector3 _fallbackThreshold;
        private DateTime _nextScan, _nextLog;
        private int _mode = -1, _oppositeSector = -1;
        public Attempt Active { get; private set; }
        public DateTime LastProgress { get; private set; }
        public bool FullSpaceCovered => _coveredRings.Count >= _settings.Sectors *
            new[] { _settings.ProbeRadius, _settings.MaxProbeRadius, 6f }.Distinct().Count();
        public int CoveredSectors => _covered.Count;

        public EntranceAcquisition(AcceptedMission mission, List<AcceptedMission> accepted, Vector3 anchor,
            OutdoorNavigationSettings settings, EntranceLearning learning, Action<string> say)
        {
            _mission = mission; _accepted = accepted; _anchor = anchor; _missionId = mission.Id.Instance;
            _settings = settings; _learning = learning; _say = say; _memory = learning.For(mission.PlayfieldId, anchor);
            _sectorVisits = new int[settings.Sectors]; _sectorScores = new float[settings.Sectors];
            _blockedRadii = Enumerable.Repeat(float.NaN, settings.Sectors).ToArray(); LastProgress = DateTime.UtcNow;
            _say($"Entrance search: mission={_missionId}, playfield={mission.PlayfieldId}, anchor=({LocalRoutePlanner.Coordinates(anchor)}), " +
                $"door association radius={SearchRadius} m, movement search radius={settings.MaxProbeRadius} m, sectors={settings.Sectors}, " +
                $"learning key={_memory.Key}; quest altitude is not an entry target.");
        }

        public void Scan(Vector3 player, bool flying, bool force = false)
        {
            int mode = flying ? 1 : 0;
            if (_mode != mode)
            {
                Finish("movement mode changed; sector history/deadline retained", player, false);
                _mode = mode; _plans.Clear(); _nextScan = DateTime.MinValue; BuildPlans(null, flying);
            }
            DateTime now = DateTime.UtcNow;
            if (!force && now < _nextScan) return;
            _nextScan = now.AddSeconds(2);
            foreach (Candidate item in _candidates.Values) item.Visible = false;
            foreach (Door door in Playfield.Doors)
            {
                // Filter selected anchor before considering identity/context. No remote door ranking.
                if (door == null || !door.IsValid || !AcceptedMissions.Finite(door.Position) ||
                    LocalRoutePlanner.HorizontalDistance(door.Position, _anchor) > SearchRadius) continue;
                if (!Associated(door, out string association, out bool linked)) continue;
                if (!_candidates.TryGetValue(door.Identity, out Candidate candidate))
                { candidate = new Candidate { Id = door.Identity }; _candidates.Add(candidate.Id, candidate); }
                candidate.Position = door.Position; candidate.Rotation = door.Rotation;
                Vector3 forward = door.Rotation.Forward; forward.Y = 0;
                double magnitude = door.Rotation.Magnitude;
                candidate.Oriented = !double.IsNaN(magnitude) && magnitude >= 0.5 && magnitude <= 1.5 &&
                    AcceptedMissions.Finite(forward) && LocalRoutePlanner.HorizontalDistance(forward, Vector3.Zero) > 0.3f;
                candidate.Forward = candidate.Oriented ? forward.Normalize() : Vector3.Zero;
                candidate.Name = door.Name ?? ""; candidate.Association = association;
                candidate.QuestLinked = linked; candidate.Visible = true;
            }
            var visible = _candidates.Values.Where(x => x.Visible).ToList();
            bool linkedExists = visible.Any(x => x.QuestLinked);
            var fallback = visible.Where(x => !x.QuestLinked)
                .OrderBy(x => LocalRoutePlanner.HorizontalDistance(x.Position, _anchor)).ThenBy(x => x.Id.Instance).ToList();
            Candidate closest = fallback.FirstOrDefault();
            bool ambiguous = closest != null && fallback.Any(x => x.Id != closest.Id &&
                LocalRoutePlanner.HorizontalDistance(x.Position, _anchor) <= LocalRoutePlanner.HorizontalDistance(closest.Position, _anchor) + 1 &&
                LocalRoutePlanner.HorizontalDistance(x.Position, closest.Position) > 0.5f);
            foreach (Candidate item in fallback)
            {
                bool same = closest != null && LocalRoutePlanner.HorizontalDistance(item.Position, closest.Position) <= 0.5f;
                bool bound = !_fallbackBound || LocalRoutePlanner.HorizontalDistance(item.Position, _fallbackThreshold) <= 0.5f;
                if (!linkedExists && !ambiguous && same && bound) continue;
                item.Visible = false;
                Decision(item.Id, item.Position, item.Name, false, linkedExists ? "quest-linked entrance supersedes unlinked door" :
                    !bound ? "different unlinked threshold; refusing another building" : ambiguous ? "ambiguous local door association" : "farther unlinked threshold excluded");
            }
            foreach (Candidate candidate in _candidates.Values.Where(x => x.Visible))
            { Decision(candidate.Id, candidate.Position, candidate.Name, true, candidate.Association); BuildPlans(candidate, flying); }
            if (Active != null && Active.DoorId != Identity.None &&
                (!_candidates.TryGetValue(Active.DoorId, out Candidate live) || !live.Visible ||
                 Vector3.Distance(Active.DoorOrigin, live.Position) > 0.5f ||
                 (Active.Record.DoorForward?.Valid == true && live.Oriented && Vector3.Dot(Active.Record.DoorForward.Vector, live.Forward) < 0.95f)))
                Finish("live mission door disappeared or geometry/association changed", player, false);
            foreach (Identity id in _candidates.Where(x => !x.Value.Visible && Active?.DoorId != x.Key).Select(x => x.Key).ToList()) _candidates.Remove(id);
            _plans.RemoveAll(x => x.DoorId != Identity.None && !_candidates.ContainsKey(x.DoorId));
            if (_decisions.Count > 128) _decisions.Clear();
            if (now >= _nextLog)
            {
                _nextLog = now.AddSeconds(10);
                _say($"Live mission-door association: mission={_missionId}, accepted local doors={_candidates.Values.Count(x => x.Visible)}, " +
                    $"active={Active?.DoorId.ToString() ?? "none"}, sectors explored={CoveredSectors}/{_settings.Sectors}, " +
                    $"no new observed progress={(now - LastProgress).TotalSeconds:F0}/{_settings.NoProgressSeconds} s.");
            }
        }

        private void BuildPlans(Candidate door, bool flying)
        {
            string prefix = door == null ? "anchor" : Geometry(door);
            if (door != null) _plans.RemoveAll(x => x.DoorId == door.Id && x.DoorGeometry != prefix);
            var directions = Enumerable.Range(0, _settings.Sectors).Select(x => LocalRoutePlanner.Direction(x * Math.PI * 2 / _settings.Sectors)).ToList();
            if (door?.Oriented == true) { directions.Insert(0, door.Forward); directions.Insert(1, door.Forward * -1); }
            Vector3 remembered = _memory.LastSuccessVector?.Vector ?? Vector3.Zero;
            remembered.Y = 0;
            bool known = _memory.LastSuccessVector?.Valid == true && LocalRoutePlanner.HorizontalDistance(remembered, Vector3.Zero) > 0.1f && _memory.LastSuccessMode == (flying ? "Fly" : "Run");
            if (known && door != null && _memory.LastSuccessDoorPosition?.Valid == true)
                known = Vector3.Distance(_memory.LastSuccessDoorPosition.Vector, door.Position) <= 2 &&
                    (!door.Oriented || _memory.LastSuccessDoorForward?.Valid != true || Vector3.Dot(_memory.LastSuccessDoorForward.Vector, door.Forward) >= 0.8f);
            if (known) directions.Insert(0, remembered.Normalize());
            foreach (float radius in new[] { _settings.ProbeRadius, _settings.MaxProbeRadius, 6f }.Distinct())
                foreach (Vector3 normal in directions)
                {
                    double angle = PositiveAngle(LocalRoutePlanner.Angle(normal));
                    string key = $"{_mode}:{prefix}:{Math.Round(angle * 180 / Math.PI)}:{radius}";
                    if (_plans.Any(x => x.Key == key)) continue;
                    bool rememberedDirection = known && Vector3.Dot(normal, remembered.Normalize()) > 0.995f && radius == _settings.ProbeRadius;
                    float alignment = door?.Oriented == true ? Math.Abs(Vector3.Dot(normal, door.Forward)) : 0;
                    _plans.Add(new Attempt { Key = key, DoorGeometry = prefix, DoorId = door?.Id ?? Identity.None, Normal = normal, Sector = Sector(angle), Radius = radius,
                        Threshold = door?.Position ?? _anchor, DoorOrigin = door?.Position ?? _anchor, KnownGood = rememberedDirection,
                        Source = door == null ? rememberedDirection ? "remembered exterior vector; validation required" : "mission-anchor inferred radial side" :
                            door.Oriented && alignment > 0.99f ? "live mission-door normal (exterior sign requires validation)" : "live mission-door radial fallback",
                        Score = (door == null ? 30 : (door.QuestLinked ? -30 : -15) - alignment * 20) +
                            (radius == _settings.ProbeRadius ? 0 : radius == 6 ? 6 : 4) });
                }
        }

        public bool PreferLiveDoor => Active?.DoorId == Identity.None && _candidates.Values.Any(x => x.Visible);
        public bool HasUntriedLiveGeometry => _plans.Any(x => x.DoorId != Identity.None && Visits(x.Key) == 0);
        public Attempt Select(Vector3 player, bool flying)
        {
            if (Active != null) return Active;
            var eligible = _plans.Where(x => x.DoorId == Identity.None || (_candidates.TryGetValue(x.DoorId, out Candidate door) && door.Visible && Vector3.Distance(x.DoorOrigin, door.Position) <= 0.5f));
            // Coverage balances all sectors; each blocked face yields to an unexplored arc.
            // A finite list never exhausts into an idle wait. Minima/deadlines survive retries.
            Attempt next = eligible.OrderBy(x => x.KnownGood && Visits(x.Key) == 0 ? -1 : _sectorVisits[x.Sector])
                .ThenBy(x => Visits(x.Key)).ThenBy(x => Rank(x, player)).FirstOrDefault();
            if (next == null) return null;
            next.Pass = Visits(next.Key); _visits[next.Key] = next.Pass + 1; _sectorVisits[next.Sector]++;
            next.Lateral = next.Pass % 3 == 1 ? 0.8f : next.Pass % 3 == 2 ? -0.8f : 0; next.AlternateHeight = next.Pass % 2 == 1;
            next.Exterior = LocalRoutePlanner.LocalElevation(_anchor + next.Normal * next.Radius, player, flying, _settings, out string elevation);
            Candidate selectedDoor = null;
            if (next.DoorId != Identity.None) _candidates.TryGetValue(next.DoorId, out selectedDoor);
            Vector3 threshold = selectedDoor?.Position ?? _anchor;
            if (selectedDoor != null && !next.AlternateHeight)
            {
                bool originIsFloor = flying && LocalRoutePlanner.TryEntranceSurface(selectedDoor.Position, player.Y, out float floor, out _) &&
                    Math.Abs(selectedDoor.Position.Y - floor) <= 0.5f;
                if (originIsFloor) threshold.Y += _settings.FlightFloorClearance;
                next.Exterior.Y = threshold.Y;
                elevation = originIsFloor ? "live mission-door origin confirmed at floor plus flight clearance" : "live mission-door origin";
            }
            else threshold.Y = next.Exterior.Y;
            if (selectedDoor == null && next.AlternateHeight)
            { next.Exterior.Y = threshold.Y = player.Y; elevation = "current player elevation alternative; floor estimate being validated"; }
            if (selectedDoor != null && !selectedDoor.QuestLinked && !_fallbackBound) { _fallbackBound = true; _fallbackThreshold = selectedDoor.Position; }
            next.Threshold = threshold; next.HeightSource = elevation;
            next.Record = new EntranceAttemptRecord { StartedUtc = DateTime.UtcNow, Playfield = _mission.PlayfieldId, MissionId = _missionId,
                Sector = next.Sector, Mode = flying ? "Fly" : "Run", Stage = "ProbeExterior", Source = next.Source, ElevationSource = elevation,
                AngleDegrees = (float)(PositiveAngle(LocalRoutePlanner.Angle(next.Normal)) * 180 / Math.PI), Radius = next.Radius,
                Anchor = NavigationPoint.From(_anchor), Origin = NavigationPoint.From(player), ExteriorVector = NavigationPoint.From(next.Normal),
                CandidatePoint = NavigationPoint.From(next.Exterior), ApproachPoint = NavigationPoint.From(threshold + next.Normal * 1.5f),
                DoorIdentity = next.DoorId.ToString(), DoorAssociation = selectedDoor?.Association,
                DoorPosition = selectedDoor == null ? null : NavigationPoint.From(selectedDoor.Position),
                DoorForward = selectedDoor?.Oriented == true ? NavigationPoint.From(selectedDoor.Forward) : null,
                DoorRotation = selectedDoor == null ? null : NavigationRotation.From(selectedDoor.Rotation), Result = "in progress" };
            Active = next; _learning.Record(_memory, next.Record);
            _say($"Entrance candidate: mission={_missionId}, sector={next.Sector}/{_settings.Sectors}, angle={next.Record.AngleDegrees:F1} deg, radius={next.Radius:F1} m, " +
                $"pass={next.Pass + 1}, source={next.Source}, point=({LocalRoutePlanner.Coordinates(next.Exterior)}), elevation source={elevation}, " +
                $"sector score={_sectorScores[next.Sector]:F1}, door={next.DoorId}, rotation={selectedDoor?.Rotation.ToString() ?? "unavailable"}.");
            return next;
        }
        private float Rank(Attempt attempt, Vector3 player)
        {
            float rank = attempt.Score + _sectorScores[attempt.Sector] + LocalRoutePlanner.HorizontalDistance(player, _anchor + attempt.Normal * attempt.Radius) * 0.2f;
            if (_oppositeSector >= 0)
            { int gap = Math.Abs(attempt.Sector - _oppositeSector); rank += Math.Min(gap, _settings.Sectors - gap) * 4; }
            double degrees = PositiveAngle(LocalRoutePlanner.Angle(attempt.Normal)) * 180 / Math.PI;
            return rank + Math.Min(8, _memory.Attempts.Count(x => x != null && x.Mode == (_mode == 1 ? "Fly" : "Run") && x.Result == "blocked" &&
                Math.Min(Math.Abs(x.AngleDegrees - degrees), 360 - Math.Abs(x.AngleDegrees - degrees)) <= 180.0 / _settings.Sectors) * 0.5f);
        }
        public List<Vector3> Orbit(Attempt attempt, Vector3 player, bool flying)
        {
            double start = LocalRoutePlanner.Angle(player - _anchor);
            // A failed radial escape must not be repeated before every other sector.
            // Try an outward/tangential escape from the actually blocked player side.
            if (!float.IsNaN(_blockedRadii[Sector(start)])) start += (attempt.Sector % 2 == 0 ? 1 : -1) * Math.PI / 8;
            double difference = LocalRoutePlanner.Angle(attempt.Normal) - start;
            while (difference > Math.PI) difference -= Math.PI * 2;
            while (difference < -Math.PI) difference += Math.PI * 2;
            if (attempt.Pass % 2 == 1 && Math.Abs(difference) > 0.1) difference += difference > 0 ? -Math.PI * 2 : Math.PI * 2;
            var path = new List<Vector3>();
            Vector3 initial = _anchor + LocalRoutePlanner.Direction(start) * attempt.Radius; initial.Y = player.Y;
            if (!flying) initial = LocalRoutePlanner.LocalElevation(initial, player, false, _settings, out _);
            path.Add(initial); // Escape radially before following the ring; never cut through the anchor.
            int legs = Math.Max(1, (int)Math.Ceiling(Math.Abs(difference) / (Math.PI / 8)));
            for (int i = 1; i <= legs; i++)
            {
                Vector3 point = _anchor + LocalRoutePlanner.Direction(start + difference * i / legs) * attempt.Radius; point.Y = player.Y;
                if (!flying) point = LocalRoutePlanner.LocalElevation(point, player, false, _settings, out _);
                path.Add(point);
            }
            return path;
        }
        public List<Vector3> FinalPoints(Attempt attempt)
        {
            Vector3 tangent = new Vector3(-attempt.Normal.Z, 0, attempt.Normal.X);
            var points = new List<Vector3>();
            foreach (float offset in new[] { 3f, 1.5f, 0.4f })
            {
                Vector3 point = attempt.Threshold + attempt.Normal * offset + tangent * attempt.Lateral;
                float boundary = attempt.DoorId == Identity.None ? SearchRadius : _settings.MaxProbeRadius;
                if (LocalRoutePlanner.HorizontalDistance(point, _anchor) <= boundary) points.Add(point);
            }
            return points;
        }
        public void Observe(Vector3 player, Vector3 target, string stage, bool flying, float legStart, float legBest, double stalled)
        {
            if (Active == null) return;
            // Compare fixed candidate goals across retries, not a fresh clock for each
            // orbit waypoint. Repeating a loop cannot manufacture overall progress.
            string key = $"{Active.Key}:{Active.AlternateHeight}:{Active.Lateral}:{stage}";
            Vector3 goal = stage == "ProbeExterior" || stage == "AlignElevation" ? Active.Exterior : Active.Threshold;
            float distance = stage == "ProbeExterior" ? LocalRoutePlanner.HorizontalDistance(player, goal) : LocalMovement.Distance(player, goal, flying);
            if (!_minima.TryGetValue(key, out float previous))
            {
                _minima[key] = previous = stage == "ProbeExterior" ? LocalRoutePlanner.HorizontalDistance(Active.Record.Origin.Vector, goal) :
                    LocalMovement.Distance(Active.Record.Origin.Vector, goal, flying);
            }
            if (distance + 0.5f < previous) { _minima[key] = distance; LastProgress = DateTime.UtcNow; }
            Active.Record.Stage = stage; Active.Record.LastPosition = NavigationPoint.From(player); Active.Record.LastTarget = NavigationPoint.From(target);
            Active.Record.StartDistance = legStart; Active.Record.BestDistance = legBest;
            Active.Record.ProgressMetres = Math.Max(Active.Record.ProgressMetres, Math.Max(0, legStart - legBest)); Active.Record.StallSeconds = (float)stalled;
        }
        public void Finish(string reason, Vector3 player, bool blocked)
        {
            if (Active == null) return;
            Attempt attempt = Active; attempt.Record.LastPosition = NavigationPoint.From(player); attempt.Record.FinishedUtc = DateTime.UtcNow;
            Cover(attempt);
            attempt.Record.Result = blocked ? "blocked" : "incomplete"; attempt.Record.Reason = reason;
            if (blocked)
            {
                int blockedSector = Sector(LocalRoutePlanner.Angle(player - _anchor));
                _sectorScores[blockedSector] += 8; _blockedRadii[blockedSector] = LocalRoutePlanner.HorizontalDistance(player, _anchor);
                _say($"Blocked sector={blockedSector}, requested sector={attempt.Sector}, actual stall radius={_blockedRadii[blockedSector]:F2} m.");
                foreach (int neighbor in new[] { (blockedSector + 1) % _settings.Sectors, (blockedSector + _settings.Sectors - 1) % _settings.Sectors })
                    if (!float.IsNaN(_blockedRadii[neighbor]) && Math.Abs(_blockedRadii[neighbor] - _blockedRadii[blockedSector]) <= 3)
                    {
                        _oppositeSector = (blockedSector + _settings.Sectors / 2) % _settings.Sectors;
                        _say($"Inferred likely building face/corner: blocked sectors={neighbor},{blockedSector}, stall radius={_blockedRadii[blockedSector]:F2} m; " +
                            $"prioritize opposite arc near sector={_oppositeSector}; entrance remains a candidate.");
                    }
            }
            _learning.Record(_memory, attempt.Record);
            _say($"Entrance attempt result: mission={_missionId}, sector={attempt.Sector}, door={attempt.DoorId}, progress delta={attempt.Record.ProgressMetres:F2} m, " +
                $"result={attempt.Record.Result}, reason={reason}; retained for later retries."); Active = null;
        }
        public void ExteriorReached()
        {
            if (Active == null) return;
            Cover(Active);
            _sectorScores[Active.Sector] = Math.Max(-12, _sectorScores[Active.Sector] - 4);
            _say($"Chosen exterior side: mission={_missionId}, sector={Active.Sector}, source={Active.Source}, outward vector=({LocalRoutePlanner.Coordinates(Active.Normal)}); " +
                "exterior reached, entrance access still being validated.");
        }
        public void CompleteHandoff(bool success, string reason)
        {
            if (Active == null) return;
            Active.Record.FinishedUtc = DateTime.UtcNow; Active.Record.Result = success ? "verified entry" : "failed transition"; Active.Record.Reason = reason;
            _learning.Record(_memory, Active.Record, success);
            _say($"Entrance learning result: key={_memory.Key}, mission={_missionId}, result={Active.Record.Result}, reason={reason}, " +
                $"successful exterior vector={(success ? LocalRoutePlanner.Coordinates(Active.Normal) : "not updated")}."); Active = null;
        }
        public Door RefreshDoor(bool rescan = false)
        {
            if (rescan) Scan(DynelManager.LocalPlayer.Position, DynelManager.LocalPlayer.MovementState == MovementState.Fly, true);
            if (Active == null || Active.DoorId == Identity.None || !_candidates.TryGetValue(Active.DoorId, out Candidate candidate) || !candidate.Visible) return null;
            Mission live = Mission.List?.FirstOrDefault(x => x.Identity == _mission.Id); MissionLocation location = live?.Location;
            if (location == null || location.Playfield.Instance != _mission.PlayfieldId || !AcceptedMissions.Finite(location.Pos) || Vector3.Distance(location.Pos, _anchor) > 0.5f) return null;
            Door door = Playfield.Doors.FirstOrDefault(x => x.Identity == candidate.Id);
            if (door == null || !Associated(door, out _, out _) || Vector3.Distance(door.Position, candidate.Position) > 0.5f) return null;
            return door;
        }
        private int Visits(string key) => _visits.TryGetValue(key, out int visits) ? visits : 0;
        private static string Geometry(Candidate door) => $"door:{door.Id}:{Math.Round(door.Position.X * 2)}:" +
            $"{Math.Round(door.Position.Z * 2)}:{Math.Round(door.Position.Y * 2)}:{(door.Oriented ? Math.Round(LocalRoutePlanner.Angle(door.Forward) * 180 / Math.PI) : 999)}";
        private void Cover(Attempt attempt)
        { _covered.Add(attempt.Sector); _coveredRings.Add($"{attempt.Sector}:{attempt.Radius}"); }
        private static double PositiveAngle(double angle) => angle < 0 ? angle + Math.PI * 2 : angle;
        private int Sector(double angle) => (int)Math.Round(PositiveAngle(angle) * _settings.Sectors / (Math.PI * 2)) % _settings.Sectors;
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


    }
}
