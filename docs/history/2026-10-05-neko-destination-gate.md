# Destination-local Neko entrance resolution — 2026-10-05

After returning manually to Old Athen with an accepted Eastern Fouls Plains mission, `/rkm auto` built 49 static Neko ACG key/entrance pairs and began submitting them from playfield 540. The coordinator previously ran entrance resolution before cross-playfield travel by design. Static entrance identities provide no current-zone association there, and the two-second bounded response window could delay the verified travel provider by roughly 98 seconds for this mission.

The coordinator now validates mission preflight, completes `RubiKaTravelPlanner` whenever the live playfield differs from the mission destination, and clears stale entrance attempts during that travel. Only after exact destination-playfield arrival does it rescan the selected live mission and try Neko candidates before local Run/Fly entrance travel. Exact dungeon identity verification and the existing bounded candidate/fallback behavior are unchanged.
