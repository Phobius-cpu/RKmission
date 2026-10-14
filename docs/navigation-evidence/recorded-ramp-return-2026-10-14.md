# Recorded ramp return — AWAITING AO TEST

Baseline: 7fc83fd725409a7e90e6d8a5da3fb2f9e31c1157.
Workstream: Dungeon.

The runtime successfully completed `U Turn Ramp V2`, looted the upper container,
then stalled twice approaching the room 4->1 ground-floor doorway. This change
retains a copy of the selected physical ascent slice when loot topology playback
completes, before playback state is cleared. It does not resume the original full
recording, modify route files, infer coordinate transforms or create new points.

When the same room/floor/playfield needs a lower doorway, BeginTransition prepares
that completed ascent in reverse. A separate RecordedReturn phase owns movement,
blocks both the initial door destination and subsequent door probing, and ends at
the original slice's ground attachment. Normal source-side doorway approach then
resumes with fresh timing/progress state. It does not steer directly to the door
while the character remains on the upper platform.

Reversal requires a continuous retained ascent, >=1.5m vertical span, <=4m upper
connector, <=0.75m upper height difference, and <=1.25m ground-to-door height
difference. State resets on mission start/stop and floor changes. Return failure
or unsafe attachment stops explicitly, without charging the door-edge blacklist.
Descent has a 75-second total limit and a 10-second no-displacement limit; combat
pauses halt descent and refresh those deadlines. Opened-container settlement and
post-combat corpse ownership retain their existing gates.

Scope: completed recorded loot ascents followed by inter-room door transitions.
Partial/failed ascents and arbitrary generated ramps are not treated as proven
return paths. External exit-door navigation and multiple nested ascent stacks
are not changed by this focused fix.

Validation: RecordedRampReturnRegression plus nine related suites passed:
UTurnOperationalTopology, RecordedTopologySubpath, CombatRecordedTopology,
CombatLootTopologyContinuation, CombatAwareContainerDeferral, LootApproachRetry,
SameRoomTopology, PostCombatCorpseGuard, ObjectiveLifecycleReliability.
The new suite compiles and executes the production reversal/continuity methods
with stubbed AO vector data, verifies exact reversed coordinates, and rejects
wrong playfield, unrelated position, wrong elevation and discontinuities. Source
invariants cover initial/tick doorway gates, completion snapshot ordering,
room/floor ownership, deadline reset and failure behavior. Whitespace checks pass.
Full net48/AOSharp build remains for the user's local compile.

AO validation: repeat room 4 upper-container loot. Expect `Prepared recorded
return descent`, reverse waypoint progress, then `recorded return descent reached
ground attachment ... normal doorway approach resumed`, followed by successful
4->1 entry. Verify no doorway destination/blacklist failure while descending,
no descent for ordinary ground-floor loot, and safe pause/resume if combat starts.
