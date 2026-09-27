# RKMission Conversation History

This log records user-visible RKMission conversation context. It intentionally excludes hidden system instructions, hidden chain-of-thought, credentials, and other private internal data.

## 2026-09-27

### User

> I want to create a bot for the game Anarchy Online that rolls missions in Ruby-ka and explores, kill all the enemies, and lockpicks doors and any treasure chests etc. in the missions it rolls. The project will be named RKMission as seens on the zip attached. The program that runs the bots/plugins is AO#. Save your memories and conversations on GITHUB.

Files supplied:

- `aosharp.newbots-master.zip`
- `aosp.knows-aosharp-mods-master.zip`
- `aosp-bots-master.zip`
- `malis-dungeon-map-2.0-master.zip`
- `malis-mission-roller-2.0-main.zip`
- `RKmission.zip`

### Assistant

> I’ll treat **RKMission** as the main project and the other ZIPs as reference implementations. I’ll inspect their structure/APIs first, then we can build the automation around mission rolling → travel/entry → dungeon exploration → combat → locked doors/chests → completion/exit, while keeping the AO# integration consistent with the code you supplied.

The RKMission project goal and supplied references were also saved to ChatGPT project memory.

### User

> you don't need to compile or test. I will pull, copile and test ingame and point out if any changes need to be made

### Assistant

> Understood. I’ll focus on **source changes only** for RKMission and won’t spend time compiling or testing unless you specifically ask me to.
>
> You’ll pull the changes, compile them, test in Anarchy Online, and send me any compiler errors, AO# exceptions, logs, or in-game behavior that needs correcting. I’ll use those results to iterate on the code.

### User

> Save your memories and conversations on https://github.com/Phobius-cpu/RKmission

### Assistant action

Confirmed access to the private `Phobius-cpu/RKmission` repository and created persistent project-memory and visible-conversation history files.

### User (continued in Codex)

> Inspect the uploaded ZIP archives and connected GitHub repo. Merge the relevant
> AO#/AOSharp, mission roller, dungeon mapping/navigation, combat, looting,
> lockpicking, and mission selection into RKMission. Do not compile or test
> locally; I will compile and test in-game. Commit coherent source changes.

### Assistant action

Inspected all six supplied archives and the repository. Replaced duplicate
placeholder classes with one AO# plugin, a zone-filtered mission roller, and
a dungeon room-clearing loop. Documented the cross-playfield navigation and
quest-completion checks for in-game follow-up. No local compile or test was
performed.
