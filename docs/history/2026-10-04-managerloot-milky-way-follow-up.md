# ManagerLoot inventory policy and Milky Way route exclusion (2026-10-04)

Base: `main` at `2ff3e6c3c5a74083940d1be91a9511dea2f5dd30`.

## Combat review

`1c48f81` already changed the combat contract to `CombatTickResult.ApproachUnavailable` and added bounded room recovery/deferral. `2ff3e6c` later extended the safe layered-room scan. This checkpoint retained those changes. A three-stall movement attempt does not call `MissionDungeon.Stop()` through `FightInRoom`.

## Item classification and storage

- ManagerLoot now exposes `Classify(Item, newlyAcquiredDuringMission)` with `Protected`, `Keep`, `Reject`, and `Unknown`. Name/ID, Exact, QL, Quantity, OneEach, ordered-rule precedence, BagName and Reverse selection share its rule matcher. A newly acquired item is kept if a rule or ManagerLoot's observed collection selected it; an unlisted reward is a future disposal candidate, never automatically sold or deleted here.
- Exact mission action items, MissionKey identities, pending return hand-in items and items already in `RKM Keep` or `RKM Mission` bags are protected. Mission-critical inventory identities are remembered when dungeon ownership ends, including a pending return hand-in. The old blanket protection for everything collected from an objective container was removed: objective container origin alone does not prove permanent value.
- Items selected by ManagerLoot remain marked as keep candidates for this session if a quantity rule is consumed after collection. A keep-bag item remains protected independently of later rule edits.
- `RKM Keep` rules use the base bag, then numbered `RKM Keep 01`, `02`, etc. in ascending order, choosing the first non-full bag. The existing rule editor offers `RKM Keep` as a family choice even when only numbered bags exist. `RKM Sell` and `RKM Archive` are recognized as strict families for future logistics. A numbered BagName and ordinary non-RKM BagName continue to match exactly. The same matcher is used when opening bags, moving selected items, checking OneEach duplicates and protecting stored items. No bag creation API is assumed.
- `RKM Mission` is reserved for mission-critical items. Active objective items remain in main inventory because current objective interaction code reads `Inventory.Items`; moving them mid-mission would risk breaking use/hand-in. No automatic movement to `RKM Sell` or `RKM Archive` occurs.
- ManagerLoot still decides which ordinary corpse/chest items to collect. Reverse mode still selects the inverse of rule matches. Optional loot is not deleted during a mission even if ManagerLoot's existing Delete setting is enabled. If an optional open container cannot be safely looted for lack of a main-inventory slot, its source is marked skipped so sorting cannot hold mission completion.
- At dungeon entry, RKMission snapshots main-inventory identities. After verified exit it calls ManagerLoot's classifier for the current inventory, flagging newly acquired identities, and reports counts and capacity. `LogisticsRequired` is recorded when free slots fall below the inventory policy threshold. This does not delay confirmed mission completion. Exact reward-item origin is not exposed by the current completion proof, so post-exit counts do not assert that a particular inventory item was a reward. A new unselected item is only a candidate for future review, never an automatic disposal instruction.

## Scottyboi route pair

The exclusion is `MilkyWay + Warpdude31` only. Evidence: live mission `1442967240`, `FlyAvoidObstacle`, no net horizontal improvement for 90 seconds, 1492.03 m remaining, at 22:27. The reported log has no arrival coordinates.

A verified queue reply assigning this pair is rejected immediately. The provider retries the same verified destination command for an alternate assignment, up to three additional requests within the existing 180-second window. It never accepts an unverified Milky Way invite or an invite while this assignment is rejected. A different verified warper follows the existing lookup, offline queue and invite path. Exhaustion returns `Failed`, allowing the existing FGrid/Grid/normal-travel planner to take over. Paged menu parsing and aliases are unchanged.

## Verification and deferred work

The pinned SDK project builds with zero errors. In-game checks remain necessary for alternate Scotty assignment behavior, backpack availability and the combat recovery route. No verified AOSharp vendor or bank transaction interface is present in this repository; vendor disposal, bank archive, bag creation and movement of active mission items to `RKM Mission` await live interface evidence. No inventory item is sold or banked by this checkpoint.
