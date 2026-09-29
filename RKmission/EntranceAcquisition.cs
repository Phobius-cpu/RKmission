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
        private sealed class FlightHeight { public float Height; public string Source; }
        private readonly List<FlightHeight> _flightHeights = new List<FlightHeight>();
        private readonly HashSet<int> _flightHeightCoveredSectors = new HashSet<int>();
        private int _flightHeightTrial;
        private Identity _flightHeightDoor = Identity.None;
        private float _flightDoorOriginHeight;
        private bool _flightInitialHeightVerified;
        private NavigationPoint _flightHeightMatchPoint;
        private readonly Dictionary<Identity, Candidate> _candidates = new Dictionary<Identity, Candidate>();
        private readonly Dictionary<Identity, string> _decisions = new Dictionary<Identity, string>();
        private readonly Dictionary<string, float> _minima = new Dictionary<string, float>();
        private readonly List<Attempt> _plans = new List<Attempt>();
        private readonly Dictionary<string, int> _visits = new Dictionary<string, int>();
        private readonly int[] _sectorVisits;
        private readonly float[] _sectorScores, _blockedRadii;
        private readonly Vector3[] _blockedPositions;
        private readonly HashSet<string> _coveredRings = new HashSet<string>();
        private readonly HashSet<string> _orbitCoverage = new HashSet<string>();
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
        private float _wallRadius, _lastRingRadius, _lastAngularSpan;
        private int _lastBypassDirection;
        private bool _sessionWallObserved;
        public bool BypassRequired { get; private set; }
        public bool BypassExhausted { get; private set; }
        public Attempt Active { get; private set; }
        public DateTime LastProgress { get; private set; }
        public bool FullSpaceCovered => _mode == 1 ? _flightHeightCoveredSectors.Count >= _settings.Sectors :
            _coveredRings.Count(x => x.StartsWith(_mode + ":", StringComparison.Ordinal)) >= _settings.Sectors *
            new[] { _settings.ProbeRadius, _settings.MaxProbeRadius, 6f }.Distinct().Count();
        public int CoveredSectors => _coveredRings.Where(x => x.StartsWith(_mode + ":", StringComparison.Ordinal))
            .Select(x => x.Split(':')[1]).Distinct().Count();

        public EntranceAcquisition(AcceptedMission mission, List<AcceptedMission> accepted, Vector3 anchor,
            OutdoorNavigationSettings settings, EntranceLearning learning, Action<string> say)
        {
            _mission = mission; _accepted = accepted; _anchor = anchor; _missionId = mission.Id.Instance;
            _settings = settings; _learning = learning; _say = say; _memory = learning.For(mission.PlayfieldId, anchor);
            _sectorVisits = new int[settings.Sectors]; _sectorScores = new float[settings.Sectors];
            _blockedRadii = Enumerable.Repeat(float.NaN, settings.Sectors).ToArray(); LastProgress = DateTime.UtcNow;
            _blockedPositions = new Vector3[settings.Sectors];
            // Version-1 records are still readable. Recover actual wall bearings
            // from their positions, never reinterpret their requested sectors.
            foreach (EntranceAttemptRecord record in _memory.Attempts.Where(x => x != null && x.Result == "blocked" &&
                x.LastPosition?.Valid == true && x.Stage != "CoarseTravel"))
                RememberWall(record.LastPosition.Vector, null, false);
            _say($"Entrance search: mission={_missionId}, playfield={mission.PlayfieldId}, anchor=({LocalRoutePlanner.Coordinates(anchor)}), " +
                $"door association radius={SearchRadius} m, Ground movement bound={settings.MaxProbeRadius} m, " +
                $"Fly exterior bound={settings.MaxFlightBypassRadius} m, sectors={settings.Sectors}, " +
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
            // Fly diagnoses sides on one adaptive exterior, rather than repeat
            // the same ground ring three times with different nominal radii.
            foreach (float radius in (flying ? new[] { _settings.ProbeRadius } :
                new[] { _settings.ProbeRadius, _settings.MaxProbeRadius, 6f }).Distinct())
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
            if (_candidates.Values.Any(x => x.Visible)) eligible = eligible.Where(x => x.DoorId != Identity.None);
            if (BypassRequired)
                eligible = eligible.Where(x => Math.Abs(AngleDelta(LocalRoutePlanner.Angle(x.Normal) -
                    LocalRoutePlanner.Angle(player - _anchor))) >= Math.PI / 6 || (x.KnownGood && !_sessionWallObserved));
            // Coverage balances all sectors; each blocked face yields to an unexplored arc.
            // A finite list never exhausts into an idle wait. Minima/deadlines survive retries.
            Attempt next = eligible.OrderBy(x => x.KnownGood && Visits(x.Key) == 0 ? -1 : _sectorVisits[x.Sector])
                .ThenBy(x => Visits(x.Key)).ThenBy(x => Rank(x, player)).FirstOrDefault();
            if (next == null) return null;
            next.Pass = Visits(next.Key); _visits[next.Key] = next.Pass + 1; _sectorVisits[next.Sector]++;
            next.Lateral = next.Pass % 3 == 1 ? 0.8f : next.Pass % 3 == 2 ? -0.8f : 0; next.AlternateHeight = next.Pass % 2 == 1;
            string elevation;
            if (flying)
            {
                next.Exterior = _anchor + next.Normal * Math.Max(next.Radius, FlightRingRadius);
                next.Exterior.Y = player.Y;
                elevation = FlightEntryHeightSource;
            }
            else next.Exterior = LocalRoutePlanner.LocalElevation(_anchor + next.Normal * next.Radius, player, false, _settings, out elevation);
            Candidate selectedDoor = null;
            if (next.DoorId != Identity.None) _candidates.TryGetValue(next.DoorId, out selectedDoor);
            Vector3 threshold = selectedDoor?.Position ?? _anchor;
            if (!flying && selectedDoor != null && !next.AlternateHeight)
            {
                next.Exterior.Y = threshold.Y;
                elevation = "live mission-door origin";
            }
            else threshold.Y = flying ? FlightEntryHeight : next.Exterior.Y;
            if (!flying && selectedDoor == null && next.AlternateHeight)
            { next.Exterior.Y = threshold.Y = player.Y; elevation = "current player elevation alternative; floor estimate being validated"; }
            if (selectedDoor != null && !selectedDoor.QuestLinked && !_fallbackBound) { _fallbackBound = true; _fallbackThreshold = selectedDoor.Position; }
            next.Threshold = threshold; next.HeightSource = elevation;
            next.Record = new EntranceAttemptRecord { StartedUtc = DateTime.UtcNow, Playfield = _mission.PlayfieldId, MissionId = _missionId,
                Sector = next.Sector, SectorCount = _settings.Sectors, Mode = flying ? "Fly" : "Run", Stage = "ProbeExterior", Source = next.Source, ElevationSource = elevation,
                AngleDegrees = (float)(PositiveAngle(LocalRoutePlanner.Angle(next.Normal)) * 180 / Math.PI), Radius = next.Radius,
                Anchor = NavigationPoint.From(_anchor), Origin = NavigationPoint.From(player), ExteriorVector = NavigationPoint.From(next.Normal),
                CandidatePoint = NavigationPoint.From(next.Exterior), ApproachPoint = NavigationPoint.From(threshold + next.Normal * 1.5f),
                DoorIdentity = next.DoorId.ToString(), DoorAssociation = selectedDoor?.Association,
                DoorPosition = selectedDoor == null ? null : NavigationPoint.From(selectedDoor.Position),
                DoorForward = selectedDoor?.Oriented == true ? NavigationPoint.From(selectedDoor.Forward) : null,
                DoorRotation = selectedDoor == null ? null : NavigationRotation.From(selectedDoor.Rotation), Result = "in progress" };
            next.Record.BypassDirection = _lastBypassDirection;
            next.Record.ExteriorRingRadius = _lastRingRadius;
            next.Record.AngularSpanDegrees = _lastAngularSpan;
            if (flying)
            {
                next.Record.EntryPoint = NavigationPoint.From(threshold);
                next.Record.HeightMatchPoint = _flightHeightMatchPoint;
                next.Record.HeightMatchTriggerDistance = 10;
            }
            Active = next; _learning.Record(_memory, next.Record);
            _say($"Entrance candidate: mission={_missionId}, sector={next.Sector}/{_settings.Sectors}, angle={next.Record.AngleDegrees:F1} deg, requested radius={next.Radius:F1} m, " +
                $"actual exterior radius={LocalRoutePlanner.HorizontalDistance(next.Exterior, _anchor):F1} m, " +
                $"pass={next.Pass + 1}, source={next.Source}, point=({LocalRoutePlanner.Coordinates(next.Exterior)}), elevation source={elevation}, " +
                $"sector score={_sectorScores[next.Sector]:F1}, door={next.DoorId}, rotation={selectedDoor?.Rotation.ToString() ?? "unavailable"}.");
            return next;
        }
        private float Rank(Attempt attempt, Vector3 player)
        {
            float rank = attempt.Score + _sectorScores[attempt.Sector] + LocalRoutePlanner.HorizontalDistance(player, _anchor + attempt.Normal * attempt.Radius) * 0.2f;
            if (_mode == 1)
            {
                // Diagnose the inward corridor on EACH side before choosing it.
                // All inferred sides use the SAME mission entrance height.
                // Surrounding terrain must not reset it at each orbit point.
                Vector3 exterior = _anchor + attempt.Normal * Math.Max(attempt.Radius, FlightRingRadius);
                float height = FlightEntryHeight;
                exterior.Y = height;
                Vector3 inward = attempt.Threshold + attempt.Normal * 3; inward.Y = height;
                if (LocalRoutePlanner.FlightCorridor(exterior, inward, out _)) rank += 50;
            }
            if (_oppositeSector >= 0)
            { int gap = Math.Abs(attempt.Sector - _oppositeSector); rank += Math.Min(gap, _settings.Sectors - gap) * 4; }
            // A candidate that never arrived is not a blocked entrance side.
            // Rank historical obstruction evidence by actual wall bearing only.
            return rank + (_memory.WallSectorCount == _settings.Sectors &&
                _memory.FailedWallBearingSectors?.Contains(attempt.Sector) == true ? 4 : 0);
        }
        public EntranceOrbit GroundOrbit(Attempt attempt, Vector3 player)
        {
            bool compatible = _memory.LastSuccessMode == "Run";
            int preferred = _lastBypassDirection != 0 ? _lastBypassDirection : compatible ? _memory.LastBypassDirection : 0;
            float rememberedRadius = Math.Max(_lastRingRadius, compatible ? _memory.LastExteriorRingRadius : 0);
            bool requireChange = BypassRequired && !(attempt.KnownGood && !_sessionWallObserved &&
                Math.Abs(AngleDelta(LocalRoutePlanner.Angle(attempt.Normal) - LocalRoutePlanner.Angle(player - _anchor))) < Math.PI / 6);
            _say($"Perimeter route requested: sector={attempt.Sector}, target bearing={attempt.Record.AngleDegrees:F1} deg; side not yet reached.");
            return new EntranceOrbit(_anchor, attempt.Exterior, player, requireChange, _wallRadius,
                rememberedRadius, preferred, _settings, _say);
        }
        public float FlightRingRadius => Math.Min(_settings.MaxFlightBypassRadius,
            Math.Max(_settings.ProbeRadius, Math.Max(_wallRadius + 4, _memory.LastSuccessMode == "Fly" ? _memory.LastExteriorRingRadius : 0)));
        public int PreferredFlightDirection => _memory.LastSuccessMode == "Fly" ? _memory.LastBypassDirection : 0;
        public bool NeedsFlightSideChange(Attempt attempt) => BypassRequired && !(attempt.KnownGood && !_sessionWallObserved);
        public float FlightEntryHeight => _flightHeights[_flightHeightTrial].Height;
        public string FlightEntryHeightSource => _flightHeights[_flightHeightTrial].Source;
        public bool FlightEntryHeightVerified => _flightInitialHeightVerified && _flightHeightTrial == 0;
        public void ResolveFlightEntryHeight(Vector3 player, float fallbackHeight)
        {
            Candidate door = _candidates.Values.Where(x => x.Visible).OrderByDescending(x => x.QuestLinked)
                .ThenBy(x => LocalRoutePlanner.HorizontalDistance(x.Position, _anchor)).FirstOrDefault();
            if (door != null)
            {
                if (_flightHeightDoor == door.Id && _flightHeights.Count > 0 &&
                    Math.Abs(_flightDoorOriginHeight - door.Position.Y) < 0.25f) return;
                float height = door.Position.Y; string source = "associated live mission Door height";
                if (LocalRoutePlanner.TryExteriorFloor(door.Position, player.Y, out float doorFloor) && Math.Abs(height - doorFloor) <= 0.5f)
                { height += _settings.FlightFloorClearance; source += " plus clearance at confirmed exterior support"; }
                _flightHeights.Clear(); _flightHeightTrial = 0; _flightHeightDoor = door.Id;
                _flightHeightCoveredSectors.Clear();
                _flightInitialHeightVerified = false;
                _flightDoorOriginHeight = door.Position.Y;
                AddFlightHeight(height, source);
            }
            else if (_flightHeights.Count == 0)
            {
                bool verified = _memory.LastSuccessMode == "Fly" && _memory.LastSuccessEntryPoint?.Valid == true;
                _flightInitialHeightVerified = verified;
                List<float> supports = LocalRoutePlanner.MissionEntrySupports(_anchor, player.Y);
                float height = verified ? _memory.LastSuccessEntryPoint.Y :
                    supports.Count > 0 ? supports[0] + _settings.FlightFloorClearance : fallbackHeight;
                string source = verified ? "previous exact verified mission entrance height, shared across sides" :
                    supports.Count > 0 ? "mission-anchor local support plus clearance; provisional entrance height" :
                    "run-start aircraft height; mission entrance height unknown, provisional";
                AddFlightHeight(height, source);
                if (!verified)
                {
                    foreach (float support in supports.Skip(1))
                        AddFlightHeight(support + _settings.FlightFloorClearance, "lower mission-anchor supported plane; roof-height hypothesis failed");
                    if (supports.Count == 0)
                        foreach (float offset in new[] { 2f, 4f, -2f, -4f, -8f })
                            AddFlightHeight(height + offset, "bounded mission entrance height hypothesis; no local support");
                    // Keep observed planes first, then correct their own clearance.
                    // Offsets from the initial roof alone cannot refine a lower
                    // doorway plane. Lowest local support gets the spare trials first.
                    foreach (float support in supports.OrderBy(x => x))
                    {
                        AddFlightHeight(support + 0.5f, "mission-anchor supported plane plus 0.5 m clearance; provisional entrance height");
                        AddFlightHeight(support + _settings.FlightFloorClearance + 2f,
                            "mission-anchor supported plane plus clearance and 2 m; provisional entrance height");
                    }
                }
                if (supports.Count > 0 || verified)
                {
                    foreach (float offset in new[] { 2f, 4f, 6f })
                        AddFlightHeight(height + offset, "bounded mission entrance height upward correction");
                    AddFlightHeight(height + Math.Max(-1f, 0.5f - _settings.FlightFloorClearance), "small mission entrance clearance correction");
                }
            }
            else return;
            _say($"Fly mission entrance height resolved: mission={_missionId}, anchor=({LocalRoutePlanner.Coordinates(_anchor)}), " +
                $"height={FlightEntryHeight:F2}, source={FlightEntryHeightSource}; match height before choosing approach sector, never use orbit terrain as doorway height.");
            _say("Fly mission entrance height plan: " + string.Join("; ", _flightHeights.Select((x, i) =>
                $"{i + 1}: Y={x.Height:F2} ({x.Source})")) + "; each candidate still requires verified entry.");
        }
        private void AddFlightHeight(float height, string source)
        {
            if (_flightHeights.Count < 6 && !_flightHeights.Any(x => Math.Abs(x.Height - height) < 0.25f))
                _flightHeights.Add(new FlightHeight { Height = height, Source = source });
        }
        public void FlightHeightMatched(Vector3 player)
        { _flightHeightMatchPoint = NavigationPoint.From(player); LastProgress = DateTime.UtcNow; }
        public bool TryNextFlyingHeight(string reason)
        {
            if ((Active != null && Active.DoorId != Identity.None) || _flightHeightDoor != Identity.None) return false;
            if (Active != null) Active.Record.FailedEntryHeights.Add(Active.Threshold.Y);
            if (_flightHeightTrial + 1 >= _flightHeights.Count) return false;
            _flightHeightTrial++;
            _flightHeightCoveredSectors.Clear();
            _say($"Fly mission entrance height correction: trial={_flightHeightTrial + 1}/{_flightHeights.Count}, " +
                $"height={FlightEntryHeight:F2}, source={FlightEntryHeightSource}, reason={reason}; match before new side diagnostics.");
            SaveAttempt(); return true;
        }
        private void SetFlightHeight(float height, string source)
        {
            Active.Threshold.Y = Active.Exterior.Y = height; Active.HeightSource = source;
            Active.Record.ElevationSource = source; Active.Record.EntryPoint = NavigationPoint.From(Active.Threshold);
            Active.Record.ApproachPoint = NavigationPoint.From(Active.Threshold + Active.Normal * 1.5f);
            _say($"Fly entrance height: sector={Active.Sector}, height={height:F2}, source={source}; " +
                "align diagonally at the reached exterior, then approach along its direction.");
        }
        public void ObserveFlight(Vector3 player, FlightPathPlanner flight, float angularTravel)
        {
            if (Active == null) return;
            Active.Record.BypassDirection = flight.BypassDirection;
            Active.Record.ExteriorRingRadius = LocalRoutePlanner.HorizontalDistance(Active.Exterior, _anchor);
            Active.Record.AngularSpanDegrees = angularTravel;
            Active.Record.FlightStrategy = flight.Strategy;
            Active.Record.OverpassResult = flight.OverpassResult;
            if (_orbitCoverage.Add($"fly:{Sector(LocalRoutePlanner.Angle(player - _anchor))}:{(int)(player.Y / 4)}"))
                LastProgress = DateTime.UtcNow;
        }
        public void FlyingExteriorReached(Vector3 player)
        {
            if (Active == null) return;
            SetFlightHeight(FlightEntryHeight, FlightEntryHeightSource);
            Active.Record.ExteriorReached = true; Active.Record.BypassSideReached = BypassRequired;
            Active.Exterior.X = player.X; Active.Exterior.Z = player.Z;
            Active.Record.ReachedExteriorPoint = NavigationPoint.From(player);
            _sectorScores[Active.Sector] = Math.Max(-12, _sectorScores[Active.Sector] - 4);
            Cover(Active); BypassExhausted = false;
            _flightHeightCoveredSectors.Add(Active.Sector);
            _say($"Fly reached-side confirmation: requested sector={Active.Sector}, actual bearing=" +
                $"{PositiveAngle(LocalRoutePlanner.Angle(player - _anchor)) * 180 / Math.PI:F1} deg, " +
                $"radius={LocalRoutePlanner.HorizontalDistance(player, _anchor):F2}, height={player.Y:F2}; entry alignment may begin.");
        }
        public void SaveAttempt() { if (Active != null) _learning.Record(_memory, Active.Record); }
        public void ObserveOrbit(EntranceOrbit orbit, Vector3 player)
        {
            orbit.Observe(player);
            // Retain angular coverage across candidate/phase retries. Repeating
            // the same arc cannot keep renewing the overall progress deadline.
            if (orbit.MadeNewProgress && _orbitCoverage.Add($"{_mode}:{Sector(LocalRoutePlanner.Angle(player - _anchor))}:{(int)(orbit.Radius / 3)}"))
                LastProgress = DateTime.UtcNow;
            if (Active == null) return;
            Active.Record.BypassDirection = orbit.Direction;
            Active.Record.ExteriorRingRadius = orbit.Radius;
            Active.Record.AngularSpanDegrees = orbit.AngularSpanDegrees;
        }
        public void OrbitBlocked(Vector3 player, bool exhausted)
        {
            BypassRequired = true; BypassExhausted |= exhausted;
            RememberWall(player, Active?.Record, true);
            if (Active != null) _learning.Record(_memory, Active.Record);
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
            bool perimeter = stage == "ProbeExterior" || stage == "OrbitBypass" || stage == "FlyAvoidObstacle";
            Vector3 goal = perimeter || stage == "AlignElevation" ? Active.Exterior : Active.Threshold;
            float distance = perimeter ? LocalRoutePlanner.HorizontalDistance(player, goal) : LocalMovement.Distance(player, goal, flying);
            if (!_minima.TryGetValue(key, out float previous))
            {
                _minima[key] = previous = perimeter ? LocalRoutePlanner.HorizontalDistance(Active.Record.Origin.Vector, goal) :
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
            attempt.Record.Result = blocked ? "blocked" : "incomplete"; attempt.Record.Reason = reason;
            if (blocked)
            {
                RememberWall(player, attempt.Record, true);
                if (!attempt.Record.ExteriorReached) BypassRequired = true;
            }
            _learning.Record(_memory, attempt.Record);
            _say($"Entrance attempt result: mission={_missionId}, sector={attempt.Sector}, door={attempt.DoorId}, progress delta={attempt.Record.ProgressMetres:F2} m, " +
                $"exterior reached={attempt.Record.ExteriorReached}, wall-bearing sector={attempt.Record.WallBearingSector}, " +
                $"result={attempt.Record.Result}, reason={reason}; retained for later retries."); Active = null;
        }
        private void RememberWall(Vector3 player, EntranceAttemptRecord record, bool log)
        {
            if (log) _sessionWallObserved = true;
            int wall = Sector(LocalRoutePlanner.Angle(player - _anchor));
            float radius = LocalRoutePlanner.HorizontalDistance(player, _anchor);
            float maximum = _mode == 0 ? _settings.MaxProbeRadius : _settings.MaxFlightBypassRadius;
            if (radius > maximum + 4 || radius < 1) return;
            foreach (int neighbor in new[] { wall, (wall + 1) % _settings.Sectors, (wall + _settings.Sectors - 1) % _settings.Sectors })
                if (!float.IsNaN(_blockedRadii[neighbor]) && Math.Abs(_blockedRadii[neighbor] - radius) <= 3 &&
                    LocalRoutePlanner.HorizontalDistance(_blockedPositions[neighbor], player) <= 6)
                {
                    BypassRequired = true; _oppositeSector = (wall + _settings.Sectors / 2) % _settings.Sectors;
                    if (log) _say($"Inferred likely building face/corner: wall-bearing sectors={neighbor},{wall}, radius={radius:F2} m; " +
                        $"opposite candidate sector={_oppositeSector} requires OrbitBypass and observed angular change.");
                }
            _sectorScores[wall] += 8; _blockedRadii[wall] = radius; _blockedPositions[wall] = player;
            _wallRadius = Math.Max(_wallRadius, radius);
            if (record != null)
            {
                record.WallBearingSector = wall; record.WallRadius = radius;
                record.WallBearingDegrees = (float)(PositiveAngle(LocalRoutePlanner.Angle(player - _anchor)) * 180 / Math.PI);
                if (log) _say($"Blocked route: requested sector={record.Sector}, actual wall-bearing sector={wall}, " +
                    $"stall radius={radius:F2} m, requested exterior reached={record.ExteriorReached}; attribution kept separate.");
            }
        }
        public void ExteriorReached(Vector3 player, EntranceOrbit orbit)
        {
            if (Active == null) return;
            ObserveOrbit(orbit, player);
            Active.Record.ExteriorReached = true; Active.Record.BypassSideReached = BypassRequired;
            // Keep the reached ring X/Z for elevation alignment. Do not collapse
            // back to the original small candidate radius after a successful bypass.
            Active.Exterior.X = player.X; Active.Exterior.Z = player.Z;
            Active.Record.CandidatePoint = NavigationPoint.From(Active.Exterior);
            _lastBypassDirection = orbit.Direction; _lastRingRadius = orbit.Radius; _lastAngularSpan = orbit.AngularSpanDegrees;
            BypassExhausted = false;
            Cover(Active);
            _sectorScores[Active.Sector] = Math.Max(-12, _sectorScores[Active.Sector] - 4);
            _say($"Verified exterior arrival: mission={_missionId}, requested sector={Active.Sector}, actual bearing=" +
                $"{PositiveAngle(LocalRoutePlanner.Angle(player - _anchor)) * 180 / Math.PI:F1} deg, source={Active.Source}; FinalApproach still requires entry validation.");
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
        { _coveredRings.Add($"{_mode}:{attempt.Sector}:{attempt.Radius}"); }
        private static double PositiveAngle(double angle) => angle < 0 ? angle + Math.PI * 2 : angle;
        private static double AngleDelta(double angle)
        { while (angle > Math.PI) angle -= Math.PI * 2; while (angle < -Math.PI) angle += Math.PI * 2; return angle; }
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
            if (!_mission.Present || _mission.Completed ||
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
                        !x.Completed && x.PlayfieldId == _mission.PlayfieldId &&
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
