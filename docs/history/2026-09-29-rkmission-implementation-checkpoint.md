# RKMission implementation checkpoint — 2026-09-29

This continues the architecture checkpoint in the same directory and the feature
branch `feature/rkmission-architecture-2026-09-29`. RKMission remains the
coordinator on net48, AOSharpSDK 1.0.106 and SharpNav 1.0.44. The stable
Mali room geometry, stateful DoorTransition, failed-edge cooldown, exact
`Mission.FindMissionForCurrentDungeon` gate, and embedded Manager.Loot remain.
The implementation code milestone is `1a998f8`.

## Source adapted

| Source already extracted in the workspace | Adapted behavior |
| --- | --- |
| `aosharp.utils-master/Neko/KeyWarper.cs`, `ACGEntrances.json` | Key to ACG candidates, wrong/accepted feedback IDs, successful association cache. RKMission limits attempts to the selected mission vicinity and verifies exact identity after zoning. |
| `aosharp.utils-master/Neko/FgridWarper.cs`, `GridTerminals.json` | Fixer Grid terminal identities and one-at-a-time item use for a selected exit. |
| `aosp.knows-aosharp-mods-master/AOSharp.Navigator` | Playfield-link graph, Grid/FGrid/teleporter/zone-border transitions and destination playfield verification. RKMission retains its one movement controller. |
| `aosharp.newbots-master/DungeonSolver` | Observed up/down/boss lift semantics, combined with current floor navmesh reload and room-route transitions. |
| Existing Mali Mission Roller, Mali Dungeon Map, Manager.Loot, and RKMission | Rolling, room mapping, safe door crossings, native loot processing, readiness and dynamic `DungeonNavMeshFactory` generation remain integrated. |
| `knows-helpers.zip`: `CollectorBuddy/DiedState.cs`, `Extensions.cs`; `AutomatonTemplate/DiedState.cs`; `Shared/Kits.cs` | Garden Exit discovery/use, zoning and rez readiness, backpack kit scan. RKMission uses generic live-state checks rather than encounter waypoints. |
| `knows-helpers.zip`: `DB3Farmer`, `CollectorBuddy` team/chat/zone handlers; `Zod Finder 3000` | Expected-identity team invites, chat response parsing, post-zone delay, stuck/rubberband hooks. The old direct player-position assignments were explicitly excluded, including the pinned SDK's default stuck action, which RKMission overrides. |
| `knows-helpers.zip`: `DoorMap/PathState.cs`, `NavGenState.cs` | Confirmed the value of room routes and generating missing navmeshes. RKMission keeps Mali geometry and its current `DungeonNavMeshFactory` rather than the old movement controller or broad navmesh deletion routine. |

## Implemented in this continuation

- `MissionEntranceResolver` tries multiple accepted mission keys, prioritizes
  live ACG dynels near the selected mission and cached associations, then static
  Neko candidates. It backs off to local physical entry only after candidates
  fail. A successful key/entrance pair is cached only after exact mission
  verification in the dungeon.
- `MovementArbiter` prevents another subsystem from taking the controller
  while a doorway crossing owns it. Door completion/failure releases that owner.
  `Stuck` and `OnRubberband` now drive bounded repath, halt/rebuild and
  failed-edge replan tiers without writing player position.
- `ScottyboiWarpProvider` retries menu discovery with `help` once,
  observes teleport start/end, waits for a post-zone settle, verifies the
  target playfield, and leaves a team it joined. Its original single warp
  request and timeout backoff remain.
- `RubiKaTravelPlanner` uses the Neko Fixer Grid IDs when a Data Receptacle
  exists, verifies each zone, blacklists timed-out links and replans. The
  existing graph is still sparse and requires in-game validation. It skips
  Scotty requests for a nearby direct mapped transition.
- `CombatDriver` owns enemy approach, range/LOS and attack initiation while
  `MissionDungeon` selects enemies. `RouteToRoom` exposes the existing
  failed-edge-aware room route for lift, objective and exit targets.
- `MissionCompletionTracker` requires the exact quest to remain absent after
  a recent final action and either all relevant objective steps have proof or
  an explicit server completion message. Return-item hand-in stays manual.
- `DeathRecoveryController` waits for reclaim, rez sickness and zoning, runs
  the existing health/nano/buff readiness mechanism, uses a visible Garden Exit if present,
  and replans from actual game state. It does not restore stale movement.
- `InventoryPolicy` writes a user-editable
  `RKMissionData/inventory-policy.json` (default three free slots). Below
  threshold Manager.Loot stops optional corpse/chest work; exact mission
  interactions and exit continue.
- `MissionCheckpoint` stores only durable intent and identity, phase,
  destination, entrance room, floor, provider and objective evidence. A
  restarted plugin reconciles the accepted quest and current dungeon before
  replanning. No stale path or clearance claim is restored.

## Verification and limits

- A local net48 build with the pinned packages completed with zero errors.
  Existing nullable warnings remain; no game session was run.
- `knows-helpers.zip` was directly inspected at the path supplied later in
  the session. Its binaries, encounter paths, hardcoded raid states, WarpManager
  and direct position writes were not imported.
- Scotty's real current menu, team invitation and zone timing need in-game
  observation. The provider does not hardcode destination commands.
- Grid/FGrid and border links are limited by the imported graph; unreachable
  destinations still stop with a clear reason. Fixer Grid item/terminal use
  needs in-game confirmation.
- Lift direction, floor numbering, Garden Exit presence, ACG feedback and
  multi-key association need in-game confirmation. No direct position writes,
  old movement-controller replacement, raid teleports, or profession-specific
  combat rotations were imported.
- Dynamic dungeon navmesh generation already existed and remains the active
  approach. Cross-run cache or a generalized fallback for failed generation
  remains future work.
