# Travel reliability Batch A — 2026-10-09

## Scope

This batch changes Travel only. It preserves the existing provider ordering,
timeouts, cooldowns, team cleanup, FGrid traversal, numbered Scotty handling,
offline-warper queue, assignment verification and Milky Way blacklist.

## FGrid service acknowledgement

The old service state waited twelve seconds for AOSharp's inventory snapshot to
expose Data Receptacle template 160978. A successful Team Fixer Grid cast could
therefore be followed by a false service timeout when AO had already completed
the cast/team transition but the inventory mirror lagged.

While waiting after an identity-verified invite, RKMission now observes two
AO/AOSharp state signals: Hack Grid Data Stream (Team) in local NCU (nano
160981), or the accepted service team ending after the cast. Either signal
acknowledges the service transition and restarts a bounded 30-second inventory
reconciliation window. RKMission still requires the real Data Receptacle before
using the nearby Grid terminal. If it never appears, the next configured service
and then the existing provider fallback remain authoritative.

## Scotty intra-playfield command ranking

The old parser requested the first matching command under a destination
playfield heading. It now collects every distinct command across the complete
paged menu, keeps each command's location and outdoor WP coordinate, and ranks
verified command landings by horizontal distance to the selected mission
entrance. Choices without a verified waypoint remain usable only after choices
with distance evidence. For Broken Shores, this permits City of Home
(`/tell scty bs`, Priest Fontain WP) to beat Atalas when its menu waypoint is
closer to the mission.

Provider-level Scotty scoring remains unchanged: before a warper assignment is
known it still uses conservative observed destination/warper landings. The new
ranking is only inside the already-selected Scotty provider.

## Verification

- Release build: zero errors; existing nullable/obsolete warnings remain.
- `tests/TravelReliabilityRegression.ps1`: validates bounded FGrid cast
  acknowledgement/fallback and exercises a synthetic two-command Broken Shores
  menu where City of Home wins by mission distance.
- Existing focused PowerShell regression suite: pass.

## AO tests still required

1. Request FGrid service, observe the successful cast/team transition, confirm
   no 12-second false timeout, then verify receptacle use, FGrid entry, existing
   traversal and destination settlement.
2. Exercise an unavailable/no-receptacle service and confirm bounded alternate
   service/provider fallback and team cleanup still occur.
3. Select a PF665 mission nearer City of Home/Priest Fontain, confirm both
   Broken Shores choices are logged, `/tell scty bs` is selected over Atalas,
   the numbered/offline assignment path still verifies, and the settled outdoor
   arrival proceeds to the selected mission entrance.

