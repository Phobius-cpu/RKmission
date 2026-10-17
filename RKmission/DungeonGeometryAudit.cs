using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AOSharp.Common.GameData;
using AOSharp.Common.Unmanaged.DbObjects;
using AOSharp.Core;
using AOSharp.Pathfinding;
using MalisDungeonMap2;
using Newtonsoft.Json;
using SharpNav;
using SharpNav.Pathfinding;
using SVector = SharpNav.Geometry.Vector3;
using IOPath = System.IO.Path;

namespace RKmission
{
    // Explicit, read-only evidence collection for multi-level rooms. It never
    // submits movement or changes the generated navmesh.
    internal sealed class DungeonGeometryAudit
    {
        private readonly string _directory;
        private readonly Action<string> _say;
        private readonly NavigationRouteRecorder _routes;

        internal sealed class Report
        {
            public int SchemaVersion = 1;
            public DateTime TimestampUtc;
            public int Playfield, RoomInstance, Floor;
            public string RoomName, Fingerprint;
            public float[] PlayerPosition, RoomPosition, RoomCenter, TemplatePosition, LocalRect;
            public float RoomRotationDegrees, TemplateRotationDegrees, YOffset;
            public GeometrySummary SurfaceResource, MaliTerrain;
            public NavSummary GeneratedNavMesh;
            public List<DungeonRoomCapture.DoorEvidence> Doors;
            public RouteProbe RecordedTopologyProbe;
            public List<string> Failures = new List<string>();
        }
        internal sealed class GeometrySummary
        {
            public int MeshCount, VertexCount, TriangleCount;
            public float? MinimumY, MaximumY, VerticalSpan;
            public Dictionary<string, int> SlopeBands = new Dictionary<string, int>();
            public float? TileSize, HeightmapScale;
        }
        internal sealed class NavSummary
        {
            public bool PathfinderLoaded, PlayerNearestPoly;
            public float? PlayerSnapDistance;
            public string Note;
        }
        internal sealed class RouteProbe
        {
            public string RouteName;
            public int PointCount;
            public List<PointProbe> Points = new List<PointProbe>();
            public int? FirstNavGapIndex, LongestNavGapStart, LongestNavGapLength;
        }
        internal sealed class PointProbe
        {
            public int Index;
            public float[] Position;
            public bool SurfaceSupported, TerrainSupported, NavmeshSnapped;
            public float? SurfaceVerticalError, TerrainVerticalError, NavmeshSnapDistance;
            public bool? PathFromPrevious, PathFromPlayer;
            public string NavmeshFailure;
        }
        internal sealed class Triangle
        {
            public Vector3 A, B, C;
        }

        public DungeonGeometryAudit(string pluginDir, Action<string> say,
            NavigationRouteRecorder routes)
        {
            _directory = IOPath.Combine(pluginDir, "RKMissionData", "diagnostics");
            _say = say;
            _routes = routes;
        }

        public void AuditCurrentRoom()
        {
            try
            {
                DungeonRoomCapture.Snapshot capture = DungeonRoomCapture.ReadCurrent();
                Room room = DynelManager.LocalPlayer.Room;
                var report = new Report
                {
                    TimestampUtc = DateTime.UtcNow,
                    Playfield = capture.Playfield,
                    RoomInstance = capture.RoomInstance,
                    Floor = capture.Floor,
                    RoomName = capture.RoomName,
                    Fingerprint = capture.CandidateFingerprint,
                    PlayerPosition = DungeonRoomCapture.P(DynelManager.LocalPlayer.Position),
                    RoomPosition = capture.Position,
                    RoomCenter = capture.Center,
                    TemplatePosition = capture.TemplatePosition,
                    LocalRect = capture.LocalRect,
                    RoomRotationDegrees = capture.RotationDegrees,
                    TemplateRotationDegrees = capture.TemplateRotationDegrees,
                    YOffset = capture.YOffset,
                    Doors = capture.Doors
                };
                List<Triangle> surfaces = SurfaceTriangles(capture, report.Failures);
                report.SurfaceResource = Summarize(surfaces, capture.Surfaces.Count,
                    capture.Surfaces.Sum(x => x.WorldVertices?.Count ?? 0));

                List<Triangle> terrain = TerrainTriangles(room, report.Failures);
                report.MaliTerrain = Summarize(terrain, terrain.Count > 0 ? 1 : 0,
                    terrain.Count * 3);
                report.MaliTerrain.TileSize = capture.TileSize;
                report.MaliTerrain.HeightmapScale = capture.HeightmapScale;

                NavMeshQuery query = null;
                report.GeneratedNavMesh = CreateNavSummary(DynelManager.LocalPlayer.Position,
                    out query, report.Failures);
                NavigationRouteRecorder.AuditRoute route = _routes.FindCurrentRoomAuditRoute(capture);
                if (route != null)
                    report.RecordedTopologyProbe = ProbeRoute(route, surfaces, terrain, query);
                else report.Failures.Add("No matching validated recorded-topology reference route is available for this room fingerprint.");

                Directory.CreateDirectory(_directory);
                string path = IOPath.Combine(_directory, "room-audit-" +
                    report.TimestampUtc.ToString("yyyyMMdd-HHmmss") + "-" +
                    report.RoomInstance + ".json");
                File.WriteAllText(path, JsonConvert.SerializeObject(report, Formatting.Indented));
                string routeSummary = report.RecordedTopologyProbe == null ? "no matching reference route" :
                    $"route='{report.RecordedTopologyProbe.RouteName}', points={report.RecordedTopologyProbe.PointCount}, " +
                    $"first nav gap={Format(report.RecordedTopologyProbe.FirstNavGapIndex)}, " +
                    $"longest nav gap={Format(report.RecordedTopologyProbe.LongestNavGapStart)}/" +
                    $"{Format(report.RecordedTopologyProbe.LongestNavGapLength)}";
                _say($"Room geometry audit complete: room={report.RoomInstance} '{report.RoomName}', " +
                    $"fingerprint={report.Fingerprint}, surface={report.SurfaceResource.TriangleCount} tris/" +
                    $"{report.SurfaceResource.VerticalSpan:0.00}m span, terrain={report.MaliTerrain.TriangleCount} tris/" +
                    $"{report.MaliTerrain.VerticalSpan:0.00}m span, navmesh loaded={report.GeneratedNavMesh.PathfinderLoaded}, " +
                    $"{routeSummary}. JSON: {path}");
            }
            catch (Exception ex)
            {
                _say("Room geometry audit failed without changing navigation: " + ex.Message);
            }
        }

        private static string Format(int? value) => value.HasValue ? value.Value.ToString() : "none";

        private static List<Triangle> SurfaceTriangles(DungeonRoomCapture.Snapshot capture,
            List<string> failures)
        {
            var result = new List<Triangle>();
            foreach (DungeonRoomCapture.Surface mesh in capture.Surfaces)
            {
                if (mesh.WorldVertices == null)
                { failures.Add("A SurfaceResource mesh did not expose transformed world vertices."); continue; }
                for (int i = 0; i + 2 < mesh.Triangles.Count; i += 3)
                {
                    int a = mesh.Triangles[i], b = mesh.Triangles[i + 1], c = mesh.Triangles[i + 2];
                    if (a < 0 || b < 0 || c < 0 || a >= mesh.WorldVertices.Count ||
                        b >= mesh.WorldVertices.Count || c >= mesh.WorldVertices.Count) continue;
                    result.Add(new Triangle { A = V(mesh.WorldVertices[a]),
                        B = V(mesh.WorldVertices[b]), C = V(mesh.WorldVertices[c]) });
                }
            }
            return result;
        }

        private static List<Triangle> TerrainTriangles(Room room, List<string> failures)
        {
            var result = new List<Triangle>();
            try
            {
                var tilemap = Playfield.RDBTilemap as DungeonRDBTilemap;
                if (tilemap == null) { failures.Add("Dungeon tilemap is unavailable."); return result; }
                foreach (var triangle in DungeonTerrainHeight.CreateMesh(room, tilemap))
                    result.Add(new Triangle {
                        A = new Vector3(triangle.A.X, triangle.A.Y, triangle.A.Z),
                        B = new Vector3(triangle.B.X, triangle.B.Y, triangle.B.Z),
                        C = new Vector3(triangle.C.X, triangle.C.Y, triangle.C.Z) });
            }
            catch (Exception ex) { failures.Add("Mali terrain mesh unavailable: " + ex.Message); }
            return result;
        }

        private static GeometrySummary Summarize(List<Triangle> triangles, int meshes, int vertices)
        {
            var summary = new GeometrySummary { MeshCount = meshes, VertexCount = vertices,
                TriangleCount = triangles.Count };
            summary.SlopeBands["0-20"] = 0; summary.SlopeBands["20-35"] = 0;
            summary.SlopeBands["35-50"] = 0; summary.SlopeBands["50-90"] = 0;
            if (triangles.Count == 0) return summary;
            summary.MinimumY = triangles.Min(x => Math.Min(x.A.Y, Math.Min(x.B.Y, x.C.Y)));
            summary.MaximumY = triangles.Max(x => Math.Max(x.A.Y, Math.Max(x.B.Y, x.C.Y)));
            summary.VerticalSpan = summary.MaximumY - summary.MinimumY;
            foreach (Triangle triangle in triangles)
            {
                double ux = triangle.B.X - triangle.A.X, uy = triangle.B.Y - triangle.A.Y, uz = triangle.B.Z - triangle.A.Z;
                double vx = triangle.C.X - triangle.A.X, vy = triangle.C.Y - triangle.A.Y, vz = triangle.C.Z - triangle.A.Z;
                double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
                double length = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                double slope = length < 0.00001 ? 90 : Math.Acos(Math.Min(1, Math.Abs(ny) / length)) * 180 / Math.PI;
                string band = slope < 20 ? "0-20" : slope < 35 ? "20-35" : slope < 50 ? "35-50" : "50-90";
                summary.SlopeBands[band]++;
            }
            return summary;
        }

        private static NavSummary CreateNavSummary(Vector3 player, out NavMeshQuery query,
            List<string> failures)
        {
            query = null;
            var result = new NavSummary { PathfinderLoaded = SMovementController.NavAgent?.HasPathfinder == true };
            if (!result.PathfinderLoaded) { result.Note = "SMovementController pathfinder is not loaded."; return result; }
            try
            {
                query = new NavMeshQuery(SMovementController.NavAgent.NavMesh, 8192);
                result.PlayerNearestPoly = TrySnap(query, player, out _, out float distance, out _);
                if (result.PlayerNearestPoly) result.PlayerSnapDistance = distance;
            }
            catch (Exception ex) { failures.Add("Generated navmesh query unavailable: " + ex.Message); query = null; }
            return result;
        }

        private static RouteProbe ProbeRoute(NavigationRouteRecorder.AuditRoute route,
            List<Triangle> surfaces, List<Triangle> terrain, NavMeshQuery query)
        {
            var result = new RouteProbe { RouteName = route.Name, PointCount = route.Points.Count };
            NavPoint previous = default(NavPoint); bool previousSnapped = false;
            Vector3 player = DynelManager.LocalPlayer.Position;
            NavPoint playerNav = default(NavPoint); bool playerSnapped = query != null &&
                TrySnap(query, player, out playerNav, out _, out _);
            foreach (var pair in route.Points.Select((point, index) => new { point, index }))
            {
                var probe = new PointProbe { Index = pair.index, Position = DungeonRoomCapture.P(pair.point) };
                probe.SurfaceSupported = TrySupport(surfaces, pair.point, out float surfaceError);
                if (probe.SurfaceSupported) probe.SurfaceVerticalError = surfaceError;
                probe.TerrainSupported = TrySupport(terrain, pair.point, out float terrainError);
                if (probe.TerrainSupported) probe.TerrainVerticalError = terrainError;
                if (query == null) probe.NavmeshFailure = "pathfinder unavailable";
                else
                {
                    probe.NavmeshSnapped = TrySnap(query, pair.point, out NavPoint nav,
                        out float snapDistance, out string failure);
                    if (probe.NavmeshSnapped)
                    {
                        probe.NavmeshSnapDistance = snapDistance;
                        probe.PathFromPlayer = playerSnapped && HasCompletePath(query, playerNav, nav);
                        probe.PathFromPrevious = pair.index == 0 ? (bool?)null :
                            previousSnapped && HasCompletePath(query, previous, nav);
                    }
                    else probe.NavmeshFailure = failure;
                    previous = nav; previousSnapped = probe.NavmeshSnapped;
                }
                result.Points.Add(probe);
            }
            CalculateGaps(result);
            return result;
        }

        private static void CalculateGaps(RouteProbe route)
        {
            int runStart = -1, bestStart = -1, bestLength = 0;
            for (int i = 0; i < route.Points.Count; i++)
            {
                PointProbe point = route.Points[i];
                bool gap = !point.NavmeshSnapped || point.PathFromPrevious == false;
                if (gap && !route.FirstNavGapIndex.HasValue) route.FirstNavGapIndex = i;
                if (gap && runStart < 0) runStart = i;
                if (!gap && runStart >= 0)
                { if (i - runStart > bestLength) { bestStart = runStart; bestLength = i - runStart; } runStart = -1; }
            }
            if (runStart >= 0 && route.Points.Count - runStart > bestLength)
            { bestStart = runStart; bestLength = route.Points.Count - runStart; }
            if (bestLength > 0) { route.LongestNavGapStart = bestStart; route.LongestNavGapLength = bestLength; }
        }

        private static bool TrySnap(NavMeshQuery query, Vector3 point, out NavPoint nav,
            out float distance, out string failure)
        {
            nav = default(NavPoint); distance = float.PositiveInfinity; failure = null;
            SVector input = new SVector(point.X, point.Y, point.Z), extents = new SVector(3, 3, 3);
            if (!query.FindNearestPoly(ref input, ref extents, out nav) || nav.Polygon == NavPolyId.Null)
            { failure = "nearest polygon not found within 3m extents"; return false; }
            var snapped = new Vector3(nav.Position.X, nav.Position.Y, nav.Position.Z);
            distance = Vector3.Distance(point, snapped);
            return true;
        }

        private static bool HasCompletePath(NavMeshQuery query, NavPoint from, NavPoint to)
        {
            var path = new SharpNav.Pathfinding.Path();
            return query.FindPath(ref from, ref to, new NavQueryFilter(), path) &&
                path.Count > 0 && path[path.Count - 1] == to.Polygon;
        }

        internal static bool TrySupport(List<Triangle> triangles, Vector3 point, out float verticalError)
        {
            verticalError = float.PositiveInfinity;
            foreach (Triangle triangle in triangles)
            {
                float denominator = (triangle.B.Z - triangle.C.Z) * (triangle.A.X - triangle.C.X) +
                    (triangle.C.X - triangle.B.X) * (triangle.A.Z - triangle.C.Z);
                if (Math.Abs(denominator) < 0.00001f) continue;
                float a = ((triangle.B.Z - triangle.C.Z) * (point.X - triangle.C.X) +
                    (triangle.C.X - triangle.B.X) * (point.Z - triangle.C.Z)) / denominator;
                float b = ((triangle.C.Z - triangle.A.Z) * (point.X - triangle.C.X) +
                    (triangle.A.X - triangle.C.X) * (point.Z - triangle.C.Z)) / denominator;
                float c = 1 - a - b;
                if (a < -0.03f || b < -0.03f || c < -0.03f) continue;
                float y = a * triangle.A.Y + b * triangle.B.Y + c * triangle.C.Y;
                verticalError = Math.Min(verticalError, Math.Abs(y - point.Y));
            }
            return verticalError <= 1.25f;
        }

        private static Vector3 V(float[] point) => new Vector3(point[0], point[1], point[2]);
    }
}
