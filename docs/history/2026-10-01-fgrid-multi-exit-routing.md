# FGrid multi-exit mission routing checkpoint — 2026-10-01

The completed portal survey is promoted from observation to the canonical
shipped route list in `RKmission/Data/FixerGridSurveyExits.json`. Every verified
record retains portal identity, FGrid floor and XYZ, destination playfield,
outdoor arrival XYZ, and any supplied verification metadata. Destinations may
have multiple exits. The supplied survey validated as 78 unique portals across
46 destination playfields, with exactly eight on each floor 1–9 and six on
floor 10. Twenty playfields have more than one exit. Runtime survey
observations merge into the same route
index without replacing another portal in the playfield.

For an accepted mission, the long-range planner passes its `Entrance` quest
world-position anchor to FGrid. The initial cost is horizontal distance from
each verified outdoor arrival to that anchor. A reliable local ground route
estimate can later replace this cost; the current ground navmesh cannot be
assumed available for remote playfields. The provider logs the selected
portal, floor, arrival, and estimated remaining distance, travels via the
existing MovementArbiter and SMovementController, and targets the exact
surveyed portal. If that portal cannot be found or does not initiate zoning,
it tries another verified portal on the current or a higher FGrid floor. A
wrong destination or unverified portal remains a failed FGrid attempt.

The old `RKMissionData/fixer-grid-exits.json` is read but never rewritten.
Successful new observations are saved in the existing survey file and the
multi-exit `RKMissionData/fixer-grid-exits-v2.json`. This prevents a legacy
one-exit record from erasing other routes and preserves the user's old file.
Nearby mapped links, Scotty, FGrid, and normal mapped graph travel retain
their existing order. Dungeon loot, pathing, and combat are outside this
checkpoint.

Validation: the .NET 4.8 project builds with zero errors. For playfield 570,
sample mission anchors near `(3000, 2850)` and `(1100, 2200)` select portal
`1478272516` and `1478272517`, respectively. In-game portal
visibility, zoning, and two missions in one playfield near different arrivals
remain for the user's live test.
