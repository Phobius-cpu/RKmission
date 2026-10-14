# FindPerson finale under exact protected-target aggro — AWAITING AO TEST

Baseline: 519b0271b783fed2954aa5b935dedaf3396d6d9b.
Workstream: Objectives/Lifecycle.

Runtime: room 26, exact FindPerson target SimpleChar:7ADD1702 mapped to room 9,
repeated attacks by Emery Ratti (user confirmed this is the reserved target),
no transition, and a 120-second watchdog replan without progress.

Source cause: GuardReservedEnemy previously returned reservedAggro=false whenever
Finale && FinalActionsAllowed became true. FindPerson HoldEnemy remained active,
so ordinary combat could not engage the target. The combat branch then skipped
the reserved-sweep/objective-room routing path, halted movement, and ticked the
objective from the wrong room. MissionObjective cannot observe that target in
room 26 and intentionally resets its acknowledgement wait while PendingRoom is 9.
Readiness.Hold itself returns false in combat; no readiness exception is needed.

The fix preserves reserved aggro reporting after finale activation only for the
exact still-held FindPerson target attacking the player/pets. Existing combat
handling continues ordinary-enemy clearing and mapped-room routing before exact
objective observation. Released targets and KillPerson finale behavior retain
their previous policy. Target attack cancellation, pet protection, five-second
uninterrupted observation, server acknowledgement, and deferred-loot-before-exit
requirements are unchanged.

A failed FindPerson mapped-room route now retries with five-second diagnostics
and a 30-second limit, then stops explicitly without attacking the target or
accepting completion. Successful route ownership, target-room arrival, mission
start/stop, or loss of eligible finale/aggro state reset the route-wait budget.
Temporary edge cooldowns remain retryable within that bound.

Validation: eight suites passed: FindPersonHostileAggro, ObjectiveLifecycleReliability,
KillPersonFinaleOwnership, CombatAwareContainerDeferral, AcceptedObjectiveMetadata,
CombatBlockedCorpseSettlement, PostCombatCorpseGuard, DeferredLootRouteCooldown.
The updated suite compiles and executes the production aggro predicate, covering
before/after finale activation, released/unrelated targets and absent aggro.
Source regressions verify routing priority, bounded failure diagnostics, preserved
observation/protection/acknowledgement ordering and KillPerson behavior.
Git whitespace checks pass. Full net48/AOSharp build remains for local compilation.

AO validation: repeat FindPerson under Emery's aggro. Expect mapped room routing
from 26 toward 9, exact-target approach/selection, uninterrupted five-second
observation, accepted mission acknowledgement, then normal target release and
remaining loot collection before exit. Confirm other enemies still take normal
combat priority, player/pets do not kill the held target, temporary route failures
emit diagnostics rather than silent idle, and persistent failures stop explicitly.
