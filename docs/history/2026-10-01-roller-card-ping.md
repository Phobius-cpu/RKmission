# Roller mission-card Ping checkpoint — 2026-10-01

The embedded roller's five existing mission cards now use their content area
as the Ping target. The card's title, playfield, credits, experience, rewards,
and otherwise empty upper area sit inside an invisible AOSharp Button. Three
more invisible click regions cover the bottom left, right, and padding around
Accept. Each region carries only its own card's mission identity and calls the
existing `PingClick` handler, which uploads that mission to the map.

The separate visible Ping button was removed. Accept remains a separate
Button, horizontally centered near the bottom of the 235-by-123 card and 46
pixels wide, about 20% wider than the preceding centered 38-pixel version.
Its callback still calls `AcceptMission`. Keeping
it outside the Ping controls prevents the Accept click from invoking Ping.
Mission data binding, reward slots, stacked-card size, fonts, colors, borders,
and the roller's request and acceptance pipeline are otherwise unchanged.

AOSharp's managed `View` has no click event. The implementation uses nested
Button content and hides each Button state's border locally while preserving
its child views. Hover feedback was omitted to avoid changing the card's
appearance without an in-game UI check. The user will pull, compile, and test;
this session did not compile or run tests.

The first in-game visual check showed cyan corner marks around the invisible
Ping regions. The follow-up sets each region's local alpha to zero as well as
its state border alpha. The Roll List and DB Browser row strip bitmaps were
removed, leaving the item icons and action controls in place. The browser QL
input now has a cyan border and yellow text matching the framed search fields.
