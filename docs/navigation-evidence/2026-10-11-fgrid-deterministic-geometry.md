# Fixer Grid deterministic geometry provenance

The canonical PF 4107 model was fitted from the user-supplied `navigation-routes(1).json`
recording dated 2026-10-11. The source contains raw, unmodified `Fgrid Floor 1` through
`Fgrid Floor 10` routes; the corrected Floor 6 route contains 47 points at Y 61.435.
The raw file remains user/runtime evidence and is not rewritten by RKMission.

The common least-squares horizontal ring fit is:

- center X: 307.003
- center Z: 67.037
- centerline radius: 11.694 m
- floor height: `floor * 10 + 1.435`

Per-floor recordings vary slightly because they were walked manually. The shared model
is intentionally used instead of ten separately noisy circles. Ring anchors are always
calculated as the horizontal intersection of the center-to-target radial line with this
centerline circle. No portal anchor table is hand-maintained: all 78 surveyed exit
anchors and all lift anchors therefore use exactly the same calculation.

Generated arc waypoint spacing is at most 2.5 m of arc length. Every waypoint is placed
on the canonical circle, preventing a controller from replacing the route with a long
inward chord. The initial live tolerances are 0.75 m for anchor acceptance, 1.35 m ring
cross-track, and 1.25 m radial cross-track; live AO testing may tune these tolerances
without changing the fitted geometry.
