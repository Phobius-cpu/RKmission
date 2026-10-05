# FGrid interior route smoothing — 2026-10-05

A live floor 5 run rejected Recast leg 2/4 because its edge floor-support sample missed. The log also reported that `NavMeshes/4107.nav` was absent, so the separate SharpNav fallback was unavailable. The user observed that successful interior FGrid movement used short legs.

The short movement came from `FGridRecastPlanner`: after validating the complete Recast funnel path, it divided every segment into fixed four-metre pieces. That subdivision has been removed. The planner now finds the minimum retained Recast corners whose complete connecting legs pass the existing FGrid safety predicate. It revalidates the reduced result before returning it to movement.

The safety predicate is unchanged: it samples the centre and both 0.3 m side tracks every 0.25 m, requires a walkable floor normal within 1.25 m of the planned height, rejects floor changes, and rejects a wall ray hit. This means a longer leg is used only along a fully supported walkway. A void, narrow edge, wall, or different floor keeps the necessary corner or rejects the path.

Recorded `fgrid-floor-*` fallback routes receive similar playback smoothing. The selected chord must remain within the original recording corridor and pass the live centre/edge floor and wall checks. Endpoints remain exact. Other recorded paths keep their existing rules.

The missing `4107.nav` message describes the unavailable SharpNav fallback; Recast still bakes its own in-memory mesh. This change does not treat a missing mesh or failed floor ray as safe. If the floor 5 Recast route still loses support, RKMission will continue to stop and request a recorded `fgrid-floor-5-lift` walkway. Floor-support coordinates now use invariant labelled `X`, `Z`, and `height(Y)` output so comma-decimal locales cannot make the diagnostic ambiguous.
