# Automatic logistics cycle validation — 2026-10-05

OA, Borealis, and ICC routes, exact destination actors, bank transfers, and one-item sales were individually validated. The next checkpoint composes those pieces without yet allowing the mission cycle to invoke them.

`/rkm logistics cycle [1-20]` must start on foot beside a unique surveyed mission terminal while mission automation is disarmed. It derives the site from the live origin terminal identity, playfield, and surveyed position. Before counting, it refreshes RKM Sell bag snapshots with the same open/close action already used by ManagerLoot for managed bags. It then counts main-inventory ManagerLoot Keep items and unique ManagerLoot Reject items already in RKM Sell. Bank work runs first, followed by shop work. Empty phases are skipped.

The route navigator, bank transaction, and shop transaction now expose `Running`, `Succeeded`, and `Failed` outcomes. The cycle starts its next phase only after verified success. It never retries a failed route, deposit, trade submission, or acceptance. It stops with the underlying failure and the completed bank/sale counts. Manual commands retain their existing behavior.

The ICC bank trace ended with a 1.7-metre backward sample after reaching `(3210.745, 35.655, 949.4249)`. The independently recorded ICC shop trace reaches that same doorway coordinate and the user verified its automatic PF 655 to PF 1186 crossing. The backward bank sample was removed, making the live-verified shared threshold the final bank approach point. Expected playfield arrival remains mandatory.

Live validation should place at least one expendable Keep item in main inventory and one expendable Reject in RKM Sell, then run the cycle from each site's roller terminal. Confirm both transactions, both returns, the final summary, and fully automatic ICC bank entry. Only after these end-to-end results should the controller be scheduled from `/rkm auto` when capacity or Sell-bag pressure requires recycling.
