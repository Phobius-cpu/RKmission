# Room 3 combat geometry recovery (2026-10-03)

## Live evidence

The user supplied a room 3 screenshot and `/pos` output at 286.6, 210.5, 5.3. The prior failed combat diagnostic placed the player at X/Z 286.5949/210.4514, and the new trace issued five mapped approach points toward a Tough Scoundrel, including a stuck signal, while the character remained at that location. The screenshot shows the character pressed against rock geometry. The generated navmesh could return complete routes, but the live actor could not follow those commands. The exact blocked navmesh polygon is not identifiable from the screenshot alone.

## Source correction

- Combat candidate scoring now checks each straight navmesh leg for a world obstruction at shoulder height. Loot and objective scoring keep their established behavior.
- A live combat stall halts the current movement and remembers the failed heading near the observed position. Other destinations in that heading are skipped instead of sending another near-collinear command.
- The first stall can request one short side or backward retreat, constrained to the mapped room, a complete navmesh route, and a clear scene corridor. The same movement owner and controller execute it; no position writes or teleport recovery are used.
- If the character still cannot move after three observed stalls, combat movement halts with a diagnostic. The mission stops through its existing unreachable-enemy gate instead of repeatedly pushing into geometry.

## Next live check

After the user's compile, retry the same location. Record the first selected approach or retreat, the next `/pos`, any rejected-heading message, and whether line of sight is reached. If the corridor check yields zero candidates, the actor should stop cleanly; the captured navmesh/room geometry will then be needed for a route around this particular rock.

Source and diff checks only. The user requested to perform pulling and compiling personally.
