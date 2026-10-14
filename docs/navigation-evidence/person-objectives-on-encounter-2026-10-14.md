# Person objectives on room encounter — AWAITING AO TEST

Baseline: 484d14e9e252945de1204ce647232f8f5883b393.
Workstreams: Dungeon + Objectives/Lifecycle.

FindPerson and KillPerson no longer require whole-dungeon clearance before their
actions activate. A stable source-room encounter with the mapped exact target or
its live containment activates the existing objective action and latches permission
for the rest of the mission. Existing preflight and server acknowledgement remain.
KillPerson uses the existing exact-target combat owner. FindPerson uses its normal
range/ray gate and uninterrupted five-second observation, with player/pet attack
protection until completion is acknowledged. Ordinary threats in the current room
retain defensive priority before sustained FindPerson observation; other rooms'
enemies and unopened loot no longer postpone activation.

The existing NextRoom ordering is retained as a soft preference: choose an
unexplored non-objective room on an avoiding route first, allow cut-through travel
next, then allow the objective room itself when that is the remaining reachable
option. Entering a target room for transit also activates the person objective.
Previously unknown room metadata can activate through exact live containment.
The target room is preferred late but is never a strict last-room requirement.

Completed person objectives do not waive remaining exploration, ordinary combat,
loot settlement, floor progression or exit verification. PersonEncounterActive
keeps permission from being revoked by later global clearance scans. Encountered
FindPerson retains local observation ownership under its own aggro rather than
being sent back into the global sweep. Other objective types retain the previous
finale policy. Route files and navigation geometry are unchanged.

Validation: PersonEncounterActivationRegression compiles and executes the actual
production activation method with AO data stubbed, covering mapped FindPerson and
KillPerson, wrong rooms, unknown-room live containment, repeated activation,
preflight failure and unrelated objective types. Source invariants verify soft
routing preference, persistent permission, protected observation and retained
remaining-clearance/loot exit gates. Eight related suites pass:
FindPersonHostileAggro, ObjectiveLifecycleReliability, KillPersonFinaleOwnership,
CombatAwareContainerDeferral, AcceptedObjectiveMetadata, CombatBlockedCorpseSettlement,
PostCombatCorpseGuard, DeferredLootRouteCooldown. Whitespace checks pass.
Full net48/AOSharp compilation remains for the user's local build.

AO validation: enter a known target room before all other rooms are cleared and
observe immediate person-objective activation. Test both FindPerson under exact
protected-target aggro and KillPerson with exact target engagement. Verify unknown
room discovery, unavoidable target-room transit, ordinary local threats, server
acknowledgement, and continued exploration/loot after objective completion before
exit. Confirm other mission objective types still retain their previous ordering.
