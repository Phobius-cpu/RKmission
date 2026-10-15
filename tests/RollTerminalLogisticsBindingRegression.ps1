$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$bot = Get-Content -LiteralPath (Join-Path $root 'RKmission/RkMissionBot.cs') -Raw
$routes = Get-Content -LiteralPath (Join-Path $root 'RKmission/Data/LogisticsRoutes.json') -Raw | ConvertFrom-Json

function Require([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
    Write-Output "PASS $message"
}

$remember = $bot.IndexOf('private bool RememberRollTerminal(Dynel? terminal)')
$hubGuard = $bot.IndexOf('if (string.IsNullOrEmpty(site)) return false;', $remember)
$mutation = $bot.IndexOf('_rollTerminalPlayfield = playfield;', $remember)
Require ($remember -ge 0 -and $hubGuard -gt $remember -and $mutation -gt $hubGuard) `
    'a non-hub terminal cannot overwrite the durable roller binding'

$auto = $bot.IndexOf('case "auto":')
$preserve = $bot.IndexOf('preserving the verified roller/logistics binding', $auto)
$arm = $bot.IndexOf('_autoCycle = true; Start(); Say("Automatic mission cycle armed.");', $auto)
Require ($preserve -gt $auto -and $preserve -lt $arm -and
    $bot.Contains('!HasVerifiedRollLogisticsBinding(out _)')) `
    'restart beside a PF620 terminal preserves a previously verified city hub'

Require ($bot.Contains('no previously verified city roller/logistics binding is saved') -and
    $bot.Contains('no live terminal in a validated OA, Borealis, or ICC')) `
    'restart without a verified hub stops with a precise diagnostic'

$startRolling = $bot.IndexOf('private void StartAutoRolling()')
$liveTerminal = $bot.IndexOf('Dynel? terminal = FindVisibleRollTerminal(7.5f);', $startRolling)
$bindTerminal = $bot.IndexOf('RememberRollTerminal(terminal);', $liveTerminal)
Require ($liveTerminal -gt $startRolling -and $bindTerminal -gt $liveTerminal) `
    'rolling still binds from an exact live city terminal'

$expected = @(540, 655, 800)
$validated = @($routes.Routes | Group-Object OriginPlayfield | Where-Object {
    ($_.Group.Purpose -contains 'bank') -and ($_.Group.Purpose -contains 'shop')
}).Name | ForEach-Object { [int]$_ }
$validatedText = (($validated | Sort-Object) -join ',')
$expectedText = (($expected | Sort-Object) -join ',')
Require ($validatedText -eq $expectedText) `
    'only OA, ICC and Borealis provide complete validated bank+shop bindings'

Require ($bot.Contains('MissionOriginBinding') -and
    $bot.Contains('TerminalInstance = _rollingTerminalIdentity.Instance')) `
    'quest-specific issuing-terminal provenance remains independent'
