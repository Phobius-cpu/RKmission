# Automatic roller, mission limit, and window checkpoint — 2026-10-01

The embedded roller's destination mode previously set rolling active and
requested an offer, but the default Auto Adjust Level Slider immediately
treated an empty item roll list as an error and turned rolling off. The
destination mode now bypasses that item-list adjustment. It still uses the
original mission terminal request, offer callback, and CreateQuest acceptance.

`/rkm auto` uses enabled Rubi-Ka locations from the roller when no zone is
explicitly set. `/rkm zone <id>` chooses one destination, and `/rkm zone all`
returns to enabled locations. Mission type toggles and configured location
bounds apply to automatic destination selection. A failed request or missing
terminal reports a reason instead of claiming that rolling started.

`/rkm limit <count|off>` sets the automatic acceptance cap; `/rkm rolls`
continues to cap offer attempts. The acceptance count increments only after
the quest appears in RKMission's accepted list. RKMission processes accepted
work before stopping at the cap. An offer whose acceptance remains unconfirmed
stops the cycle for user inspection, avoiding an unintended extra acceptance.
The limit and current count are saved in the checkpoint.

The roller main and help windows use `WindowStyle.Default`. The main header
and content panels use native borders; the header has labeled buttons and
Mali's top-left icon was removed. The underlying roller settings, item
browser, and mission controls remain.

Per user instruction, this change was not compiled or tested in this session.
The user will pull, compile, and validate rolling, limits, and appearance in
game.
