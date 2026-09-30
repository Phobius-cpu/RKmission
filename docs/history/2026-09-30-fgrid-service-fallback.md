# Public FGrid service fallback checkpoint

_Date: 2026-09-30_

## Why this exists

RKMission already prefers verified local travel and Scottyboi for long-distance
Rubi-Ka mission travel. Scotty warpers are not all permanently online, and that
is expected operating behavior rather than an integration failure. A second
community-service fallback is now available for destinations covered by the
Fixer Grid.

Player-run FGrid service characters in main cities can cast Team Fixer Grid on
the mission character. That creates the temporary **Data Receptacle** used by
the existing Neko FgridWarper pattern. The character therefore does not need to
own a pre-filled Data Receptacle Container when a configured service bot is
available.

## Implementation

- Added `FGridServiceProvider`.
- Added exclusive `MovementOwner.FGridTravel` ownership.
- Added `Data/FGridServices.json` for live service bot configuration.
- The live service list now contains three user-observed commands:
  - `Fgridgirl` -> `fgrid`
  - `Fgridder` -> `fgrid`
  - `Chewysfgrid` -> `cast fgrid`
  They are currently configured with `PlayfieldId: 0` and no fixed terminal
  position, so they are eligible wherever the bot is reachable and a visible
  normal Grid terminal is available.
- Optional service fields:
  - `Name`
  - `Command`
  - `InviteFrom`
  - `PlayfieldId`
  - `GridTerminalPosition`
- The provider only requests service after moving beside the configured/visible
  normal Grid terminal.
- Team invites are accepted only from the resolved configured bot/expected
  inviter identities.
- It waits for temporary Data Receptacle template **160978**.
- Destination terminal identities come only from the existing confirmed
  `Data/GridTerminals.json` dataset.
- The receptacle is applied using the same identity-driven AO# pattern already
  extracted from Neko's FgridWarper.
- `TeleportStarted` / `TeleportEnded` plus a settle delay gate success.
- The observed destination playfield must equal the requested playfield.
- Temporary FGrid service team membership is released after success/failure.
- One configured service is tried at a time; timeouts advance to the next
  configured service and ultimately fall back to the normal travel planner.
- `/rkm fgrid` reports configured/active/last-failure state.

## Travel priority

For a mission outside the current playfield the intended priority is now:

1. nearby verified mapped link when clearly cheaper,
2. Scottyboi,
3. configured public FGrid service when the target has confirmed terminal IDs,
4. normal mapped Grid / teleporter / zone-border travel,
5. blocked with a clear reason when no verified route remains.

An assigned Scotty warper explicitly reported offline is treated as expected
availability. It releases the Scotty attempt without applying the normal
two-minute integration-error cooldown, allowing FGrid or mapped travel to start
immediately.

## Mort consequence

`GridTerminals.json` already contains confirmed Fixer Grid destination
identities for Mort. Therefore the previous limitation

> no Scotty route + no owned Data Receptacle = no confirmed Mort fallback

becomes

> no Scotty route + configured/available FGrid service = request Team FGrid,
> receive the temporary receptacle, use the confirmed Mort destination identity,
> verify Mort, then resume LocalMissionTravel.

If no FGrid service is configured/available, the planner still refuses to invent
an unverified Mort route.

## Safety / exclusions

This implementation does **not** use direct `LocalPlayer.Position` writes,
WarpManager-style position manipulation, or unverified exit IDs. It uses normal
AO chat/team/item/zoning interactions and the existing movement arbiter.

## Validation still required

The user will compile and test in-game. In particular validate:

- whether the three observed service commands remain stable in live use,
- whether any of the service bots invite from a different character identity,
- whether any bot should later be restricted to a specific city/playfield or Grid terminal,
- timing from accepted invite to Team FGrid cast,
- temporary receptacle detection,
- direct destination terminal invocation for each tested RK playfield,
- team cleanup and fallback when the service bot is offline or fails to cast,
- Mort travel with Scotty's assigned warper offline.
