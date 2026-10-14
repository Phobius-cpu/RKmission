# Dungeon room metadata capture — AWAITING AO TEST

Baseline: 2e4804908c8b7f0e2e775dadc4994ae304cc949b.
Workstream: Dungeon. This batch collects evidence only; reusable template playback
and upper-layer navmesh generation are not enabled.

AOSharpSDK 1.0.106 exposes Room.Position, Center, Rotation, YOffset, TemplatePos,
TemplateRotation, LocalRect, SurfaceResource.Meshes and GetDoorPosRot/connect-zone.
The new /rkm nav capture command snapshots those values in a stable dungeon room.
It preserves surface vertices/triangles and each surface's position, scale and yaw,
plus door world positions/yaw/connections. Candidate room-local door/route coordinates
use an explicitly recorded normalization convention; its equivalence to the native
template transform still requires comparison in real missions.

A candidate SHA256 fingerprint canonicalizes surface triangles at 0.01m precision,
scale and door count independently of triangle/mesh ordering. Door arrangements,
full metadata and raw world points remain separate evidence for stricter validation.
A fingerprint match alone is not treated as proof of reusable topology. Different
hashes may reflect quantization or geometry variants; captures retain the raw data.

New dungeon nav recordings automatically snapshot metadata at recording start.
/rkm nav stop exports the snapshot together with that recording's actual world
points and candidate room-local points. Room-at-recording-end is stored to detect
room-crossing recordings. All evidence goes to separate timestamp+GUID JSON files
in RKMissionData/room-captures via FileMode.CreateNew. Existing route snapshots and
bundled routes remain untouched. Use a fresh automatic route name for each capture.
Capture failures report diagnostics and do not prevent ordinary recording.

The user's report that the upper platform lacks navmesh is a separate investigation.
Captures also retain the room's bounded raw heightmap and collision-grid patches,
origins/dimensions, tile size and height scale. Mali DungeonTerrainHeight builds its
mesh from one height per tile; this cannot alone represent two vertically stacked
walkable surfaces at the same horizontal location. Surface meshes plus a manually
walked upper-platform recording will help determine what geometry/navmesh is absent.
Optional terrain extraction errors are recorded without discarding surface evidence.

Validation: DungeonRoomCaptureRegression compiles and executes candidate normalization
and fingerprint methods, checks translation/rotation normalization, order invariance,
changed geometry distinction, required raw metadata, unique sidecar writes and
capture-only command behavior. RecordedRampReturn and four related topology suites
also pass (six suites total). The complete new capture class separately compiles
against the actual AOSharpSDK 1.0.106 assemblies with PowerShell's Newtonsoft/runtime
references; only runtime-version reference warnings occur. This is not a full
net48 plugin build or an AO native capture test. Local full compile remains required.

## User procedure

1. Pull/compile/reload the plugin. When the U-turn room is found, pause RKMission
   with /rkm stop and stand at the lower ramp approach inside that room.
2. Run /rkm nav record foot (no name, for a fresh automatic route name).
3. Manually walk the ramp approach, first climb, turn, second climb, and upper
   platform to the container interaction position. Avoid wandering or leaving the room.
4. Run /rkm nav stop while still in the room. Confirm the Room metadata and route
   evidence saved message; it includes the exact JSON filename and candidate hash.
5. Repeat in a different mission containing the same room. Keep both JSON files
   from RKMissionData/room-captures and supply them for comparison. These files
   contain both route points and placement/geometry evidence; no extra route-file
   replacement is needed.

/rkm nav capture is available for a metadata-only snapshot at any point in that
room, including the upper platform if movement is currently stuck. It does not
attach old recordings whose original room placement was never captured.

AO validation: verify stable capture, complete route metadata, unchanged navigation,
matching normalized geometry/doors in two missions, transform alignment and upper
surface/navmesh evidence. Reusable playback remains disabled until those checks pass.
