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
an existing RKM Sell bag is selected. It is moved to main and verified first. The test sends
`Trade.AddItem` once and waits for the matching server echo and the item
leaving main inventory. It then sends the zero-parameter NPC-shop Accept
observed in the OA, Borealis and ICC manual probes via
`Trade.Accept(Identity.None)`. Success requires a Complete message from the
same surveyed actor, the item still absent from main inventory and cash not
falling below the pre-sale value. Any uncertainty stops without retrying a
trade action and tells the operator to inspect the shop window and item.

This test sells exactly one item and is not connected to `/rkm auto`.
Automatic shop recycling and full bank/shop travel scheduling remain open.
At 21:50 the OA shop test sold one ammo box and verified trade completion and
cash increase. This exposed an unsafe selection rule: ammo in main inventory
could be selected when ManagerLoot classified it Reject. The follow-up limits
selection to RKM Sell bags and protects ammo, lockpicks, health/nano recharge
supplies, backpacks, and vehicles in ManagerLoot's shared classification.
Additional item ids and name fragments can be protected through
`RKMissionData/inventory-policy.json`. `/rkm logistics shop preview` lists
eligible and protected items without moving anything. Existing RKM Sell bag
contents are reclassified at selection and immediately before AddItem.

At 22:09 the user ran the new preview: zero unique Reject candidates were
available in RKM Sell, 15 items were protected across main/RKM Sell, and no
item moved. That output did not reveal whether RKM Sell was absent, empty, or
contained excluded items. Preview now reports the bag count and observed
contents by classification, the number of main Rejects that cannot be sold,
duplicate exclusions, and the reason for each displayed protection. The
generic `vehicle` name fragment was narrowed because an implant named
`Eye Implant: Vehicle Air, Shiny` could match it; actual vehicles remain
protected by the item stat and specific vehicle names. A Release build and
focused name checks passed. The next live preview will establish why no
candidate was available.
