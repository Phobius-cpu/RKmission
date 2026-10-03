using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading.Tasks;
using AOSharp.Common.GameData;
using AOSharp.Core;
using AOSharp.Recast;
using org.critterai.nav;
using org.critterai.nmgen;
using CVector = org.critterai.Vector3;

namespace RKmission
{
    // CritterAI is only a source of candidate waypoints. Movement remains with
    // MovementArbiter and the installed SMovementController.
    internal sealed class FGridRecastPlanner
    {
        private readonly string _file;
        private Navmesh _mesh;
        private Task<Navmesh> _bake;
        private bool _loadAttempted;
        private bool _bakeAttempted;
        public string Status { get; private set; } = "not started";
        public string LastPath { get; private set; } = "not queried";
        public bool Pending => _bake != null && !_bake.IsCompleted;
        public bool Available => _mesh != null;
        public bool FileExists => File.Exists(_file);

        public FGridRecastPlanner(string pluginDir)
        {
            _file = Path.Combine(pluginDir, "NavMeshes", "4107.Navmesh");
        }

        public void EnsureReady()
        {
            if (Playfield.ModelIdentity.Instance != (int)PlayfieldId.FixerGrid ||
                !Playfield.IsDungeon || Game.IsZoning) return;
            if (_mesh != null) return;
            if (_bake != null)
            {
                if (!_bake.IsCompleted) return;
                if (_bake.IsFaulted || _bake.IsCanceled)
                    Status = "bake failed: " + (_bake.Exception?.GetBaseException().Message ?? "cancelled");
                else if (_bake.Result == null) Status = "bake returned no mesh";
                else
                {
                    _mesh = _bake.Result;
                    Status = "baked in memory (never saved)";
                }
                _bake = null;
                return;
            }
            if (!_loadAttempted)
            {
                _loadAttempted = true;
                if (FileExists)
                {
                    try
                    {
                        // The legacy AOSharp format is a BinaryFormatter byte[];
                        // read only this explicitly named local file, never .nav.
                        using (var stream = new FileStream(_file, FileMode.Open, FileAccess.Read, FileShare.Read))
                        {
                            if (stream.Length > 64 * 1024 * 1024) throw new InvalidDataException("mesh exceeds 64 MiB");
#pragma warning disable SYSLIB0011
                            var data = new BinaryFormatter().Deserialize(stream) as byte[];
#pragma warning restore SYSLIB0011
                            if (data == null || data.Length == 0) throw new InvalidDataException("mesh payload is empty");
                            NavStatus result = Navmesh.Create(data, out _mesh);
                            if (NavUtil.Failed(result) || _mesh == null) throw new InvalidDataException("CritterAI rejected mesh: " + result);
                        }
                        Status = "loaded 4107.Navmesh (read only)";
                        return;
                    }
                    catch (Exception ex) { Status = "load failed: " + ex.Message + "; baking in memory"; }
                }
            }
            if (_bakeAttempted) return;
            _bakeAttempted = true;
            // Use the parameters from the helper that successfully baked 4107.
            var settings = new NMGenParams
            {
                xzCellSize = 0.2f, yCellSize = 0.1f, walkableSlope = 45f,
                walkableHeight = 19, walkableStep = 3, walkableRadius = 2,
                maxEdgeLength = 20, edgeMaxDeviation = 4f,
                minRegionArea = 400, mergeRegionArea = 75,
                maxVertsPerPoly = 6, detailSampleDistance = 6f,
                detailMaxDeviation = 1f,
                contourOptions = ContourBuildFlags.TessellateWallEdges | ContourBuildFlags.TessellateAreaEdges,
                useMonotone = false, tileSize = 512, borderSize = 16
            };
            try { _bake = NavmeshGenerator.BakeAsync(settings); Status = "baking in memory"; }
            catch (Exception ex) { Status = "bake start failed: " + ex.Message; }
        }

        public bool TryPath(Vector3 start, Vector3 goal, out List<Vector3> waypoints)
        {
            waypoints = null;
            EnsureReady();
            if (_mesh == null) { LastPath = Pending ? "waiting for bake" : Status; return false; }
            if (!AcceptedMissions.Finite(start) || !AcceptedMissions.Finite(goal) ||
                Math.Abs(start.Y - goal.Y) > 2f)
            { LastPath = "invalid or different-floor endpoints"; return false; }
            try
            {
                if (NavUtil.Failed(NavmeshQuery.Create(_mesh, 8192, out NavmeshQuery query)))
                { LastPath = "query creation failed"; return false; }
                var filter = new NavmeshQueryFilter();
                var extents = new CVector(0.8f, 1.5f, 0.8f);
                if (NavUtil.Failed(query.GetNearestPoint(new CVector(start.X, start.Y, start.Z), extents, filter, out NavmeshPoint first)) ||
                    NavUtil.Failed(query.GetNearestPoint(new CVector(goal.X, goal.Y, goal.Z), extents, filter, out NavmeshPoint last)) ||
                    first.polyRef == 0 || last.polyRef == 0 ||
                    Vector3.Distance(start, ToAO(first.point)) > 1f ||
                    Vector3.Distance(goal, ToAO(last.point)) > 1f)
                { LastPath = "endpoint does not snap to the same-floor mesh"; return false; }
                uint[] corridor = new uint[8192];
                int count;
                if (first.polyRef == last.polyRef) { corridor[0] = first.polyRef; count = 1; }
                else if (NavUtil.Failed(query.FindPath(first, last, filter, corridor, out count)) || count < 1 ||
                         count > corridor.Length || corridor[count - 1] != last.polyRef)
                { LastPath = "incomplete polygon corridor"; return false; }
                CVector[] straight = new CVector[8192];
                if (NavUtil.Failed(query.GetStraightPath(first.point, last.point, corridor, 0, count,
                    straight, null, null, out int vertices)) || vertices < 1 || vertices > straight.Length)
                { LastPath = "straight path query failed"; return false; }
                var raw = new List<Vector3> { start };
                raw.AddRange(straight.Take(vertices).Select(ToAO));
                raw.Add(goal);
                bool inset = false;
                if (!ValidateRaw(raw, start.Y, out int failedSegment, out string segmentReason))
                {
                    if (!segmentReason.EndsWith(" edge", StringComparison.Ordinal) ||
                        !TryInsetEdge(raw, failedSegment, start.Y, out List<Vector3> repaired))
                    {
                        LastPath = $"rejected segment {failedSegment}/{raw.Count - 1}: {segmentReason}";
                        return false;
                    }
                    raw = repaired;
                    inset = true;
                }
                var checkedPath = new List<Vector3> { start };
                float length = 0;
                for (int i = 1; i < raw.Count; i++)
                {
                    Vector3 a = raw[i - 1], b = raw[i];
                    float segment = Vector3.Distance(a, b);
                    length += segment;
                    // Issue meaningful strides along the fully checked segment.
                    int pieces = Math.Max(1, (int)Math.Ceiling(segment / 4f));
                    for (int j = 1; j <= pieces; j++) checkedPath.Add(a + (b - a) * (j / (float)pieces));
                }
                if (checkedPath.Count < 2 || checkedPath.Count > 2048 || length > 2000f)
                { LastPath = "rejected: excessive path length or waypoints"; return false; }
                waypoints = checkedPath;
                LastPath = $"valid complete floor-supported path, {length:0.0} m, {checkedPath.Count - 1} legs" +
                    (inset ? " (edge inset validated)" : "");
                return true;
            }
            catch (Exception ex) { LastPath = "query failed: " + ex.Message; return false; }
        }

        private static bool ValidateRaw(List<Vector3> points, float floorHeight,
            out int failedSegment, out string reason)
        {
            for (int i = 1; i < points.Count; i++)
                if (!LocalRoutePlanner.SupportedFGridSegment(points[i - 1], points[i], floorHeight, out reason))
                { failedSegment = i; return false; }
            failedSegment = 0;
            reason = "supported";
            return true;
        }

        // A funnel corner can skim a platform edge even when a small inset is
        // walkable. Never relax the clearance test: move only internal vertices
        // and accept a candidate only after validating every revised segment.
        private static bool TryInsetEdge(List<Vector3> points, int segment, float floorHeight,
            out List<Vector3> repaired)
        {
            repaired = new List<Vector3>();
            if (points.Count > 32 || segment < 1 || segment >= points.Count ||
                LocalRoutePlanner.HorizontalDistance(points[segment - 1], points[segment]) > 8f)
                return false;
            float routeLength = 0;
            for (int i = 1; i < points.Count; i++)
                routeLength += LocalRoutePlanner.HorizontalDistance(points[i - 1], points[i]);
            if (routeLength > 120f) return false;
            Vector3 travel = points[segment] - points[segment - 1];
            Vector3 side = new Vector3(-travel.Z, 0, travel.X);
            if (side.Magnitude < 0.1f) return false;
            side = side.Normalize();
            foreach (float offset in new[] { 0.35f, -0.35f, 0.7f, -0.7f, 1.05f, -1.05f })
            {
                var candidate = new List<Vector3>(points);
                if (segment - 1 > 0) candidate[segment - 1] += side * offset;
                if (segment < candidate.Count - 1) candidate[segment] += side * offset;
                if (ValidateRaw(candidate, floorHeight, out _, out _))
                { repaired = candidate; return true; }
            }
            return false;
        }

        private static Vector3 ToAO(CVector point) => new Vector3(point.x, point.y, point.z);
    }
}
