# RKMission

AO# plugin for solo Rubi-Ka missions in Anarchy Online. It combines the original
Mali Mission Roller 2.0, Mali Dungeon Map 2.0, and Manager.Loot interfaces with
local mission travel, room exploration, combat, door handling and looting.

The embedded plugin windows use ManagerLoot's native AO button, text, and
border palette. Roller action buttons and compact toggles use that same button
skin; its mission cards keep invisible Ping hit regions. Roller mission sliders
use the native red track and cyan handle shown in Mali's Dungeon Map 2.

**2026-09-29 implementation continuation:** Automatic mode now combines nearby
live/static ACG entrance candidates across multiple mission keys, exact
post-zone mission verification, Scotty menu discovery and settled warp checks,
Grid/Fixer Grid and mapped zone-link fallback, floor lifts, room routes and
automatic exit. Doorway crossing retains exclusive movement ownership;
stuck/rubberband events replan without moving the player position directly.
Combat approach, mission completion, reclaim readiness and durable checkpoint
reconciliation have separate controllers. Optional corpse/chest loot pauses
below the free-slot threshold in `RKMissionData/inventory-policy.json`
(default 3); objective interactions continue. The imported outdoor link graph
is sparse, and all new travel/reclaim flows still need in-game validation.

**Status (2026-09-29):** mission entry and post-combat room actions now wait for
buff preparation and HP/nano recovery when needed; new enemy engagements are
limited to 20 m. Every flying approach now starts above its resolved entry target,
aligns to entry+1.5 m outside before close staging and descends in final legs.
Objectives now wait for ordinary room/enemy/loot clearance, followed by automatic
completion, return to the entry door, actual exit and nearest local mission chaining.
Return-item runs finish for the bot when the item is collected; terminal hand-ins
remain entirely manual, with a separate pending-hand-in flag.
Outdoor travel/entrance navigation has been consolidated
into one state machine with Run/Fly movement and persistent entrance diagnostics.
Fly gains cruise clearance, travels toward the selected mission while avoiding
obstacles, resolves mission entrance height and prepares raised staging within 10 m, then diagnoses the
approach side and enters. Ground retains observed perimeter recovery.
Mapped interior crossings, readiness, combat range and normal loot rules are
preserved while objective ordering and automatic exit are added. No local build,
package restore, tests or game run were performed; the user compiles and validates.

## Setup

- Use the classic client; embedded Manager.Loot rejects the new engine.
- Compile `RKmission/RKmission.csproj` yourself. It targets .NET Framework 4.8
  with the existing AOSharpSDK, AOSharpSDK.SharpNav and Newtonsoft.Json packages.
- Deploy the output/dependencies through AO#. Keep the embedded plugin folders
  and `Data` beside `RKmission.dll`; the project copies their required UI/assets
  and entrance/travel reference data.
- Outdoor `NavMeshes/<playfield id>.nav` files are optional. Complete meshes can
  improve Run estimates and coarse travel. Direct local movement works without
  a mesh; Fly never requires a mesh or a synthetic clearance certificate.
- Keep a Lock Pick, sufficient skill, inventory/backpack space and configured
  `/ManagerLoot` rules. Its Delete option can delete unselected items.
- Keep `RKMissionData` beside the deployed plugin across updates. The plugin
  creates settings/history there on load/use. A persistence error is logged;
  navigation continues with session observations. Runtime data is Git-ignored.

### Public FGrid service fallback

Automatic cross-playfield travel can optionally request Team Fixer Grid from
player-run service bots. Check the bot names and commands in
`RKmission/Data/FGridServices.json` against current in-game service. Each
service can be scoped to a playfield and a
specific nearby Grid terminal position, and can list alternate expected inviter
characters.

RKMission moves beside that normal Grid terminal *before* sending the tell. It
accepts a team invite only from the configured/expected identities, waits for
the temporary Data Receptacle (template 160978), and uses it on the nearby
Grid entrance to enter Fixer Grid. `Data/FixerGridSurveyExits.json` ships the
verified portal identities, FGrid floors and positions, destination playfields,
and outdoor arrival positions: 78 exits across 46 destinations, including
20 destinations with multiple exits. For a selected mission, RKMission ranks all
verified exits into its playfield by horizontal distance from each arrival to
the mission's quest entrance anchor. It traverses the required floors and
uses the exact selected portal. If that portal is unavailable or does not
start zoning, it tries another verified exit on the current or a higher floor.
The destination playfield and selected portal are verified before local mission
travel resumes from the observed outdoor position. `/rkm fgrid` shows the
destination count, current target's exit count, selected portal, arrival,
estimated remaining distance, and last failure.

`FixerGridExits.json` remains a legacy floor/name fallback where no verified
arrival is available. The old single-exit `RKMissionData/fixer-grid-exits.json`
is read for compatibility and never overwritten. Newly verified exits are
merged into `RKMissionData/fixer-grid-exits-v2.json`; repeated identities or
matching FGrid positions do not replace another exit in the same playfield.
The previous
`GridTerminals.json` lists *entrances by source playfield*, not destination
exits; it is not used to select an FGrid destination. A missing/offline
service, invite timeout, missing receptacle, unknown exit or wrong destination
stops the FGrid attempt and records the cause. Lift and exit selection still
need in-game validation. AO# reports Fixer Grid as a
dungeon, but RKMission keeps its travel provider active there and can resume
the mapped exit route after `/rkm auto` is restarted inside it.

Normal Grid/mapped travel remains the fallback. Scotty warpers
reported offline are also treated as expected availability failures and no
longer impose the normal two-minute integration-error cooldown.

### Fixer Grid exit survey

RKMission records manually traversed `Exit the Grid` portals even when the
mission bot is stopped. After the outdoor zone settles, it saves the portal's
stable identity, floor, full FGrid position, destination playfield, and outdoor
arrival position in `RKMissionData/fixer-grid-survey.json`. A repeated portal
keeps one record. `/rkm fgrid scan` reports verified identities against the
78 physical exits (eight on each of floors 1–9, six on floor 10) and shows
per-floor counts. `/rkm fgrid` includes the overall count. Keep `RKMissionData`
when updating the plugin. New survey observations become runtime route
candidates after verified outdoor zoning; the shipped survey remains the
canonical source for known exits.

## Local takeover (existing workflow)

1. Roll/select/accept any number of missions yourself in Mali's window or the
   game UI. `/rkm start` does not auto-roll, auto-accept or delete quests.
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
6. Fly first gains local cruise clearance and compares over/around obstacle routes
   during transit. Within 10 m of the anchor, resolve the mission entrance height
   and prepare staging above it before comparing approach sides. Keep the entry target across exterior sectors;
   surrounding terrain cannot redefine it. Fly starts the inward approach above
   the entry target, aligning diagonally to entry+1.5 m outside first,
   and descends during final precision. Ground uses radial acquisition/perimeter recovery. Align elevation, approach
   along its normal, uses an associated live Door if present and tries a short
   threshold crossing. Without a Door, it probes the inferred side/proximity
   threshold. Neither arrival nor Use proves entry: AO# must identify the exact
   selected mission dungeon with a stable room for one second before
   `MissionDungeon` starts.
7. Clear ordinary rooms/enemies/loot before the reserved objective. On confirmed
   objective completion (or return-item collection), RKMission records completion
   and routes back through the mapped rooms to exit automatically. While armed,
   RKMission selects the next closest accepted mission
   in that same playfield from the new origin. If none remains, it stops local
   automation; travel elsewhere yourself and use `/rkm start` again.

Starting inside a mission retains AO#'s exact dungeon-to-mission lookup.
`/rkm stop` gives back control; stop/start abandons an unfinished run binding
and allows retry. Quest disappearance alone never proves reward. `/rkm complete
[bound id]` remains a manual completion override; automatic exit follows while armed.

## Automatic cycle

At a mission terminal, use `/rkm auto` to roll for the Rubi-Ka destinations
enabled in the embedded roller's Locations panel. `/rkm zone <Rubi-Ka
playfield id>` restricts the next rolls to one destination; `/rkm zone all`
returns to the enabled locations. `/rkm rolls <count>` limits offers per
rolling attempt (default 100). `/rkm limit <count>` caps confirmed automatic
mission acceptances in one cycle; `/rkm limit off` removes that cap. RKMission
finishes already accepted missions before ending a capped cycle. The limit is
saved in the RKMission checkpoint; `/rkm status` shows the count. Use
`/rkm auto` to roll with the embedded roller, accept a matching mission,
travel, enter, clear, exit, return to the remembered terminal, and repeat.
Already accepted missions take priority. `/rkm local` switches back to the
previous local-only takeover; `/rkm stop` stops RKMission's automatic rolling
and movement. Destination rolling does not require an item roll list, and the
roller's Auto Adjust Level Slider option no longer stops it for an empty list.
The roller uses the same native window frame and bordered panels as the other
embedded plugins; its main header is labeled RKMission Roller without Mali's
top-left icon. Each offered mission card is now the Ping target across its
title, details, rewards, and unused background. The separate Ping button is
gone; the Accept button is centered below the rewards and 69 pixels wide.
Roller and DB Browser item rows have transparent backgrounds. Both tabs use
aligned column widths, and the browser QL input keeps a visible label beside
its numeric entry. The header, mission cards, and settings panels share a
270-pixel content width with extra room for the full slider labels.
Accept uses its own click control, so it does not invoke Ping.

Automatic travel tries bounded Neko ACG key/entrance candidates before
cross-playfield and local Run/Fly travel, even when the mission entrance is not
nearby. It verifies the exact selected mission after zoning. Unlocated labels
with more than 64 entrances yield to normal travel; smaller lists are tried
one candidate at a time before fallback. Mission keys with the displayed
`Temporary:` prefix are recognized. For longer travel,
the bot asks Scottyboi for its current paged menu, reads a location command
under the destination playfield's heading for any zone, and waits for
the assigned warper's team invite. For other zones, if some menu pages are missing, it
requests the menu once more. A character already in a team cannot receive
Scottyboi's invite.
Expected Scottyboi or assigned-warper team invites are accepted automatically;
an early warper invite waits briefly for its name lookup before acceptance.
When a numbered Scottyboi account sends the queue response, RKMission checks
its identity through the game chat server before trusting the assigned warper.
For Mort, the confirmed `scty` command `hope` is used when the Mort menu page
does not arrive. An offline warper reply ends that request and reports why no
team invite can follow.
If the warp fails, travel uses mapped Grid, Fixer Grid (with a Data
Receptacle), border, and teleporter links; a visible Grid terminal can also
supply an entry link.
The graph is sparse, so some terminal-to-mission routes still have no mapped
path. If a Fixer Grid route is mapped but the Data Receptacle is missing, the
travel error names the missing item. `/rkm status` shows the current provider
and last travel issue. The automatic cycle still needs in-game validation;
see the [2026-09-29 checkpoint](docs/history/2026-09-29-rkmission-architecture-checkpoint.md).

## Outdoor navigation architecture

Navigation checkpoint (2026-09-28): the user reports navigation is working well
after diagonal-elevation commit `965003e`. Preserve this behavior for now. The
saved decisions and conversation history are in `PROJECT_MEMORY.md` and
`CONVERSATION_LOG.md`, with full supplied logs under `docs/navigation-evidence/`
and implementation history in Git. Subsequent tuning should build on this checkpoint.

`LocalMissionTravel` owns one sequence:

**CoarseTravel (Run) / FlyClearance -> FlyToEntrance -> FlyMatchEntryHeight (Fly, 10 m trigger) -> ProbeExterior
with OrbitBypass (Run) / FlyAvoidObstacle (Fly) when needed
-> AlignElevation -> FlyCloseApproach (Fly, when farther than 7 m) -> FinalApproach -> Interact
(if a valid Door exists) -> CrossThreshold -> AwaitTransition -> verified
MissionDungeon handoff.**

`LocalMovement` executes Run/Fly legs with observed distance/stall tracking.
`LocalRoutePlanner` contains estimates, optional complete mesh costs and local
floor/advisory ray queries. `EntranceAcquisition` owns mission-door association,
radial candidates and sector scores; it does not run a second movement controller.
`EntranceOrbit` plans Ground perimeter legs. `FlightPathPlanner` is the single
Fly transit/side-relocation planner, with immediate over/around comparison.
`EntranceLearning` stores managed runtime records with no native object pointers.

### Coarse travel and recovery

Quest `Mission.Location.Pos` is a search anchor, not guaranteed doorway geometry.
AOSharp Vector3 uses **Y altitude**, with X/Z on the outdoor plane. Nonzero quest
Y is no more authoritative than zero/stale Y. Fly first requests enough elevation
above advisory local/forward/mission surface samples, bounded by the climb limit.
It then retains actual cruise altitude until the 10 m entrance-height trigger.
Corridor hints compare routes before wall-running; observed
stalls retain failed position/direction and trigger a new over/around choice. Run
uses optional complete mesh movement or progressive direct local legs, then
full local heading/radius recovery after actual stalls. Rays affect scores and
logs, not launch permission. There is no three-side-step failure limit.

A Ground leg defaults to eight seconds without observed improvement before yielding.
Fly transit/initial height matching yields after four seconds; final entry probes after three.
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
intercardinal directions. Ground uses **12, 20 and 6 m** movement radii;
Fly diagnoses sides on one adaptive exterior ring instead of repeating Ground
rings. These points are
local movement probes around the selected anchor; other doors/buildings never
become destinations. Sector coverage balances retries. Neighboring actual stalls
at similar anchor distances suggest a building face/corner and prioritize the
opposite arc. A blocked side is recorded as a blocked side, not an unreachable
mission. The actual player side and requested candidate sector are logged separately.

**Changing target sector without perimeter routing caused repeated wall-running.**
The attached 17:02-17:03 PF665 log showed a fixed 12 m arc scraping the same south/
west walls even after selecting opposite sector 3. Changing the label did not
mean that side was reached. The preserved log is in
[`docs/navigation-evidence/2026-09-28-pf665-wall-running.txt`](docs/navigation-evidence/2026-09-28-pf665-wall-running.txt).

For Ground, repeated same/neighbor-bearing stalls at similar position/radius activate explicit
`OrbitBypass`; an exterior-route stall also activates recovery. After a current-run
wall stall, candidates require at least 30 degrees of bearing separation. A previously
verified side already nearby can be revalidated first before a new wall is observed. First
push outward toward a ring at least 4 m beyond the observed wall radius, bounded
by `MaxProbeRadius`. Maintain a ring band of approximately radius +/-1 m. Widen it
by 3 m after a stall, up to the outer bound; correct inward drift before continuing.
Each <=15-degree tangential leg is generated from the actual position, with
clearance compensation for chord sag. There is no precomputed arc to blindly advance.

Corridor rays rank CW/CCW hints. When equally clear or uncertain, observe both
directions for up to 3 s each and choose the better angular/radial progress;
ties prefer the remembered direction or shorter arc. Preserve the opposite as
fallback. Actual arrival requires the requested bearing within movement tolerance,
radial clearance, meaningful net bearing change (30 degrees minus arrival tolerance)
and at least two successful angular movement legs. Unwrapped angular travel is
logged separately from net side change. Requested sectors that were never reached
are route failures, not completed exterior coverage. Angular coverage and fixed
candidate minima survive retries so repeated arcs cannot refresh overall progress.

**Fly follows gain clearance -> destination/avoid obstacle -> within 10 m resolve
entry height and prepare raised staging -> diagnose entrance side -> approach from above/enter.** It does not inherit Ground's floor-following
orbit or wait for ring retries before climbing. Five narrow corridor rays score
the next direct segment. If obstructed, compare estimated distance, corridor hits,
previous blocked directions and revisited waypoints for routes around both sides
and vertical-over routes. Execute one leg, then re-evaluate at the actual position.
Rays remain incomplete hints; motion failures can override apparently clear rays.
No outdoor navmesh or clearance certificate is required.

Clear Fly transit uses `FlightCruiseLegLength` (default 60 m, previously 20 m).
With 8 m left to a waypoint, check a continuation at most every 250 ms. If the
next corridor is clear and nearly straight/level, extend the target while keeping
forward movement and the existing no-progress clock. Record this as continued
flight, not waypoint arrival. A distant obstruction shortens the horizon to 20 m;
nearby obstruction or actual non-progress still invokes the same over/around
planner. Exterior relocation retains its 20 m maximum and short perimeter legs.
No cruise continuation occurs within 18 m of the anchor; the actual 10 m stop
starts raised staging preparation before side diagnosis and precise final approach.

The 18:47-18:48 PF665 evidence verified entry for mission 1442298249 at Y=20.61.
Its repeated clear 20 m legs contained no observed stalls: waypoint completion
sent a full stop before each new leg. Longer legs with continued steering address
those transit pauses without changing the successful entrance sequence. Full log:
`docs/navigation-evidence/2026-09-28-pf665-1847-success-and-cruise-pauses.txt`.

`FlyClearance` samples support/obstacles below the current point, the next 20 m
and the mission anchor. Request `FlightCruiseClearance` (default 6 m) above observed
surfaces, including allowance for transit stopping tolerance. Keep a sufficient
existing altitude; unknown geometry uses a modest bounded climb. An actually
blocked initial ascent yields to normal over/around recovery instead of rejecting
the mission. Cruise height and entrance height are separate targets.

All Fly ascents and descents use diagonal movement, including initial clearance,
overpasses, entrance height matching and height alignment after a bypass. Compare
eight horizontal directions with corridor and observed-failure hints. Elevation
legs use a 2-16 m horizontal run, shortened near the entrance to stay near the
reached side. Bound the exterior slide and keep the protected footprint; collision
hints still rank choices rather than reject flight. The executor retains horizontal
steering if collision or rounding consumes that component before height is reached,
without resetting its target or progress deadline. Arrival tolerance still ends
the leg; no straight vertical finishing movement is commanded. Logs show the
elevation stage, horizontal run, height delta and any diagonal finishing correction.

Over candidates combine diagonal movement with local 6/12/24 m climbs, increased
by an advisory roof sample when useful, within `FlightClimbLimit` above run-start
height.
After ascending, retain the higher actual altitude across the obstacle; do not
descend back toward the old waypoint height. Around candidates near a mission use
short tangential/outward segments outside the inferred footprint, expanding up to
`MaxFlightBypassRadius` when actual walls show a larger structure. Clear elevated
transit may cross above the footprint. Side arrival still requires actual exterior
position/radius and a measured bearing change when recovering from a blocked side.
Keep the chosen CW/CCW (or transit left/right) direction while its next corridor
remains clear; release it after an observed stall, obstruction hint or changed goal.
Shorten the final tangential step to the target bearing instead of overshooting it
and reversing. Overpass remains available when it offers a better route.

On actual arrival within 10 m, halt transit and resolve one mission entrance
height: associated live Door first, then previous exact verified height for this anchor
(regardless of requested sector), otherwise support at the mission marker
itself. Anchor support uses only five nearby columns, requiring centre plus two
other columns. Try the highest supported plane plus clearance first. Lower local
layers are bounded fallback hypotheses, not presumed accessible ground. If no
support is available, run-start aircraft height is explicitly provisional.
Zero/stale quest Y is never blindly used as the doorway height.

For every flying mission entrance, `FlyMatchEntryHeight` prepares a staging target
1.5 m above that entrance's current resolved entry height. There are no coordinate or playfield
exceptions. Keep native X/Z targets, Door association and ownership checks.
Align diagonally to that fixed target, including descent from a higher aircraft
altitude. Proceed when it is matched within 0.35 m, giving 1.15-1.85 m clearance
above the entry target, even if the incidental horizontal leg has not finished. Below-entry climbs
exclude inward directions using horizontal geometry, independent of quest Y.
If blocked, compare neighboring elevation corridors on a bounded exterior and
use the same Fly planner to relocate. Preparation may finish outside 10 m while
sliding or recovering from an obstructed corridor.
Six observed recovery failures stop with staging access unresolved, without
claiming a side was selected or reached.
If no diagonal choice satisfies the exterior bound and direction rule, initial
preparation stops with a reason or a reached candidate yields to another side;
an empty choice list cannot launch movement or throw from this preparation path.

Rank sides' inward corridors at that shared mission height. Reach the selected
exterior, then align diagonally to the current entry height plus 1.5 m before
moving inward. Higher altitude is retained while bypassing obstacles; it is
returned to the approach target at the reached exterior. Lower entry hypotheses
get their own entry+1.5 m staging target; an initial roof plane cannot hold them
up. This restores the saved checkpoint's deliberate height return with the
requested small above-entry cushion. Do not derive a different doorway height
from low terrain at each exterior point. Ground's floor logic is unchanged.

After confirming that exterior side and raised staging, `FlyCloseApproach` moves
inward on the selected approach direction to a point 6 m outside the entrance,
keeping that raised staging height. Final approach descends diagonally toward
the same 3/1.5/0.4 m horizontal points, with the same 4.68 m leg cap. Flying final
and crossing targets sit 0.35 m above the doorway plane, with 0.35 m arrival
precision so an accepted arrival stays at or above that plane. A guard halts
close/final approach, interaction or crossing whenever actual Y is below entry
or more than 2 m above it, then regains entry+1.5 m staging before continuing.
Below-entry repair excludes inward diagonal choices. Final descent toward the
doorway plane is allowed. Allow at most two
height repairs per candidate; a third loss yields to another side without
recording a wall from height drift alone.
This is one observed leg before final precision, with the same stall recovery;
it does not reduce the safe perimeter used to bypass walls or change sectors.
Close approach also uses 0.35 m arrival precision to retain the 1-2 m height band.
A capped final leg advances to the next full staging waypoint only when the
current full waypoint is within that precision; the old 0.9 m gate cannot skip it.
If already within 7 m after height alignment, start final approach directly.
Keep the working 10 m height-check trigger separate from this closer staging point.

A stalled approach yields to another sector at the same height. Crossing without
zoning, or reaching every sector without verified entry, can advance the mission's
bounded height hypotheses (up to six, retained across sectors), re-match height
and then resume diagnostics. A previously verified height stays in use until all
sectors at that height have been reached; a single unsuccessful crossing cannot
replace it. With two or more local supports, retain every nominal
support-plus-clearance height, then fill spare slots by bisecting the largest
gaps between them. Try the resulting plan
from highest to lowest, within the same six-candidate limit and observed nominal
height range. Intermediate candidates remain provisional; rays cannot establish
an invisible doorway's height. Verified-height, single-support and unknown-support
plans retain their existing bounded offsets.
Logs list candidate heights and sources. An associated live Door height
does not receive speculative alternatives. Sector changes never reset the height
trial index. These hypotheses still require exact dungeon verification.
The 11:25 mission 1442368811 excerpt showed an unverified lower-plane target
Y=27.01, actual Y=27.11 and no associated Door. It ends with a manual reset,
without the real doorway height or verified entry. The full pasted log remains
local; project history records this analysis.
The footprint rule protects side-to-side chords; a clear radial outward escape
does not force another climb. Match height at the 10 m gate and at each confirmed
exterior before close approach, using the same current entrance hypothesis.
Only exact dungeon verification validates a learned entry height; roof/floor rays
cannot identify an otherwise invisible door with certainty.
PF665 measurements remain evidence only. The later measured entrance at X=588.3,
Z=1367.5, Y=30.5 exposed an approach at Y=27.01 below its doorway after an overpass
at Y=38. The raised staging and descending height trials apply to all flying
entrances using each entrance's own live, learned or inferred geometry.
The user's 12:28 feedback put the character at Y=35.0 and the entrance at Y=30.5.
Comparison with the dated local backup at `0350491` showed `cefb80d` had retained
high bypass/initial-support altitude through close staging. Staging is now
entry+1.5 m: a resolved Y=30.5 entrance targets Y=32.0 before final descent.
This is a source correction; successful entry at this doorway still needs in-game verification.

Fly final approach advances up to 4.68 m per leg, another 20% farther than the
previous 3.9 m cap (which was 30% farther than the original 3 m). Each leg ends at
the existing horizontal staging point if it is closer. Flying final/crossing
arrival is tightened to 0.35 m for the above-entry rule; stall detection and exact
dungeon verification remain. The 19:03-19:04 PF665 log confirmed
successful cruise and entry but showed repeated 3 m approach adjustments. Larger
steps and the closer 6 m staging point reduce intermediate stops; percentage
changes describe step length rather than a guaranteed speed gain.
Evidence: `docs/navigation-evidence/2026-09-28-pf665-1904-approach-steps.txt`.

Final targets lie about 3/1.5/0.4 m outside the threshold, with lateral alternatives
on later passes. Inferred final points/crossings stay within the 6 m anchor boundary;
live-door staging can lie on the wider movement ring, while the Door itself must
still pass the fixed association gate. A short 0.8 m inward crossing follows missing
Door/proximity entry or uses without zoning. No transition after a brief wait
tries remaining Fly height hypotheses, then returns to directional probing.
Labels explicitly distinguish inferred sides,
remembered vectors and live door normals.

Acquisition continues across candidate sides. It stops after complete Ground
sector/radius coverage or Fly exterior-side coverage plus the configured elapsed no-progress interval, or the
total travel bound. New live geometry gets an observed attempt before a no-progress
failure. Stable candidate minima survive retries; changing a waypoint or walking
the same orbit cannot reset the overall clock. Exact dungeon verification alone
allows success and the working dungeon handoff.
If perimeter recovery is exhausted without reaching candidates, the no-progress
deadline can stop the local run with an unresolved-route reason; it does not claim
full sector coverage or prove that the mission itself is unreachable.

### Persistent learning and settings

`RKMissionData/entrance-learning.json` is keyed by playfield and X/Z anchor
quantized to 2 m; mission identity is metadata on every attempt. Altitude is not
part of the key. Near quantization-boundary matches remain tightly bounded.
Records retain time, Run/Fly mode, angle/vector, sector/radius, origin/anchor,
candidate/final point, elevation source, live Door identity/position/quaternion/
forward/association, observed distance improvement/stall, final result and reason.
Coarse run failures and interrupted/failed transitions are recorded too.
Records also retain requested `Sector`, separate `WallBearingSector`/angle/radius,
actual exterior arrival, CW/CCW direction (-1/+1), exterior ring radius, angular
span traveled, bypass-side confirmation and vertical-overpass result. Each anchor
keeps failed wall-bearing sectors and the verified successful approach sector/vector,
bypass direction, ring and span. Existing version-1 history remains readable;
old wall bearings are recovered from recorded stall positions, not sector labels.
Fly records also retain actual exterior position, entry-height/source, failed entry
heights and up to 12 completed/stalled flight legs with origin/target/result/strategy.
Store cruise height, the actual entrance-height matching point and the 10 m
trigger distance. Older exterior-support records remain readable as history.
Exact verified success saves the entry point/height alongside the approach vector.

Only **exact verified entry** updates the last successful exterior vector. On a
later visit in the same movement mode, try it first when live door geometry is
compatible. It must traverse current geometry and pass all association/range/
handoff checks again; remembered orbit direction/radius get first preference and
yield to current corridor/movement evidence. Failure falls back to the full search.
Prior failures add
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
| `MaxFlightBypassRadius` | 36 | Fly footprint clearance limit, outer Ground radius to 60 m. |
| `FlightClimbLimit` | 48 | Maximum climb above this run's initial aircraft height, 8-96 m. |
| `FlightCruiseClearance` | 6 | Requested clearance above sampled obstacles before transit, 2-16 m. |
| `FlightCruiseLegLength` | 60 | Clear Fly transit horizon, 20-120 m; distant obstacles shorten it to 20 m. Missing field in older settings uses 60 m. |
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

## Mission preparation, recovery and engagement range

On verified dungeon startup and after combat, RKMission holds new room actions
when HP/nano need recovery or preparation is still active. Default ready targets
are **95% HP and 95% nano**. A healthy character with no combat handler, pending
action or configured missing buff proceeds immediately. When an AOSharp combat
handler is installed, allow a short 3-second stationary observation window for its
configured buffs/recovery, then wait for casts, recharge and a 2-second quiet gap.
The handler keeps its own profession-specific buff choices and combat rules.
Outgoing nano casts retain their base cast/recharge horizon, because AOSharp's
short pending-send flag alone does not describe the whole cast.

Recovery considers usable inventory items, learned spells and available perks
whose exposed effects restore HP/nano, including programs applied by kits/perks.
Actions target self, respect native use requirements and skill locks, serialize
with pending actions, and avoid repeating an active heal-over-time spell. Sitting
allows treatment kits and natural regeneration; standing is confirmed before
movement or casting resumes. Keep consumables in the main inventory. Actions with
unrecognized effect metadata remain the responsibility of your combat handler.

The pause never starts while the character or owned pets are fighting or being
attacked. Aggro interrupts recovery and resumes combat; recovery is reconsidered
once it ends. Manager.Loot's original process/settings are retained but its update
is suspended during combat/preparation, preventing loot actions from competing
with recovery. An already active doorway crossing finishes under its original
ownership before the next safe-room readiness check.

If recovery makes no progress for 45 seconds, or preparation lasts 180 seconds,
stop with the actual HP/nano and missing configured buffs instead of starting the
next fight unprepared. Check supplies, skills, cooldowns, NCU and handler settings,
then use `/rkm start` to retry. This is source-only pending your in-game validation.

`RKMissionData/readiness-settings.json` is created on plugin load. Edit while the
plugin is unloaded, then reload. Defaults:

```json
{
  "HealthPercent": 95,
  "NanoPercent": 95,
  "HandlerStartSeconds": 3,
  "QuietSeconds": 2,
  "NoProgressSeconds": 45,
  "TimeoutSeconds": 180,
  "BuffNanoIds": []
}
```

Set the percentages to 100 if you want completely full bars. `BuffNanoIds` is an
optional explicit list of required self-buff nano IDs, for example when no combat
handler manages them. The bot casts only configured buffs from its learned list,
accepts a stronger active nano in the same nonzero nanoline, and waits for actual
buff presence. An empty list does not invent a profession buff loadout; enable
your handler's own auto-buffing. RKMission never replaces `CombatHandler.Instance`.

**20 m limits new Target/Attack/pet engagements**, including ordinary mapped-room
enemies, spawned entities and Alarm Sentries. An existing fight is not canceled
just because its target moves beyond 20 m. For a large room with distant enemies,
continue moving within its mapped outline before acquiring a target in range;
do not claim the room is cleared merely because no enemy is currently within 20 m.
A stalled room scan stops without claiming clearance.

## Commands

| Command | Purpose |
| --- | --- |
| `/rkm` or `/rkm status` | Show armed state, movement mode/phase, accepted count, bound mission/progress, and dungeon status. |
| `/rkm missions` | List tracked mission IDs, entrances/playfields, objectives, acceptance, and completion state. |
| `/rkm travel auto\|ground\|flying` | Set session travel mode; default auto uses the actual flight state. |
| `/rkm start` | Arm accepted-mission monitoring/local takeover, or verify and resume the current dungeon. |
| `/rkm auto` | Arm the new automatic roll, travel, clear, exit, and return cycle. |
| `/rkm local` | Use the established local takeover workflow. |
| `/rkm stop` | Stop RKMission movement and dungeon automation. User-owned roller controls remain independent. |
| `/rkm complete [mission id]` | Manually mark the verified bound run completed; while armed, exit automatically and continue locally. |
| `/rkm zone <id\|all>` / `/rkm rolls <count>` | Set automatic rolling destination and offer limit; `all` uses enabled Rubi-Ka locations. |
| `/rkm limit <count\|off>` | Cap confirmed automatic mission acceptances per cycle, or remove the cap. |
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

### Objective ordering and automatic completion

The five regular Rubi-Ka terminal types are find item, return item, repair/use
item, find person and kill person. See the original player guide reproduced on
[Funcom's forum](https://forums.funcom.com/t/rubi-ka-mission-settings-101/6664).
Native mission icons use the same mapping as Mali's existing roller. Accepted
quest updates and subsequent action changes are retained as managed snapshots;
offers never become travel candidates without acceptance. Unavailable metadata
holds completion rather than guessing an objective from a name.

| Type | Reserved final action |
| --- | --- |
| Find item | Target/select the exact quest item and wait for completion acknowledgement; do not use, open or pick it up. |
| Return item | Pick up the exact ground quest item with AO's pickup action, then mark the bot run completed with `manual return hand-in pending=true` after inventory confirmation. No terminal travel or hand-in is performed. |
| Repair/use item | Use the native source item on the exact destination. |
| Find person | Observe/target the exact NPC after ordinary clearance; wait for acknowledgement, then kill it last and process its corpse. |
| Kill person | Keep the exact NPC out of normal target acquisition; engage it last within 20 m, then process its corpse. |

Known objective rooms are avoided while another room can be reached without
them. If the room is a required passage, enter/pass through it while holding
the objective. A room whose objective has not loaded cannot be reserved in
advance; the exact target/item identities remain protected. Every room must
finish its ordinary enemies and loot before the final objective action. Recheck
already cleared rooms for loaded new enemies/loot. Only an enemy mapped inside
that specific room can reopen its enemy clearance; a fight or nearby attacker
elsewhere cannot reset unrelated cleared rooms. Reopening logs the room and
enemy/loot identity. Early objective aggression
or player/pet attacks stop the bot and recall pets rather than waive the order.
An independent combat plugin or manual action can still act outside RKMission;
the guard reports the conflict when observed.

Non-return completion needs our final action plus observed objective evidence
(exact item selection, source-item consumption, NPC death/observation, or server completion
text) and absence of the exact bound quest for two seconds. An observed manual
quest deletion blocks this inference. No separate authoritative reward flag is
exposed by this SDK; missing evidence/acknowledgement stops for manual review.
Return collection is explicitly a bot-run completion and does not claim a game
reward: hand in the item yourself later, usually in another playfield. Completed
records are excluded from local selection even while a return quest remains
accepted. Completion tracking, like the previous manual tracking, lasts for the
loaded plugin session.

Manager.Loot must finish opening/transferring/closing every discovered normal
container/corpse according to its existing rules; merely opening it is insufficient.
The ordinary-loot gate excludes exact reserved objective identities, including
entries seen before their objective metadata arrived. Those entries cannot block
the finale that handles them. Late-identified return containers get one fresh
finale attempt with objective-item pickup rules. Find/repair targets that only
require targeting or item use do not need opening/looting to permit exit.
Return-item objective containers are held separately and their contents bypass
the normal allowlist so the required item is collected. Find-item observation
objects remain protected from opening/looting even after selection and do not
count as unfinished loot during exit. Quest items stay protected from
automatic bag/reverse transfers. Unreachable/skipped or unfinished loot and
unreachable rooms hold automatic completion/exit. This does not enable Loot All
for ordinary chests or change the user's normal delete/reverse/quantity rules.
Opening retains a managed pending identity until the matching contents have
finished processing, even when the corpse disappears or its response is late.
Unknown/unavailable contents remain pending; received credits or disappearance
alone never mark an ordinary container finished.

After clearance, return through the same mapped crossings to the saved entry
room and cross verified external-door geometry. Starting inside can recover an
external entry room from the map. Missing exit geometry, blocked routes or a
crossing that never zones stop with a diagnostic; no teleport or assumed exit.
New enemies/loot encountered on return reopen clearance. Only actual outdoor
zoning releases the run and selects the next closest eligible mission from the
new position. Inter-playfield travel and return hand-ins remain manual.

- Mali's world-space room outlines provide safe interior waypoints and map
  visible enemies, corpses, and containers to rooms. Discovery follows what
  the client has loaded or spawned.
- The closest usable room route to unfinished ordinary work takes priority,
  reserving known objective rooms when possible. Backtracking uses mapped
  connections and logs intermediate and goal rooms.
- A dedicated crossing approaches the mapped threshold, resolves a live door
  within 3 m, opens/lockpicks as needed, then continues inside. Locked/closed
  flags first trigger a passage probe; a distant door identity cannot redirect it.
- Entry requires 500 ms of stable target-room detection and a safe interior
  position. Combat, loot, and normal room selection resume after confirmation.
  The reverse connection has a one-second cooldown after confirmed arrival.
- Previously cleared rooms are passages when they still have no work: keep
  movement active during entry confirmation, skip the repeated two-second
  clearance wait and route onward in the same update. Open doorways do not
  issue a halt. Required backtracking to other branches, the objective or exit
  can still revisit a room. New enemies/loot, needed recovery, closed doors and
  unavailable routes can require a stop; entry confirmation remains enforced.
- Failed crossings block a connection for 30 then 90 seconds; a third failure
  blocks it for that run. Other reachable routes are considered.
- Combat interrupts loot approaches. Nearby attackers, hostile spawned
  entities, and Alarm Sentries are considered alongside room enemies;
  players and the local player's pets are excluded.
- Manager.Loot handles container opening, chest lockpicking, rules, and item
  transfers. A blocked loot approach gets an alternate attempt, then an
  unreachable object is skipped with a log for that run.
  Skips now prevent automatic completion rather than count as finished loot.
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
- **Objective/exit held:** inspect reserved-objective, loot and exit diagnostics.
  Loot blockers now show ordinary skipped/unfinished counts, reserved entries,
  processing state and up to eight blocking identities with room/visibility.
  Resolve aggro, missing items/free space or locked routes. After checking the
  objective in game, `/rkm complete` can override completion while keeping automatic
  exit available when armed. Return-item hand-ins are always manual.
- **Dungeon map missing:** Mali recommends `Direct 3D T&L HAL` in the launcher.
- **Interior door/loot issues:** check existing transition/skip logs, Lock Pick,
  skill, loot rules and free space; unfinished loot holds completion.
- **AO# error/death:** the coordinator stops and reports the reason.

## History and local backup

GitHub `main` is the source of truth. PROJECT_MEMORY.md stores durable context;
CONVERSATION_LOG.md stores user-visible history and keeps earlier successful and
failed navigation revisions. The earlier Desktop/OneDrive source backup is not
refreshed by this request. Pull main, compile yourself, and validate both Run/Fly
entrances, native map upload, association safety, full-direction recovery,
persisted retries and the unchanged dungeon handoff in AO#.
