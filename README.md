# RKmission — Rubi-Ka Mission Bot

This repository adapts the dungeon-oriented layout of `AOSharp.NewBots` into a Rubi-Ka mission runner for Anarchy Online.

## Architecture

The solution currently contains one portable core project:

- `RKmission/MissionRunner.cs` — deterministic mission state machine.
- `RKmission/AoSharpMissionAdapter.cs` — the only AOSharp-specific integration boundary.
- `RKmission/RkMissionPlugin.cs` — host lifecycle and command facade.

This separation mirrors the source solution's division between shared bot behavior, dungeon solving, dungeon execution, and mission-specific plugins:

| AOSharp.NewBots concept | RKmission equivalent |
| --- | --- |
| `BTBotBase` | `MissionRunner` and `IMissionWorld` |
| `DungeonSolver` | visited-room tracking and target selection |
| `DungeonRunner` | `RkMissionPlugin` update loop |
| mission-specific bot | `AoSharpMissionAdapter` plus mission configuration |

## Intended Rubi-Ka flow

1. Detect that the character is inside a mission instance.
2. Discover visible rooms/areas and record visited room keys.
3. Find the nearest unopened door or chest.
4. Navigate to it.
5. Check inventory for a usable key or lockpick when locked.
6. Interact with the door/chest.
7. Loot chests.
8. Continue until the mission is explored or a safety limit is reached.

## AOSharp integration

The linked GitLab solution file identifies the project layout but does not contain the API declarations needed to compile against a specific AOSharp checkout. Therefore, all calls that vary by AOSharp version are deliberately isolated in `AoSharpMissionAdapter.cs` and marked with `NotImplementedException`.

To finish the integration, bind these adapter methods to the checked-out AOSharp.NewBots APIs:

- mission-zone and character state
- visible door/chest entity discovery
- navmesh or dungeon movement
- door/chest interaction
- inventory key/lockpick lookup
- chest loot/container access
- movement cancellation
- chat output

No raw packet manipulation or combat automation is included.

## Commands

The host facade accepts:

- `start`
- `stop`
- `status`

Register these with the exact AOSharp chat-command API used by the target checkout.

## Safety behavior

- Stops if the character leaves the mission or dies.
- Limits duration and visited rooms.
- Uses interaction cooldowns.
- Skips locked objects without a usable key/lockpick.
- Stops after repeated interaction failures.
- Does not automatically attack characters or mobs.
