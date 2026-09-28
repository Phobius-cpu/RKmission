# RKMission

AO# plugin for solo Rubi-Ka missions in Anarchy Online. It combines the original
Mali Mission Roller 2.0, Mali Dungeon Map 2.0, and Manager.Loot interfaces with
local mission travel, room exploration, combat, door handling and looting.

**Status (2026-09-28):** outdoor travel/entrance navigation has been consolidated
into one state machine with Run/Fly movement, full directional probing and
persistent entrance diagnostics. Dungeon exploration, room navigation, combat,
interior doors/lockpicking, loot and objectives are unchanged. The coordinator
only adds diagnostics at the existing verified dungeon handoff. No local build,
package restore, tests or game run were performed; the user compiles and validates.

## Setup

- Use the classic client; embedded Manager.Loot rejects the new engine.
- Compile `RKmission/RKmission.csproj` yourself. It targets .NET Framework 4.8
  with the existing AOSharpSDK, AOSharpSDK.SharpNav and Newtonsoft.Json packages.
- Deploy the output/dependencies through AO#. Keep the embedded plugin folders
  beside `RKmission.dll`; the project copies their required UI/assets.
- Outdoor `NavMeshes/<playfield id>.nav` files are optional. Complete meshes can
  improve Run estimates and coarse travel. Direct local movement works without
  a mesh; Fly never requires a mesh or a synthetic clearance certificate.
- Keep a Lock Pick, sufficient skill, inventory/backpack space and configured
  `/ManagerLoot` rules. Its Delete option can delete unselected items.
- Keep `RKMissionData` beside the deployed plugin across updates. The plugin
  creates settings/history there on load/use. A persistence error is logged;
  navigation continues with session observations. Runtime data is Git-ignored.

## Quick start

1. Roll/select/accept any number of missions yourself in Mali's window or the
   game UI. RKMission never auto-rolls, auto-accepts or deletes quests.
2. Inspect `/rkm missions`, configure loot and use `/rkm start` to arm takeover.
   Handle all inter-playfield transport yourself.
3. In an outdoor playfield with accepted Rubi-Ka missions, RKMission captures
   your current origin, ranks only those local missions in the active movement
   mode and selects the lowest estimate (mission ID breaks ties). Run uses a
   complete optional mesh cost or direct horizontal estimate; Fly uses direct
   horizontal distance while final elevation remains unresolved.
4. It uploads that exact live accepted mission using `Mission.UploadToMap`,
   the API already used by Mali's mission map button. Selection/anchor changes
   and outdoor zoning trigger a refresh. Logs report command delivery; the
   API exposes no destination readback/acknowledgement.
5. `/rkm travel auto` follows actual `MovementState.Fly`. Overrides are
   `/rkm travel ground` (Run) and `/rkm travel flying` (Fly). Stay in the vehicle
   for flying entry. Commands never equip/dismount or write player position,
   speed, altitude or movement state. Manual landing in auto mode retains the
   selected mission and entrance history/deadline.
6. Coarse travel approaches the anchor vicinity, then radial acquisition moves
   around the structure, validates an exterior side, aligns elevation, approaches
   along its normal, uses an associated live Door if present and tries a short
   threshold crossing. Without a Door, it probes the inferred side/proximity
   threshold. Neither arrival nor Use proves entry: AO# must identify the exact
   selected mission dungeon with a stable room for one second before
   `MissionDungeon` starts.
7. Check the objective/reward, use `/rkm complete [bound mission id]`, and exit
   the dungeon yourself. While armed, RKMission selects another accepted mission
   in that same playfield from the new origin. If none remains, it stops local
   automation; travel elsewhere yourself and use `/rkm start` again.

Starting inside a mission retains AO#'s exact dungeon-to-mission lookup.
`/rkm stop` gives back control; stop/start abandons an unfinished run binding
and allows retry. Removed/expired/deleted missions are never proof of reward.

## Outdoor navigation architecture

`LocalMissionTravel` owns one sequence:

**CoarseTravel -> ProbeExterior -> AlignElevation -> FinalApproach -> Interact
(if a valid Door exists) -> CrossThreshold -> AwaitTransition -> verified
MissionDungeon handoff.**

`LocalMovement` executes Run/Fly legs with observed distance/stall tracking.
`LocalRoutePlanner` contains estimates, optional complete mesh costs and local
floor/advisory ray queries. `EntranceAcquisition` owns mission-door association,
radial candidates and sector scores; it does not run a second movement controller.
`EntranceLearning` stores managed runtime records with no native object pointers.

### Coarse travel and recovery

Quest `Mission.Location.Pos` is a search anchor, not guaranteed doorway geometry.
AOSharp Vector3 uses **Y altitude**, with X/Z on the outdoor plane. Nonzero quest
Y is no more authoritative than zero/stale Y. Fly starts with direct world-space
legs at current height. Actual stalls permit lateral/raised alternatives. Run
uses optional complete mesh movement or progressive direct local legs, then
full local heading/radius recovery after actual stalls. Rays affect scores and
logs, not launch permission. There is no three-side-step failure limit.

A leg defaults to eight seconds without observed improvement before yielding.
Coarse travel fails only after 90 seconds without a new net horizontal minimum,
or the 15-minute total travel bound. Detours and mode/phase changes cannot reset
those clocks. These are local estimates, not guaranteed global routes.

### Door association stays narrow

Acquisition begins within `MaxProbeRadius + 4` metres (24 m by default).
**Live door association is fixed at 6 m horizontally from the selected accepted
anchor**, even while movement explores larger rings. Scans every two seconds
consider only valid local `IdentityType.Door` objects. Before Use, refresh the
live accepted mission/location, door identity, context, competing candidates,
actual <=2 m horizontal / <=3.5 m 3D range, and short corridor ending before the
face. Use retries remain two per side, four seconds apart.

The existing gate rejects conflicting `QuestInstance`, shop/store/building/
apartment/bar/bank/transport names, unreadable context, unlinked nonzero
`BuildingType`/`BuildingInstance`, competing accepted anchors and ambiguous
neutral thresholds. Matching quest-instance stats are an optional hint; AOSharp
reference sources do not document a complete outdoor ownership mapping. Prefer
those hints. Otherwise bind only the closest unambiguous neutral threshold;
failed entry/disappearance cannot promote a farther unrelated building.

Finite, nondegenerate live Door rotation gives the strongest orientation signal.
Both normal signs are tested because rotation alone cannot prove which side is
exterior. Invalid rotation falls back to radial/observed movement. A newly loaded
associated Door replaces an inferred target; identity/position/rotation changes
invalidate old geometry while retaining observations and the overall deadline.

A statless unrelated door inside 6 m cannot be conclusively identified outdoors.
The conservative checks can also refuse genuine unlinked building doors or doors
outside 6 m. Diagnose the association log rather than widening door search.

### Full directional search and final approach

Default search uses **16 sectors** (22.5 degrees), including cardinal and
intercardinal directions, at **12, 20 and 6 m** movement radii. These points are
local movement probes around the selected anchor; other doors/buildings never
become destinations. Sector coverage balances retries. Neighboring actual stalls
at similar anchor distances suggest a building face/corner and prioritize the
opposite arc. A blocked side is recorded as a blocked side, not an unreachable
mission. The actual player side and requested candidate sector are logged separately.

A committed orbit escapes outward/tangentially and follows short ring segments
instead of cutting across the anchor/building. A later retry can go around the
other way or use another radius. Fly holds current altitude around the ring,
aligns at the chosen exterior point, then closes along the normal. Supported local
floor samples/current player elevation supply provisional heights; live Door
origin takes priority, with flight clearance added only when that origin is
confirmed at floor height. Floor/player alternatives are validated on later passes.
The historical PF665 measured point remains in the project history; it is no
longer a hardcoded substitute for current geometry.

Final targets lie about 3/1.5/0.4 m outside the threshold, with lateral alternatives
on later passes. Inferred final points/crossings stay within the 6 m anchor boundary;
live-door staging can lie on the wider movement ring, while the Door itself must
still pass the fixed association gate. A short 0.8 m inward crossing follows missing
Door/proximity entry or uses without zoning. No transition after a brief wait
returns to directional probing. Labels explicitly distinguish inferred sides,
remembered vectors and live door normals.

Acquisition continues across candidate radii/directions. It stops after complete
sector/radius coverage and the configured elapsed no-progress interval, or the
total travel bound. New live geometry gets an observed attempt before a no-progress
failure. Stable candidate minima survive retries; changing a waypoint or walking
the same orbit cannot reset the overall clock. Exact dungeon verification alone
allows success and the working dungeon handoff.

### Persistent learning and settings

`RKMissionData/entrance-learning.json` is keyed by playfield and X/Z anchor
quantized to 2 m; mission identity is metadata on every attempt. Altitude is not
part of the key. Near quantization-boundary matches remain tightly bounded.
Records retain time, Run/Fly mode, angle/vector, sector/radius, origin/anchor,
candidate/final point, elevation source, live Door identity/position/quaternion/
forward/association, observed distance improvement/stall, final result and reason.
Coarse run failures and interrupted/failed transitions are recorded too.

Only **exact verified entry** updates the last successful exterior vector. On a
later visit in the same movement mode, try it first when live door geometry is
compatible. It must traverse current geometry and pass all association/range/
handoff checks again; failure falls back to the full search. Prior failures add
small preference penalties, never permanent blacklists. Defaults retain 256
entrances and 96 records each, with one bounded `.bak` and atomic replacement.
Unreadable/unsupported history is preserved and persistence is disabled for that
session instead of overwriting it. Keep the data file for future in-game diagnosis.

`RKMissionData/navigation-settings.json` is created with these defaults. Edit while
the plugin is unloaded, then reload it; no new chat commands were added.

| Setting | Default | Meaning |
| --- | --- | --- |
| `Sectors` | 16 | 8-32, rounded to a multiple of 8. |
| `ProbeRadius` | 12 | Preferred exterior movement ring, 8-20 m. |
| `MaxProbeRadius` | 20 | Outer movement ring, preferred radius to 28 m; never changes door association. |
| `LegStallSeconds` | 8 | Observed leg no-progress, 6-20 seconds. |
| `NoProgressSeconds` | 90 | Net/candidate no-progress deadline, 60-300 seconds. |
| `TravelLimitMinutes` | 15 | Overall bound, 5-30 minutes. |
| `FlightFloorClearance` | 1.5 | Floor-based Fly clearance, 0.5-3 m. |
| `MaxEntrances` | 256 | Retained entrances, 16-512. |
| `AttemptsPerEntrance` | 96 | Retained diagnostic records each, 16-192. |

### Historical evidence and removed layers

The chronology remains intact in PROJECT_MEMORY/CONVERSATION_LOG and Git history.
The cleanup was compared with main `f299006` and the earlier outdoor commits:

- `6e86c52` required an outdoor mesh; `cd74489` retained hard flight clearance
  and three ground recoveries. Finite local missions consequently never moved
  or failed too early. Mesh/flight feasibility is now advisory.
- Connected flight/descent searches accumulated launch prefixes, outside drop
  planners, footprint gates, fixed coordinates/heights and alternate arrival
  checks (`20dac70` through `bb47f40`). Successful direct flight/precise steering
  and supported-floor ideas are retained; overlapping search engines, ray-held
  flight, prefix plumbing and contradictory gates are removed.
- `4a822e1` treated measured/nonzero anchors too strongly as exact doorways.
  Hardcoded coordinates and stale quest-altitude authority are removed.
- `aa1e0af` scanned doors up to 40 m and could choose shops. `f299006` corrected
  association/map upload; its narrow context checks remain, while its small,
  sequential waypoint/approach exhaustion is replaced with directional coverage.
- Mission **1442298255**, anchor near X=630.91/Z=1416.02: the 16:00 logs show
  south/negative-Z then west approaches, `door=(None:0000)`, and the misleading
  `travel to selected live-door approach side`. The user identified east/+X as
  accessible. This is directional evidence, not a recovered Door rotation or
  verified-entry record; no synthetic success has been inserted into learning.
  The new grid explicitly includes east and prioritizes the opposite arc after
  observed neighboring stalls.

The native mission-map API is confirmed in embedded
`Plugins/MaliMissionRoller2/Views/MissionView.cs` and
[AOSharp Mission source](https://github.com/anarchydevs/aosp.knows-aosharp-mods/blob/master/AOSharp.Core/Mission.cs).
[Door](https://github.com/anarchydevs/aosp.knows-aosharp-mods/blob/master/AOSharp.Core/Dynel/Door.cs),
[Quaternion](https://github.com/anarchydevs/aosp.knows-aosharp-mods/blob/master/AOSharp.Common/GameData/Quaternion.cs)
and [Playfield](https://github.com/anarchydevs/aosp.knows-aosharp-mods/blob/master/AOSharp.Core/Playfield/Playfield.cs)
reference sources explain rotation and surface-ray APIs. Surface/rotation evidence
cannot certify all client scene geometry; observed movement and in-game logs decide.

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

Manager.Loot keeps character lists under `%LOCALAPPDATA%\AOSharp\ManagerLoot\<character>`
and shared lists under `%LOCALAPPDATA%\AOSharp\ManagerLoot\Shared`. Roller/map
settings remain in their deployed plugin folders.

- **Waiting for local missions:** inspect acceptance/destinations with
  `/rkm missions`; reach the matching outdoor playfield and arm with `/rkm start`.
- **Route diagnosis:** capture `Mission route estimate`, `Selected accepted mission`
  and `Map/minimap marker update`, followed by `Entrance search`, live association
  acceptance/rejection, candidate angle/radius/elevation, `Movement leg`,
  `Outdoor progress`, blocked sector/likely face, chosen side, interaction/crossing
  and exact handoff/HARD FAILURE. Include the persistent learning record and whether
  Run/Fly was active. Progress logs are every five seconds; scans summarize every ten.
- **Shop/wrong building:** association rejects unrelated contexts and ambiguous
  neutral thresholds. Wider movement rings do not authorize other live doors.
  The selected mission dungeon must still be verified after zoning.
- **Fallback stopped:** inspect the elapsed no-progress/total bound or invalid
  state reason. Three probes never fail a mission. Move to a clearer local origin
  and `/rkm start`; prior attempt diagnostics remain available.
- **Persistence failed:** read the logged file/path error; give the deployed data
  folder normal write access before reloading. Malformed history is preserved.
- **Rooms cleared but reward unconfirmed:** check the game objective/reward,
  `/rkm complete`, then exit yourself. Removal/expiration is not completion.
- **Dungeon map missing:** Mali recommends `Direct 3D T&L HAL` in the launcher.
- **Interior door/loot issues:** check existing transition/skip logs, Lock Pick,
  skill, loot rules and free space. These systems were not rewritten.
- **AO# error/death:** the coordinator stops and reports the reason.

## History and local backup

GitHub `main` is the source of truth. PROJECT_MEMORY.md stores durable context;
CONVERSATION_LOG.md stores user-visible history and keeps earlier successful and
failed navigation revisions. The earlier Desktop/OneDrive source backup is not
refreshed by this request. Pull main, compile yourself, and validate both Run/Fly
entrances, native map upload, association safety, full-direction recovery,
persisted retries and the unchanged dungeon handoff in AO#.