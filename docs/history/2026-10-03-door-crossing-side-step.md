# 2026-10-03 post-crossing side-step correction

The user reported that a small sideways movement still occurs after crossing
mission doors. Review of the current `MissionDungeon` transition found two
post-crossing causes: `SafeInterior` navigated to Mali's nearest sampled
interior point, which can be laterally offset from the doorway, and the route
kept running during the 500 ms safe-entry confirmation window.

`DungeonLayout` now derives an aligned safe-interior point from the same
source-to-target doorway axis as `TargetCenterline`. Once AO identifies the
target room, normal and direct fallback movement use that aligned point. If
the player is already safely inside, or has moved at least 1.5 m inward while
the polygon margin is uncertain, the door movement owner halts while exact
room confirmation settles. The direct fallback and independent inward proof
also use the doorway axis instead of a vector toward Mali's offset interior
sample. Target-detection and confirmation logs now include inward and lateral
displacement for live diagnosis.

The 30-second transition deadline, room identity check, 500 ms safe geometry
confirmation, two-second fallback identity confirmation, deep fallback before
room detection and movement ownership remain in place. The .NET Framework 4.8
build passes. In-game motion and doorway geometry still need user validation.
