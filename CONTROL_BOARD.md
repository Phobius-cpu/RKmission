# RKMission Control Board

_Updated: 2026-10-14_

## Current release gate

**PRE-SOAK batch — VALIDATED**

Implementation is present on `main` beginning with `53058ab` and includes:

- Vehicle Lifecycle
- Neko Handoff
- Neko UI
- City Vehicle Routing for OA / Borealis / ICC
- Resurrection / Death Recovery expansion

On 2026-10-14 the user reported RKMission "ready for the soak." This is the AO
validation statement for the batch as a whole: the pre-soak checks are complete
enough to begin long-duration testing. It does not claim separate evidence for
every checklist scenario beyond that explicit readiness statement.

## Active validation phase

**LONG AUTONOMOUS SOAK — AWAITING AO TEST (ready to begin)**

Run repeated full autonomous mission cycles from OA, Borealis, and ICC origins,
covering rolling, travel, Scotty/FGrid/Neko, city routes, vehicle lifecycle,
mission dungeon, combat, loot/inventory settlement, exit/chaining, terminal
return, logistics, and death recovery when naturally encountered.

The primary goal is to detect cumulative or stale-state failures, rare routing
fallbacks, inventory/logistics drift, repeated-transition failures, and
long-running lifecycle/watchdog problems. Do not reopen already validated
mechanics without evidence. Future changes should be driven by concrete soak
failures and logs.

## Workstreams

### Travel

- **VALIDATED:** Borealis Scotty routing exclusively uses live-verified
  `/tell scty bor` for `Borealis, Near Baffbotz, Implantbob, Reclaim`. The
  assigned warper remains dynamic and identity-verified; `Warpboi3` is only the
  observed assignment from the confirming run. Current `main` also contains the
  OA Sirocco, OA Clan Agency, ICC Lift, ICC Statue, and Borealis Baffbots
  warper-to-terminal navpaths (`600b2be`); the user's soak-readiness statement
  covers this city-return item without asserting route-specific evidence beyond
  that statement.
- **VALIDATED:** PRE-SOAK Vehicle Lifecycle, Neko-first handoff, and
  vehicle-aware OA/Borealis/ICC routing, based on the user's 2026-10-14
  soak-readiness statement.
- **DECIDED:** global Scotty nearest-subdestination ranking across all
  playfields remains a separate Travel item. It is not folded into PRE-SOAK.
- **OPPORTUNITY:** staircase recovery and unreachable-combat continuation stay
  available for evidence-led improvement; neither is reopened as a PRE-SOAK
  blocker.

### Dungeon

- **VALIDATED:** PRE-SOAK exact dungeon verification after Neko entry, stale
  movement cancellation on key acceptance/zoning, and same-playfield chaining,
  based on the user's 2026-10-14 soak-readiness statement.
- **VALIDATED:** fingerprint-gated cross-instance `Mine_Big16_1` multi-floor
  ramp navigation at 90 degrees: entrance alignment, ascent, upper-container
  loot, ManagerLoot handoff, dense reverse descent, ground run-out and doorway
  resumption. Validated main commit: `88ba828`.
- **AWAITING AO TEST:** reusable placements at 0, 180 and 270 degrees have
  static transform/regression coverage but no live AO validation yet. Preserve
  dense semantic ramp segments; do not reduce a complete ramp to one leg.

### Combat

- No new PRE-SOAK implementation item. Existing bounded unreachable-combat
  continuation remains an opportunity-based behavior, not a reopened blocker.

### Inventory / Logistics

- **AWAITING AO TEST:** post-hydration/post-transfer bag auto-close remains a
  separate Inventory/Logistics item and needs specific AO evidence; user-owned
  bags must not be closed. The batch-level soak-readiness statement does not
  supply that separate evidence.
- Existing validated logistics evidence remains unchanged.

### Objectives / Lifecycle

- **VALIDATED:** PRE-SOAK Resurrection / Death Recovery expansion, based on the
  user's 2026-10-14 soak-readiness statement. This extends the existing
  resurrection topic; it is not a second subsystem.
- Batch 1 Autonomous Cycle Closure is implemented on `main` (`2579cab`), but no
  new AO validation is inferred by this closeout.

### UI

- **VALIDATED:** Neko key discovery, manual Key Warp, ON/OFF state, and help
  text covering the Rubi-Ka reclaim prerequisite and Nano Can-or-wait behavior,
  based on the user's 2026-10-14 soak-readiness statement.

## PRE-SOAK AO test checklist

**Phase result:** complete enough to proceed to soak, per the user's 2026-10-14
AO readiness statement. Retain this checklist as the pre-soak gate record; it is
not a claim of separately captured evidence for every numbered scenario.

1. **Startup/basic regression smoke test:** confirm no unsolicited movement;
   the Neko UI loads; and Roller, ManagerLoot, Map, and Settings still work.
2. **Neko UI key discovery:** confirm detected keys, icons, and context,
   including a same-name/same-playfield case if reproducible.
3. **Neko UI enable/disable:** OFF suppresses automatic Neko and ON restores
   it; the state is text-only green `ON` / red `OFF`.
4. **Neko UI manual Key Warp:** confirm it uses the same bounded engine as
   `/rkm auto` and requires exact dungeon verification.
5. **Neko-first handoff:** confirm destination playfield settles, then Neko,
   then dungeon, with no local door travel first.
6. **Neko success while already flying:** preserve flight where possible and
   avoid unnecessary door landing.
7. **Neko fallback:** fall back cleanly to `LocalMissionTravel` and preserve a
   useful flight state.
8. **Neko stale-movement cancellation:** cancel stale movement on
   `KeyAccepted` and zoning.
9. **Vehicle ownership when already flying:** respect user-owned state.
10. **Vehicle ownership when RKMission activates flight:** verify live state
    before Fly movement.
11. **Short-leg decision:** stay on foot when flight is not worthwhile.
12. **Post-zone flight safety:** retain diagonal/lateral clearance.
13. **Mission exit -> next same-playfield Neko mission:** avoid a pointless
    vehicle cycle.
14. **OA city vehicle routing:** test warper -> roller, roller -> Grid/FGrid,
    and roller <-> bank/shop.
15. **Borealis city vehicle routing:** run the same hub tests.
16. **ICC city vehicle routing:** emphasize ramp/elevation and reverse
    direction.
17. **City route reacquisition:** join a safe forward portion instead of
    returning to route start.
18. **Vehicle -> precise terminal interaction:** use a transit/landing anchor,
    then a grounded precision approach where required.
19. **Death inside mission with Rubi-Ka reclaim:** preserve the mission/cycle
    and enter recovery.
20. **Resurrection Nano Can available:** use it and verify sickness clears
    before travel.
21. **Nano Can unavailable:** wait the full resurrection-sickness cycle; do not
    use Scotty, FGrid, or warp while `TemporarySkillReduction`/sickness remains.
22. **Death recovery -> Scotty:** return to the actual saved roller city
    (OA/Borealis/ICC), not a faction assumption.
23. **Death recovery -> exact saved terminal:** verify terminal identity and
    position after city arrival.
24. **Death recovery -> original accepted mission:** resume through the normal
    cycle without duplicate rolling.
25. **Outside-RK reclaim guard:** stop safely for manual recovery; do not
    improvise Garden/SL travel.
26. **Help/interface documentation:** explicitly state that autonomous death
    recovery requires save/reclaim on Rubi-Ka and explain Nano Can-or-wait.
27. **Final integrated pre-soak run:** complete 2–3 consecutive missions using
    Scotty, vehicle city travel, Neko entry, dungeon completion, exit/chaining,
    and terminal/logistics if triggered, with no stale state or manual
    intervention.

### Readiness gates

- Travel/Neko is soak-ready after tests 1–18 and 26–27 pass.
- Resurrection is soak-ready after tests 19–24 pass as well.
- Test 25 remains the required safe guard for unsupported reclaim locations.
