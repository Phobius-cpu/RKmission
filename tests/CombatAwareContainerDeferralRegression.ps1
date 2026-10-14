$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$loot = Get-Content (Join-Path $root 'RKmission/Plugins/ManagerLoot/ManagerLoot.cs') -Raw
$dungeon = Get-Content (Join-Path $root 'RKmission/MissionDungeon.cs') -Raw

function Require([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}

foreach ($required in @(
    'CombatBlocked, Opening',
    'cant open a chest while youre in a fight',
    'occurrence != _pendingMissionLootOccurrence',
    'opening ownership released without side probing or navigation-failure charge',
    'MissionCombatFree?.Invoke() == true',
    'CombatFreeConfirmationSeconds = 5',
    'CombatBlockedRetryCooldownSeconds = 8',
    'MaxOrdinaryCombatBlockedAttempts = 3',
    'DeferredApproachReady(c.Identity)',
    '_openedMissionLootOccurrences[container.Identity] = _pendingMissionLootOccurrence',
    '_finishedMissionLootOccurrences[CorpseContainer.Identity] = occurrence')) {
    Require ($loot.Contains($required)) "Missing combat-aware loot invariant: $required"
}

$combatBranch = $loot.IndexOf('if (combatRejected)')
$releasePending = $loot.IndexOf('_pendingMissionLoot = MissionIdentity.None;', $combatBranch)
$releaseCorpse = $loot.IndexOf('CurrentCorpse = null;', $combatBranch)
$state = $loot.IndexOf('MissionSourceState.CombatBlocked', $combatBranch)
Require ($combatBranch -ge 0 -and $releasePending -gt $combatBranch -and
    $releaseCorpse -gt $releasePending -and $state -gt $releaseCorpse) `
    'Combat rejection must release exact pending/opening ownership before recording CombatBlocked.'
Write-Output 'PASS exact server combat rejection safely releases opening ownership'

$openPicker = $loot.IndexOf('case ProcessState.Open_Corpse:')
$pickerEligibility = $loot.IndexOf('DeferredApproachReady(c.Identity)', $openPicker)
$use = $loot.IndexOf('chest.Use();', $openPicker)
Require ($pickerEligibility -gt $openPicker -and $pickerEligibility -lt $use) `
    'Independent Open_Corpse selection can bypass shared deferred/combat eligibility.'
Require ($loot.Contains('&& DeferredApproachReady(x.Identity)')) `
    'NextMissionLoot no longer applies shared deferred/combat eligibility.'
Write-Output 'PASS both mission chest pickers share deferred/combat eligibility'

Require ($dungeon.Contains('_loot.MissionCombatFree = () => !_readiness.InCombat')) `
    'Dungeon readiness no longer supplies verified combat-free evidence.'
Require ($dungeon.Contains('!_objective.RewardConfirmed')) `
    'Reserved objective lifecycle no longer gates combat-blocked loot release.'
Require ($dungeon.Contains('if (_loot.UnfinishedMissionLootCount > _loot.CombatBlockedMissionLootCount)')) `
    'Combat-blocked ordinary loot can trap the reserved objective finale.'
Require ($dungeon.Contains('_loot.MissionCombatFree = null;') -and
    $dungeon.Contains('_loot.MissionCombatBlockReleaseAllowed = null;')) `
    'ManagerLoot mission delegates can dangle after mission stop.'
Write-Output 'PASS reserved FindPerson may finish before post-objective combat-blocked retry'

Require ($loot.Contains('SetMissionSource(blockedIdentity, MissionSourceState.CriticalBlocked)')) `
    'Mission-critical combat-blocked loot no longer safe-stops.'
Require ($dungeon.IndexOf('if (_loot.CriticalBlockedMissionLootCount > 0)',
    $dungeon.IndexOf('private bool LootInRoom(Room room)')) -ge 0) `
    'Mission-critical combat-blocked loot is not checked before reselection.'
Require ($loot.Contains('SetMissionSource(blockedIdentity, MissionSourceState.SkippedUnavailable)')) `
    'Ordinary combat-blocked loot lacks an explicit bounded fallback.'
Require ($loot.Contains('it was not counted as successfully looted')) `
    'Bounded ordinary fallback does not diagnose non-success semantics.'
Write-Output 'PASS critical safe-stop and bounded ordinary fallback remain distinct'

Require ($dungeon.Contains('DungeonApproachPlanner.Candidates(_layout,') -and
    $dungeon.Contains('new[] { 2.5f, 3f }, 0.4f)')) `
    'Chest interaction geometry changed in the combat-only batch.'
Write-Output 'PASS existing in-range chest geometry is unchanged'

Require ($loot.Contains('BeginMissionControl();') -and $loot.Contains('ReleaseMissionControl();')) `
    'ManagerLoot mission ownership lifecycle is missing.'
Write-Output 'PASS ManagerLoot mission ownership remains explicit and releasable'

Require ($loot.Contains('&& MissionContainerAcquisitionReady(x.Identity)') -and
    $loot.Contains('MissionContainerAcquisitionReady(c.Identity)')) 'Both acquisition pickers must reject combat-active chests before ownership.'
$release = $loot.IndexOf('!MissionContainerAcquisitionReady(_pendingMissionLoot)')
$pause = $loot.IndexOf('if (MissionActionsPaused) return;', $release)
Require ($release -ge 0 -and $pause -gt $release) 'Pending unopened ownership must be rechecked even while actions are paused.'
$body = $loot.Substring($release, $pause - $release)
Require ($body.Contains('_pendingMissionLoot = MissionIdentity.None;') -and
    $body.Contains('_sourceAcquisition = null;') -and
    -not $body.Contains('Attempts++') -and -not $body.Contains('DeferMissionLoot')) 'Combat pause must release ownership without consuming retry budgets.'
Require ($dungeon.Contains('_readiness.InCombat && (!_loot.IsProcessingMissionLoot ||')) 'Approach timers must pause when combat resumes during unopened ownership.'
Write-Output 'PASS proactive acquisition and resumed-combat ownership gates preserve retry budgets'
# Execute the production acquisition expression with AO identity data stubbed.
$gate = [regex]::Match($loot, 'private bool MissionContainerAcquisitionReady\(MissionIdentity identity\) =>\s*([^;]+);').Groups[1].Value
Require (-not [string]::IsNullOrWhiteSpace($gate)) 'Production acquisition gate not found.'
Add-Type -TypeDefinition (@'
using System;
public enum IdentityType { Container, Corpse }
public struct MissionIdentity { public IdentityType Type; }
public class AcquisitionGate {
    public Func<bool> MissionCombatFree;
    public bool Ready(MissionIdentity identity) =>
'@ + $gate + '; }')
$g = [AcquisitionGate]::new()
$chest = [MissionIdentity]::new(); $chest.Type = [IdentityType]::Container
$corpse = [MissionIdentity]::new(); $corpse.Type = [IdentityType]::Corpse
Require (-not $g.Ready($chest) -and $g.Ready($corpse)) 'Missing combat evidence must block chests only.'
$g.MissionCombatFree = [Func[bool]] { $false }
Require (-not $g.Ready($chest) -and $g.Ready($corpse)) 'Combat/hostile aggro must block chests while preserving corpses.'
$g.MissionCombatFree = [Func[bool]] { $true }
Require ($g.Ready($chest)) 'Chest must become eligible after combat without traversal or retry charge.'
$g.MissionCombatFree = [Func[bool]] { $false }
Require (-not $g.Ready($chest)) 'Resumed combat must revoke chest acquisition eligibility.'
Write-Output 'PASS production C# acquisition gate covers unknown, combat, combat-free and resumed combat'
