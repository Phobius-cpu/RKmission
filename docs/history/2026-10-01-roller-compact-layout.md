# Compact roller Settings and mission-card cleanup — 2026-10-01

The user chose the earlier compact restored-list mockup after testing the
spacious layout. Settings now stacks Roll List/DB Browser, a TYPES/EXTRAS row,
Playfields, and sliders in a 310-pixel column. The Playfields scroll viewport
shows three 30-pixel rows and retains its separate scroll client.

The AO screenshots exposed a purple hover strip on a mission-card Ping region
and small scroll controls at the end of the reward row. Ping buttons now clear
the AO color override and all state borders; the empty footer Ping buttons are
also fully transparent. The reward list is constrained to
six cells in a 210x22 viewport. Card values have wider right padding, and
long title truncation uses an ellipsis. Ping and Accept callbacks are unchanged.

The embedded XML parses and the project builds with zero errors. AO client
rendering and interactions still need an in-game check.
