# Explicit shop staging — 2026-10-05

The 07:07 live preview found one RKM Sell bag with no observed contents and
six Reject items in main inventory. The two-item shop command stopped before
submitting anything. Automatic post-mission staging only handles newly
acquired items after verified mission completion/exit; it does not sweep
pre-existing main-inventory Rejects into a sale bag.

Preview now lists up to eight main Rejects with names, IDs, QLs, and the
explicit staging command. `/rkm logistics shop stage <item id>` selects one
main-inventory item by ID. Exactly one item must match, ManagerLoot must
classify it Reject, and its id/QL/name must not already appear in RKM Sell.
The destination is an existing RKM Sell family bag with space. One move is
sent and both inventory counts must verify within eight seconds. Zoning,
missing/renamed bags, or uncertain results stop without retrying. This is a
reversible inventory move and never requests a sale or opens a shop.

The command requires the mission cycle to be disarmed and no active bank or
shop operation. To resume the live batch checkpoint, preview, choose two
different expendable main Rejects, stage each by its printed ID, wait for
each verification, preview again, then run `shop sell 2` at the surveyed
shop. Alternatively move the items into RKM Sell manually and preview.

Release build and existing inventory classification checks passed. Actual
staging and the two-item sale sequence still require AO validation.
