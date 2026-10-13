using System;
using AOSharp.Common.GameData;

namespace RKmission
{
    internal sealed class CycleReadinessSnapshot
    {
        public bool Outdoors { get; set; }
        public bool Alive { get; set; }
        public bool NotZoning { get; set; }
        public bool MissionListKnown { get; set; }
        public bool NoAcceptedWork { get; set; }
        public bool NoHandInPending { get; set; }
        public bool NoRewardPending { get; set; }
        public bool NoInventorySettlementPending { get; set; }
        public bool NoLogisticsPending { get; set; }
        public bool InventoryReady { get; set; }
        public bool NoPendingItemAction { get; set; }
        public bool NoOwnedMovement { get; set; }
        public bool ExactRollTerminalVerified { get; set; }
        public bool PriorAcceptanceSettled { get; set; }
        public bool QuestDetailsSettled { get; set; }
        public bool HasBatchCapacity { get; set; }
        public bool UnderMissionLimit { get; set; }
    }

    internal static class CycleReadiness
    {
        public static bool IsReadyToRoll(CycleReadinessSnapshot state, out string unresolved)
        {
            if (!state.Outdoors) return Fail("player is not outdoors", out unresolved);
            if (!state.Alive) return Fail("player is not alive", out unresolved);
            if (!state.NotZoning) return Fail("zoning is active", out unresolved);
            if (!state.MissionListKnown) return Fail("accepted mission list is unavailable", out unresolved);
            if (!state.NoAcceptedWork) return Fail("accepted mission work remains", out unresolved);
            if (!state.NoHandInPending) return Fail("mission hand-in remains pending", out unresolved);
            if (!state.NoRewardPending) return Fail("mission reward settlement remains pending", out unresolved);
            if (!state.NoInventorySettlementPending) return Fail("post-mission inventory settlement remains pending", out unresolved);
            if (!state.NoLogisticsPending) return Fail("automatic logistics remains pending or active", out unresolved);
            if (!state.InventoryReady) return Fail("managed inventory snapshots are not ready", out unresolved);
            if (!state.NoPendingItemAction) return Fail("an item use or spell cast remains pending", out unresolved);
            if (!state.NoOwnedMovement) return Fail("RKMission still owns movement", out unresolved);
            if (!state.ExactRollTerminalVerified) return Fail("saved roller terminal identity/position is not live-verified", out unresolved);
            unresolved = null;
            return true;
        }

        // Batch refill deliberately permits accepted mission work. Everything
        // else must be settled before another offer can be accepted.
        public static bool IsReadyToRefill(CycleReadinessSnapshot state, out string unresolved)
        {
            if (!state.Outdoors) return Fail("player is not outdoors", out unresolved);
            if (!state.Alive) return Fail("player is not alive", out unresolved);
            if (!state.NotZoning) return Fail("zoning is active", out unresolved);
            if (!state.MissionListKnown) return Fail("accepted mission list is unavailable", out unresolved);
            if (!state.PriorAcceptanceSettled) return Fail("the prior mission acceptance is not settled", out unresolved);
            if (!state.QuestDetailsSettled) return Fail("accepted mission quest details are unresolved", out unresolved);
            if (!state.NoHandInPending) return Fail("mission hand-in remains pending", out unresolved);
            if (!state.NoRewardPending) return Fail("mission reward settlement remains pending", out unresolved);
            if (!state.NoInventorySettlementPending) return Fail("post-mission inventory settlement remains pending", out unresolved);
            if (!state.NoLogisticsPending) return Fail("automatic logistics remains pending or active", out unresolved);
            if (!state.InventoryReady) return Fail("managed inventory snapshots are not ready", out unresolved);
            if (!state.NoPendingItemAction) return Fail("an item use or spell cast remains pending", out unresolved);
            if (!state.NoOwnedMovement) return Fail("RKMission still owns movement", out unresolved);
            if (!state.ExactRollTerminalVerified) return Fail("saved roller terminal identity/position is not live-verified", out unresolved);
            if (!state.HasBatchCapacity) return Fail("two free main-inventory slots are required", out unresolved);
            if (!state.UnderMissionLimit) return Fail("the configured mission limit is already reached", out unresolved);
            unresolved = null;
            return true;
        }

        private static bool Fail(string reason, out string unresolved)
        {
            unresolved = reason;
            return false;
        }
    }
}
