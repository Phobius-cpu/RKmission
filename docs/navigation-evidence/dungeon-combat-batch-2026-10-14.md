# Dungeon + Combat batch — AWAITING AO TEST

Baseline: bc0f184dbd14800376fbe42caaf677a7017c40fb. The complete user recording
remains byte-for-byte unchanged (Git blob 87335ca54ace02d0ab4cffa280fb96866900cdaf).
Existing bundled and user routes are not replaced or deleted by this batch.

## Combat-aware container acquisition

The prior server-rejection deferral remains intact. A shared proactive eligibility
gate now prevents both NextMissionLoot approach selection and ManagerLoot's
independent chest picker from acquiring containers without verified combat-free
evidence. MissionReadiness includes player/pet attacks and hostile targeting of
player/pets. Corpses retain their existing eligibility. If combat resumes after
binding an unopened chest, ManagerLoot releases pending ownership even while
mission actions are paused, without incrementing approach or rejection attempts.
Dungeon approach timing resets across the combat pause; ordinary exploration and
post-combat selection remain available. Existing exact server-feedback cooldown,
critical-container handling and occurrence tracking remain in place.

## U-turn operational ascent

`Data/DungeonTopologyRoutes.json` adds exactly `U turn ramp - Upper Container`,
using original `U turn Ramp` points 0–15 inclusive from PF 14680242, captured
2026-10-12T12:37:00Z. The endpoint [291.6195, 8.015, 97.01] is a physically recorded
upper landing; the recording alone does not prove the container's exact location.
Upper-floor wandering and descent are excluded. No coordinates are invented.

The derivative is topology-only and forward-only, and cannot compete in generic
endpoint playback. An existing route with the same name retains authority.
A viable derivative takes preference over a generic recording only after the
existing 4m physical attachment and ordered vertical-progress gates, additional
0.75m attachment-height gates, matching playfield, and current-room endpoint
containment. Loot and blocked multi-level combat already prepare recorded
navigation before generated staging; this batch extends that existing selection.

DungeonLayout uses Mali outlines transformed into world-space room walls. There
is no supported recording-to-room anchor/transform. The recording around x285–293,
z90–104 therefore does **not** establish a match for live BBABEA3 around x50,z212.
No translation or rotation is inferred. A route recorded in that live room or
confirmed room-anchor metadata is needed before cross-instance reuse can be enabled.

## Validation

12 targeted PowerShell regression suites passed: CombatAwareContainerDeferral,
UTurnOperationalTopology, RecordedTopologySubpath, CombatRecordedTopology,
CombatLootTopologyContinuation, CombatRampRoute, LootApproachRetry,
ManagerLootAcquisition, CombatBlockedCorpseSettlement, DeferredLootRouteCooldown,
SameRoomTopology, SideRampsTopology. New tests compile and execute the production
C# acquisition predicate and subpath selector using stubbed AO data types; the
selector accepts the recorded ascent and rejects unrelated room-4 and flat targets.
Other checks are source invariants and fixture checks, not AO integration tests.
Full net48/AOSharp build was not run in this environment (no .NET SDK); user will
pull and compile locally. Git diff whitespace and immutable snapshot checks pass.

## Remaining AO validation

- Compile locally and verify the new Data JSON is copied with the plugin.
- During player/pet combat or hostile aggro, observe no chest ownership handoff,
  side-candidate exhaustion or approach-attempt charge. Verify exploration/combat
  continues and the chest is acquired after combat.
- Resume combat after chest binding; verify unopened ownership is released and
  completed/open containers keep their existing processing behavior.
- In matching world-space geometry, verify the derivative selection diagnostic,
  ordered ramp traversal and strict final container interaction.
- In the mismatched room-4 instance, verify conservative rejection. Capture a
  room-local recording/anchor before claiming deterministic reuse there.
