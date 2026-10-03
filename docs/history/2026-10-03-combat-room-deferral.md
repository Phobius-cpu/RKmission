# Combat room deferral after geometry stalls (2026-10-03)

The preceding room 3 revision stopped the run indirectly: three stalls exhausted combat approaches, `CombatDriver.Tick` returned `false`, and `MissionDungeon.FightInRoom` called `Stop()`. The user specified that this must end only the current movement attempt.

## Behavior

- `CombatDriver.Tick` distinguishes ongoing engagement from an unavailable approach. Its deadline and an exhausted route both request recovery; neither directly stops the dungeon.
- The room stays unfinished and is recorded as `CombatDeferred`. A room scan that cannot navigate never counts as clearance while a live enemy remains. Enemy presence still blocks objective finale, all-room clearance, and mission exit.
- Recovery selects at most one scene-checked, complete-navmesh alternate position for that episode. It tries the point with a movement-progress and time bound. Once the player moves at least 3 m from the failure position, the enemy moves, or attack LOS/range opens, it rebuilds approaches. Repeated failures at the same position do not cause an unbounded collision loop.
- If the player is safe to leave, routing works on other unfinished rooms first. When those are exhausted, recovery can use one adjacent room as an alternate entry, including a previously cleared room. A deferred room becomes a route goal again after a cooldown and a changed position. If no safe alternate or other room is reachable, the bot holds with the mission armed; the global progress watchdog treats that as an intentional wait.
- A different immediate enemy threat can still be fought while one enemy in the room is deferred. Existing fatal objective, readiness, and mission identity gates retain their own behavior.

The user requested to pull and compile personally. Static source and diff checks only; no successful AO client movement is claimed.
