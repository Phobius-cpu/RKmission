# Full Autonomous Baseline (`fullautonomous`)

**Project-history checkpoint:** 2026-09-29  
**Repository:** Phobius-cpu/RKmission  
**Purpose:** Baseline reference for future work toward indefinitely autonomous RKMission.

## Scope and evidence

This checkpoint records the intended architecture and remaining work from the 2026-09-29 “Análise de plugins AO#” discussion. **It is conditional on the latest implementation brief being integrated; it is not a claim that the full loop is implemented or validated.** The discussion did not include the Work session's completion report or commit list. At this checkpoint, [the repository README](../../README.md) still describes manual rolling/acceptance and manual inter-playfield transport. Reconcile the integrated code and its in-game results against this baseline before marking any expected capability complete.

## Intended full autonomous mission loop

```text
ROLL → ACCEPT → SELECT BEST MISSION
  → SCOTTY / GRID / FGRID / FALLBACK TRAVEL → LOCAL TRAVEL
  → RESOLVE ACG ENTRANCE → USE CORRECT MISSION KEY
  → VERIFY EXACT MISSION → EXPLORE → KILL → LOOT / LOCKPICK
  → HANDLE OBJECTIVES → USE LIFTS / CLEAR FLOORS
  → VERIFY MISSION COMPLETE → ROUTE BACK TO EXIT → EXIT
  → NEXT MISSION → RETURN TO TERMINAL → ROLL AGAIN
```

The coordinator should own transitions between these stages. An accepted mission, an entrance use, a zone event, or a disappearing quest alone is not proof that the intended mission or completion state has been reached. Verify the exact dungeon binding and confirmed completion before proceeding.

The intended recovery branches cover zoning verification and post-zone stabilization; stuck and rubberband recovery; alternate travel when Scotty or a route fails; death and reclaim recovery; persisted progress and restart reconciliation; exclusive movement ownership through arbitration; and safe low-inventory behavior that stops optional looting before slots run out.

## Architecture and source decisions

| Area | Retain or adapt |
| --- | --- |
| Coordination and dungeon core | RKMission remains the lifecycle coordinator. Keep MissionDungeon, DungeonLayout, Mali geometry, stateful DoorTransition, failed-edge handling and reverse-cooldown logic. |
| Loot and identity | Keep Manager.Loot for loot policy and the exact `Mission.FindMissionForCurrentDungeon` check for mission verification. |
| Entry | Adapt Neko `ACGEntrances` and `KeyWarper` concepts to resolve the entrance and select the correct mission key. |
| Rubi-Ka routing | Adapt AOSharp.Navigator playfield-routing concepts. Prefer Scottyboi for long-range travel, with Grid, FGrid and other fallback routes when unavailable or unsuitable. |
| Floors and exit | Adapt DungeonSolver and DB3Farmer floor/lift patterns. Use DoorMap's `RouteToRoom` and exit-routing concepts within RKMission's existing room graph and safe door transitions. |
| Recovery and readiness | Adapt knows-helpers patterns for death/reclaim, readiness, team invites, chat responses, zoning and stuck/rubberband events. |
| Movement boundary | Route movement through the arbiter and normal game mechanisms. Do not copy direct `LocalPlayer.Position` writes or WarpManager-style teleport/position manipulation. |

These are cannibalization decisions and integration inputs, not assertions that every source has already been incorporated.

## Status matrix

“Expected covered” means *if the latest brief is integrated as intended*. Every row still needs comparison with actual commits and in-game evidence.

| System | Expected after integration | Remaining hardening or proof |
| --- | --- | --- |
| Roll, accept and mission selection | Loop architecture planned | Roll filters, bad-roll/time-out recovery, terminal recovery; current README still says manual |
| Scotty and cross-playfield travel | Preferred provider plus Grid/FGrid/fallback routing planned | Command/chat parsing, online/offline behavior, destination verification and route coverage |
| Local travel and ACG entry | Approach, entrance resolution, correct key and exact mission binding planned | Awkward/multiple entrances, similar keys and zoning delays |
| Exploration and doors | MissionDungeon/DungeonLayout, Mali geometry, lock handling and edge recovery provide strong base | Unusual/disconnected room graphs, locked doors and unusable return edges |
| Combat, loot and objectives | General combat and Manager.Loot integration expected | Profession readiness, unreachable targets, all mission objective/action combinations |
| Floors and completion | Lift/floor handling, completion checks and routed exit planned | Lift layouts, boss floors, delayed mission updates and reliable exit verification |
| Death, zoning and movement recovery | Reclaim, stabilization, stuck/rubberband and movement arbitration planned | Real AO timing thresholds, no arbiter bypasses and retry limits |
| Persistence and inventory | Restart reconciliation and low-slot optional-loot stop planned | Crash/reload proof; actual bank/shop/deposit/sell/discard cycle is missing |
| Repeated mission cycle | Full autonomous loop is the design target | Long-duration in-game soak results and bounded failure escalation |

## Full autonomous versus indefinitely autonomous

**Full autonomous** means the bot can execute and repeat the mission loop above under supported conditions, including its planned recovery branches. It can still stop safely when an inventory or unrecoverable-state limit is reached.

**Indefinitely autonomous** means unattended operation can keep replenishing its ability to run missions over long periods, with disposal, character preparation, broad destination coverage, bounded retries and demonstrated stability. This checkpoint does not claim that standard.

The remaining work for indefinitely autonomous operation is:

1. Implement an actual bank/shop/deposit/sell/discard loop, with item-value rules, backpack arrangement and a return to the mission terminal. Stopping optional loot at low free slots is only a safe interim policy.
2. Add profession-specific post-death and pre-mission readiness: required buffs, pets, equipment/weapons, nano and HP restoration, and a minimum-readiness gate. Keep this separate from MissionDungeon so profession plugins can provide it.
3. Validate exhaustive transport and entrance coverage and fallback data across Rubi-Ka destinations, including Scotty, Grid/FGrid and local ACG approaches.
4. Add a watchdog, failure escalation and safe-stop policy for repeated or unrecoverable failures; persist enough context for a controlled restart.
5. Run long-duration in-game soak tests and tune AO timing/API edge cases, especially zoning, command replies, movement and delayed completion updates.
6. Validate the matrix of mission types and objectives, ACG entrance types, multiple keys, multi-floor dungeons, locked doors, Scotty online/offline, death, full inventory, zoning delays, rubberbanding, and unreachable mobs/chests.

## Starting point for indefinitely autonomous

Recommended work order:

```text
bank/shop disposal
  → profession readiness/recovery
  → watchdog/failure policy
  → coverage hardening
  → long-duration soak testing
```

First reconcile this baseline with the implementation Work session's actual commits and completion report. Then use this checkpoint as the project-history reference for all future work toward indefinitely autonomous RKMission. Record observed in-game evidence and exceptions rather than promoting an expected status to “complete” from design alone.
