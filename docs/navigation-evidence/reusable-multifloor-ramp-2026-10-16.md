# Reusable cross-instance multi-floor ramp — VALIDATED at 90 degrees

Date: 2026-10-16  
Workstream: Dungeon  
Validated main commit: `88ba828739e31f62920a6e943064c8af99ace25f`

## AO validation result

The user confirmed that RKMission autonomously entered and climbed the ramp,
reached and looted the upper container, reacquired the saved topology, descended
the ramp, exited onto the ground floor, and resumed normal navigation. No manual
positioning was required in the successful run.

Runtime-validated placement:

- Source: PF 14680685, room 10, `Mine_Big16_1`, position `(86,5,226)`, yaw 0.
- Live: PF 14680681, room 4, `Mine_Big16_1`, position `(66,5,194)`, yaw 90.
- Template position `(128,5,182)`, template yaw 0.
- Exact fingerprint `1C49400F8CC4B73664FFFBEE26F9EDE595F5F8A58CD1CFFBAD87E49F6DBBCC1C`.
- Geometry: 303 captured surfaces and 3 doors.
- Route: `Mine Big16 reusable two-ramp route`, 35 recorded points.
- Reusable climbing begins at recorded point 3; points 1–2 are restricted to
  fingerprint-gated ground entrance/exit alignment.

Static regressions cover 0, 90, 180 and 270 degree round trips. Only the
cross-instance 90-degree placement above has live AO validation. Other
orientations remain awaiting AO evidence. A non-cardinal placement such as 260
degrees is mathematically transformable but must not be described as validated.

## Required safety model

Reuse is permitted only when the live room has the same trustworthy fingerprint,
room/template metadata, surface count, door count, compatible bounds, and
validated placement. Name, room number, or approximate shape alone is not
sufficient. Existing exact-PF routes retain their prior behavior.

AO positive room yaw is clockwise in XZ. The transform pair must remain true
inverses: world to candidate-local uses the positive metadata angle, while
candidate-local to live world uses the negative metadata angle. Translation and
relative Y are applied after rotation.

The dense recorded route is physical corridor evidence. Mali's coarse room
outline and AO downward rays are useful diagnostics, but proved unreliable as
hard vetoes on this inclined mesh. Runtime stalls remain bounded.

## Ramp path invariants

A complete reusable ramp traversal is not one leg. It contains distinct ordered
roles:

1. Ground run-up.
2. Entrance alignment.
3. Trusted slope start.
4. Inclined samples.
5. Slope/landing joints.
6. Upper-platform turns.
7. Target-side handoff.
8. The same physical evidence in reverse for descent.
9. Reversed entrance-alignment points to reach actual ground before door travel.

Straight samples inside one proven corridor segment may be simplified, but a
multi-level route must never be collapsed across entrance, slope, landing,
switchback, clearance-sensitive, or level-transition boundaries. Reusable ramp
playback currently retains all selected physical points. Target-side attachment
must be within 4 m and on the target level. The generated start connector may
approach the recorded run-up, but may not invent a direct player-to-ramp leg.

Points 1–2 were initially unsupported for trusted ramp playback. Live evidence
showed that point 3 is already on the lower slope. The safe repair was to use
points 1–2 only as ordered ground alignment after fingerprint validation. They
are played before point 3 on ascent and after point 3 in reverse on descent.

## Lifecycle invariants

ManagerLoot may become interaction-ready before the final recorded ascent point.
Before movement ownership is released, RKMission must snapshot a meaningful,
vertically progressed, precise reusable ascent. That saved evidence becomes a
mandatory return route for the same playfield, floor and room.

Door navigation must not start while the character is on the upper level or on
the lower ramp slope. Descent reverses the selected dense physical path, skips
only consecutive loot-side points already within a bounded 1.5 m radius, and
continues through both reversed ground-alignment points. The validated live
ground attachment is approximately `(78.28,5.01,198.48)`. The earlier trusted
point-3 attachment `(78.53,5.70,195.81)` is still on the slope and is not a safe
handoff to ordinary doorway navigation.

Failures remain bounded. A return that cannot validate its context, continuity,
upper attachment, ground alignment, or room lifecycle must hold doorway crossing
and stop safely; it must not issue a direct ground-door command from the upper
platform.

## Failure history and lessons

- A counterclockwise transform mirrored the route. AO yaw is clockwise.
- Strict Mali-outline and AO-ray vetoes rejected captured, user-walked ramp
  geometry despite an exact fingerprint.
- A 30 m reusable target allowance selected an early landing 10+ m from loot and
  falsely completed topology.
- Two-point simplification discarded required ramp entrance and turn geometry.
- Projecting point 3 to ground aimed beside/under the ramp because point 3 is
  already on the slope.
- Dropping the active route at ManagerLoot handoff prevented any return descent
  from being prepared.
- Ending descent at point 3 released door navigation while still on the ramp and
  drove the character into a wall.

The general lesson is to preserve semantic boundaries and ordered recorded
evidence. Do not compensate with global coordinate offsets, arbitrary nearest
segment selection, teleports, room-name matching, or unconditional template
playback.

## Expected diagnostics

A healthy run should show an exact template/fingerprint match, all selected
physical points retained, target attachment at or below 4 m, the generated start
connector followed by trusted point 3, ascent preservation before ManagerLoot
handoff when needed, prepared reverse descent, and a final ground attachment near
`Y=5.01`. Normal doorway approach begins only after descent completion.

## Future improvements

- Persist semantic route roles explicitly rather than relying only on indices.
- Track AO validation separately for each fingerprint and orientation.
- Save concise outcomes for ascent, interaction, descent and ground handoff.
- Permit simplification only within classified straight corridor segments.
- Add bounded calibration only from repeated evidence; never globally shift a
  route from one stall.
- Apply this mechanism to another template only after it has its own exact
  fingerprint and reviewed entrance/exit evidence.

## Commit trail

- `7bf83f2` initial reusable template support.
- `60cdfe3` corrected AO clockwise handedness.
- `b884786` restored the 4 m target-side safety gate.
- `1abc8d3` retained dense reusable ramp turns.
- `f77efad` used the two recorded ground points for entrance alignment.
- `c17f8ba` preserved partial ascent across ManagerLoot ownership handoff.
- `88ba828` completed descent through the ground exit alignment; live validated.
