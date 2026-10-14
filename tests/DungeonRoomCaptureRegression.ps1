$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$source=Get-Content (Join-Path $root 'RKmission/DungeonRoomCapture.cs') -Raw
$recorder=Get-Content (Join-Path $root 'RKmission/NavigationRouteRecorder.cs') -Raw
$bot=Get-Content (Join-Path $root 'RKmission/RkMissionBot.cs') -Raw
function Require([bool]$ok,[string]$why){if(-not $ok){throw $why}}
$start=$source.IndexOf('        internal static float[] CandidateLocal(')
$end=$source.IndexOf('        private static string PointKey(', $start)
$methods=$source.Substring($start,$end-$start).Replace('internal static','public static')
Add-Type -TypeDefinition ('using System; using System.Linq; using System.Collections.Generic; using System.Text; using System.Security.Cryptography; public static class CaptureMath {'+$methods+'}')
$p=[float[]]@(105,8,200);$origin=[float[]]@(100,5,200)
$local=[CaptureMath]::CandidateLocal($p,$origin,90)
Require ([math]::Abs($local[0]) -lt 0.0001 -and [math]::Abs($local[1]-3) -lt 0.0001 -and [math]::Abs($local[2]+5) -lt 0.0001) 'Capture convention must remove translation and rotation while preserving relative height.'
$other=[CaptureMath]::CandidateLocal([float[]]@(500,18,305),[float[]]@(500,15,300),180)
Require ([math]::Abs($other[0]-$local[0]) -lt 0.0001 -and [math]::Abs($other[1]-$local[1]) -lt 0.0001 -and [math]::Abs($other[2]-$local[2]) -lt 0.0001) 'Same candidate local point in another pose must normalize consistently.'
$a=[CaptureMath]::GeometryFingerprint([string[]]@('triangle:abc','doors:2','triangle:def'))
$b=[CaptureMath]::GeometryFingerprint([string[]]@('triangle:def','triangle:abc','doors:2'))
$c=[CaptureMath]::GeometryFingerprint([string[]]@('triangle:abc','doors:3','triangle:def'))
Require ($a -eq $b -and $a -ne $c -and $a.Length -eq 64) 'Candidate geometry hash must be order invariant and distinguish changed evidence.'
Write-Output 'PASS production capture convention and deterministic candidate fingerprint methods'
foreach($text in @('room.TemplatePos','room.TemplateRotation','room.SurfaceResource','room.GetDoorPosRot',
 'CandidateRoomLocalPoints','RecordedWorldPoints','RoomAtRecordingEnd','ReusablePlaybackValidated = false',
 'FileMode.CreateNew','Guid.NewGuid()', 'Game.IsZoning || !Playfield.IsDungeon')) {
 Require ($source.Contains($text)) "Missing capture evidence/safety invariant: $text"
}
Require ($recorder.Contains('_recordingRoomCapture = DungeonRoomCapture.ReadCurrent();') -and
 $recorder.Contains('_recordingRoomCapture, route.Name, route.Points)')) 'Room metadata must attach at recording start and export with actual completed route points.'
Require ($bot.Contains('args[1].Equals("capture", StringComparison.OrdinalIgnoreCase)') -and
 $bot.Contains('_navRoutes.CaptureCurrentRoom();')) 'Manual capture command must be available.'
Require (-not $source.Contains('SetDestination(') -and -not $source.Contains('MoveToInventory(')) 'Capture must not change navigation or equipment.'
Write-Output 'PASS raw room/template/surface/door evidence, non-overwriting sidecars, route linkage and capture-only behavior'
