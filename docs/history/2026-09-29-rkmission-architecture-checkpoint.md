# RKMission architecture checkpoint — 2026-09-29

This is the timeline anchor for the user's continuation from “Análise de plugins AO#”. It summarizes visible decisions, current source, and unverified work. The current GitHub `main` at the start of this revision was `3cd0ec3`, 35 commits ahead of the 2026-09-28 local backup; those newer entrance, Fly, objective, readiness, and exit improvements were preserved.

## Source and design decisions

- RKMission remains the coordinator and sole AO# plugin entry point. It stays on `net48`, AOSharpSDK `1.0.106`, AOSharpSDK.SharpNav `1.0.44`, and Newtonsoft.Json `13.0.3`.
- Preserve the embedded Mali Mission Roller 2.0 (`MainWindow.StartZoneRolling`, `MissionView.AcceptMission`), Mali Dungeon Map 2.0 (`DungeonMapFactory.GetDungeonData` world-space room outlines), and Manager.Loot (`BeginMissionRoom`, `NextMissionLoot`, original rules/loot processor).
- Preserve `MissionDungeon`, `DungeonLayout`, the current stateful `ApproachDoor → ProbeDoor → OpenDoor → CrossDoor` logic, failed-edge and reverse-cooldown handling, objective reservation/readiness, `LocalMissionTravel`/`LocalMovement` Fly and ground entrance navigation, and exact `Mission.FindMissionForCurrentDungeon` handoff. Do not replace these with the older DungeonRunner or AOSharp.Navigator movement controllers.
- Adapt Neko's mission key → ACGEntrance candidates and feedback IDs (`0x0FCA6FF9` wrong key, `0x0BC6E104` accepted, category `0x6E`). Its `ACGEntrances.json` and `GridTerminals.json` are included as source data.
- Adapt DungeonSolver/DungeonRunner's `Button (up/down/boss)` discovery, forward/backward floor transitions, floor navmesh reload, and rubberband path invalidation. Keep RKMission's existing return-to-entry exit path.
- Adapt AOSharp.Navigator's playfield-link graph and verify each expected destination playfield after zoning. Its supplied graph is sparse; imported data do not imply all-Rubi-Ka coverage. NavGen's outdoor mesh inspection concepts are reference material, not a second controller.

## Implementation state in this commit

| Piece | State |
| --- | --- |
| Mission entrance | New `MissionEntranceResolver` tries an unambiguous mission key against the Neko ACG candidate list, reacts to wrong/accepted feedback, and times out. Existing `LocalMissionTravel` remains physical-entry fallback. `RkMissionBot.TickDungeon` still requires exact mission identity and a stable room after zoning. |
| Warp bot | `ScottyboiWarpProvider` looks up the bot by name, requests current `!help`, parses a command only for the target playfield name, accepts only that bot's team invite, verifies destination after teleport, and backs off on failure. Actual help format and availability need in-game evidence. |
| Movement ownership | `MovementArbiter` is shared by local Run/Fly legs, warp/graph travel, dungeon room/door/lift/combat/loot/objective/exit paths, and readiness movement. Owner changes halt the previous route. Existing Mali/Manager.Loot interactions remain in their classes. |
| Rubi-Ka travel | `RubiKaTravelPlanner` prefers warp, then searches the imported Navigator graph for terminal/Grid/teleporter/zone-border links and advances only after the observed playfield matches the link. Direct local movement handles only the current playfield. |
| Floors | `DungeonLiftController` caches observed buttons by floor; `MissionDungeon` clears a floor, routes to a forward lift through its existing room graph/crossing, verifies a floor change and reloads that floor's navmesh. During exit it uses backward lifts before the existing entry-room exit procedure. |
| Completion/exit | Current `MissionObjective` and `AcceptedMissions` already distinguish supported objective acknowledgement, collected return item, manual confirmation, and unconfirmed quest removal. The existing automatic exit is preserved. Ordinary room/loot clearance alone does not claim a reward. |
| Recovery/inventory | Optional automatic cycle waits for reclaim and replans a still-accepted mission. Full main inventory stops before new room loot. Current readiness/combat recovery is preserved. |
| Coordinator | `/rkm auto` enables automatic Mali zone rolling, accepted-mission selection, ACG/warp/graph/local travel, exact dungeon handoff, clear/exit, next accepted mission, return to the remembered roller terminal, and roll again. `/rkm start` and `/rkm local` retain the previous local-only workflow. `/rkm zone <id>` and `/rkm rolls <count>` configure automatic rolling. |

## Limits and next in-game evidence

1. Compile with the user's AO# setup, then test one short same-playfield mission first. This revision was not built or tested locally under the user's established workflow.
2. Confirm mission-key inventory identity/name, Neko feedback, exact dungeon association after an ACG warp, and behavior with multiple accepted missions/keys. The resolver falls back when key choice is ambiguous.
3. Capture Scottyboi's real help menu, team invite flow, and destination responses. The parser intentionally accepts only an exact destination association; unrecognized menus fall back.
4. Expand and verify travel data. AOSharp.Navigator's archived graph has only a small set of nodes. Whompa and Fixer Grid execution are still staged; Neko `GridTerminals.json` is data only. Outdoor navmeshes are optional for local ground travel but graph links and terminal placement need in-game checking.
5. Test up/down and boss lifts, floor IDs, room-route recovery, return lifts, and the existing external exit door across multi-floor dungeons. Failure stops with a reason rather than substituting an interior doorway.
6. Confirm objective acknowledgement and reward semantics for each mission type. Return-item terminal hand-in remains manual; mission-list disappearance alone is never considered a reward. Profession-specific heals, pets, rebuffs, and combat policy remain outside this revision.
7. Test death/reclaim, full inventory with Manager.Loot bag rules, terminal return, and repeated rolling. The automatic cycle is opt-in until these checks pass; missing graph links or bot help stop safely with a reported reason.

No hidden conversation state, credentials, or private reasoning is stored here. `CONVERSATION_LOG.md` contains the user-visible chronology, and Git history is the code timeline.
