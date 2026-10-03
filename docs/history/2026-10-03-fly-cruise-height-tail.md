# Fly cruise waypoint height tail (2026-10-03)

The user observed a distant outdoor flight leg that reached approximately
`X=850.09, Z=1898.69` while its waypoint was at `X=850.03, Z=1898.64`.
The player was at `Y=137.15` and the old waypoint at `Y=140.44`.
The 3.29 m height gap left the 3D arrival test open. The movement executor
reduced horizontal steering to 0.25 m, waited for its four-second progress
limit, and recorded a failed flight corridor even though the horizontal
waypoint had been reached. The earlier cruise continuation had correctly
kept the 60 m forward leg, but flight had lost height along that leg.

Only distant `FlyToEntrance` cruise waypoints now settle when their X/Z is
within 1.5 m and the remaining height gap exceeds 0.8 m. The result is
recorded as a horizontal cruise pass with the actual height gap, not as a
reached 3D point or a blocked corridor. When the observed altitude is more
than 1.5 m below the advisory cruise height, transit replans a diagonal
clearance leg from the actual position. Cruise clearance has at least 12 m
of horizontal runway, bounded at 24 m, to avoid another almost vertical
short leg. If that clearance attempt also finishes its X/Z run without
matching Y, it proceeds to reactive transit from the actual position rather
than remembering a collision at the already traversed corridor. Other flight
stages retain their 3D arrival and stall rules,
especially entrance height matching, close approach, and interaction.

The user will pull, compile, and inspect live AO flight. Verify a long cruise
with height loss, a clear but vertically offset waypoint, an actual terrain
obstruction, and entry height alignment. The expected log for the first case
is `Fly cruise waypoint passed at X/Z` followed by a new clearance or transit
leg, without `Fly obstruction observed` for the old waypoint. No build or
live AO check was performed in this change.
