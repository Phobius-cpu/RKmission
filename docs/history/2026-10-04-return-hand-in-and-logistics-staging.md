# Return-item hand-in and post-mission staging (2026-10-04)

Base: `main` at `eb338fa54251cdc8137f5fde42f14e748d14db3f`.

## Reused source and ownership

- The archived `DungeonRunner` demonstrated `UseItemOnItemAction.Source` / `Destination` and `Item.UseOn`. RKMission uses that action only when its destination equals the *bound quest's* `Mission.Source`; it retains RKMission's stronger completion proof.
- The embedded Manager.Loot classifier and RKM bag-family matcher select newly acquired rejects. AOSharp's `Item.MoveToContainer` performs one move at a time into an existing `RKM Sell` bag. RKMission checks the item disappeared from main inventory and appeared in the selected bag before advancing.
- The opt-in `/rkm logistics probe start|stop` adapts the narrow bank, shop and container message observations from knows-helpers `N3Inspector`. It records protocol evidence; it does not send bank or vendor transactions.

Bank and shop terminals may be inside buildings or backyards on a different playfield from the roller. The roller's saved playfield and position are never treated as a bank/shop destination. Logistics requires a separate route with a verified entrance transition, interior playfield and terminal, and a verified exit back to the outdoor travel graph. Scotty/FGrid can only help reach the surrounding outdoor playfield; the interior leg must be observed separately.

## Behavior

Return-item dungeon clearance now enters `AwaitingHandIn`, not `CompletedAutomatically`. After the exact dungeon exit is verified, RKMission locates the action's destination, uses the exact carried source item on it, and waits for the item to be consumed and the bound quest to remain absent for two seconds. The saved roller location can bring the character near the source only when `Mission.Source` is a mission-terminal identity; the exact destination must still become visible. An ambiguous or missing action, lost item, timeout, or failed use stops safely. The verified-exit hand-in intent is recorded in the checkpoint for restart reconciliation.

After reward confirmation, only items newly acquired since dungeon entry and classified `Reject` are staged into existing `RKM Sell` bags. No bag is created and no item is sold, deleted or banked. If main inventory remains below the configured minimum, the automatic cycle waits before another mission. An unverified item move stops the cycle.

## Validation still requiring the AO client

1. Complete one return-item mission. Confirm `AwaitingHandIn` after pickup, exact dungeon exit, travel to the bound source, one item use, item disappearance, bound quest removal, and no new roll before those proofs.
2. Repeat with no visible source, a wrong/ambiguous source, delayed quest update, interrupted zoning, and a restart after verified exit. Confirm the bot waits or stops rather than declaring a reward.
3. Complete a mission with a new unlisted item and an existing `RKM Sell` bag. Confirm one transfer and that the item appears in the bag before chaining. Repeat with a full/missing bag and low free-slot count.
4. For each mission-pulling location, stand near its roller terminal and use `/rkm logistics probe start <site> bank` or `/rkm logistics probe start <site> shop`. Manually walk into the relevant building or backyard, perform one deposit or sale, walk back out, then stop the probe. Use separate traces for bank and shop when their routes differ. The `RKMissionData/logistics-probe-<site>-<purpose>-*.json` trace records the source mission terminal identity/position, walked path points in each playfield, transitions with departure/arrival positions, nearby terminal candidates, sent/received messages, inventory space and bank-open state. Confirm each entrance/exit identity and terminal against live behavior before adding it to a logistics route graph. Use the packet evidence to implement a separate verified transaction loop; the current SDK source has no proven vendor sale API.

Local validation: `dotnet build RKmission/RKmission.csproj --no-restore -v:q -p:WarningLevel=0` succeeded. No in-game run was available for this change.
