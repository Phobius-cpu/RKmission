# RKmission

A conservative Rubi-Ka mission exploration bot scaffold for AOSharp.NewBots.

The repository is intentionally split into a mission state machine and an AOSharp adapter. The adapter is the only layer that should call version-specific AOSharp APIs, so changes in the upstream SDK do not spread through the mission logic.

## Status

This is an integration scaffold. The exact AOSharp.NewBots source was not available through the public GitLab browser, so `AoSharpMissionAdapter` contains explicit TODOs for binding navigation, entity discovery, interaction, keys/lockpicks, and loot to the version checked out locally.

## Commands

- `/rkm start` — start exploration
- `/rkm stop` — stop and clear movement
- `/rkm status` — print current state

## Safety defaults

- No combat automation is included.
- Interactions are restricted to objects supplied by the adapter.
- Every interaction has a cooldown and retry limit.
- The bot stops after a configurable number of rooms, failures, or minutes.
- Locked objects are only attempted when the adapter reports a usable key or lockpick.

Copy the project into the AOSharp.NewBots solution and replace the adapter TODOs with the exact APIs used by that checkout.
