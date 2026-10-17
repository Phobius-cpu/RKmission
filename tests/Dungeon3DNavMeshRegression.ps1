$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$generator = Get-Content -LiteralPath (Join-Path $root 'RKmission/DungeonNavMeshGeneration.cs') -Raw
$recorder = Get-Content -LiteralPath (Join-Path $root 'RKmission/NavigationRouteRecorder.cs') -Raw
$capture = Get-Content -LiteralPath (Join-Path $root 'RKmission/DungeonRoomCapture.cs') -Raw
$routes = (Get-Content -LiteralPath (Join-Path $root 'RKmission/Data/DungeonTopologyRoutes.json') -Raw | ConvertFrom-Json).Routes

function Require([bool]$condition, [string]$message) { if (-not $condition) { throw $message } }

$route = $routes | Where-Object SourceRoomFingerprint -eq '1C49400F8CC4B73664FFFBEE26F9EDE595F5F8A58CD1CFFBAD87E49F6DBBCC1C'
Require ($null -ne $route) 'Known Mine_Big16_1 geometry route is missing.'
Require ($route.Points.Count -eq 35) 'Known two-ramp route point count changed.'

$segments = [Collections.Generic.HashSet[int]]::new()
for ($i = 0; $i + 1 -lt $route.Points.Count; $i++) {
    if ([Math]::Abs([double]$route.Points[$i + 1][1] - [double]$route.Points[$i][1]) -lt 0.15) { continue }
    [void]$segments.Add([Math]::Max(0, $i - 1))
    [void]$segments.Add($i)
    [void]$segments.Add([Math]::Min($route.Points.Count - 2, $i + 1))
}
Require ($segments.Count -eq 14) 'Ramp extraction must cover both slopes plus one attachment leg at each end.'

foreach ($required in @(
    'TerrainData.GetTriGeometry(room)',
    'BuildRampCorridor(route.Points)',
    'tile.FilterPolyMesh(room)',
    'new DungeonNavMeshFactory(_cacheDirectory, settings).GenerateNavMesh()',
    'Task.Run(Generate)',
    'FindReusableRoomAuditRoute(capture)',
    'ReportConnectivity(meshes, probe.Item1, probe.Item2)',
    'path[path.Count - 1] == end.Polygon',
    'if (File.Exists(tilePath)) File.Delete(tilePath)')) {
    Require ($generator.Contains($required)) "Missing guarded AOSharp pipeline step: $required"
}
Require ($recorder.Contains('DungeonRoomCapture.Compatible(live, route.SourceRoomName') -and
    $recorder.Contains('TransformReusableDungeonRoute(route, live)')) 'Cross-instance reuse is not fingerprint and transform gated.'
Require ($capture.Contains('mesh.LocalToWorldMatrix.MultiplyPoint3x4(vertex)')) 'Surface geometry must retain the AO mesh world transform.'
Require (-not $generator.Contains('MissionObjective') -and -not $generator.Contains('SetNavDestination')) 'Generation must not trigger objectives or movement.'

Write-Output 'PASS 3D dungeon bake overlays two fingerprint-gated ramp runs and retains AOSharp factory fallback'
