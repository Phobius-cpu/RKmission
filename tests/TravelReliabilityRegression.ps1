$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$fgrid = Get-Content -LiteralPath (Join-Path $root 'RKmission/FGridServiceProvider.cs') -Raw
$scotty = Get-Content -LiteralPath (Join-Path $root 'RKmission/ScottyboiWarpProvider.cs') -Raw
$planner = Get-Content -LiteralPath (Join-Path $root 'RKmission/RubiKaTravelPlanner.cs') -Raw

foreach ($required in @(
    'ObserveServiceCastTransition()',
    'buff.Id == 160981',
    '!Team.IsInTeam',
    'TimeSpan.FromSeconds(_serviceCastObserved ? 30 : 12)',
    'TryNextService(_serviceCastObserved')) {
    if (-not $fgrid.Contains($required)) {
        throw "FGrid cast acknowledgement lost required behavior: $required"
    }
}
Write-Output 'PASS FGrid uses AO nano/team transitions while retaining bounded alternate-service fallback'

foreach ($required in @(
    'ParseMenuCommands(reply.Text, alias)',
    'SelectMenuCommand(_menuCommands, _missionAnchor)',
    'TryWaypointFromUrl',
    'HorizontalDistance(command.Landing.Value, missionAnchor.Value)',
    '_menuPages.Count >= _menuPageCount')) {
    if (-not $scotty.Contains($required)) {
        throw "Scotty command ranking lost required behavior: $required"
    }
}
if (-not $planner.Contains('_warp.Tick(target, missionAnchor)')) {
    throw 'Travel planner no longer supplies the selected mission entrance to Scotty command ranking.'
}

$dependencyPaths = Get-ChildItem "$env:USERPROFILE\.nuget\packages" -Recurse -Filter '*.dll' |
    Select-Object -ExpandProperty FullName
[AppDomain]::CurrentDomain.add_AssemblyResolve({
    param($sender, $eventArgs)
    $name = ($eventArgs.Name -split ',')[0] + '.dll'
    $path = $dependencyPaths | Where-Object { [IO.Path]::GetFileName($_) -eq $name } |
        Select-Object -First 1
    if ($path) { [Reflection.Assembly]::LoadFrom($path) }
})
$assembly = [Reflection.Assembly]::LoadFrom(
    (Join-Path $root 'RKmission/bin/Release/net48/RKmission.dll'))
$provider = $assembly.GetType('RKmission.ScottyboiWarpProvider')
$parse = $provider.GetMethod('ParseMenuCommands',
    [Reflection.BindingFlags]'NonPublic,Static')
$menu = '<header2>Broken Shores<end><br>' +
    'Atalas <a href="chatcmd:///tell scty atalas">Warp</a> ' +
    '<a href="chatcmd:///waypoint 100 100 665">WP</a><br>' +
    'City of Home <a href="chatcmd:///tell scty bs">Warp</a> ' +
    '<a href="chatcmd:///waypoint 900 900 665">WP</a><br>' +
    '<header2>Mort<end>'
$commands = $parse.Invoke($null, @($menu, 'brokenshores'))
if ($commands.Count -ne 2) {
    throw "Expected both Broken Shores commands, found $($commands.Count)."
}
$byText = @{}
foreach ($command in $commands) { $byText[$command.Text] = $command }
if (-not $byText.ContainsKey('atalas') -or -not $byText.ContainsKey('bs') -or
    $byText['bs'].Destination -ne 'cityofhome' -or
    [Math]::Abs($byText['bs'].Landing.X - 900) -gt 0.01) {
    throw 'Broken Shores command/location/waypoint parsing did not preserve distinct choices.'
}
Write-Output 'PASS Scotty collects distinct Atalas and City of Home commands with outdoor landing points'

$select = $provider.GetMethod('SelectMenuCommand',
    [Reflection.BindingFlags]'NonPublic,Static')
$commonAssembly = [Reflection.Assembly]::LoadFrom(
    "$env:USERPROFILE\.nuget\packages\aosharpsdk\1.0.106\lib\net48\AOSharp.Common.dll")
$vectorType = $commonAssembly.GetType('AOSharp.Common.GameData.Vector3')
$anchor = [Activator]::CreateInstance($vectorType, [object[]]@(910.0, 0.0, 910.0))
$nullableVector = [Activator]::CreateInstance(
    ([Nullable``1].MakeGenericType($vectorType)), [object[]]@($anchor))
$selected = $select.Invoke($null, @($commands, $nullableVector))
if ($selected.Text -ne 'bs') {
    throw "Expected /tell scty bs near City of Home, selected $($selected.Text)."
}
Write-Output 'PASS nearer City of Home /tell scty bs beats Atalas for a Broken Shores mission anchor'
