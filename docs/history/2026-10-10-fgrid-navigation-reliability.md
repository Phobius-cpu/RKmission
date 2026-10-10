# FGrid navigation reliability — 2026-10-10

## Scope

This Travel-only follow-up preserves Batch A service entry and Scotty behavior,
the Recast-first hybrid strategy, movement ownership, bounded stall handling,
lift-arrival recovery, and unsupported-walking protections.

## Changes

FGrid no longer treats one hardcoded `NavMeshes/4107.nav` filename as proof that
SharpNav is unavailable. Artifact diagnostics and the short AOSharp loader-settle
window recognize case-insensitive `4107.nav` and `4107.Navmesh` candidates. The
live AOSharp `HasPathfinder` state is the runtime availability authority, and a
SharpNav leg still requires the existing complete corridor, endpoint projection,
physical-floor sampling, and wall checks.

The movement order is now explicit: Recast first; after rejection or a bounded
nine-second stall, SharpNav; after SharpNav is unavailable, rejected, or stalled,
the recorded fallback. Recorded fallback selection is limited to the exact
`fgrid-floor-{floor}-lift` or `fgrid-floor-{floor}-portal-{identity}` name and
retains both endpoint matching and live walkway validation. RKMission does not
substitute an unrelated recording or walk blindly.

## Verification

- Release build: zero errors; existing nullable, obsolete, and package-audit
  warnings remain.
- Full focused PowerShell regression suite: pass.
- New `tests/FGridNavigationReliabilityRegression.ps1`: protects artifact
  resolution, Recast → SharpNav → named-recording ordering, bounded rejection,
  and the floor-4 lift/portal fallback naming and endpoint gates.

## AO tests still required

1. Reproduce the floor-4 lift case with `4107.Navmesh` available. Confirm a
   stalled/rejected Recast leg falls through to SharpNav without a false
   `4107.nav missing` failure and reaches the lift.
2. With SharpNav unavailable or its corridor rejected, confirm only a matching
   `fgrid-floor-4-lift` recording runs and that a mismatched endpoint/name stops
   safely.
3. Complete a multi-floor run through the selected portal and verify exact
   destination zoning and settled local continuation.
