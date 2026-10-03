# Scottyboi queue and internal-wall combat follow-up (2026-10-03)

## Evidence and decisions

- The supplied trace shows seven static combat approaches to a Tough Scoundrel in room 3, two stuck recoveries and a final failure at 16 m with line of sight false. This proves the selected approach route did not produce an attack opportunity; it does not prove which internal wall or navmesh edge caused the blockage.
- Scottyboi's verified queue may name an offline assigned warper. The bots log in on demand, so offline means wait for the assigned bot, not immediate fallback. The wait remains bounded at 180 seconds. Identity lookup retries every 12 seconds, buffered invites survive unresolved lookups, and only a verified warper identity can authorize acceptance.
- Automatic missions with confirmed completion and clearance may ask for a cross-playfield Scotty warp while still inside the dungeon. The destination follows the existing refill/accepted-work priority. The dungeon exit is paused during the request. Timeout or renewed combat returns to the normal exit route, with its timer restored. A settled, verified Scotty destination is required to chain from a warped exit; ordinary outdoor exit proof still applies to a manual exit.
- Combat candidates now include diagonal points through a 10 m radius. A complete navmesh corridor is still mandatory. If AO surface rays identify a clear firing side, blocked sides are deprioritized. Player displacement, rather than Euclidean distance to a candidate, extends a path attempt. A stuck signal advances to another candidate; the engagement limit is 120 active seconds to allow the wider route.

## Live checks requested from the user

1. Queue a known offline assigned warper and capture the offline reply, later login/lookup, auto-accepted invite and destination. Confirm unrelated team invites remain unaccepted.
2. Finish a mission with the next destination in another playfield. Confirm the warp request occurs inside the completed dungeon, exact destination settles, and chaining resumes. Also exercise timeout and manual exit fallback.
3. Revisit the Tough Scoundrel layout. Capture chosen points, actual path around its wall, line of sight before attack, and any remaining stuck diagnostics.

Source/diff review only. The user asked to handle pulling and compiling from now on; no build or AO client test is claimed for this follow-up.
