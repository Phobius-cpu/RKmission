using System;
using System.Collections.Generic;
using System.Linq;
using AOSharp.Common.GameData;
using AOSharp.Core;
using AOSharp.Core.Inventory;

namespace RKmission
{
    internal sealed class UseItemRoles
    {
        public Identity InventoryItem;
        public Identity WorldTarget;
        public string Evidence;
    }

    // AO can encode UseItemOnItem in either direction. Resolve the semantic
    // roles once, from mission context plus observed inventory/world evidence,
    // and never let callers independently guess Source versus Destination.
    internal static class UseItemRoleResolver
    {
        public static bool TryResolve(AcceptedMission record, UseItemOnItemAction action,
            out UseItemRoles roles, out string reason)
        {
            roles = null;
            reason = null;
            if (record == null || action == null)
            { reason = "The mission or UseItemOnItem action is unavailable."; return false; }

            // Return-item hand-in has an intentionally strict binding: the
            // destination must be the verified issuing mission terminal.
            if (record.Kind == RkMissionKind.ReturnItem ||
                (record.Source != Identity.None && action.Destination == record.Source))
            {
                if (record.Source == Identity.None || action.Destination != record.Source)
                { reason = "Return-item action is not bound to the mission's issuing terminal."; return false; }
                roles = new UseItemRoles
                {
                    InventoryItem = action.Source,
                    WorldTarget = action.Destination,
                    Evidence = "return-item destination matches issuing terminal"
                };
                return true;
            }

            bool sourceInInventory = InMainInventory(action.Source);
            bool destinationInInventory = InMainInventory(action.Destination);
            bool sourceInWorld = DynelManager.GetDynel(action.Source) != null;
            bool destinationInWorld = DynelManager.GetDynel(action.Destination) != null;

            if (record.Kind != RkMissionKind.Repair && record.Kind != RkMissionKind.Unknown)
            {
                reason = Failure(record, action, sourceInInventory, sourceInWorld,
                    destinationInInventory, destinationInWorld,
                    $"mission kind {record.Kind} does not support repair semantics");
                return false;
            }

            if (sourceInWorld && destinationInInventory && !sourceInInventory)
                return Resolved(action.Destination, action.Source,
                    "repair source is a live world dynel and destination is carried", out roles);
            if (destinationInWorld && sourceInInventory && !destinationInInventory)
                return Resolved(action.Source, action.Destination,
                    "repair destination is a live world dynel and source is carried", out roles);

            bool sourceWorldType = IsWorldObject(action.Source);
            bool destinationWorldType = IsWorldObject(action.Destination);

            // Some repair quests expose two world identities here (not the
            // carried tool). Keep the fixture target, then resolve the tool
            // independently from accepted quest metadata and inventory.
            if (sourceWorldType && destinationWorldType)
            {
                if (!TryResolveWorldTarget(action, sourceInWorld, destinationInWorld,
                    out Identity worldTarget, out string worldEvidence))
                {
                    reason = Failure(record, action, sourceInInventory, sourceInWorld,
                        destinationInInventory, destinationInWorld,
                        "both action identities are world-like but the repair fixture is ambiguous");
                    return false;
                }
                if (!TryResolveRepairTool(record, out Item tool, out string toolEvidence, out string candidates))
                {
                    reason = Failure(record, action, sourceInInventory, sourceInWorld,
                        destinationInInventory, destinationInWorld,
                        $"repair tool was not uniquely resolved; {toolEvidence}; inventory candidates=[{candidates}]");
                    return false;
                }
                RememberTool(record, tool);
                return Resolved(tool.UniqueIdentity, worldTarget,
                    $"{worldEvidence}; {toolEvidence}", out roles);
            }

            if (sourceWorldType && destinationInInventory && !sourceInInventory)
                return Resolved(action.Destination, action.Source,
                    "repair source is world-like and destination is carried", out roles);
            if (destinationWorldType && sourceInInventory && !destinationInInventory)
                return Resolved(action.Source, action.Destination,
                    "repair destination is world-like and source is carried", out roles);

            if (destinationInInventory && !sourceInInventory && !destinationInWorld)
                return Resolved(action.Destination, action.Source,
                    "only repair destination is carried", out roles);
            if (sourceInInventory && !destinationInInventory && !sourceInWorld)
                return Resolved(action.Source, action.Destination,
                    "only repair source is carried", out roles);

            reason = Failure(record, action, sourceInInventory, sourceInWorld,
                destinationInInventory, destinationInWorld,
                "inventory/world and terminal evidence do not identify exactly one carried item and one world target");
            return false;
        }

        private static bool InMainInventory(Identity identity) => Inventory.Items.Any(item =>
            item.Slot.Type == IdentityType.Inventory && item.UniqueIdentity == identity);

        private static bool IsWorldObject(Identity identity) =>
            identity.Type == IdentityType.Terminal || identity.Type == IdentityType.MissionTerminal;

        private static bool TryResolveWorldTarget(UseItemOnItemAction action,
            bool sourceLive, bool destinationLive, out Identity target, out string evidence)
        {
            target = Identity.None;
            evidence = null;
            if (sourceLive != destinationLive)
            {
                target = sourceLive ? action.Source : action.Destination;
                evidence = sourceLive ? "repair source is the only live world dynel" :
                    "repair destination is the only live world dynel";
                return true;
            }
            bool sourceFixture = action.Source.Type == IdentityType.Terminal;
            bool destinationFixture = action.Destination.Type == IdentityType.Terminal;
            if (sourceFixture == destinationFixture) return false;
            target = sourceFixture ? action.Source : action.Destination;
            evidence = sourceFixture ? "repair source is the fixture Terminal" :
                "repair destination is the fixture Terminal";
            return true;
        }

        private static bool TryResolveRepairTool(AcceptedMission record, out Item tool,
            out string evidence, out string candidates)
        {
            tool = null;
            var carried = Inventory.Items.Where(x => x.Slot.Type == IdentityType.Inventory).ToList();
            candidates = string.Join(", ", carried.Select(DescribeItem));

            var persisted = carried.Where(x => x.UniqueIdentity == record.RepairToolIdentity ||
                (record.RepairToolLowId != 0 && x.LowId == record.RepairToolLowId) ||
                (record.RepairToolHighId != 0 && x.HighId == record.RepairToolHighId)).ToList();
            if (Unique(persisted, out tool))
            { evidence = "persisted repair-tool identity/template matches one carried item"; return true; }
            if (persisted.Count > 1)
            { evidence = "persisted repair-tool metadata matches multiple carried items"; return false; }

            var ids = new HashSet<int>((record.QuestActionIdentities ?? new List<Identity>())
                .Where(x => x != Identity.None && !IsWorldObject(x)).Select(x => x.Instance));
            var structural = carried.Where(x => ids.Contains(x.UniqueIdentity.Instance) ||
                ids.Contains(x.LowId) || ids.Contains(x.HighId)).ToList();
            if (Unique(structural, out tool))
            { evidence = "accepted QuestActions identify one carried item/template"; return true; }
            if (structural.Count > 1)
            { evidence = "accepted QuestActions match multiple carried items"; return false; }

            string text = (record.QuestShortInfo ?? "") + "\n" + (record.QuestLongInfo ?? "");
            var textMatches = carried.Where(x => !string.IsNullOrWhiteSpace(x.Name) &&
                text.IndexOf(x.Name, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            if (Unique(textMatches, out tool))
            { evidence = $"accepted quest text uniquely names carried item '{tool.Name}'"; return true; }
            evidence = textMatches.Count == 0 ? "accepted quest text names no carried item" :
                "accepted quest text names multiple carried items";
            return false;
        }

        private static bool Unique(List<Item> items, out Item item)
        {
            item = items.Count == 1 ? items[0] : null;
            return item != null;
        }

        private static void RememberTool(AcceptedMission record, Item item)
        {
            record.RepairToolIdentity = item.UniqueIdentity;
            record.RepairToolLowId = item.LowId;
            record.RepairToolHighId = item.HighId;
        }

        private static string DescribeItem(Item item) =>
            $"{item.UniqueIdentity}/'{item.Name}'/low={item.LowId}/high={item.HighId}";

        private static bool Resolved(Identity inventoryItem, Identity worldTarget,
            string evidence, out UseItemRoles roles)
        {
            roles = new UseItemRoles
            {
                InventoryItem = inventoryItem,
                WorldTarget = worldTarget,
                Evidence = evidence
            };
            return true;
        }

        private static string Failure(AcceptedMission record, UseItemOnItemAction action,
            bool sourceInInventory, bool sourceInWorld,
            bool destinationInInventory, bool destinationInWorld, string detail) =>
            $"UseItemOnItem role resolution failed: kind={record.Kind}, source={action.Source} " +
            $"(worldType={IsWorldObject(action.Source)}, inventory={sourceInInventory}, world={sourceInWorld}), " +
            $"destination={action.Destination} (worldType={IsWorldObject(action.Destination)}, " +
            $"inventory={destinationInInventory}, world={destinationInWorld}); reason={detail}.";
    }
}
