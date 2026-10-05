# Logistics route smoothing — 2026-10-05

The user validated Borealis bank/shop and ICC bank/shop endpoints,
transactions, zoning arrivals, and return routes. Borealis completed both
doorway crossings automatically. ICC reached and returned from both targets;
the ICC bank outward crossing correctly held for manual movement because its
recorded approach direction was uncertain. Bank round trips and one-item shop
sales were verified at both sites.

The recorded paths contain a sample about every 1.5 metres. Replaying every
sample created many short movement legs. `LogisticsRouteNavigator` now looks
ahead along the unchanged recorded route and selects the farthest point that
meets all of these gates:

- no more than 18 metres from the current recorded anchor;
- every skipped sample remains within a one-metre horizontal corridor;
- every skipped sample remains within 1.25 metres of interpolated height;
- two live body-height rays find no obstacle before the target.

If a gate fails, the original next sample remains the target. Corners, height
changes, walls, target positions, and the recorded final doorway approach are
therefore retained. Doorway crossing direction, bounded forward movement,
playfield verification, arrival checks, and transaction bindings are
unchanged. The controller also stops resubmitting the same destination every
two seconds while AOSharp reports active navigation; it resubmits only after
navigation becomes idle and retains the existing eight-second progress stop.

The geometry regression checks every bundled stage, preserves every endpoint,
and reduces the dense 29–58 sample stages to 3–9 geometric legs before live
ray checks. Short straight indoor stages reduce to 1–2 legs. Release build and
the smoothing regression passed. Live movement quality and obstacle rays
still require one route run after the user pulls the change.
