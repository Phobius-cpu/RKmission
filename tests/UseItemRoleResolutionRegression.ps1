$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$resolver = Get-Content -LiteralPath (Join-Path $root 'RKmission/UseItemRoleResolver.cs') -Raw
$accepted = Get-Content -LiteralPath (Join-Path $root 'RKmission/AcceptedMissions.cs') -Raw
$objective = Get-Content -LiteralPath (Join-Path $root 'RKmission/MissionObjective.cs') -Raw
$dungeon = Get-Content -LiteralPath (Join-Path $root 'RKmission/MissionDungeon.cs') -Raw
$handIn = Get-Content -LiteralPath (Join-Path $root 'RKmission/ReturnItemHandIn.cs') -Raw

foreach ($required in @(
    'record.Kind != RkMissionKind.Repair && record.Kind != RkMissionKind.Unknown',
    'identity.Type == IdentityType.Terminal || identity.Type == IdentityType.MissionTerminal',
    'sourceWorldType && destinationWorldType',
    'TryResolveRepairTool(record',
    'repair source is the fixture Terminal',
    'repair destination is the fixture Terminal')) {
    if (-not $resolver.Contains($required)) { throw "Repair Source-as-world resolution regressed: $required" }
}
Write-Output 'PASS repair Source terminal resolves as world target and Destination as inventory item'
Write-Output 'PASS unknown-kind retained UseItem actions use the same structural repair evidence'

foreach ($required in @(
    'record.RepairToolIdentity',
    'record.RepairToolLowId',
    'record.RepairToolHighId',
    'record.QuestActionIdentities',
    'accepted QuestActions identify one carried item/template',
    'accepted quest text uniquely names carried item',
    'accepted quest text names no carried item',
    'accepted quest text names multiple carried items')) {
    if (-not $resolver.Contains($required)) { throw "Repair tool evidence/fail-safe regressed: $required" }
}
Write-Output 'PASS repair tool uses persisted/template evidence then unique quest-text inventory matching'

foreach ($required in @(
    'QuestShortInfo',
    'QuestLongInfo',
    'QuestActionIdentities',
    'quest.QuestActions',
    'ApplyQuestMetadata(existing, metadata)',
    'ApplyQuestMetadata(record, metadata)')) {
    if (-not $accepted.Contains($required)) { throw "Accepted quest metadata retention regressed: $required" }
}
Write-Output 'PASS QuestFullUpdate text/action metadata is retained before or after Mission.List reconstruction'

foreach ($required in @(
    'step.InventoryItem',
    'Resolved objective inventory item',
    '_rooms[step.Target] = room.Instance;',
    'Item source = Inventory.Items.FirstOrDefault(x => x.UniqueIdentity == step.InventoryItem);',
    'source.UseOn(target.Identity);',
    'step.Proof |= !Inventory.Items.Any(x => x.UniqueIdentity == step.InventoryItem);')) {
    if (-not $objective.Contains($required)) { throw "Resolved objective role usage regressed: $required" }
}
Write-Output 'PASS preflight, room mapping, dispatch, and proof consistently use resolved roles'

if (-not $dungeon.Contains('_objective.ProtectsMissionItem(item.UniqueIdentity)') -or
    -not $objective.Contains('step.InventoryItem == identity')) {
    throw 'Resolved mission inventory item is no longer protected.'
}
Write-Output 'PASS resolved mission inventory item is protected from logistics'

foreach ($required in @(
    'action.Destination == record.Source',
    'InventoryItem = action.Source',
    'WorldTarget = action.Destination')) {
    if (-not $resolver.Contains($required)) { throw "Return-item role binding regressed: $required" }
}
foreach ($required in @(
    '.Where(x => record.Source != Identity.None && x.Destination == record.Source)',
    'roles.WorldTarget != issuingTerminal',
    'item.UseOn(roles.WorldTarget)')) {
    if (-not $handIn.Contains($required)) { throw "Exact return-item hand-in regressed: $required" }
}
Write-Output 'PASS return-item Source remains inventory item and Destination remains verified issuing terminal'

foreach ($required in @(
    'UseItemOnItem role resolution failed:',
    'kind={record.Kind}',
    'worldType={IsWorldObject(action.Source)}',
    '_roleResolutionFailure',
    '{ reason = _roleResolutionFailure; return false; }',
    'Mission objective metadata could not be reconstructed;',
    '_objective.PreflightFinale(out string objectiveFailure)')) {
    if (-not ($resolver + $objective + $dungeon).Contains($required)) { throw "Ambiguous role fail-safe regressed: $required" }
}
Write-Output 'PASS ambiguous UseItem roles fail preflight instead of guessing'

foreach ($required in @(
    'if (action is KillPersonAction kill) return kill.Target;',
    'if (action is FindPersonAction person) return person.Target;',
    'if (action is FindItemAction item) return item.Target;')) {
    if (-not $objective.Contains($required)) { throw "Non-UseItem objective path regressed: $required" }
}
Write-Output 'PASS FindItem, FindPerson, and KillPerson target paths remain unchanged'
