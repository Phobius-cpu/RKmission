# Roller initial layout refresh (2026-10-04)

The window is first fitted while the mission panel is hidden. Opening a mission terminal then shows the panel on the already active RKMission tab, which previously skipped the tab fit and left the window clipped until another tab was selected.

Showing the mission panel now refits the active content and host. Selecting RKMission while it is already active also refreshes layout and tab appearance without removing or re-adding the content. Hosted tabs, settings behavior, and window dimensions are unchanged.

Source build verifies the change. The initial terminal-open and tab-switch behavior still needs in-game validation.
