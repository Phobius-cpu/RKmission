$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$recorder = Get-Content (Join-Path $root 'RKmission/NavigationRouteRecorder.cs') -Raw
$original = Get-Content (Join-Path $root 'docs/navigation-evidence/navigation-routes-2026-10-14-full-user-recording.json') -Raw | ConvertFrom-Json
$asset = Get-Content (Join-Path $root 'RKmission/Data/DungeonTopologyRoutes.json') -Raw | ConvertFrom-Json
$route = @($original.Routes | Where-Object Name -CEQ 'U turn Ramp')
if ($route.Count -ne 1 -or $route[0].Points.Count -ne 54) { throw 'Original evidence changed' }
$derived = $asset.Routes[0]
if ($asset.Routes.Count -ne 1 -or $derived.Name -cne 'U turn ramp - Upper Container' -or
    $derived.Playfield -ne 14680242 -or $derived.Points.Count -ne 16 -or
    -not $derived.DungeonTopologyOnly -or $derived.Directionality -ne 'Forward') { throw 'Derivative metadata changed' }
for ($i = 0; $i -lt 16; $i++) {
    for ($axis = 0; $axis -lt 3; $axis++) {
        if ($derived.Points[$i][$axis] -ne $route[0].Points[$i][$axis]) { throw 'Invented derivative coordinate' }
    }
}
if ($derived.Points[-1][1] -ne 8.015 -or $derived.RecordedAtUtc -ne $route[0].RecordedAtUtc) { throw 'Upper landing/provenance changed' }
Write-Output 'PASS derivative is exactly original points 0..15 with preserved capture provenance'

# Compile the production selector itself, with only AO vector/playfield data stubbed.
$start = $recorder.IndexOf('        private static DungeonSubpath FindDungeonSubpath(')
$end = $recorder.IndexOf('        private static bool IsContinuousDungeonRecording', $start)
$method = $recorder.Substring($start, $end - $start).Replace('private static DungeonSubpath', 'public static DungeonSubpath')
Add-Type -TypeDefinition (@'
using System;
using System.Collections.Generic;
public struct Vector3 {
    public float X,Y,Z;
    public Vector3(float x,float y,float z) { X=x;Y=y;Z=z; }
    public static Vector3 operator -(Vector3 a,Vector3 b) => new Vector3(a.X-b.X,a.Y-b.Y,a.Z-b.Z);
    public float Magnitude => (float)Math.Sqrt(X*X+Y*Y+Z*Z);
    public static float Distance(Vector3 a,Vector3 b) => (a-b).Magnitude;
}
public static class Playfield { public static class ModelIdentity { public static int Instance=14680242; } }
public class Route { public bool DungeonTopologyOnly; public int Playfield; }
public class DungeonSubpath {
    public Route Route; public List<Vector3> Points;
    public int SourceIndex,TargetIndex; public bool Reversed;
    public float SourceDistance,TargetDistance,VerticalSpan,Cost;
}
public static class Selector {
    const float DungeonMinimumVerticalSpan=1.5f,DungeonSourceAttachDistance=4f,DungeonTargetAttachDistance=4f;
'@ + $method + '}')
$points = [System.Collections.Generic.List[Vector3]]::new()
foreach ($point in $derived.Points) { $points.Add([Vector3]::new($point[0],$point[1],$point[2])) }
$r = [Route]::new(); $r.Playfield = 14680242; $r.DungeonTopologyOnly = $true
$match = [Selector]::FindDungeonSubpath($r,$points,$false,$points[0],$points[-1])
if ($null -eq $match -or $match.Points.Count -lt 2 -or $match.Points[0].Y -gt 6 -or $match.Points[-1].Y -lt 7.9) { throw 'Physical ground-to-upper route not selected' }
if ($null -ne [Selector]::FindDungeonSubpath($r,$points,$false,[Vector3]::new(50,5.315,212),[Vector3]::new(50,8.015,212))) { throw 'Unconfirmed instance translation accepted' }
if ($null -ne [Selector]::FindDungeonSubpath($r,$points,$false,$points[0],[Vector3]::new(291.6195,5.315,97.01))) { throw 'Flat target accepted' }
Write-Output 'PASS production C# selector matches physical ascent and rejects unrelated room-4 and flat geometry'
foreach ($required in @('LoadDungeonTopology(', 'best.Route.DungeonTopologyOnly && !route.DungeonTopologyOnly',
    '!layout.IsInside(roomId, candidate.Points[0])', '!x.DungeonTopologyOnly && RouteModeMatches',
    'if (reverse && route.DungeonTopologyOnly && RouteDirection(route) == "Forward") continue;',
    'if (!route.DungeonTopologyOnly || _routes.Any(x =>')) {
    if (-not $recorder.Contains($required)) { throw "Operational isolation/selection missing: $required" }
}
Write-Output 'PASS matched derivative preference, same-room gate, forward direction and user-name authority are explicit'
