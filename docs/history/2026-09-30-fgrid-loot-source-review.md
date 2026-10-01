# FGrid, loot, and source-comparison checkpoint

Recorded 2026-10-01 from the 2026-09-30 conversation "Análise de plugins AO#".
The authoritative GitHub `main` before this documentation update was
`253d8c12542d47a626ffbd349ae0f858d9cf9749`. This checkpoint preserves
conversation context and design direction; it makes no functional code change.
Earlier FGrid, Scotty, loot, pathing, and mission-objective history remains in
`PROJECT_MEMORY.md`, `CONVERSATION_LOG.md`, and the prior history checkpoints.

## Verified Fixer Grid learning and the multi-exit gap

Current main includes generic `Exit the Grid` portal learning after verified
zoning. The live log supplied by the user was:

```text
Learned Fixer Grid floor 10 portal 1478272517 at (290,8,67,2) for playfield 570 from verified zoning.
```

`FGridServiceProvider` currently persists a `Dictionary<int, LearnedExit>` in
`RKMissionData/fixer-grid-exits.json`. `LearnedExit` contains floor, identity,
and portal X/Z inside Fixer Grid. The map key is destination playfield, and a
new observation assigns to that key. Consequently, a second portal to the same
playfield replaces the first. The record also lacks the outdoor arrival
position needed to compare exits for a selected mission.

Future design: store a list of verified exits per playfield, keyed or
deduplicated by stable portal identity and floor. Each exit should retain the
FGrid floor, portal identity, portal position inside FGrid, outdoor arrival
playfield and position after verified zoning, and verification metadata. A
schema migration should preserve the existing learned route. For a selected
mission entrance, choose the exit with the least estimated outdoor travel cost;
use a valid outdoor route/path estimate when available and treat straight-line
distance only as a fallback estimate. Keep final destination verification.
Do not manually map many more portals before the multi-exit learner exists:
later observations for the same playfield can overwrite earlier ones.

## Ordinary loot and mission progression

The user observed ordinary containers that were unpickable because of
insufficient skill and others marked unreachable because internal room walls
defeated the approach. A live diagnostic included:

```text
Ordinary loot remains skipped or unfinished; objective completion and automatic exit are held. ordinary skipped=3, ordinary unfinished=3... skipped still visible
```

The failure mode is double treatment of terminally skipped ordinary loot as
unfinished work. Ordinary loot that has reached a terminal skip outcome must
not block objective completion or automatic exit merely because it remains
visible. Exact objective/reserved loot remains blocking and requires its own
recovery or completion path. Current main already excludes skipped identities
from `UnfinishedMissionLootCount` and ordinary room checks; the earlier focused
fix is documented in `PROJECT_MEMORY.md`. This checkpoint records the live
evidence and a future explicit state model: `Pending`, `Completed`,
`TerminallySkipped(reason)`, and `CriticalBlocked`. Counts and diagnostics
should derive from a single state per identity, so skipped and unfinished
cannot both describe the same ordinary source. This is a design recommendation,
not a claim that the state model is implemented or newly tested in game.

## Eight supplied source archives and integration conclusions

The source-comparison discussion covered:

1. `aosharp.newbots-master.zip`
2. `aosharp.utils-master.zip`
3. `aosp.knows-aosharp-mods-master.zip`
4. `aosp-bots-master.zip`
5. `malis-dungeon-map-2.0-master.zip`
6. `malis-mission-roller-2.0-main.zip`
7. `RKmission.zip`
8. `knows-helpers.zip`

The strongest integration direction is to keep RKMission's lifecycle,
`MovementArbiter`, and `SMovementController`. A shared `DungeonApproachPlanner`
should produce reachable interaction candidates for ordinary loot, combat, and
mission objectives. Its path API should report whether a navmesh route is
complete; movement acceptance alone must not be mistaken for a complete path.
Rank valid candidates by route cost instead of target distance. Mali dungeon
walkable and collision geometry can generate candidate points around internal
walls. Later, weighted room routing with Dijkstra or A* can improve over plain
BFS where edges have different costs.

The `aosp-bots` combat handlers are useful as future profession action
profiles, not a second combat controller. Backpack-aware inventory capacity
and `knows-helpers` recovery/watchdog patterns are useful secondary work.
Avoid direct `LocalPlayer.Position` writes, `WarpManager` teleports, competing
`MovementController` implementations, and encounter-specific hardcoded
movement. These conclusions are recorded from the source-comparison
conversation; this documentation task did not re-audit or import the ZIPs.
