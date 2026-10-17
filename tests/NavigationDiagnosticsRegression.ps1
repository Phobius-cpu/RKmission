$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$diagnostics = Get-Content (Join-Path $root 'RKmission/NavigationDiagnostics.cs') -Raw
$dungeon = Get-Content (Join-Path $root 'RKmission/MissionDungeon.cs') -Raw
$bot = Get-Content (Join-Path $root 'RKmission/RkMissionBot.cs') -Raw

function Require([bool]$condition, [string]$message) { if (-not $condition) { throw $message } }

Require ($diagnostics.Contains('public bool NavDebugEnabled { get; private set; }')) 'Nav debug must default false.'
Require ($diagnostics.Contains('if (NavDebugEnabled) _say(message);')) 'NavTrace must expose messages only when enabled.'
Require ($bot.Contains('case "debug":') -and $bot.Contains('Usage: /rkm debug nav on|off.')) 'The navigation debug command is missing.'
Require ($bot.Contains('nav-debug={(_diagnostics.NavDebugEnabled ? "on" : "off")}')) 'Status must expose the nav-debug state.'

foreach ($routine in @('Routing from room', ': source approach', ': door reached', ': doorway open; crossing.',
    ': target room detected', ': boundary crossed', ': confirmed in ')) {
    $line = ($dungeon -split "`n" | Where-Object { $_.Contains($routine) } | Select-Object -First 1)
    Require ($line -and $line.Contains('_navTrace(')) "Routine trace '$routine' must use the gated path."
}
foreach ($operational in @('FailTransition("entry was not confirmed within 30 seconds")',
    'FailTransition("source-side doorway approach stalled")',
    '_say($"Transition {edge.Source}->{edge.Target}: {reason}; failure')) {
    Require ($dungeon.Contains($operational)) "Operational transition evidence '$operational' must remain unconditional."
}
Require ($bot.Contains('_geometryAudit.AuditCurrentRoom();')) 'Explicit room audit command must use its unconditional command path.'

Write-Host 'Navigation diagnostics regression passed.'
