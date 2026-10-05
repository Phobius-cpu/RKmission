# Strict operational inventory classification — 2026-10-05

Started from GitHub `main` `a1306f6999a3ceda2b37c3625469cb3a7a76a6ec`.
The previous `InventoryPolicy` protected broad item-name fragments, including
vehicle, ammo, and recharge words. A Vehicle Air implant and Nano Formula
Recompiler could therefore be classified as operational despite no RKMission
consumer for those items.

Built-in protection now follows concrete consumers: the exact `Lock Pick`
name used by the RKMission door and ManagerLoot chest paths, container item
type for bags, and positive HP/nano use effects accepted by the same filter
that `MissionReadiness` uses when choosing a recovery item. A handler can
register a verified item ID through `RegisterOperationalItemId`; no IDs were
invented. `ProtectedItemIds` and `ProtectedNameFragments` remain explicit user
overrides. Names alone no longer protect vehicles, ammo, clusters, Free
Movement, taunt tools, grenades, or other useful equipment when no concrete
consumer/identity evidence exists; ManagerLoot decides their value. The live
`IsVehicle` item stat remains excluded because it previously marked unrelated
equipment as vehicles.

ManagerLoot's existing precedence remains: mission keys/reserved objectives,
operational items, and contents of `RKM Keep*` or `RKM Mission` are Protected
before selected items and ManagerLoot rules. A rule removed after placing an
item in `RKM Keep 01` cannot make it a sale candidate. The one-item shop
diagnostic still selects only verified Rejects observed in `RKM Sell*` and
rechecks the classification before submission. No mission travel, loot
movement, or trade code was changed.

Release build: zero errors. `tests/InventoryClassificationRegression.ps1`
checks exact Lock Pick, Vehicle Air implant, recompiler, bag-family matching,
and the protected-bag-before-rule source path against the built assembly.
The AO client is needed to validate real item use effects and an actual
inventory/shop preview. Check that the named implant and recompiler follow
ManagerLoot's rule, while a Lock Pick and existing Keep-bag contents report
Protected. Do not infer sale eligibility from a main-inventory Reject label.
