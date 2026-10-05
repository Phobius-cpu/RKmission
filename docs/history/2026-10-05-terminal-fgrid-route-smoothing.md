# Terminal-to-FGrid route smoothing — 2026-10-05

The OA, ICC, and Borealis terminal-to-FGrid recordings contained raw samples roughly every 0.75 to 1.7 metres. Navigation replay submitted those samples one at a time, producing visibly short movement legs even along straight sections.

`NavigationRouteRecorder` now recognizes terminal-to-Grid route names and computes a minimum-leg path through their recorded samples before playback. A leg is eligible only when every omitted point stays within 1 metre horizontally and 1.25 metres vertically of the proposed segment. Dynamic programming chooses the smallest eligible set rather than taking a fixed sampling interval. Both recorded endpoints always remain, as do corners or height transitions that exceed the corridor.

The bundled routes now replay as:

- OA: 100 samples to 2 legs.
- ICC: 59 samples to 3 legs, retaining the ramp and direction changes.
- Borealis: 10 samples to 1 leg.

Reverse playback produces the same leg counts. The transformation occurs after route selection, so a user copy in `RKMissionData/navigation-routes.json` that overrides the bundled route receives the same behavior without rerecording. Other recorded navigation paths retain their prior sample-by-sample behavior, and FGrid interior walkway validation is unchanged.
