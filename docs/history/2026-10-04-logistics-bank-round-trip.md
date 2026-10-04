# OA doorway follow-up and bank round-trip diagnostic — 2026-10-04

The user's 21:05 OA bank route log shows the newly bounded forward crossing
target on both sides of the backyard door, then the expected PF 540 -> 3135
and PF 3135 -> 540 changes and surveyed endpoint arrivals. The prior test
required the user to walk forward through the return door. The new log does
not show a manual-crossing timeout. Other site routes still need live checks.

The next logistics checkpoint is an explicit `/rkm logistics bank test` at a
surveyed bank target. It verifies the recorded terminal's identity, location,
name, and playfield, opens it if needed, and waits for the bank view to settle.
It chooses one main-inventory ManagerLoot `Keep` item with a unique
id/quality/name fingerprint absent from the bank. It sends `MoveToBank` once,
waits until the same fingerprint has left main inventory and appeared in the
bank, then sends `MoveToInventory` once and verifies the original counts.
Timeouts and lost route context stop the test without retrying a transfer.
The log names the item so an operator can inspect its actual location if the
round trip cannot be confirmed. Return travel is held while the test runs.

The test performs no sale, deletion, or persistent deposit. It does not yet
couple bank transfers to the automatic mission cycle. This checkpoint needs
live AO validation before enabling those actions. The pinned SDK build passed
with zero errors and warnings.
