using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using AOSharp.Common.GameData;
using AOSharp.Common.Unmanaged.DbObjects;
using AOSharp.Core;
using Newtonsoft.Json;

namespace RKmission
{
    // Captures immutable room-template evidence. A fingerprint can only enable
    // playback when a route also carries an explicitly validated source capture.
    internal static class DungeonRoomCapture
    {
        internal sealed class Snapshot
        {
            public int SchemaVersion = 1;
            public DateTime CapturedAtUtc;
            public int Playfield, RoomInstance, Floor, RoomAtRecordingEnd;
            public string RoomName, RouteName, CandidateFingerprint;
            public float[] Position, Center, TemplatePosition, LocalRect;
            public float RotationDegrees, TemplateRotationDegrees, YOffset;
            public List<Surface> Surfaces = new List<Surface>();
            public List<DoorEvidence> Doors = new List<DoorEvidence>();
            public List<float[]> RecordedWorldPoints, CandidateRoomLocalPoints;
            public int[] HeightGridOrigin, CollisionGridOrigin;
            public int HeightGridWidth, HeightGridHeight, CollisionGridWidth, CollisionGridHeight;
            public float TileSize, HeightmapScale;
            public List<byte> HeightSamples, CollisionSamples;
            public string TerrainCaptureNote;
            public bool ReusablePlaybackValidated = false;
        }
        internal sealed class Surface
        {
            public float[] Position, Scale;
            public float RotationYaw;
            public List<float[]> LocalVertices;
            public List<int> Triangles;
        }
        internal sealed class DoorEvidence
        {
            public int Index, ConnectedRoom;
            public float[] WorldPosition, CandidateLocalPosition;
            public float RotationYaw;
        }

        internal static float[] P(Vector3 p) => new[] { p.X, p.Y, p.Z };

        // Explicit capture convention; native room/template metadata is retained
        // alongside it for validation against the game's actual surface transform.
        internal static float[] CandidateLocal(float[] point, float[] origin, float degrees)
        {
            double angle = -degrees * Math.PI / 180d;
            double x = point[0] - origin[0], z = point[2] - origin[2];
            return new[] { (float)(x * Math.Cos(angle) - z * Math.Sin(angle)),
                point[1] - origin[1], (float)(x * Math.Sin(angle) + z * Math.Cos(angle)) };
        }

        internal static Vector3 WorldFromCandidateLocal(float[] point, float[] origin,
            float degrees)
        {
            double angle = degrees * Math.PI / 180d;
            return new Vector3(origin[0] + (float)(point[0] * Math.Cos(angle) - point[2] * Math.Sin(angle)),
                origin[1] + point[1], origin[2] + (float)(point[0] * Math.Sin(angle) + point[2] * Math.Cos(angle)));
        }

        internal static bool Compatible(Snapshot live, string roomName, string fingerprint,
            float[] templatePosition, float templateRotation, float[] localRect,
            int surfaceCount, int doorCount, out string reason)
        {
            reason = null;
            if (live == null || string.IsNullOrWhiteSpace(fingerprint) ||
                !string.Equals(live.CandidateFingerprint, fingerprint, StringComparison.Ordinal))
                reason = "template fingerprint differs";
            else if (!string.Equals(live.RoomName, roomName, StringComparison.Ordinal) ||
                live.Surfaces.Count != surfaceCount || live.Doors.Count != doorCount)
                reason = "room identity or geometry counts differ";
            else if (templatePosition == null || templatePosition.Length != 3 ||
                localRect == null || localRect.Length != 4 || live.TemplatePosition == null ||
                live.LocalRect == null || live.TemplatePosition.Zip(templatePosition,
                    (a, b) => Math.Abs(a - b)).Any(x => x > 0.05f) ||
                Math.Abs(live.TemplateRotationDegrees - templateRotation) > 0.05f ||
                live.LocalRect.Zip(localRect, (a, b) => Math.Abs(a - b)).Any(x => x > 0.05f))
                reason = "template placement metadata differs";
            else if (live.Position == null || live.Position.Length != 3 ||
                !Finite(live.Position) || !Finite(new[] { live.RotationDegrees }))
                reason = "live room transform is invalid";
            return reason == null;
        }

        private static bool Finite(IEnumerable<float> values) =>
            values != null && values.All(x => !float.IsNaN(x) && !float.IsInfinity(x));

        internal static string GeometryFingerprint(IEnumerable<string> geometry)
        {
            string canonical = string.Join("\n", geometry.OrderBy(x => x, StringComparer.Ordinal));
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(canonical))).Replace("-", "");
        }
        private static string PointKey(float[] p) => string.Join(",", p.Select(x =>
            Math.Round(x, 2).ToString("F2", CultureInfo.InvariantCulture)));

        internal static Snapshot ReadCurrent()
        {
            Room room = DynelManager.LocalPlayer?.Room;
            if (Game.IsZoning || !Playfield.IsDungeon || room == null)
                throw new InvalidOperationException("room capture requires a stable dungeon room");
            var result = new Snapshot
            {
                CapturedAtUtc = DateTime.UtcNow, Playfield = Playfield.ModelIdentity.Instance,
                RoomInstance = room.Instance, Floor = room.Floor, RoomName = room.Name,
                Position = P(room.Position), Center = P(room.Center),
                TemplatePosition = P(room.TemplatePos), RotationDegrees = room.Rotation,
                TemplateRotationDegrees = room.TemplateRotation, YOffset = room.YOffset,
                LocalRect = new[] { room.LocalRect.MinX, room.LocalRect.MinY,
                    room.LocalRect.MaxX, room.LocalRect.MaxY }
            };
            var surface = room.SurfaceResource;
            if (surface?.Meshes == null || surface.Meshes.Count == 0)
                throw new InvalidOperationException("room has no readable surface geometry");
            var geometry = new List<string>();
            foreach (var mesh in surface.Meshes)
            {
                var vertices = mesh.Vertices.Select(P).ToList();
                result.Surfaces.Add(new Surface { Position = P(mesh.Position), Scale = P(mesh.Scale),
                    RotationYaw = (float)mesh.Rotation.Yaw, LocalVertices = vertices,
                    Triangles = new List<int>(mesh.Triangles) });
                geometry.Add("scale:" + PointKey(P(mesh.Scale)));
                for (int i = 0; i + 2 < mesh.Triangles.Count; i += 3)
                    geometry.Add("triangle:" + string.Join(";", new[] {
                        PointKey(vertices[mesh.Triangles[i]]), PointKey(vertices[mesh.Triangles[i + 1]]),
                        PointKey(vertices[mesh.Triangles[i + 2]]) }.OrderBy(x => x, StringComparer.Ordinal)));
            }
            geometry.Add("doors:" + room.NumDoors);
            for (int i = 0; i < room.NumDoors; i++)
            {
                room.GetDoorPosRot(i, out Vector3 pos, out Quaternion rot);
                result.Doors.Add(new DoorEvidence { Index = i, ConnectedRoom = room.GetDoorConnectZone(i),
                    WorldPosition = P(pos), CandidateLocalPosition = CandidateLocal(P(pos), result.Position, room.Rotation),
                    RotationYaw = (float)rot.Yaw });
            }
            result.CandidateFingerprint = GeometryFingerprint(geometry);
            // Mali's mesh source is a single height per tile. Preserve this input
            // separately from surface meshes when investigating stacked floors.
            try
            {
                var tilemap = Playfield.RDBTilemap as DungeonRDBTilemap;
                if (tilemap != null)
                {
                    result.TileSize = tilemap.TileSize;
                    result.HeightmapScale = tilemap.HeightmapScale;
                    int x0 = Math.Max(0, (int)room.LocalRect.MinX - 1);
                    int z0 = Math.Max(0, (int)room.LocalRect.MinY - 1);
                    int x1 = Math.Min(tilemap.Heightmap.GetLength(0) - 1, (int)room.LocalRect.MaxX);
                    int z1 = Math.Min(tilemap.Heightmap.GetLength(1) - 1, (int)room.LocalRect.MaxY);
                    result.HeightGridOrigin = new[] { x0, z0 };
                    result.HeightGridWidth = Math.Max(0, x1 - x0 + 1);
                    result.HeightGridHeight = Math.Max(0, z1 - z0 + 1);
                    if ((long)result.HeightGridWidth * result.HeightGridHeight > 65536)
                        throw new InvalidDataException("room height grid exceeds capture bound");
                    result.HeightSamples = new List<byte>();
                    for (int z = z0; z <= z1; z++)
                        for (int x = x0; x <= x1; x++) result.HeightSamples.Add(tilemap.Heightmap[x, z]);
                    x0 = Math.Max(0, (int)room.LocalRect.MinX);
                    z0 = Math.Max(0, (int)room.LocalRect.MinY);
                    x1 = Math.Min(tilemap.CollisionData.GetLength(0) - 1, (int)room.LocalRect.MaxX - 1);
                    z1 = Math.Min(tilemap.CollisionData.GetLength(1) - 1, (int)room.LocalRect.MaxY - 1);
                    result.CollisionGridOrigin = new[] { x0, z0 };
                    result.CollisionGridWidth = Math.Max(0, x1 - x0 + 1);
                    result.CollisionGridHeight = Math.Max(0, z1 - z0 + 1);
                    if ((long)result.CollisionGridWidth * result.CollisionGridHeight > 65536)
                        throw new InvalidDataException("room collision grid exceeds capture bound");
                    result.CollisionSamples = new List<byte>();
                    for (int z = z0; z <= z1; z++)
                        for (int x = x0; x <= x1; x++) result.CollisionSamples.Add(tilemap.CollisionData[x, z]);
                }
            }
            catch (Exception ex) { result.TerrainCaptureNote = ex.Message; }
            return result;
        }

        internal static string Save(string directory, Snapshot capture, string routeName = null,
            List<float[]> recordedPoints = null)
        {
            capture.RouteName = routeName;
            capture.RecordedWorldPoints = recordedPoints?.Select(x => (float[])x.Clone()).ToList();
            capture.CandidateRoomLocalPoints = recordedPoints?.Select(x =>
                CandidateLocal(x, capture.Position, capture.RotationDegrees)).ToList();
            capture.RoomAtRecordingEnd = DynelManager.LocalPlayer?.Room?.Instance ?? -1;
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "room-" + capture.CapturedAtUtc.ToString("yyyyMMdd-HHmmss") +
                "-" + Guid.NewGuid().ToString("N") + ".json");
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
            using (var writer = new StreamWriter(stream))
                writer.Write(JsonConvert.SerializeObject(capture, Formatting.Indented));
            return path;
        }
    }
}
