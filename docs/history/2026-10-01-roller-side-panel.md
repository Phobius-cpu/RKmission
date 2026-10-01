# Spacious roller Settings and clean Accept area — 2026-10-01

The Settings layout follows the selected spacious mockup: Roll List and
EXTRAS in the left column; Playfields, TYPES, and sliders in the right.
The Playfields scroll view has a fixed visible viewport and its own scroll
client, with its action buttons below the list. This retains all settings
and controls while giving Playfields four visible rows before scrolling.
The existing Mali on/off graphics remain on Mission Types, EXTRAS,
Playfields, and the five DB category filters, as requested after the mockup.
The Roll List and DB Browser selectors and item plus/minus controls also use
their original Mali graphics. Types and Extras options are centered within
their panels. List headers and both row types share a centered 292-pixel cell
grid inside the 298-pixel list body, with a separate 12-pixel scrollbar area.

The mission card keeps its four scoped Ping hit regions and the separate
centered Accept button. The Ping regions now use a fully transparent roller
texture for raised, hover, and pressed states to prevent the AO button skin
from appearing in the spaces beside or below Accept. Mission behavior and
slider values are unchanged.

The embedded XML files parse and the project builds with zero errors. AO
client rendering, scrolling, and click behavior require in-game review.
