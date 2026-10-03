# 2026-10-03 mission lifecycle optimization

## Base and scope

Cloned current `Phobius-cpu/RKmission` `main` at
`d493802f2162bd280db0e330aa11c421018d50fd`. That base already contains the
78 verified Fixer Grid exits and recent guarded Recast/SharpNav portal routing.
The existing Scottyboi, public FGrid bot/receptacle, Grid/Navigator,
Neko-derived entrance/key, Mali Roller and Dungeon Map, Manager.Loot,
MovementArbiter, SMovementController, exact mission binding, lift and mission
chain implementations remain the owners of their respective actions.

## Changes

- Recorded FGrid walkway fallback now checks its start connector, every leg,
  live next leg and portal endpoint against supported same-floor geometry.
  Selection considers another recorded route when the closest one is unsafe.
  An unreachable selected portal can advance to another verified exit.
- Mali room connections expose source approach, door center, target centerline,
  safe interior and deep fallback. Normal doorway movement starts on the source
  side instead of navigating to the threshold. Exact AO room identity plus
  physical interior confirmation is retained. Stuck handling responds by door
  phase. Room routing uses distance and failure history with bounded cooldowns.
- Room clearance has explicit observed states. A diagnostic mission execution
  snapshot records rooms, floors, failed edges, objective steps, loot blockers
  and exit information in the checkpoint; it is never replayed as live clearance.
- Live ordinary threats are swept before full HP/nano preparation. A bounded
  emergency HP gate can run before corpse loot. Corpse sources precede ordinary
  containers, then full readiness resumes. The external CombatHandler remains
  responsible for its combat actions.
- Combat, loot and objective approach candidates use one complete dungeon
  navmesh corridor check and Mali room clearance. Loot retains bounded side
  attempts and lockpick attempts. A per-source state ledger distinguishes
  active, completed, skipped ordinary and critical blocked work. Processing
  deadlines reset only on observed state/container-content progress.
- Objective steps expose a state and last known position, use mapped approach
  candidates, perform finale preflight for missing source items and pickup
  capacity, and continue to require action-specific proof plus bound quest
  acknowledgement. Return-item collection and manual hand-in policy remain.
- Selected-mission metadata/capacity is checked before travel. Chaining after
  a completed run requires observed zoning from the verified dungeon into the
  selected outdoor playfield. A bounded dungeon watchdog replans then stops
  when room, movement, loot and objective progress all cease.

## Validation and limits

`dotnet build RKmission/RKmission.csproj --no-restore -v:q -p:WarningLevel=0`
passed with zero errors. The repository has existing nullable/obsolete warnings
in an unsuppressed build. No AO# or in-game run was available. The exact portal
walkway, door centerline and fallback geometry, combat approach LOS/range,
corpse-to-recovery timing, objective pickup/acknowledgement, return hand-in,
reclaim, and exit zoning still need live logs. The 78 surveyed exits were
preserved; no new portal coordinates were inferred. The runtime execution
snapshot is diagnostic, not a persistent navigation or combat resume plan.
