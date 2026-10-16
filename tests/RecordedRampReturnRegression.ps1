$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$recorder = Get-Content (Join-Path $root 'RKmission/NavigationRouteRecorder.cs') -Raw
$dungeon = Get-Content (Join-Path $root 'RKmission/MissionDungeon.cs') -Raw
function Require([bool]$ok, [string]$message) { if (-not $ok) { throw $message } }
$start = $recorder.IndexOf('        internal static List<Vector3> RecordedReturnPoints(')
$end = $recorder.IndexOf('        public bool TryPrepareRecordedReturn(', $start)
$method = $recorder.Substring($start, $end - $start).Replace('internal static', 'public static')
$simplifyStart = $recorder.IndexOf('        internal static List<Vector3> SimplifyDungeonTopology(')
$simplifier = $recorder.Substring($simplifyStart, $start - $simplifyStart).Replace('internal static', 'public static')
$continuousStart = $recorder.IndexOf('        private static bool IsContinuousDungeonRecording(')
$continuousEnd = $recorder.IndexOf('        public bool TryNavigatePreparedDungeonTopology(', $continuousStart)
$continuous = $recorder.Substring($continuousStart, $continuousEnd - $continuousStart)
Add-Type -TypeDefinition (@'
using System;
using System.Collections.Generic;
using System.Linq;
public struct Vector3 {
    public float X,Y,Z;
    public Vector3(float x,float y,float z) { X=x; Y=y; Z=z; }
    public static Vector3 operator -(Vector3 a,Vector3 b) => new Vector3(a.X-b.X,a.Y-b.Y,a.Z-b.Z);
    public static float Distance(Vector3 a,Vector3 b) => (float)Math.Sqrt((a.X-b.X)*(a.X-b.X)+(a.Y-b.Y)*(a.Y-b.Y)+(a.Z-b.Z)*(a.Z-b.Z));
}
public class DungeonTopologyContinuation { public int Playfield; public List<Vector3> Points,PhysicalPoints; public bool PreciseRecordedPlayback; }
public static class RampReturn {
    const float DungeonMinimumVerticalSpan=1.5f,DungeonSourceAttachDistance=4f;
'@ + $simplifier + $method + $continuous + '}')
$asset = Get-Content (Join-Path $root 'RKmission/Data/DungeonTopologyRoutes.json') -Raw | ConvertFrom-Json
$ascent = [DungeonTopologyContinuation]::new()
$ascent.Playfield = 14680242
$ascent.Points = [System.Collections.Generic.List[Vector3]]::new()
foreach ($p in $asset.Routes[0].Points) { $ascent.Points.Add([Vector3]::new($p[0],$p[1],$p[2])) }
$upper = $ascent.Points[-1]
$door = [Vector3]::new(285,5,85)
$return = [RampReturn]::RecordedReturnPoints($ascent,$upper,$door,14680242)
Require ($return.Count -eq $ascent.Points.Count) 'Return must preserve the complete selected ascent slice.'
for ($i=0; $i -lt $return.Count; $i++) {
    Require ([Vector3]::Distance($return[$i],$ascent.Points[$ascent.Points.Count-1-$i]) -eq 0) 'Return fabricated or reordered a physical point.'
}
Require ($return[-1].Y -lt 6 -and $return[0].Y -gt 8) 'Return must end at the ground attachment, not a door or upper wandering point.'
Require ($null -eq [RampReturn]::RecordedReturnPoints($ascent,$upper,$door,999)) 'Wrong playfield accepted.'
Require ($null -eq [RampReturn]::RecordedReturnPoints($ascent,[Vector3]::new(50,8,212),$door,14680242)) 'Unrelated room/instance connector accepted.'
Require ($null -eq [RampReturn]::RecordedReturnPoints($ascent,[Vector3]::new($upper.X,5,$upper.Z),$door,14680242)) 'Ground-to-upper attachment shortcut accepted.'
Require ($null -eq [RampReturn]::RecordedReturnPoints($ascent,$upper,[Vector3]::new(285,8,85),14680242)) 'Upper-level door must not trigger descent.'
$ascent.Points[4] = [Vector3]::new(999,5,999)
Require ($null -eq [RampReturn]::RecordedReturnPoints($ascent,$upper,$door,14680242)) 'Discontinuous retained path accepted.'
Write-Output 'PASS production C# reversal preserves physical points, ground endpoint and conservative context/connector gates'
foreach ($text in @('out DungeonTopologyContinuation completedPath', 'Points = new List<Vector3>(_playPoints)')) {
    Require ($recorder.Contains($text)) "Missing completion snapshot: $text"
}
$prepareReturn = $recorder.Substring($recorder.IndexOf('public bool TryPrepareRecordedReturn('),
 $recorder.IndexOf('public void StopPlayback()', $recorder.IndexOf('public bool TryPrepareRecordedReturn(')) -
 $recorder.IndexOf('public bool TryPrepareRecordedReturn('))
foreach ($text in @('points.Count - reachedPrefix > 2',
 'Vector3.Distance(from, points[reachedPrefix]) <= 1.5f',
 'points = points.GetRange(reachedPrefix, points.Count - reachedPrefix)',
 'skipped {reachedPrefix} already-reached loot-side point(s)')) {
    Require ($prepareReturn.Contains($text)) "Missing bounded descent reacquisition invariant: $text"
}
Require (-not $prepareReturn.Contains('OrderBy(') -and -not $prepareReturn.Contains('Nearest')) 'Descent reacquisition may not jump to an arbitrary nearest route segment.'
Write-Output 'PASS descent skips only consecutive already-reached loot-side points and retains at least two ordered physical points'
$completion = $recorder.IndexOf('completedPath = new DungeonTopologyContinuation')
Require ($completion -lt $recorder.IndexOf('StopPlayback();', $completion)) 'Completed ascent must be copied before playback state is cleared.'
foreach ($text in @('_recordedRoomReturn.RoomId == source', '_recordedRoomReturn.Floor == _floor',
    '_recordedRoomReturn.Playfield == Playfield.ModelIdentity.Instance', 'Ascent = completedPath',
    '_transition.Phase = TransitionPhase.RecordedReturn', 'crossing.ReturnGround, MovementArbiter.Current, MovementOwner.DoorTransition',
    'crossing.Started = crossing.PhaseStarted = crossing.LastProgress = now;',
    'normal doorway approach resumed.', 'TimeSpan.FromSeconds(75)', 'TimeSpan.FromSeconds(10)')) {
    Require ($dungeon.Contains($text)) "Missing descent lifecycle invariant: $text"
}
$lootReadyStart = $dungeon.IndexOf('NavigationRouteRecorder.DungeonTopologyContinuation activeAscent')
$lootReadyEnd = $dungeon.IndexOf('_combatTopologyHandoff = null;', $lootReadyStart)
$lootReady = $dungeon.Substring($lootReadyStart, $lootReadyEnd - $lootReadyStart)
foreach ($text in @('PauseDungeonTopology(next.Position, out activeAscent)',
 'activeAscent.PreciseRecordedPlayback',
 'activeAscent.Points[activeAscent.ProgressIndex].Y -',
 'DynelManager.LocalPlayer.Position.Y - activeAscent.Points[0].Y',
 'Ascent = activeAscent',
 'before ManagerLoot handoff; return descent required before doorway travel.')) {
    Require ($lootReady.Contains($text)) "Missing partial-ascent ManagerLoot handoff invariant: $text"
}
Require ($lootReady.IndexOf('PauseDungeonTopology(next.Position, out activeAscent)') -lt
 $lootReady.IndexOf('_lootRecordedTransit = false;')) 'Active ascent must be snapshotted before recorded loot ownership is released.'
Write-Output 'PASS interaction-ready ManagerLoot handoff preserves meaningful in-progress reusable ascent for mandatory return descent'
$tick = $dungeon.Substring($dungeon.IndexOf('private void TickTransition(Room detectedRoom)'))
Require ($tick.IndexOf('crossing.Phase == TransitionPhase.RecordedReturn') -lt $tick.IndexOf('IsPassageOpen(edge, door)')) 'Door probing must follow descent completion.'
$returnStart = $dungeon.IndexOf('private void TickRecordedReturn(')
$returnEnd = $dungeon.IndexOf('private void TickTransition(', $returnStart)
$body = $dungeon.Substring($returnStart,$returnEnd-$returnStart)
Require (-not $body.Contains('FailTransition(') -and -not $body.Contains('.Use()')) 'Descent must not charge door-edge failures or open doors.'
Require ($body.Contains('if (_readiness.InCombat)') -and $body.Contains('crossing.Started = crossing.LastProgress = now;')) 'Combat pause must halt descent and preserve its timing budget.'
Write-Output 'PASS room/floor lifecycle, exclusive return phase, bounded stall, combat pause and ground-to-door ownership handoff'

$beginStart = $dungeon.IndexOf('private void BeginTransition(')
$begin = $dungeon.Substring($beginStart, $dungeon.IndexOf('private void TickRecordedReturn(', $beginStart)-$beginStart)
Require ($begin.IndexOf('if (_transition.Phase == TransitionPhase.RecordedReturn) return;') -lt $begin.IndexOf('Navigate(DoorwayWaypoint(')) 'Initial transition setup must not issue a direct door destination during return descent.'
Write-Output 'PASS initial transition command also waits for recorded descent'

$raw = [System.Collections.Generic.List[Vector3]]::new()
foreach ($p in $asset.Routes[0].Points) { $raw.Add([Vector3]::new($p[0],$p[1],$p[2])) }
$reduced = [RampReturn]::SimplifyDungeonTopology($raw)
Require ($reduced.Count -eq 7) 'U-turn fixture must reduce 15 sampled legs to six corner/slope-preserving legs.'
foreach ($index in @(0,7,9,10,11,13,15)) {
    Require (@($reduced | Where-Object { [Vector3]::Distance($_,$raw[$index]) -eq 0 }).Count -eq 1) "Lost physical ramp joint $index"
}
$ascent.PhysicalPoints = $raw
$ascent.Points = $reduced
$return = [RampReturn]::RecordedReturnPoints($ascent,$raw[-1],$door,14680242)
Require ($return.Count -eq $raw.Count) 'Return must restore dense physical evidence instead of reversing only simplified legs.'
for ($i=0; $i -lt $return.Count; $i++) {
    Require ([Vector3]::Distance($return[$i],$raw[$raw.Count-1-$i]) -eq 0) 'Descent must reverse every selected physical point in order.'
}
$ascent.Points[1] = [Vector3]::new(999,5,999)
Require ($null -eq [RampReturn]::RecordedReturnPoints($ascent,$raw[-1],$door,14680242)) 'Reduced path with invented vertex must fail physical-subsequence validation.'
$straight = [System.Collections.Generic.List[Vector3]]::new()
for ($i=0; $i -lt 21; $i++) { $straight.Add([Vector3]::new($i,5,0)) }
Require ([RampReturn]::SimplifyDungeonTopology($straight).Count -eq 2) 'Straight corridor must use the minimum one leg.'
foreach ($text in @('_playPoints = _dungeonPrecisePlayback',
    'PhysicalPoints = new List<Vector3>(_dungeonPhysicalPoints ?? _playPoints)',
    '(ascent.PhysicalPoints ?? ascent.Points).AsEnumerable().Reverse().ToList()', 'if (keep[i]) return false;',
    'dx * dx + dz * dz > 0.2f * 0.2f', 'Math.Abs(offset.Y - leg.Y * amount) > 0.15f')) {
    Require ($recorder.Contains($text)) "Missing smoothing/reversal invariant: $text"
}
Write-Output 'PASS U-turn simplification remains available while reusable descent restores every physical turn sample'
