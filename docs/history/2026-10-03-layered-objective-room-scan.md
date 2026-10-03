# Layered objective room scan (2026-10-03)

The user observed the reserved objective finale begin, then room 9's live
enemy at player `(238.1267, 5.01, 246.1788)` and enemy
`(212.2253, 9.0107, 243.5495)`. The enemy was 26.3 m away and the scan
generated zero mapped approach points. `ScanRemainingRoom` called `Stop()`,
disabling ManagerLoot even though the enemy and room were unfinished. The
user clarified that room 9 has stacked corridors connected by ramps.

The finale message is compatible with a remaining reserved objective enemy:
it says ordinary enemies were cleared. The failure was treating absence of
a proven scan route as a mission-fatal condition.

The room scan now checks candidate positions at both observed heights and
the intermediate ramp band, with a bounded wider search only when its first
ring produces no route. The target's Y is never silently replaced with the
player's Y. Candidates require a complete navmesh route with scene corridor
checks before movement is submitted. Combat firing-side selection also tests
the enemy, player, and intermediate heights in rooms with a height difference.
If the scan still has no route, or exhausts its bounded points/time, it enters
combat recovery: the room remains unfinished, its current movement is halted,
and another safe position or room is sought. Recovery resets scan candidates
so a later vantage rebuilds them. It does not stop RKMission or declare the
objective complete.

The user will pull and compile. Live AO validation should confirm whether the
loaded room 9 navmesh actually contains a complete ramp path. Record the
`layered scan` count, selected scan point and Y, route movement, combat line
of sight, and any recovery hold. If all candidates remain zero, the mesh or
Mali outline requires live geometry evidence; this change does not invent a
path through an unverified corridor. No local build or live check was run.
