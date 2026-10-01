# Shared plugin UI palette — 2026-10-01

The embedded ManagerLoot window supplies the visual reference: native AO
buttons with cyan text and borders, neutral text, and default panel borders.
Mali's Dungeon Map 2 already uses those buttons and the native red/cyan
sliders. The Roller now uses the same native button styling, text and panel
palette, and slider graphics. Its previous custom yellow labels, cyan border
tint, purple Accept image, green selection circles, and teal/purple slider
textures are no longer applied. Compact selection and list buttons now show
native framed X/blank and +/− labels with fixed sizes.

Mission-card Ping regions remain transparent and scoped to their own card;
Accept remains a separate centered action button. Existing click handlers,
rolling, mission acceptance, item lists, and slider value ranges were kept.
The graphical help Close control is a native labeled button. AO's built-in
Missions window is outside this embedded plugin UI.

Per user instruction, this checkpoint was not compiled or tested. The user
will pull, compile, and inspect the visual alignment and controls in game.
