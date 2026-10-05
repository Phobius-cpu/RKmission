$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$bot = Get-Content -LiteralPath (Join-Path $root 'RKmission/RkMissionBot.cs') -Raw

$preflight = $bot.IndexOf('if (!PreflightSelectedMission()) return;')
$longTravel = $bot.IndexOf('TravelResult longResult = _longTravel.Tick(_selected.PlayfieldId, _selected.Entrance);')
$destinationGate = $bot.IndexOf('_selected.PlayfieldId == Playfield.ModelIdentity.Instance)', $longTravel)
$entranceSelect = $bot.IndexOf('_entranceResolver.Select(live);', $destinationGate)
if ($preflight -lt 0 -or $longTravel -le $preflight -or
    $destinationGate -le $longTravel -or $entranceSelect -le $destinationGate) {
    throw 'Neko entrance resolution is not ordered after verified cross-playfield travel and its destination gate.'
}
if (-not $bot.Contains('_entranceResolver.Reset();') -or
    -not $bot.Contains('Travel first and')) {
    throw 'Cross-playfield travel does not clear stale Neko entrance candidates.'
}
Write-Output 'PASS cross-playfield travel precedes destination-local Neko entrance resolution'
