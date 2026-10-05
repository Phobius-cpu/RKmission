# Verified shop batch follow-up — 2026-10-05

The user confirmed that classification looked correct after removing the
unreliable vehicle stat and that the one-item shop test worked. The next
explicit command is `/rkm logistics shop sell [1-20]`, defaulting to one item.
It uses the same exact surveyed actor and RKM Sell-only candidate selection
as the one-item test. Each item is moved to main inventory with both views
checked, submitted once, acknowledged by the server, accepted once, and
counted only after actor-bound trade completion, item removal, and a
nondecreasing cash snapshot. A one-second settlement period follows each
verified sale. If the shop remains open, the next item is selected there;
otherwise the exact actor is used again and must produce a fresh shop update
and trade-open packet. No uncertain submission or acceptance is retried.

When no further unprotected, unique Reject is observed in RKM Sell, the
command stops early and reports the verified count. On failure or manual
stop it reports the verified count and tells the operator where to inspect
any possibly pending item. Main-inventory Rejects, protected items, and
ambiguous duplicate fingerprints remain excluded. The command is disarmed
from `/rkm auto` and holds return travel while active.

Live checkpoint: at a surveyed OA shop, preview two distinct expendable
Rejects in RKM Sell, then run `shop sell 2`. Confirm two individual sale
verifications and a final count of 2. If the second trade does not reopen or
acknowledge an item, inspect the shop and inventory; the controller must stop
without submitting or accepting that item twice. Borealis and ICC routes and
transactions still need separate live checks before automatic mission-cycle
bank/shop scheduling.
