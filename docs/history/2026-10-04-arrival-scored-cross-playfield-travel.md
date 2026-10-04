# Arrival-scored cross-playfield travel — 2026-10-04

Starting from upstream `main` `14db9ed`, RKMission now builds the available
Scottyboi, FGrid, and mapped-link candidates before asking a provider to
travel when the mission entrance anchor is known. A candidate with a verified
outdoor landing is ranked by horizontal X/Z distance to that anchor plus a
bounded penalty. Scotty adds 120 m for service wait and assignment uncertainty;
FGrid adds 100 m for terminal access, service wait, and portal traversal;
mapped links add 20 m plus at most 80 m of access and 100 m of link traversal
(30 m per link). Outdoor distance is the primary term. An already reached
target playfield immediately hands control to local mission travel after any
active provider finishes its arrival check.

FGrid exposes its nearest currently reachable surveyed exit arrival using
the same horizontal distance method as its internal portal ranking. The
provider still owns exact portal choice, ascending-floor constraints, guarded
Recast or recorded routes, alternate portals, and destination verification.
Legacy exits without surveyed arrival positions remain `Unknown` for scoring.

Scottyboi records a landing only after its assigned-warper warp has settled
in the requested playfield. Records are keyed by destination playfield and
assigned warper in local, Git-ignored
`RKMissionData/scottyboi-landings.json`. When several warpers have been
observed, scoring uses the farthest landing for the current anchor because
the next warper assignment is not known in advance. The Milky Way / Warpdude31
blacklist is retained and cannot enter the learned landing set.

Mapped Grid, border, and teleporter links learn source-specific outdoor
landings in local `RKMissionData/playfield-link-landings.json` after the
observed destination playfield remains stable for two seconds. A graph
candidate is scored only when its final link has a matching observed landing;
source terminal coordinates alone never stand in for destination coordinates.
Both runtime files are shape-validated on load, written via a temporary file,
and left intact if validation fails. No arrival was fabricated from
`GridTerminals.json` or `PlayfieldLinks.json`.

Known candidates run in increasing total cost; unknown candidates retain
the previous Scottyboi, FGrid, graph fallback order, with a nearby direct
link first among unknown candidates. Failed providers advance to the next
candidate. Scoring exceptions return to that safe order. Candidate logs show
distance, penalty, total, and unknown reasons. The coordinator now ticks an
active planner after entering the destination playfield so provider settling
and mapped-link landing observation finish before local mission travel.

Source check: .NET Framework 4.8 project build completed with zero errors.
Live AO travel, warper assignments, terminal zoning, and provider reliability
remain to be validated in game.
