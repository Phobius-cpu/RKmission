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

            if (record.Kind != RkMissionKind.Repair)
            { reason = $"UseItemOnItem roles are unsupported for mission kind {record.Kind}."; return false; }

            bool sourceInInventory = InMainInventory(action.Source);
            bool destinationInInventory = InMainInventory(action.Destination);
            bool sourceInWorld = DynelManager.GetDynel(action.Source) != null;
            bool destinationInWorld = DynelManager.GetDynel(action.Destination) != null;

            if (sourceInWorld && destinationInInventory && !sourceInInventory)
                return Resolved(action.Destination, action.Source,
                    "repair source is a live world dynel and destination is carried", out roles);
            if (destinationInWorld && sourceInInventory && !destinationInInventory)
                return Resolved(action.Source, action.Destination,
                    "repair destination is a live world dynel and source is carried", out roles);

            bool sourceTerminal = action.Source.Type == IdentityType.Terminal;
            bool destinationTerminal = action.Destination.Type == IdentityType.Terminal;
            if (sourceTerminal && !destinationTerminal)
                return Resolved(action.Destination, action.Source,
                    "repair source has terminal identity", out roles);
            if (destinationTerminal && !sourceTerminal)
                return Resolved(action.Source, action.Destination,
                    "repair destination has terminal identity", out roles);

            if (destinationInInventory && !sourceInInventory && !destinationInWorld)
                return Resolved(action.Destination, action.Source,
                    "only repair destination is carried", out roles);
            if (sourceInInventory && !destinationInInventory && !sourceInWorld)
                return Resolved(action.Source, action.Destination,
                    "only repair source is carried", out roles);

            reason = $"Ambiguous repair UseItemOnItem roles: source={action.Source} " +
                $"(inventory={sourceInInventory}, world={sourceInWorld}), destination={action.Destination} " +
                $"(inventory={destinationInInventory}, world={destinationInWorld}).";
            return false;
        }

        private static bool InMainInventory(Identity identity) => Inventory.Items.Any(item =>
            item.Slot.Type == IdentityType.Inventory && item.UniqueIdentity == identity);

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
    }
}
