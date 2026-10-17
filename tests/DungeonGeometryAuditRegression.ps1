$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$audit = Get-Content (Join-Path $root 'RKmission/DungeonGeometryAudit.cs') -Raw
$capture = Get-Content (Join-Path $root 'RKmission/DungeonRoomCapture.cs') -Raw
$routes = Get-Content (Join-Path $root 'RKmission/NavigationRouteRecorder.cs') -Raw

function Require([bool]$condition, [string]$message) { if (-not $condition) { throw $message } }

foreach ($field in @('TimestampUtc', 'Playfield', 'RoomInstance', 'Floor', 'RoomName', 'Fingerprint',
    'SurfaceResource', 'MaliTerrain', 'GeneratedNavMesh', 'Doors', 'RecordedTopologyProbe', 'Failures')) {
    Require ($audit.Contains($field)) "Audit report is missing $field."
}
foreach ($field in @('SlopeBands', 'TileSize', 'HeightmapScale', 'PlayerNearestPoly',
    'SurfaceSupported', 'TerrainSupported', 'NavmeshSnapped', 'PathFromPrevious', 'PathFromPlayer',
    'FirstNavGapIndex', 'LongestNavGapStart', 'LongestNavGapLength')) {
    Require ($audit.Contains($field)) "Audit evidence is missing $field."
}
Require ($capture.Contains('WorldVertices = mesh.Vertices.Select')) 'SurfaceResource vertices must be captured in world space.'
Require ($audit.Contains('DungeonTerrainHeight.CreateMesh(room, tilemap)')) 'Audit must inspect the exact Mali terrain mesh source.'
Require ($audit.Contains('new NavMeshQuery(SMovementController.NavAgent.NavMesh, 8192)')) 'Audit must inspect the loaded generated navmesh.'
Require ($audit.Contains('JsonConvert.SerializeObject(report, Formatting.Indented)')) 'Audit must emit structured JSON.'
Require ($routes.Contains('FindCurrentRoomAuditRoute') -and $routes.Contains('TransformReusableDungeonRoute(route, live)')) 'Known routes must be transformed only as diagnostic probes.'
Require ($routes.Contains('!x.ReusableTemplateValidated && x.Playfield == live.Playfield') -and
    $routes.Contains('.Where(x => x.Distance <= 10f)')) 'Current-instance U-turn recordings must be available only as nearby read-only probes.'
Require (-not $audit.Contains('SetDestination(') -and -not $audit.Contains('SetNavDestination(')) 'Room audit must never issue movement.'

Write-Host 'Dungeon geometry audit regression passed.'
