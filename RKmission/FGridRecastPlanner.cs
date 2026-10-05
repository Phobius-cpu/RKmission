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
                int repairs = 0;
                int failedSegment = 0;
                string segmentReason = "supported";
                for (int repair = 0; repair < 4 &&
                    !ValidateRaw(raw, start.Y, out failedSegment, out segmentReason); repair++)
                {
                    if (!segmentReason.EndsWith(" edge", StringComparison.Ordinal))
                    {
                        LastPath = Rejection(raw, failedSegment, segmentReason, repairs);
                        return false;
                    }
                    if (!TryRepairEdge(raw, failedSegment, start.Y, out List<Vector3> repaired,
                        out string repairReason))
                    {
                        LastPath = Rejection(raw, failedSegment, segmentReason, repairs) +
                            "; " + repairReason;
                        return false;
                    }
                    raw = repaired;
                    repairs++;
                }
                if (!ValidateRaw(raw, start.Y, out failedSegment, out segmentReason))
                { LastPath = Rejection(raw, failedSegment, segmentReason, repairs); return false; }
                List<Vector3> checkedPath = SimplifySupportedPath(raw, start.Y);
                if (!ValidateRaw(checkedPath, start.Y, out failedSegment, out segmentReason))
                { LastPath = Rejection(checkedPath, failedSegment, segmentReason, repairs); return false; }
                float length = 0;
                for (int i = 1; i < checkedPath.Count; i++)
                    length += Vector3.Distance(checkedPath[i - 1], checkedPath[i]);
                if (checkedPath.Count < 2 || checkedPath.Count > 2048 || length > 2000f)
                { LastPath = "rejected: excessive path length or waypoints"; return false; }
                waypoints = checkedPath;
                LastPath = $"valid complete floor-supported path, {length:0.0} m, " +
                    $"{raw.Count - 1} Recast leg(s) reduced to {checkedPath.Count - 1} walkway-supported leg(s)" +
                    (repairs > 0 ? $" ({repairs} edge repair(s) validated)" : "");
                return true;
            }
            catch (Exception ex) { LastPath = "query failed: " + ex.Message; return false; }
        }

        public bool TryFloorZeroLiftPath(Vector3 start, Vector3 lift,
            out List<Vector3> waypoints)
        {
            waypoints = null;
            // Retain the complete Recast corridor and endpoint checks, then require
            // the physical walkway itself to support one uninterrupted direct leg.
            if (!TryPath(start, lift, out _)) return false;
            if (!LocalRoutePlanner.SupportedFGridSegment(start, lift, start.Y,
                out string reason))
            {
                LastPath = "floor 0 direct lift leg rejected: " + reason;
                return false;
            }
            waypoints = new List<Vector3> { start, lift };
            LastPath = $"valid floor 0 direct lift walkway, {Vector3.Distance(start, lift):0.0} m, " +
                "1 walkway-supported leg";
            return true;
        }

        // Exit terminals can sit at a platform edge. The caller uses a terminal
        // from within 1.5 m, so a supported approach point is enough. This is
        // only for portal legs; lift paths still require the exact target.
        public bool TryPortalApproach(Vector3 start, Vector3 portal, out List<Vector3> waypoints)
        {
            if (TryPath(start, portal, out waypoints)) return true;
            string directFailure = LastPath;
            Vector3 toward = start - portal;
            toward.Y = 0;
            if (toward.Magnitude < 0.1f) toward = new Vector3(1, 0, 0);
            toward = toward.Normalize();
            foreach (int degrees in new[] { 0, 45, -45, 90, -90, 135, -135, 180 })
            {
                double radians = degrees * Math.PI / 180;
                float cosine = (float)Math.Cos(radians), sine = (float)Math.Sin(radians);
                Vector3 offset = new Vector3(toward.X * cosine - toward.Z * sine, 0,
                    toward.X * sine + toward.Z * cosine) * 1.2f;
                Vector3 approach = portal + offset;
                if (!LocalRoutePlanner.SupportedFGridSegment(approach, approach, start.Y)) continue;
                if (!TryPath(start, approach, out waypoints)) continue;
                LastPath += "; portal approach 1.2 m from terminal";
                return true;
            }
            LastPath = directFailure + "; no complete supported portal approach within 1.2 m";
            return false;
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

        // Recast supplies useful funnel corners, but every resulting movement leg is
        // independently checked against the live FGrid floor and walls. Choose the
        // minimum supported set so controller handoffs happen only at necessary turns.
        private static List<Vector3> SimplifySupportedPath(List<Vector3> points, float floorHeight)
        {
            if (points == null || points.Count < 3) return points == null
                ? new List<Vector3>() : new List<Vector3>(points);
            // A pathological mesh result should remain safe without performing an
            // unbounded number of scene raycasts on the update thread.
            if (points.Count > 128) return new List<Vector3>(points);
            int[] legs = Enumerable.Repeat(int.MaxValue, points.Count).ToArray();
            int[] previous = Enumerable.Repeat(-1, points.Count).ToArray();
            legs[0] = 0;
            for (int candidate = 1; candidate < points.Count; candidate++)
            {
                for (int anchor = 0; anchor < candidate; anchor++)
                {
                    if (legs[anchor] == int.MaxValue ||
                        legs[anchor] + 1 >= legs[candidate] ||
                        !LocalRoutePlanner.SupportedFGridSegment(
                            points[anchor], points[candidate], floorHeight)) continue;
                    legs[candidate] = legs[anchor] + 1;
                    previous[candidate] = anchor;
                }
            }
            if (previous[points.Count - 1] < 0) return new List<Vector3>(points);
            var simplified = new List<Vector3>();
            for (int at = points.Count - 1; at >= 0; at = previous[at])
            {
                simplified.Add(points[at]);
                if (at == 0) break;
            }
            simplified.Reverse();
            return simplified;
        }

        private static string Rejection(List<Vector3> points, int segment, string reason, int repairs)
        {
            Vector3 a = points[segment - 1], b = points[segment];
            return $"rejected segment {segment}/{points.Count - 1} after {repairs} edge repair(s): " +
                $"{reason}; leg ({a.X:0.00},{a.Z:0.00}) to ({b.X:0.00},{b.Z:0.00})";
        }

        // A funnel corner can skim a platform edge, or a straight funnel leg can
        // cross a small gap. Try nearby corners and short doglegs, then require
        // the entire revised route to pass the unchanged clearance test.
        private static bool TryRepairEdge(List<Vector3> points, int segment, float floorHeight,
            out List<Vector3> repaired, out string reason)
        {
            repaired = new List<Vector3>();
            reason = "no nearby supported detour";
            if (points.Count > 16 || segment < 1 || segment >= points.Count)
            { reason = "repair vertex limit reached"; return false; }
            float legLength = LocalRoutePlanner.HorizontalDistance(points[segment - 1], points[segment]);
            if (legLength > 12f)
            { reason = $"{legLength:0.0} m leg exceeds 12 m repair limit"; return false; }
            float routeLength = 0;
            for (int i = 1; i < points.Count; i++)
                routeLength += LocalRoutePlanner.HorizontalDistance(points[i - 1], points[i]);
            if (routeLength > 60f)
            { reason = "60 m repair route limit reached"; return false; }
            Vector3 travel = points[segment] - points[segment - 1];
            Vector3 side = new Vector3(-travel.Z, 0, travel.X);
            if (side.Magnitude < 0.1f) return false;
            side = side.Normalize();
            int bestProgress = segment;
            foreach (float offset in new[] { 0.35f, -0.35f, 0.7f, -0.7f, 1.05f, -1.05f })
            {
                for (int mode = 0; mode < 3; mode++)
                {
                    var candidate = new List<Vector3>(points);
                    if ((mode == 0 || mode == 1) && segment - 1 > 0)
                        candidate[segment - 1] += side * offset;
                    if ((mode == 0 || mode == 2) && segment < candidate.Count - 1)
                        candidate[segment] += side * offset;
                    if (ConsiderRepair(candidate, floorHeight, ref bestProgress, ref repaired)) return true;
                }
            }
            // The original segment endpoints stay fixed. Insert one bend or a
            // parallel two-corner dogleg so the path can go around a void.
            foreach (float offset in new[] { 0.6f, -0.6f, 1.2f, -1.2f, 1.8f, -1.8f,
                2.4f, -2.4f, 3.2f, -3.2f })
            {
                Vector3 a = points[segment - 1], b = points[segment];
                if (points.Count < 16)
                {
                    var oneCorner = new List<Vector3>(points);
                    oneCorner.Insert(segment, a + travel * 0.5f + side * offset);
                    if (ConsiderRepair(oneCorner, floorHeight, ref bestProgress, ref repaired)) return true;
                }
                if (points.Count < 15)
                {
                    var twoCorners = new List<Vector3>(points);
                    twoCorners.Insert(segment, a + travel * 0.25f + side * offset);
                    twoCorners.Insert(segment + 1, a + travel * 0.75f + side * offset);
                    if (ConsiderRepair(twoCorners, floorHeight, ref bestProgress, ref repaired)) return true;
                }
            }
            return bestProgress > segment;
        }

        private static bool ConsiderRepair(List<Vector3> candidate, float floorHeight,
            ref int bestProgress, ref List<Vector3> repaired)
        {
            if (candidate.Count > 16) return false;
            float length = 0;
            for (int i = 1; i < candidate.Count; i++)
                length += LocalRoutePlanner.HorizontalDistance(candidate[i - 1], candidate[i]);
            if (length > 60f) return false;
            if (ValidateRaw(candidate, floorHeight, out int nextFailure, out _))
            { repaired = candidate; return true; }
            if (nextFailure > bestProgress)
            { bestProgress = nextFailure; repaired = candidate; }
            return false;
        }

        private static Vector3 ToAO(CVector point) => new Vector3(point.x, point.y, point.z);
    }
}
