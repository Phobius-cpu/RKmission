$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$vehicle=Get-Content (Join-Path $root 'RKmission/VehicleLifecycle.cs') -Raw
$policy=Get-Content (Join-Path $root 'RKmission/InventoryPolicy.cs') -Raw
$bot=Get-Content (Join-Path $root 'RKmission/RkMissionBot.cs') -Raw
function Assert($ok,$message){ if(-not $ok){ throw $message } }

Assert (-not $vehicle.Contains('117322')) 'Vehicle activation must not depend on the Yalmaha template.'
Assert ($vehicle.Contains('EquipSlots.Contains(EquipSlot.Weap_Hud1)')) 'Discovery must use AOSharp HUD1 slot metadata.'
Assert ($vehicle.Contains('vehicle.MeetsUseReqs(player)')) 'Candidate requirements must be checked.'
Assert ($vehicle.Contains('item.Equip(EquipSlot.Weap_Hud1)')) 'HUD equipment must use Equip(HUD1).'
Assert ($vehicle.Contains('player.MovementState == MovementState.Fly')) 'Live Fly state must verify classification.'
Assert ($vehicle.Contains('TransitionTimeout') -and $vehicle.Contains('_rejectedCandidates')) 'Candidate attempts must be bounded and rejected per session.'
Assert ($vehicle.Contains('candidate-cleanup') -and $vehicle.Contains('Restoring displaced HUD1 after rejected vehicle candidate')) 'Rejected candidates must restore the displaced HUD1 role before fallback.'
Assert ($vehicle.Contains('_indoorItem = ItemReference.From(current)')) 'The displaced HUD1 item must be captured exactly.'
Assert ($vehicle.Contains('RequireIndoorHud1')) 'The indoor HUD1 role must be implemented.'
Assert ($vehicle.Contains('manual-hud1-change-preserved')) 'Manual HUD1 changes must defeat stale restoration.'
Assert ($vehicle.Contains('!routeBenefits')) 'Outdoor activation must remain demand driven.'
Assert ($vehicle.Contains('Game.IsZoning') -and $vehicle.Contains('player.IsAttacking') -and
    $vehicle.Contains('player.IsFalling') -and $vehicle.Contains('Item.HasPendingUse') -and
    $vehicle.Contains('Spell.HasPendingCast')) 'Equipment transitions must carry the lifecycle safety gates.'
Assert ($policy.Contains('RegisterOperationalItem(Item item)') -and
    $policy.Contains('_consumerItemModels.Contains(OperationalModel(item))')) 'Verified/owned equipment must dynamically register with InventoryPolicy.'
Assert (-not $policy.Contains('lowId == 267696')) 'Example combat HUD1 items must not be universally protected.'
Assert ($bot.Contains('_vehicle.RequireIndoorHud1()') -and
    $bot.Contains('exact selected mission dungeon stable for one second')) 'Indoor restoration must be gated by exact stable mission entry.'
Assert ($bot.Contains('HUD1={_vehicle.Status}')) '/rkm status must expose the HUD1 role.'

Write-Host 'HUD1 equipment lifecycle regression passed.'
