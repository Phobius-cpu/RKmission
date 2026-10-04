# Verified bank storage and one-item shop sale diagnostic — 2026-10-04

At 21:35 the user confirmed the OA bank storage command opened terminal
`(Terminal:C0080C3F)`, deposited a QL 200 Cold Stone classified `Keep`, and
verified main/bank counts 1/0 -> 0/1. One item remains in bank. This validates
the first persistent deposit at the OA surveyed site; Borealis and ICC bank
storage still need live tests.

The next explicit diagnostic is `/rkm logistics shop test`. It matches one
visible shop actor to a bundled survey's exact identity, playfield and
position. A running route test must first reach its own shop target. Using the
actor must produce its trade-open packet and a shop inventory update. The
client trade target must remain that actor before an item is submitted or
accepted.

Only a ManagerLoot `Reject` item with a unique id/quality/name fingerprint in
main inventory is selected. If none is in main inventory, one Reject from an
existing RKM Sell bag may be moved to main and verified first. The test sends
`Trade.AddItem` once and waits for the matching server echo and the item
leaving main inventory. It then sends the zero-parameter NPC-shop Accept
observed in the OA, Borealis and ICC manual probes via
`Trade.Accept(Identity.None)`. Success requires a Complete message from the
same surveyed actor, the item still absent from main inventory and cash not
falling below the pre-sale value. Any uncertainty stops without retrying a
trade action and tells the operator to inspect the shop window and item.

This test sells exactly one item and is not connected to `/rkm auto`.
Automatic shop recycling and full bank/shop travel scheduling remain open.
The pinned SDK build passed; live sale behavior remains unverified.
