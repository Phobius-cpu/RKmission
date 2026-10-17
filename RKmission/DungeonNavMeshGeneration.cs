using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AOSharp.Common.GameData;
using AOSharp.Core;
using AOSharp.Pathfinding;
using SharpNav;
using SharpNav.Geometry;
using SharpNav.IO.Json;
using SharpNav.Pathfinding;
using Vector3 = AOSharp.Common.GameData.Vector3;
using SVector = SharpNav.Geometry.Vector3;
using IOPath = System.IO.Path;

namespace RKmission
{
    /// <summary>
    /// Extends AOSharp's normal per-room SharpNav bake with narrow, recorded and
    /// fingerprint-gated ramp surfaces. AO's tilemap has only one height per X/Z
    /// cell; the room SurfaceResource is the authority for stacked geometry.
    /// </summary>
    internal sealed class DungeonNavMeshGeneration
    {
        private const string CacheVersion = "surface-ramp-v1";
        private const float RampVerticalStep = 0.15f;
        private const float RampHalfWidth = 0.85f;
        private readonly string _cacheDirectory;
        private readonly NavigationRouteRecorder _routes;
        private readonly Action<string> _trace;

        internal DungeonNavMeshGeneration(string pluginDir,
            NavigationRouteRecorder routes, Action<string> trace)
        {
            _cacheDirectory = IOPath.Combine(pluginDir, "RKMissionData", "NavTiles", CacheVersion);
            _routes = routes;
            _trace = trace ?? (_ => { });
        }

        internal Task<NavMesh[]> GenerateAsync() => Task.Run(Generate);

        private NavMesh[] Generate()
        {
            Directory.CreateDirectory(_cacheDirectory);
            NavMeshGenerationSettings settings = NavMeshGenerationSettings.HighDensity;
            int augmented = 0;
            var probes = new List<Tuple<Room, NavigationRouteRecorder.AuditRoute>>();
            foreach (Room room in Playfield.Rooms)
            {
                DungeonRoomCapture.Snapshot capture;
                try { capture = DungeonRoomCapture.ReadRoom(room); }
                catch (Exception ex)
                {
                    _trace($"3D navmesh: room {room.Instance} geometry inspection failed: {ex.Message}");
                    continue;
                }
                NavigationRouteRecorder.AuditRoute route = _routes?.FindReusableRoomAuditRoute(capture);
                if (route == null || route.Points.Count < 2) continue;
                List<Triangle3> corridor = BuildRampCorridor(route.Points);
                if (corridor.Count == 0) continue;
                string name = !string.IsNullOrEmpty(room.Name) ? room.Name :
                    $"{(int)Playfield.ModelId}_{room.Instance}";
                string tilePath = IOPath.Combine(_cacheDirectory, name + ".NavTile");
                try
                {
                    // Never retain a formerly augmented tile after a failed rebuild.
                    if (File.Exists(tilePath)) File.Delete(tilePath);
                    List<Triangle3> triangles = TerrainData.GetTriGeometry(room)
                        .Select(x => RoomLocal(x, room)).ToList();
                    triangles.AddRange(corridor.Select(x => RoomLocal(x, room)));
                    SNavMeshGenerator.GeneratePolyMesh(triangles, settings,
                        out PolyMesh polyMesh, out PolyMeshDetail detailMesh);
                    var tile = new RoomTile { PolyMesh = polyMesh, DetailMesh = detailMesh,
                        Settings = settings };
                    tile.FilterPolyMesh(room);
                    new NavMeshSerializer().Serialize(tilePath, polyMesh, detailMesh, settings);
                    augmented++;
                    probes.Add(Tuple.Create(room, route));
                    _trace($"3D navmesh: augmented room {room.Instance} '{room.Name}' fingerprint=" +
                        $"{capture.CandidateFingerprint}, route='{route.Name}', ramp triangles={corridor.Count}.");
                }
                catch (Exception ex)
                {
                    // The stock factory below remains the compatibility fallback.
                    try { if (File.Exists(tilePath)) File.Delete(tilePath); }
                    catch (Exception cleanupEx)
                    { _trace($"3D navmesh: failed augmented tile cleanup for '{room.Name}': {cleanupEx.Message}"); }
                    _trace($"3D navmesh: augmentation failed for room {room.Instance} '{room.Name}': {ex.Message}; stock tile will be used.");
                }
            }
            _trace($"3D navmesh: preparing AOSharp DungeonNavMeshFactory with {augmented} fingerprint-gated augmented room tile(s).");
            NavMesh[] meshes = new DungeonNavMeshFactory(_cacheDirectory, settings).GenerateNavMesh();
            foreach (var probe in probes)
                ReportConnectivity(meshes, probe.Item1, probe.Item2);
            return meshes;
        }

        private void ReportConnectivity(NavMesh[] meshes, Room room,
            NavigationRouteRecorder.AuditRoute route)
        {
            int floor = Math.Abs(room.Floor);
            if (meshes == null || floor >= meshes.Length || meshes[floor] == null)
            {
                _trace($"3D navmesh: room {room.Instance} ramp connectivity unavailable; floor mesh {floor} is missing.");
                return;
            }
            try
            {
                var query = new NavMeshQuery(meshes[floor], 8192);
                var gaps = new List<int>();
                for (int i = 0; i + 1 < route.Points.Count; i++)
                    if (!CompleteLeg(query, route.Points[i], route.Points[i + 1])) gaps.Add(i + 1);
                _trace(gaps.Count == 0
                    ? $"3D navmesh: room {room.Instance} route '{route.Name}' is connected across all {route.Points.Count - 1} legs."
                    : $"3D navmesh: room {room.Instance} route '{route.Name}' remains disconnected at leg(s) {string.Join(",", gaps)}; recorded dungeon movement remains available.");
            }
            catch (Exception ex)
            { _trace($"3D navmesh: room {room.Instance} connectivity probe failed: {ex.Message}"); }
        }

        private static bool CompleteLeg(NavMeshQuery query, Vector3 from, Vector3 to)
        {
            SVector extents = new SVector(3, 3, 3), a = from.ToSharpNav(), b = to.ToSharpNav();
            if (!query.FindNearestPoly(ref a, ref extents, out NavPoint start) ||
                !query.FindNearestPoly(ref b, ref extents, out NavPoint end) ||
                start.Polygon == NavPolyId.Null || end.Polygon == NavPolyId.Null) return false;
            var path = new SharpNav.Pathfinding.Path();
            return query.FindPath(ref start, ref end, new NavQueryFilter(), path) &&
                path.Count > 0 && path[path.Count - 1] == end.Polygon;
        }

        // Only sloped runs and one attachment leg on either side are overlaid.
        // This preserves ordinary floor filtering and does not invent exploration
        // targets or a second pathfinder.
        internal static List<Triangle3> BuildRampCorridor(IReadOnlyList<Vector3> points)
        {
            var selected = new HashSet<int>();
            for (int i = 0; i + 1 < points.Count; i++)
            {
                if (Math.Abs(points[i + 1].Y - points[i].Y) < RampVerticalStep) continue;
                selected.Add(Math.Max(0, i - 1));
                selected.Add(i);
                selected.Add(Math.Min(points.Count - 2, i + 1));
            }
            var result = new List<Triangle3>();
            foreach (int i in selected.OrderBy(x => x))
            {
                Vector3 from = points[i], to = points[i + 1];
                Vector3 heading = to - from;
                heading.Y = 0;
                if (heading.Magnitude < 0.05f) continue;
                heading = heading.Normalize();
                Vector3 side = new Vector3(-heading.Z, 0, heading.X) * RampHalfWidth;
                Vector3 a = from - side, b = from + side, c = to + side, d = to - side;
                result.Add(new Triangle3(a.ToSharpNav(), b.ToSharpNav(), c.ToSharpNav()));
                result.Add(new Triangle3(a.ToSharpNav(), c.ToSharpNav(), d.ToSharpNav()));
            }
            return result;
        }

        private static Triangle3 RoomLocal(Triangle3 triangle, Room room)
        {
            Quaternion inverse = Quaternion.AngleAxis(360f - room.Rotation, Vector3.Up);
            return new Triangle3(
                (inverse * (triangle.A.ToVector3() - room.Position)).ToSharpNav(),
                (inverse * (triangle.B.ToVector3() - room.Position)).ToSharpNav(),
                (inverse * (triangle.C.ToVector3() - room.Position)).ToSharpNav());
        }
    }
}
