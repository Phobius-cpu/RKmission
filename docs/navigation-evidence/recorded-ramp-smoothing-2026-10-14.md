# Recorded ramp leg reduction — AWAITING AO TEST

Baseline: 6bf422824a6b915a98cde1f2d5a04f4a4c7efee9.

Recorded dungeon topology playback now selects the minimum number of legs subject
to conservative physical-corridor and mandatory-corner constraints. The dense
original recordings and route assets remain unchanged. Only the already selected
playback slice is simplified; route matching and the strict final loot approach
keep their existing behavior.

All retained vertices are original recorded points. Endpoints, substantial turns
(approximately 20 degrees or more), ramp slope joints and near-vertical joints
are protected. A merged leg may not pass a protected point, reverse its ordered
horizontal progress, deviate by more than 0.2m horizontally, or by more than 0.15m
from the sampled height profile. Dynamic programming finds the fewest legs that
satisfy these constraints. This retains ground alignment, ramp entry/climb,
turn-to-second-section, second climb and final upper landing adjustment.

The preserved U-turn ascent fixture reduces from 16 points / 15 sampled legs to
7 recorded vertices / 6 legs (original indices 0,7,9,10,11,13,15). Live recordings
such as U Turn Ramp V2 may produce a different count; runtime reports the count.

Playback completion stores both the reduced ascent and the dense physical
samples. Return descent uses the exact reversed reduced vertices, with no second
simplification or new selection. Dense-sample continuity and ordered-subsequence
checks validate the return, so long retained straight legs remain eligible without
relaxing the original recording's 5m sampling continuity rule. Combat-to-loot
continuations carry the same physical evidence.

Validation: the extended RecordedRampReturnRegression compiles and executes the
production simplifier/reversal methods. It verifies the six-leg U-turn result,
retained turns/slope joints, exact reverse descent, rejection of invented vertices,
and reduction of a 21-point straight corridor to one leg. Nine related suites
also passed: UTurnOperationalTopology, RecordedTopologySubpath,
CombatRecordedTopology, CombatLootTopologyContinuation, CombatAwareContainerDeferral,
LootApproachRetry, SameRoomTopology, PostCombatCorpseGuard,
ObjectiveLifecycleReliability. Git whitespace checks pass.

Full net48/AOSharp compilation remains for the user's local build. The separate
NavigationRouteSmoothingRegression requires a built RKmission.dll and could not
run here; it covers the unchanged generic travel simplifier. The new dungeon
simplifier is exercised directly by the compiled regression harness above.

AO validation: verify fewer waypoint legs, ramp corners and both climb sections,
strict final container approach, exact reduced-route descent, then ground-to-door
handoff. Check that longer straight legs navigate reliably with the AO movement
controller and that neither turn nor ramp edge is cut. Existing recordings are
preserved for adjusting tolerances if live movement disagrees with the harness.
