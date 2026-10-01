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
Accept remains a separate centered 76-pixel action button, about 10% wider
than the previous 69-pixel control. Existing click handlers,
rolling, mission acceptance, item lists, and slider value ranges were kept.
The graphical help Close control is a native labeled button. AO's built-in
Missions window is outside this embedded plugin UI.

The fixed content width is 310 pixels: 95 for TYPES and 215 for EXTRAS.
List rows and column headings share a 298-pixel grid, with 12 pixels left
for the scroll bar; the DB Browser uses the same name, QL, and action starts.
The Playfields row now fits its name and two coordinate labels, and the
search fields and footer stay within the parent width. Slider margins match
Mali's Dungeon Map settings XML. XML parsing and `dotnet build` succeeded
with no errors; final interaction and rendering still need in-game review.
