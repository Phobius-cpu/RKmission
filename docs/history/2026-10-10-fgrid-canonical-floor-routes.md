# FGrid canonical floor-route reliability — 2026-10-10

## Implemented

Fixer Grid traversal now tries the verified canonical `Fgrid Floor N` recording
before Recast for the current floor's lift or portal approach. Endpoint matching,
the recorded corridor, live floor support, smoothing validation, and movement
ownership checks remain mandatory. Recast and the older
`fgrid-floor-N-lift` / `fgrid-floor-N-portal-ID` names remain fallbacks when the
canonical recording cannot safely serve the requested endpoint.

User route loading now resolves duplicate names case-insensitively per
playfield. The newest `RecordedAtUtc` wins; a later file entry wins a timestamp
tie. The accidental `Fgrid Floor 10a` recording is removed and the normalized
user file is saved, while `Fgrid Floor 10` and every other canonical route are
preserved.

If FGrid traversal fails while the character is still in PF 4107, the travel
planner stops and reports that normal provider fallback is paused. It no longer
hands the character directly to PlayfieldGraph, which has no internal route out
of PF 4107. Existing fallback behavior resumes after the character is in an
external playfield. The case-insensitive `.nav` / `.Navmesh` compatibility
diagnostic is unchanged.

## Validation

- `dotnet build RKmission/RKmission.csproj -c Release`: zero errors (existing
  nullable/SDK warnings remain).
- All eight PowerShell regression scripts pass.
- `FGridNavigationReliabilityRegression.ps1` covers canonical-route preference,
  deterministic route normalization, removal of floor 10a, legacy fallback,
  supported endpoints, compatible mesh diagnostics, and the PF 4107 provider
  guard.

## AO runtime validation still required

1. From floor 2 with a higher target floor, confirm `Fgrid Floor 2` is selected
   before Recast and reaches the lift.
2. Repeat from floor 4 with a higher target floor using `Fgrid Floor 4`.
3. Force the canonical route, Recast, SharpNav, and legacy route to be unusable
   inside PF 4107. Confirm movement stops, PlayfieldGraph is not selected there,
   and fallback can resume only after a safe external exit.
4. Restart once with duplicate canonical entries and `Fgrid Floor 10a` in the
   user route file. Confirm the newest canonical entry remains, floor 10a is
   removed, and `Fgrid Floor 10` is retained.
