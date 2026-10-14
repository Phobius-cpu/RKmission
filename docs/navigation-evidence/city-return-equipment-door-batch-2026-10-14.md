# City return, combat equipment protection and door timing — AWAITING AO TEST

Baseline: 8afd24e47b4a05a3cfbe2c11f220fdfa79912ad6.

## Travel

Post-zone diagonal flight escape is suppressed in Old Athen (540), ICC (655), and
Borealis (800). Mission-area outdoor exits retain the existing safety escape.
A verified direct Scotty warp from a completed dungeon into the saved terminal
city now retains its observed landing and enables the same recorded city-return
route used by long-distance travel. Previously this direct path bypassed that
eligibility flag, leaving the character at the warper and checking terminal
visibility immediately.

Recorded city routing precedes terminal lookup. If no route matches and the
terminal has not streamed yet, the saved terminal position can supply the existing
collision-safe landing/standoff planner. Exact saved identity and position are
still required before rolling. Within 10m of the saved position, missing terminal
visibility receives a bounded eight-second wait before explicit failure. Grounded
vehicle detachment, three-minute return timeout and no-progress limits remain.

## Inventory/Logistics

The observed user-designated Masterpiece Ancient Combat Tuner (template 267696)
is protected by low/high template IDs through InventoryPolicy.IsOperationalItem,
including Identity.None instances. ManagerLoot's existing operational hook applies
this protection to Junk staging and sales classification, including an item
already inside a Junk bag. No generic HUD1-tag exemption is added, since that would
also protect unrelated rewards. Other equipment can use existing ProtectedItemIds
in inventory-policy.json. No HUD1 selection setting exists in current main.
This batch does not automatically retrieve the tuner already staged in RK Junk.

## Dungeon

The initial crossing probe against a locked door is one second instead of three.
Unlocked doors retain their three-second probe. Lockpick presence/backpack snapshot
readiness, verified interaction range, two-second retry pacing and bounded attempts
remain unchanged.

## Validation

Eight suites passed: CityReturnEquipmentDoorBatch, CityWarperTerminalRoutes,
TerminalLandingApproach, AnyTerminalSharedHub, OperationalBackpackSnapshot,
RewardAndLogisticsOrigin, BackgroundBagSortingIsolation, CombatAwareContainerDeferral.
The new suite compiles and executes production city/equipment/door predicates and
checks direct-warp landing ownership, route-before-terminal order, nearby streaming
wait, exact-live arrival verification, shared equipment protection and lockpick
readiness/retry invariants. Existing terminal regression now explicitly requires
non-null live terminal proof when using the saved-or-live navigation position.
Whitespace checks pass. Full net48/AOSharp build and DLL-dependent classification
regressions remain for the user's local compile environment.

## AO validation

- Complete a mission and warp to OA/ICC/Bor: expect no post-zone escape climb,
  verified landing, recorded city route (where matched), safe landing/dismount,
  exact nearby terminal verification, then rolling.
- If the terminal is not visible at the landing, confirm travel continues; a
  missing nearby exact terminal must wait briefly then stop explicitly.
- Confirm the tuner is protected during staging and sale selection, while the
  unlisted tutoring-device reward remains independently eligible for Junk.
- Approach a locked door: expect one second of initial probing, then lockpick use
  once range and carried-item readiness are satisfied; verify unlocked-door
  crossing and subsequent lockpick retry pacing remain normal.
