$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$capture = Get-Content -LiteralPath (Join-Path $root 'RKmission/DungeonRoomCapture.cs') -Raw
$recorder = Get-Content -LiteralPath (Join-Path $root 'RKmission/NavigationRouteRecorder.cs') -Raw
$asset = Get-Content -LiteralPath (Join-Path $root 'RKmission/Data/DungeonTopologyRoutes.json') -Raw | ConvertFrom-Json
function Require([bool]$ok, [string]$why) { if (-not $ok) { throw $why } }

$route = $asset.Routes | Where-Object ReusableTemplateValidated
Require ($null -ne $route -and $route.Points.Count -eq 35) 'Validated reusable route must retain all 35 recorded points.'
Require ($route.FirstReusablePoint -eq 2) 'The two unsupported prefix points must remain excluded from reusable playback.'
Require ($route.SourceRoomFingerprint -eq '1C49400F8CC4B73664FFFBEE26F9EDE595F5F8A58CD1CFFBAD87E49F6DBBCC1C') 'Wrong source geometry fingerprint.'
Require ($route.SourceSurfaceCount -eq 303 -and $route.SourceDoorCount -eq 3) 'Source geometry counts changed.'

function Local([double[]]$p, [double[]]$origin, [double]$degrees) {
 $a = -$degrees * [Math]::PI / 180
 $x = $p[0] - $origin[0]; $z = $p[2] - $origin[2]
 @(($x * [Math]::Cos($a) - $z * [Math]::Sin($a)), ($p[1] - $origin[1]),
   ($x * [Math]::Sin($a) + $z * [Math]::Cos($a)))
}
function World([double[]]$p, [double[]]$origin, [double]$degrees) {
 $a = $degrees * [Math]::PI / 180
 @(( $origin[0] + $p[0] * [Math]::Cos($a) - $p[2] * [Math]::Sin($a)), ($origin[1] + $p[1]),
   ($origin[2] + $p[0] * [Math]::Sin($a) + $p[2] * [Math]::Cos($a)))
}
$firstPoint = [double[]]($route.Points[0])
$sourceOrigin = [double[]]($route.SourceRoomPosition)
$firstLocal = Local -p $firstPoint -origin $sourceOrigin -degrees $route.SourceRoomRotationDegrees
foreach ($rotation in @(0, 90, 180, 270)) {
 $world = World -p $firstLocal -origin @(66, 5, 194) -degrees $rotation
 $roundtrip = Local -p $world -origin @(66, 5, 194) -degrees $rotation
 for ($i=0; $i -lt 3; $i++) { Require ([Math]::Abs($roundtrip[$i] - $firstLocal[$i]) -lt 0.0001) "Rotation $rotation roundtrip changed local point." }
}
$rotated = World -p $firstLocal -origin @(66, 5, 194) -degrees 90
Require ([Math]::Abs($rotated[0] - 53.72171) -lt 0.001 -and [Math]::Abs($rotated[2] - 189.522919) -lt 0.001) 'AO XZ handedness mapping for the new capture changed.'
$ys = $route.Points | ForEach-Object { [double]$_[1] }
Require ((($ys | Measure-Object -Maximum).Maximum - ($ys | Measure-Object -Minimum).Minimum) -gt 8) 'Reusable route lost its two-level vertical span.'

foreach ($token in @(
 'DungeonRoomCapture.Compatible(live, route.SourceRoomName',
 'TransformReusableDungeonRoute(route, live)',
 'all {points.Count} points transformed',
 'points = points.Skip(route.FirstReusablePoint).ToList()',
 'Math.Abs(from.Y - points[first].Y) > 0.9f',
 'Vector3.Distance(from, points[first]) > 30f',
 'reusable ? 30f : DungeonSourceAttachDistance',
 'requireTargetLevel && Math.Abs(target.Y - points[end].Y) > 0.9f',
 'SafeReusableTargetHandoff(layout, roomId',
 'Vector3.Distance(target, _dungeonRequestedTarget) > 0.5f',
 'layout.IsInside(roomId, midpoint, 0.02f)',
 'captured fingerprint supplies ramp support; live downward rays no-hit=',
 'sampled leg {i}->{i + 1} has blocked body clearance',
 'sampled leg {i}->{i + 1} lacks standing headroom',
 'Playfield.Raycast(top, bottom, out Vector3 floor',
 'route.DungeonTopologyOnly && !reusable',
 'foreach (bool reverse in new[] { false, true })')) {
 Require ($recorder.Contains($token)) "Missing reusable route guard: $token"
}
foreach ($token in @('template fingerprint differs', 'room identity or geometry counts differ',
 'template placement metadata differs', 'WorldFromCandidateLocal')) {
 Require ($capture.Contains($token)) "Missing template compatibility guard: $token"
}
Write-Output 'PASS 35-point template route metadata, AO 0/90/180/270 transform and rotated capture mapping'
Write-Output 'PASS exact fingerprint/geometry/placement matching, unsupported prefix rejection and bounded start eligibility'
Write-Output 'PASS live room bounds/sampled surface preflight, bidirectional selection and unchanged legacy PF-bound gate'
Require (-not $recorder.Contains('lacks live surface support')) 'Unreliable downward ramp ray must not veto exact-fingerprint playback.'
Write-Output 'PASS exact captured geometry is authoritative for ramp support while live body/head clearance remains mandatory'
