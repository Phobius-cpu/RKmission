# Mission-card Ping texture and reward slots — 2026-10-01

The user reported a remaining purple pixel above Accept and small scroll
buttons beside rewards. The Ping texture now uses the same opaque green color
key pixels as Mali's original UI textures instead of alpha-zero black pixels.

Each mission reward has its own single-item native view in a 32-pixel cell.
The shared six-item list and its scroll controls are gone; item icons and
tooltips remain native. Mission data, Ping, and Accept callbacks are unchanged.
