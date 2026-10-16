$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$vehicle=Get-Content (Join-Path $root 'RKmission/VehicleLifecycle.cs') -Raw
$policy=Get-Content (Join-Path $root 'RKmission/InventoryPolicy.cs') -Raw
$bot=Get-Content (Join-Path $root 'RKmission/RkMissionBot.cs') -Raw
$profiles=Get-Content (Join-Path $root 'RKmission/Hud1EquipmentProfiles.cs') -Raw
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
Assert ($bot.IndexOf('_movement.Release(MovementOwner.MissionEntrance)') -lt
    $bot.IndexOf('_vehicle.RequireIndoorHud1()', $bot.IndexOf('_movement.Release(MovementOwner.MissionEntrance)'))) 'Verified entry must release stale MissionEntrance ownership before indoor HUD1.'
Assert (-not $vehicle.Contains('_movement.Owner == MovementOwner.MissionEntrance')) 'SafeToTransition must not admit MissionEntrance ownership.'
foreach ($evidence in @('owner=', 'zoning=', 'exactDungeonAllowed=', 'attacking=', 'falling=', 'pendingItem=', 'pendingSpell=')) {
    Assert ($vehicle.Contains($evidence)) "Gate diagnostics missing $evidence"
}
Assert ($vehicle.Contains('candidates.Count == 1') -and $vehicle.Contains('Unique usable indoor HUD1 candidate learned')) 'A unique usable non-vehicle candidate must be learned.'
Assert ($vehicle.Contains('indoor/no-candidate-vehicle-removal-requested')) 'Zero candidate result must be explicit.'
Assert ($vehicle.Contains('indoor/ambiguous-vehicle-removal-requested') -and $vehicle.Contains('will not guess')) 'Multiple candidates must remain ambiguous.'
Assert ($profiles.Contains('CharacterInstance') -and $profiles.Contains('CharacterName') -and
    $profiles.Contains('hud1-equipment.json')) 'HUD1 preferences must persist per character.'
Assert ($vehicle.Contains('PersistVehicle(hud1)') -and $vehicle.Contains('PersistIndoor(current)')) 'Live verified HUD1 roles must persist.'
Assert ($vehicle.Contains('FindModel(_profile.Indoor') -and $vehicle.Contains('FindModel(_profile.Vehicle')) 'Cached models must be re-resolved rather than trusted as slots.'
Assert ($vehicle.Contains('savedIndoor != null') -and $vehicle.Contains('FindItem(_indoorItem, true)')) 'Starting flight must re-resolve and use a persisted indoor preference.'
Assert ($vehicle.Contains('if (indoor != null && !indoor.MeetsUseReqs(player)) indoor = null;')) 'An unusable saved indoor model must fall back to discovery.'
Assert ($vehicle.Contains('_verifiedVehicle == null || !_verifiedVehicle.Matches(x)')) 'Indoor inference must exclude the verified vehicle.'
Assert ($vehicle.Contains('_inventoryPolicy.RegisterOperationalItem(indoor)')) 'Only the learned indoor candidate is dynamically protected.'
Assert (-not $vehicle.Contains('RegisterOperationalItem(x)')) 'All HUD1-compatible candidates must not be broadly protected.'
Assert ($vehicle.Contains('_characterInstance != player.Identity.Instance') -and
    $vehicle.Contains('!string.Equals(_characterName, player.Name')) 'Journey/profile state must reset across character identity or name changes.'
Assert ($vehicle.Contains('_originalFlying && hud1 != null') -and $vehicle.Contains('PersistVehicle(hud1)')) 'An already-flying live HUD1 vehicle must be verified and persisted.'
Assert ($vehicle.Contains('_pendingTarget?.Matches(current) == true') -and
    $vehicle.Contains('player.MovementState != MovementState.Fly') -and $vehicle.Contains('PersistIndoor(current)')) 'Indoor preference must persist only after live HUD1/non-Fly verification.'
Assert ($bot.Contains('HUD1={_vehicle.Status}')) '/rkm status must expose the HUD1 role.'

Write-Host 'HUD1 equipment lifecycle regression passed.'
