using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using AOSharp.Common.GameData;
using Newtonsoft.Json;

namespace RKmission
{
    // Plain managed diagnostics only. No native object pointers or inferred ownership.
    internal sealed class NavigationPoint
    {
        public float X, Y, Z;
        public static NavigationPoint From(Vector3 value) => new NavigationPoint { X = value.X, Y = value.Y, Z = value.Z };
        [JsonIgnore] public Vector3 Vector => new Vector3(X, Y, Z);
        [JsonIgnore] public bool Valid => AcceptedMissions.Finite(Vector);
    }

    internal sealed class NavigationRotation
    {
        public float X, Y, Z, W;
        public static NavigationRotation From(Quaternion value) =>
            new NavigationRotation { X = value.X, Y = value.Y, Z = value.Z, W = value.W };
    }

    internal sealed class EntranceAttemptRecord
    {
        public DateTime StartedUtc, FinishedUtc;
        public int Playfield, MissionId, Sector, UseCommands, CrossCommands;
        public bool ZoneObserved;
        public string Mode, Stage, Source, ElevationSource, DoorIdentity, DoorAssociation, Result, Reason;
        public float AngleDegrees, Radius, StartDistance, BestDistance, ProgressMetres, StallSeconds;
        public NavigationPoint Anchor, Origin, ExteriorVector, CandidatePoint, ApproachPoint, LastTarget, LastPosition, DoorPosition, DoorForward;
        public NavigationRotation DoorRotation;
        // Sector always remains the requested candidate. WallBearingSector is
        // observed geometry, including routes that never reached that candidate.
        public int WallBearingSector = -1, SectorCount, BypassDirection;
        public float WallBearingDegrees, WallRadius, ExteriorRingRadius, AngularSpanDegrees;
        public bool ExteriorReached, BypassSideReached;
        public string OverpassResult;
        public NavigationPoint EntryPoint, ReachedExteriorPoint;
        public NavigationPoint ExteriorSupportPoint;
        public float ExteriorSupportHeight;
        public string ExteriorSupportSource;
        public List<float> FailedEntryHeights = new List<float>();
        public string FlightStrategy;
        public List<FlightLegRecord> FlightLegs = new List<FlightLegRecord>();
    }

    internal sealed class FlightLegRecord
    {
        public DateTime FinishedUtc;
        public NavigationPoint Origin, Target, Position;
        public string Stage, Strategy, Result;
    }

    internal sealed class EntranceMemory
    {
        public string Key;
        public int Playfield;
        public NavigationPoint Anchor;
        public DateTime UpdatedUtc, LastSuccessUtc;
        public NavigationPoint LastSuccessVector, LastSuccessDoorPosition, LastSuccessDoorForward;
        public NavigationPoint LastSuccessEntryPoint;
        public string LastSuccessMode;
        public int LastSuccessSector = -1, LastBypassDirection, WallSectorCount;
        public float LastExteriorRingRadius, LastBypassAngularSpanDegrees;
        public List<int> FailedWallBearingSectors = new List<int>();
        public List<EntranceAttemptRecord> Attempts = new List<EntranceAttemptRecord>();
    }

    internal sealed class EntranceLearning
    {
        private sealed class Database
        {
            public int Version = 1;
            public List<EntranceMemory> Entrances = new List<EntranceMemory>();
        }
        private readonly string _path;
        private readonly Action<string> _say;
        private readonly OutdoorNavigationSettings _settings;
        private Database _data = new Database();
        private bool _writable = true;

        public EntranceLearning(string directory, OutdoorNavigationSettings settings, Action<string> say)
        {
            _path = Path.Combine(directory, "entrance-learning.json"); _settings = settings; _say = say;
            try
            {
                if (File.Exists(_path))
                {
                    _data = JsonConvert.DeserializeObject<Database>(File.ReadAllText(_path));
                    if (_data == null || _data.Version != 1 || _data.Entrances == null)
                        throw new InvalidDataException("unsupported or incomplete learning database");
                    _data.Entrances.RemoveAll(x => x == null || x.Anchor == null || !x.Anchor.Valid || x.Attempts == null);
                    Trim();
                }
            }
            catch (Exception ex)
            {
                _data = new Database(); _writable = false;
                _say("Entrance learning could not be loaded; original file preserved, session diagnostics remain in memory: " + ex.Message);
            }
            _say($"Entrance learning: file={_path}, entries={_data.Entrances.Count}, persistence={_writable}, " +
                $"bounds={settings.MaxEntrances} entrances/{settings.AttemptsPerEntrance} attempts each.");
        }

        public EntranceMemory For(int playfield, Vector3 anchor)
        {
            // Ignore altitude: stale/zero mission Y must not split the same outdoor entrance.
            string key = string.Format(CultureInfo.InvariantCulture, "{0}:{1}:{2}", playfield,
                Math.Round(anchor.X / 2, MidpointRounding.AwayFromZero), Math.Round(anchor.Z / 2, MidpointRounding.AwayFromZero));
            EntranceMemory memory = _data.Entrances.FirstOrDefault(x => x.Key == key && x.Playfield == playfield &&
                LocalRoutePlanner.HorizontalDistance(x.Anchor.Vector, anchor) <= 2);
            // Quantization boundaries may move between uploads. Never borrow a far-away entrance.
            if (memory == null) memory = _data.Entrances.Where(x => x.Playfield == playfield &&
                LocalRoutePlanner.HorizontalDistance(x.Anchor.Vector, anchor) <= 1)
                .OrderBy(x => LocalRoutePlanner.HorizontalDistance(x.Anchor.Vector, anchor)).FirstOrDefault();
            if (memory != null) return memory;
            memory = new EntranceMemory { Key = key, Playfield = playfield, Anchor = NavigationPoint.From(anchor), UpdatedUtc = DateTime.UtcNow };
            _data.Entrances.Add(memory); Trim(); return memory;
        }

        public void Record(EntranceMemory memory, EntranceAttemptRecord attempt, bool verifiedSuccess = false)
        {
            if (attempt == null) return;
            if (!memory.Attempts.Contains(attempt)) memory.Attempts.Add(attempt);
            memory.UpdatedUtc = DateTime.UtcNow;
            if (memory.FailedWallBearingSectors == null) memory.FailedWallBearingSectors = new List<int>();
            if (attempt.WallBearingSector >= 0 && attempt.SectorCount > 0)
            {
                if (memory.WallSectorCount != attempt.SectorCount)
                { memory.FailedWallBearingSectors.Clear(); memory.WallSectorCount = attempt.SectorCount; }
                if (!memory.FailedWallBearingSectors.Contains(attempt.WallBearingSector))
                    memory.FailedWallBearingSectors.Add(attempt.WallBearingSector);
            }
            if (verifiedSuccess && attempt.ExteriorVector?.Valid == true &&
                LocalRoutePlanner.HorizontalDistance(attempt.ExteriorVector.Vector, Vector3.Zero) > 0.1f)
            {
                memory.LastSuccessVector = attempt.ExteriorVector;
                memory.LastSuccessUtc = DateTime.UtcNow;
                memory.LastSuccessMode = attempt.Mode;
                memory.LastSuccessDoorPosition = attempt.DoorPosition;
                memory.LastSuccessDoorForward = attempt.DoorForward;
                memory.LastSuccessEntryPoint = attempt.EntryPoint;
                memory.LastSuccessSector = attempt.Sector;
                memory.LastBypassDirection = attempt.BypassDirection;
                memory.LastExteriorRingRadius = attempt.ExteriorRingRadius;
                memory.LastBypassAngularSpanDegrees = attempt.AngularSpanDegrees;
            }
            Trim(); Save();
        }

        private void Trim()
        {
            foreach (EntranceMemory entry in _data.Entrances)
                if (entry.Attempts.Count > _settings.AttemptsPerEntrance)
                    entry.Attempts.RemoveRange(0, entry.Attempts.Count - _settings.AttemptsPerEntrance);
            _data.Entrances = _data.Entrances.OrderByDescending(x => x.UpdatedUtc).Take(_settings.MaxEntrances).ToList();
        }

        private void Save()
        {
            if (!_writable) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                string temporary = _path + ".tmp";
                File.WriteAllText(temporary, JsonConvert.SerializeObject(_data, Formatting.Indented));
                if (File.Exists(_path)) File.Replace(temporary, _path, _path + ".bak");
                else File.Move(temporary, _path);
            }
            catch (Exception ex)
            {
                _writable = false;
                _say("Entrance diagnostics persistence failed; retaining session observations: " + ex.Message);
            }
        }
    }
}
