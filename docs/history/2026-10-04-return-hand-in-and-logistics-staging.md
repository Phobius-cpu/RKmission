# Return-item hand-in and post-mission staging (2026-10-04)

Base: `main` at `eb338fa54251cdc8137f5fde42f14e748d14db3f`.

## Reused source and ownership

- The archived `DungeonRunner` demonstrated `UseItemOnItemAction.Source` / `Destination` and `Item.UseOn`. RKMission uses that action only when its destination equals the *bound quest's* `Mission.Source`; it retains RKMission's stronger completion proof.
- The embedded Manager.Loot classifier and RKM bag-family matcher select newly acquired rejects. AOSharp's `Item.MoveToContainer` performs one move at a time into an existing `RKM Sell` bag. RKMission checks the item disappeared from main inventory and appeared in the selected bag before advancing.
- The opt-in `/rkm logistics probe start|stop` adapts the narrow bank, shop and container message observations from knows-helpers `N3Inspector`. It records protocol evidence; it does not send bank or vendor transactions.

Bank and shop terminals may be inside buildings or backyards on a different playfield from the roller. The roller's saved playfield and position are never treated as a bank/shop destination. Logistics requires a separate route with a verified entrance transition, interior playfield and terminal, and a verified exit back to the outdoor travel graph. Scotty/FGrid can only help reach the surrounding outdoor playfield; the interior leg must be observed separately.

## Behavior

Return-item dungeon clearance now enters `AwaitingHandIn`, not `CompletedAutomatically`. At automatic acceptance, RKMission saves the exact issuing mission-terminal identity, playfield, position, character and quest destination in its checkpoint. After the verified dungeon exit, it travels to that bound terminal and requires the quest's `Mission.Source` and return action destination to equal the saved identity. The exact terminal must appear at its recorded position before the carried item may be used. Item consumption and bound quest removal must then remain confirmed for two seconds. A missing or mismatched origin, ambiguous action, lost item, timeout, or failed use stops safely. Previously accepted missions without a recorded origin require manual hand-in.

After reward confirmation, only items newly acquired since dungeon entry and classified `Reject` are staged into existing `RKM Sell` bags. No bag is created and no item is sold, deleted or banked. If main inventory remains below the configured minimum, the automatic cycle waits before another mission. An unverified item move stops the cycle.

## Validation still requiring the AO client

1. Complete one return-item mission. Confirm `AwaitingHandIn` after pickup, exact dungeon exit, travel to the bound source, one item use, item disappearance, bound quest removal, and no new roll before those proofs.
2. Repeat with no visible source, a wrong/ambiguous source, delayed quest update, interrupted zoning, and a restart after verified exit. Confirm the bot waits or stops rather than declaring a reward.
3. Complete a mission with a new unlisted item and an existing `RKM Sell` bag. Confirm one transfer and that the item appears in the bag before chaining. Repeat with a full/missing bag and low free-slot count.
4. For each mission-pulling location, stand near its roller terminal and use `/rkm logistics probe start <site> bank` or `/rkm logistics probe start <site> shop`. Manually walk into the relevant building or backyard, perform one deposit or sale, walk back out, then stop the probe. Use separate traces for bank and shop when their routes differ. The `RKMissionData/logistics-probe-<site>-<purpose>-*.json` trace records the source mission terminal identity/position, walked path points in each playfield, transitions with departure/arrival positions, nearby terminal candidates, sent/received messages, inventory space and bank-open state. Confirm each entrance/exit identity and terminal against live behavior before adding it to a logistics route graph. Use the packet evidence to implement a separate verified transaction loop; the current SDK source has no proven vendor sale API.
5. Pull return-item missions from two different terminals. Confirm the bot returns each one only to its issuing terminal, survives a restart between acceptance and hand-in, and stops without using the item if the quest source names the other terminal or the origin binding is absent.
6. Validate the bundled OA (540), ICC (655) and Borealis (800) recorded approaches from their actual starting terminals. Confirm the recorder plays each path only when both endpoints match, and that a live Grid terminal is checked before use or public FGrid service. The ICC endpoint is only a positioning hint until the live terminal appears.

The first OA bank trace (`logistics-probe-unlabeled-unspecified-20261004-155444-994.json`, captured outside this repository) observed PF 540 -> 3135 -> 540, a bank terminal in PF 3135, a `ClientContainerAddItem` from inventory slot `0x43` to the bank, and a `ContainerAddItem` acknowledgment. Main free slots changed 7 -> 8. No withdrawal or deposited item name/classification appeared in that trace. The revised probe now records inventory update packets, bank/main item snapshots and ManagerLoot classifications so a new one-item deposit plus one-item withdrawal can establish the missing contract. It does not infer Keep/Reject from the old slot-only packet.

Local validation: `dotnet build RKmission/RKmission.csproj --no-restore -v:q -p:WarningLevel=0` succeeded. No in-game run was available for this change.
