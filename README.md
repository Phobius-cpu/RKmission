# RKMission

AO# plugin for solo Rubi-Ka missions in Anarchy Online. It combines the original
Mali Mission Roller 2.0, Mali Dungeon Map 2.0, and Manager.Loot interfaces with
mission travel, room exploration, combat targeting, door handling, and looting.

**Status (2026-09-28):** mission entrance acquisition is constrained to the
selected accepted mission and a fixed 6 m anchor radius. Ordinary shops/buildings,
conflicting quest context and ambiguous location matches are rejected. The
closest accepted mission is selected by the current movement mode's route
estimate and uploaded through AOSharp's native mission map/minimap interface.
Live entrance/local geometry supplies final height and alternate approaches.
Entry still requires exact mission/dungeon verification.
The user's build and in-game validation are pending; no local compilation or
tests were run. Dungeon exploration, combat, interior doors, lockpicking and loot
are unchanged by this entrance fix.

## Setup

- Use the classic client: the embedded Manager.Loot rejects the new engine.
- Compile `RKmission/RKmission.csproj` yourself. It targets .NET Framework 4.8
  and references AOSharpSDK, AOSharpSDK.SharpNav, and Newtonsoft.Json.
- Deploy the compiled output and dependencies through your AO# setup. Preserve
  the `Plugins/MaliMissionRoller2`, `Plugins/MalisDungeonMap2`, and
  `Plugins/ManagerLoot` folders beside `RKmission.dll`. The project copies
  their required JSON, UI, texture, and sound assets.
- Outdoor navmeshes are **optional**. If available, place them at
  `NavMeshes/<playfield id>.nav` beside the deployed plugin for full ground
  pathfinding. Without a usable mesh, AO# direct waypoints attempt bounded local
  ground approaches. Flight and descent do not require a ground mesh.
  Dungeon navmeshes are generated on verified entry.
- Keep a **Lock Pick** in normal inventory, enough lockpicking skill, and
  free inventory or configured backpack space.
- Open `/ManagerLoot` to choose loot rules. Review its reverse and delete
  settings; enabling Delete can remove items left in containers.

## Quick start

1. Roll and accept any number of Rubi-Ka missions yourself. Use Mali's original
   roller window, the game UI, or your preferred method. RKMission does not
   request offers, choose rewards/types, or accept missions automatically.
2. Check `/rkm missions`, configure `/ManagerLoot`, and use `/rkm start` to
   arm local takeover. There is no target-zone filter or RKMission roll limit.
3. Travel between playfields yourself by any means. When your current outdoor
   playfield contains accepted missions, RKMission captures your position as
   the origin and compares route costs to accepted mission anchors in the active
   movement mode (mission ID breaks ties). Ground uses complete mesh distance
   when available, otherwise direct horizontal distance; flight uses horizontal
   distance while entrance height is unresolved. Only the winner receives final
   terrain/flight planning. RKMission uploads that exact accepted mission through
   `Mission.UploadToMap`, updating the game's map/minimap destination on selection.
   Estimates do not guarantee full reachability.
4. Default `/rkm travel auto` reads AO# `MovementState.Fly`. Use
   `/rkm travel ground` or `/rkm travel flying` to override route selection for
   this plugin session. These commands do not equip or remove a vehicle.
5. On foot, use a complete mesh path when available, otherwise sampled local
   waypoints/arcs. Flight follows committed world-space waypoints. Within 12 m
   of the marker, both modes consider only mission-constrained doors within 6 m
   of that anchor. They rank valid approach sides, using live origin and local threshold
   height alternatives instead of demanding the marker's exact X/Y/Z.
   Flight aligns to a selected side/height before closing on the threshold;
   ground follows terrain and checks the live door's actual interaction range.
   **Stay in your flying vehicle**: the bot does not change equipment. Manual
   landing near the anchor continues the same mission through ground acquisition.
6. Door use refreshes the live identity and checks actual distance and the short
   interaction corridor. No transition after use triggers a short threshold
   crossing, then alternate sides of a valid entrance. Without a valid live door,
   bounded radial waypoints search within the same 6 m radius. Rejected shops
   and farther unlinked doors never become recovery targets. Arrival or a use command alone never
   proves entry. AO# must associate the dungeon with the exact selected mission
   for a stable second before the existing `MissionDungeon` logic starts.
7. Check the game's objective/reward. Use `/rkm complete` (or
   `/rkm complete <bound mission id>`) to record confirmed completion, then
   **exit the dungeon yourself**. While still armed, the bot chooses the next
   nearest accepted mission from your new origin in that same outdoor playfield.
   If none remains, it disarms; travel to another playfield yourself and use `/rkm start` again.

Starting inside a mission uses AO#'s exact current-dungeon mission lookup;
it never guesses from mission-list order. Stop with `/rkm stop` whenever you
want manual control. Stop/start abandons the active run binding and permits
retrying an unfinished accepted mission.

## Travel and completion boundary

- Acceptance/removal is polled from `Mission.List` about once a second. Only
  resolved outdoor Rubi-Ka destinations are travel eligible. Removal can mean
  reward, deletion or expiration; it is not proof of completion. Identity,
  objectives, dungeon binding and completion evidence remain attached to the
  selected accepted mission.
- Nearest selection compares active-mode route estimates from one captured
  origin, logging every eligible mission's anchor/cost/source. Complete ground
  paths and direct fallback estimates may be compared; the latter are explicitly
  estimates. Only the selected mission receives final terrain/flight planning.
  Identity and anchor remain fixed during travel; acceptance/playfield/anchor
  changes invalidate travel and trigger a new selection from the current origin.
  Map upload uses only that exact live accepted mission, never an offered mission
  or generic waypoint. Upload is repeated on mission/anchor changes and outdoor
  reselection after zoning. The API has no marker readback/acknowledgement.
  Completion confirmations and travel settings are session state; no quest is deleted.
- Ground uses complete navmesh paths, then local direct waypoints if the mesh
  is absent/disconnected. Fallback samples multiple headings/radii and remembers
  visited/failed points. Flight retains the connected obstacle/descent planner,
  committed waypoint execution, braking and observed blocked-leg/descent memory.
  Surface probes rank approach alternatives; synthetic hits do not discard the
  mission. Inconclusive final searches can attempt bounded advisory paths that
  still exclude observed failed movement. No direct player position, altitude,
  speed or movement-state writes are introduced.
  Coarse flight holds departure height until acquisition (obstacles can require
  climbs); stale quest Y cannot command an early climb/descent. Direct ground
  forward steps use horizontal distance before sampling local terrain, so stale
  Y cannot shrink a normal step to a negligible horizontal movement.

### Mission anchors, door association and map destination

`AcceptedMissions.Refresh` copies `Mission.Location.Pos` from AOSharp's
`GetQuestWorldPos`. Treat that accepted mission coordinate as the search anchor;
AOSharp Vector3 uses X/Y/Z with **Y altitude**, while AO displays X/Z/height(Y).
Quest elevation remains provisional until live entrance/local geometry resolves it.

The previous `aa1e0af` acquisition scanned all doors within 40 m, ranked proximity
and tried alternate buildings. This could send the player into a nearby shop;
post-zoning verification was too late to prevent an unrelated interaction.

Inspected RKMission main `aa1e0af`, the embedded Mali `MissionView.PingClick`, and
[AOSharp Mission](https://github.com/anarchydevs/aosp.knows-aosharp-mods/blob/master/AOSharp.Core/Mission.cs),
[Door](https://github.com/anarchydevs/aosp.knows-aosharp-mods/blob/master/AOSharp.Core/Dynel/Door.cs),
[Dynel](https://github.com/anarchydevs/aosp.knows-aosharp-mods/blob/master/AOSharp.Core/Dynel/Dynel.cs),
[Stat](https://github.com/anarchydevs/aosp.knows-aosharp-mods/blob/master/AOSharp.Common/GameData/Stat.cs)
and Playfield/DynelManager reference sources. `Mission.UploadToMap` forwards the
exact mission identity to the native GUI upload interface, already used by Mali's
mission map button. RKMission refreshes that accepted mission and location before
calling it. No replacement map renderer or waypoint system is introduced.

`Playfield.Doors` exposes live `IdentityType.Door` objects. Outdoor room links
cannot establish ownership. `Dynel.GetStat` exposes `QuestInstance`, `BuildingType`
and `BuildingInstance`, but the reference does not document a complete outdoor
quest-door mapping or building type codes. A positive matching quest-instance stat
is an optional identity hint within the same bounded anchor radius; it is not a
newly proven SDK ownership guarantee. Mission source/objective identities are not
assumed to be entrance identities. Ordinary context and conflicting stats still
reject a door, even if its name includes 'mission'. Final ownership is verified
by the existing exact current-dungeon mission lookup after zoning.

### Constrained entrance acquisition

- Begin acquisition within 12 m of the selected accepted mission's captured
  anchor. Scan every two seconds; candidate doors must be valid, finite, in the
  selected outdoor playfield and within **6 m horizontally** of that anchor.
  This radius never expands after missing doors or failed interactions.
- Reject positive `QuestInstance` values different from the selected mission ID.
  Reject names indicating ordinary shops, stores, buildings, apartments, bars,
  clubs, banks, headquarters or transport entrances. Without a matching quest
  hint, reject nonzero building type/instance context rather than guess undocumented
  codes. Unreadable context is rejected. Open/locked flags and corridor probes only
  rank approaches after this association gate.
- Without a quest hint, a door must match the selected anchor more closely than
  any distinct accepted mission anchor; ambiguous ownership is rejected. Refresh
  the accepted mission context during travel, including newly accepted missions.
  Prefer matching quest hints when available. Otherwise allow only the closest
  neutral door threshold; similar-offset doors more than 0.5 m apart with offsets
  within 1 m are ambiguous and held. A farther unlinked door never becomes the
  next destination just because the first door's use failed or it disappeared.
  Bind the fallback threshold once selected; a refreshed identity must remain
  within 0.5 m of that threshold. Coincident mission
  anchors can share a threshold; exact dungeon verification still decides ownership.
- Generate eight approach sides at 1.5 m and four at 3 m from valid entrances,
  keeping only targets within the same 6 m anchor boundary. Use live rotation or
  a player-facing fallback. Try live origin height, supported local threshold
  floor (with 1.5 m flight clearance) and nearby grounded-player height. Ground
  follows terrain; flight stages horizontally and aligns to the selected height.
  The prior PF665 measured point is only a bounded coarse/search hint for accepted
  anchors within 2 m of it, never an ownership link or a wider candidate radius.
- Refresh live identity, mission/context/radius and actual <=2 m horizontal and
  <=3.5 m 3D use range. Immediately before every `Use`, force the full candidate
  scan, including newly loaded competing doors, and refresh the live accepted
  mission identity. Check the short corridor ending 0.6 m before the door face.
  A disappearing/moved/rejected door yields to another valid approach, with no
  native object retained across ticks.
- Send at most two uses per approach four seconds apart. Without zoning, try a
  short 0.8 m threshold crossing only if its target remains within the boundary,
  then another valid side. If no valid door remains, search the anchor and eight
  headings at 2/4/6 m; measured-hint offsets are clipped to the original anchor
  boundary. Search waypoints never interact with rejected doors. Arrival and
  command delivery are not successful entry.
- Preserve the existing eight-second search and eighteen-second approach stall
  handling, finite-attempt exhaustion plus 90-second no-progress bound, and
  15-minute total travel cap. Mode/side changes do not reset overall progress.
  Existing obstacle planners can detour around geometry; they do not add doors
  or enlarge the entrance search. Dungeon exploration, combat, interior door
  handling, lockpicking and loot remain unchanged.

A location-only fallback cannot prove ownership of an unlabelled, statless door.
The small radius, context checks and ambiguity rejection intentionally prefer
holding/failing over visiting other buildings. Genuine entrances beyond 6 m or
with unlinked building context may be refused; inspect rejection logs for an
explicit association rather than increasing the search radius. The user must
validate stat values, map/minimap display and entrance behavior in game.

## Commands

| Command | Purpose |
| --- | --- |
| `/rkm` or `/rkm status` | Show armed state, movement mode/phase, accepted count, bound mission/progress, and dungeon status. |
| `/rkm missions` | List tracked mission IDs, entrances/playfields, objectives, acceptance, and completion state. |
| `/rkm travel auto\|ground\|flying` | Set session travel mode; default auto uses the actual flight state. |
| `/rkm start` | Arm accepted-mission monitoring/local takeover, or verify and resume the current dungeon. |
| `/rkm stop` | Stop RKMission movement and dungeon automation. User-owned roller controls remain independent. |
| `/rkm complete [mission id]` | Record the user's confirmed reward for the verified bound mission; exit yourself to continue locally. |
| `/rkm zone <id>` / `/rkm rolls <count>` | Retired commands: display the new manual rolling/all-missions boundary. |
| `/rkm loot` | Show guidance to use `/ManagerLoot`; does not open a window. |
| `/ManagerLoot` | Open the original loot rule list and settings. |
| `/lm` | Toggle Manager.Loot's independent enable state. |
| `/printitems` | Toggle Manager.Loot's item-printing option. |
| `/rkm map` or `/mapsettings` | Toggle Mali's map settings window; configure map visibility there. |
| `/mmr maxitems <count>` | Change the roller's displayed-item limit. |
| `/mmr shopvalue <value>` | Change the roller's shop-value factor. |

Bare `/mmr` has no action in this embedded version. Configure rolling through
its window. RKMission enables Manager.Loot as needed for a room; it can remain
independently enabled after a run if you enabled it yourself. `/lm` toggles that state.

## Current behavior and recent fixes

- Mali's world-space room outlines provide safe interior waypoints and map
  visible enemies, corpses, and containers to rooms. Discovery follows what
  the client has loaded or spawned.
- The closest usable adjacent unvisited room takes priority. Otherwise the
  bot follows the shortest available chain through visited rooms to another
  unvisited room, logging intermediate and goal rooms.
- A dedicated crossing approaches the mapped threshold, resolves a live door
  within 3 m, opens/lockpicks as needed, then continues inside. Locked/closed
  flags first trigger a passage probe; a distant door identity cannot redirect it.
- Entry requires 500 ms of stable target-room detection and a safe interior
  position. Combat, loot, and normal room selection resume after confirmation.
  The reverse connection has an eight-second cooldown.
- Failed crossings block a connection for 30 then 90 seconds; a third failure
  blocks it for that run. Other reachable routes are considered.
- Combat interrupts loot approaches. Nearby attackers, hostile spawned
  entities, and Alarm Sentries are considered alongside room enemies;
  players and the local player's pets are excluded.
- Manager.Loot handles container opening, chest lockpicking, rules, and item
  transfers. A blocked loot approach gets an alternate attempt, then an
  unreachable object is skipped with a log for that run.
- The `adjacent` CS0136 compile conflict in `MissionDungeon.NextRoom` was
  fixed using distinct target and traversal names without changing behavior.

## Settings and troubleshooting

Manager.Loot keeps character lists under
`%LOCALAPPDATA%\AOSharp\ManagerLoot\<character>` and shared lists under
`%LOCALAPPDATA%\AOSharp\ManagerLoot\Shared`. These personal runtime settings
are separate from the source backup. Roller/map settings remain in their
deployed plugin folders.

- **No rolling:** RKMission no longer initiates rolling. Use Mali's original
  window or the game UI to roll/accept missions yourself.
- **Waiting for local missions:** inspect `/rkm missions`, acceptance and
  resolved destination; travel to the matching outdoor playfield. `/rkm start`
  is required again after the local chain finishes.
- **Route diagnosis:** `Nearest entrance selected` logs the captured origin,
  chosen mission/anchor, estimated distance, active route/mode, route cost,
  movement state and mesh availability. `Mission route estimate` reports every
  eligible mission's cost/source in the same movement mode. `Map/minimap marker
  update` reports the exact selected mission upload command. Finite estimates remain attemptable even when
  clearance probes hit. Watch `Active movement` for the actual waypoint,
  `progress` for final-target distance,
  `Entrance door accepted` / `Entrance door rejected` for live position, anchor
  offset, quest/building context and rejection reason; `Entrance selected` shows
  the accepted identity, selected side and height source,
  `Flight path committed` for the entire waypoint sequence and replan reason,
  `complete=True/False` for complete routes versus validated sections,
  `Flight committed path leg` / `Flight committed prefix leg` for progress through that sequence, and
  `Flight blocked leg recorded` / `Flight obstacle search waiting` for observed
  obstruction and bounded search retries. `advisory=True` / `Flight committed attempt leg`
  identifies an attempted estimate when surface search is inconclusive. Watch
  `outside descent prefix` / `outside descent and entrance return`,
  `outside alignment for descent` and `descending to approach height` for roof avoidance.
  The descent search reports clear columns/drops and the direct-drop surface Y.
  Watch `Entrance scan`, `Entrance aligned`, `Entrance interaction`,
  `Entrance alternate attempt` and `Entrance search waypoint` for acquisition.
  Interaction logs distinguish sent commands from absent zoning and threshold
  crossing; `Entrance interaction result` distinguishes exact verified entry,
  mismatched/unidentified dungeons and failed travel. Alternate logs explain
  valid candidate/side changes. Flight progress
  includes the vertical gap and velocity. Movement labels use the committed
  leg's starting altitude so slight corrections cannot alternate/log every tick.
  Idle selection checks retry every five seconds; mode warnings are suppressed
  when unchanged. During movement, the selected mission remains fixed.
- **Fallback stopped:** `Local travel HARD FAILURE` identifies an actual
  timeout/no-progress, invalid coordinates/state, or entrance failure. Three
  obstruction probes no longer stop a run. Move around the obstacle or closer
  to the entrance, then `/rkm start`. An optional outdoor mesh improves difficult ground routes.
  Do not assume a direct estimate guarantees passage through every obstacle.
- **Vehicle entry:** flight routes continue to the entrance with the vehicle
  equipped. No dismount prompt/wait exists. Mode selection does not change
  equipment. Explicit ground mode requires actual ground movement.
- **Entrance/handoff failure:** read the coordinate, door identity, route cost,
  context rejection and entry logs. The 6 m fallback is intentionally conservative;
  a shop, unlinked building, competing mission anchor or ambiguous door does not
  become a recovery target. Location-only ownership is unconfirmed; unmatched or
  unidentified dungeons still stop/hold the handoff.
- **Clearance without confirmed completion:** check the objective/reward in
  game and use `/rkm complete` for that bound mission. Deleted/expired missions
  are not automatically marked complete; stop/start to abandon that binding.
- **Map missing:** the original map recommends the launcher's
  `Direct 3D T&L HAL` graphics setting.
- **Door failure:** check the transition log, Lock Pick, and skill. Temporary
  route blocks may expire and allow another attempt.
- **Skipped loot/items left behind:** check skip logs, rules, and free space.
  An empty rule list can still open containers; transfers follow loot settings.
- **Stopped run:** read the reason in chat, including death, unreachable
  enemies, failed routes/missing geometry, missing objective items or objectives,
  and AO# exceptions.

The user owns rolling, selection, inter-playfield transport, vehicle equipment,
reward confirmation, and dungeon exit. RKMission owns local route selection,
travel/door entry, verified dungeon handoff, and same-playfield continuation.
The working dungeon systems continue to own exploration, combat and loot.

## History and local backup

`PROJECT_MEMORY.md` stores durable project context and `CONVERSATION_LOG.md`
stores user-visible conversation summaries. GitHub `main` is the source of truth.
The prior source backup (created before this travel revision) is at:

    C:\Users\Sumiko\OneDrive\Desktop\RK Mission Proj

That backup is not refreshed by this revision; pull GitHub `main` for the new
source and usage notes. The user compiles and tests in AO#; no local compilation
or tests were run for this update.
