using System;
using System.Linq;
using AOSharp.Common.GameData;
using AOSharp.Core;
using AOSharp.Core.Inventory;

namespace RKmission
{
    internal enum VehicleTransitionResult { Ready, Waiting, Unavailable, Blocked }

    // Owns only transitions RKMission requested. Live MovementState is always
    // authoritative; an item use or LeaveFly command is never treated as proof.
    internal sealed class VehicleLifecycle
    {
        private const int YalmahaXl29478ItemId = 117322;
        private readonly MovementArbiter _movement;
        private readonly Action<string> _say;
        private bool _observedOriginal, _originalFlying, _activationRequested, _ownsFlight;
        private DateTime _requestedAt;
        private bool _terminalLandingActive, _terminalDismountRequested;
        private DateTime _terminalLandingStarted, _terminalGroundObservedAt;
        private Vector3 _terminalLandingStaging;
        private bool _batchRemountActive, _batchRemountRequested;
        private DateTime _batchRemountStarted;
        public string Status { get; private set; } = "unobserved";

        public VehicleLifecycle(MovementArbiter movement, Action<string> say)
        { _movement = movement; _say = say; }

        public void ObserveOriginal()
        {
            if (_observedOriginal || DynelManager.LocalPlayer == null) return;
            _observedOriginal = true;
            _originalFlying = DynelManager.LocalPlayer.MovementState == MovementState.Fly;
            Status = _originalFlying ? "original-flight-preserved" : "original-ground";
            _say($"Vehicle lifecycle: observed original mode={(_originalFlying ? "Fly" : "Foot")}; RKMission will restore only transitions it owns.");
        }

        public VehicleTransitionResult RequireFlight(bool routeBenefits)
        {
            ObserveOriginal();
            SimpleChar player = DynelManager.LocalPlayer;
            if (player == null) return VehicleTransitionResult.Waiting;
            if (player.MovementState == MovementState.Fly)
            {
                if (_activationRequested) { _ownsFlight = !_originalFlying; _activationRequested = false; }
                Status = _ownsFlight ? "rkm-flight-active" : "existing-flight-preserved";
                return VehicleTransitionResult.Ready;
            }
            if (!routeBenefits) { Status = "foot-selected-route-cost"; return VehicleTransitionResult.Unavailable; }
            if (!SafeToTransition(player)) { Status = "transition-gated"; return VehicleTransitionResult.Blocked; }
            if (_activationRequested)
            {
                if (DateTime.UtcNow - _requestedAt < TimeSpan.FromSeconds(8)) return VehicleTransitionResult.Waiting;
                _activationRequested = false; Status = "activation-unverified";
                _say("Vehicle lifecycle: activation was not verified from live Fly state; continuing on foot.");
                return VehicleTransitionResult.Unavailable;
            }
            Item vehicle = Inventory.Items.FirstOrDefault(x => x.Id == YalmahaXl29478ItemId);
            if (vehicle == null || Item.HasPendingUse || Spell.HasPendingCast)
            { Status = "vehicle-unavailable"; return VehicleTransitionResult.Unavailable; }
            vehicle.Use(); _activationRequested = true; _requestedAt = DateTime.UtcNow;
            Status = "activation-requested";
            _say("Vehicle lifecycle: route cost benefits from flight; requested the known Yalmaha and waiting for live Fly verification.");
            return VehicleTransitionResult.Waiting;
        }

        public bool RestoreOriginal()
        {
            SimpleChar player = DynelManager.LocalPlayer;
            if (!_ownsFlight || player == null || player.MovementState != MovementState.Fly) return true;
            if (!SafeToTransition(player)) { Status = "restore-gated"; return false; }
            _movement.SetMovement(MovementOwner.OutdoorTravel, MovementAction.LeaveFly);
            Status = "restore-requested";
            return false;
        }

        public VehicleTransitionResult RequireFlightAfterMissionBatch()
        {
            ObserveOriginal();
            SimpleChar player = DynelManager.LocalPlayer;
            if (player == null || Game.IsZoning) return VehicleTransitionResult.Waiting;
            if (!_batchRemountActive)
            {
                _batchRemountActive = true;
                _batchRemountRequested = false;
                _batchRemountStarted = DateTime.UtcNow;
                Status = "batch-remount-started";
            }
            bool attached = TerminalVehicleAttached(player);
            if (attached && player.MovementState == MovementState.Fly &&
                !Item.HasPendingUse && !Spell.HasPendingCast)
            {
                _activationRequested = false;
                Status = "batch-remount-live-fly-verified";
                return VehicleTransitionResult.Ready;
            }
            if (DateTime.UtcNow - _batchRemountStarted > TimeSpan.FromSeconds(15))
            {
                Status = $"batch-remount-timeout; state={player.MovementState}, attached={attached}, " +
                    $"pendingUse={Item.HasPendingUse}, pendingCast={Spell.HasPendingCast}";
                return VehicleTransitionResult.Unavailable;
            }
            if (!SafeToTransition(player))
            { Status = "batch-remount-transition-gated"; return VehicleTransitionResult.Blocked; }
            if (Item.HasPendingUse || Spell.HasPendingCast)
            { Status = "batch-remount-pending-action"; return VehicleTransitionResult.Waiting; }
            if (!_batchRemountRequested)
            {
                Item vehicle = Inventory.Items.FirstOrDefault(x =>
                    x.Id == YalmahaXl29478ItemId && !x.IsEquipped);
                if (vehicle == null)
                { Status = "batch-remount-vehicle-unavailable"; return VehicleTransitionResult.Unavailable; }
                vehicle.Use();
                _batchRemountRequested = true;
                _batchRemountStarted = DateTime.UtcNow;
                Status = "batch-remount-requested-once";
                _say("Mission batch complete: requested the known Yalmaha once; mission selection and travel remain held until equipped attachment and live Fly are verified.");
            }
            else Status = attached ? "batch-remount-awaiting-live-fly" :
                "batch-remount-awaiting-attachment";
            return VehicleTransitionResult.Waiting;
        }

        public void CompleteMissionBatchRemount()
        {
            _batchRemountActive = _batchRemountRequested = false;
            _batchRemountStarted = DateTime.MinValue;
        }

        // Terminal transactions are deliberately stricter than ordinary travel:
        // they require verified foot state even when flight predates RKMission.
        // This does not change ownership/restoration policy anywhere else.
        public VehicleTransitionResult RequireFootForTerminal(Vector3 landingStaging)
        {
            SimpleChar player = DynelManager.LocalPlayer;
            if (player == null || Game.IsZoning) return VehicleTransitionResult.Waiting;
            if (!_terminalLandingActive || Vector3.Distance(_terminalLandingStaging, landingStaging) > 0.5f)
            {
                _terminalLandingActive = true;
                _terminalDismountRequested = false;
                _terminalGroundObservedAt = DateTime.MinValue;
                _terminalLandingStarted = DateTime.UtcNow;
                _terminalLandingStaging = landingStaging;
                Status = "terminal-landing-staging";
                _say($"Terminal foot requirement: verified grounded staging target " +
                    $"({_terminalLandingStaging.X:0.0},{_terminalLandingStaging.Y:0.0},{_terminalLandingStaging.Z:0.0}); " +
                    "flight ownership is intentionally irrelevant for this terminal-only transition.");
            }
            if (DateTime.UtcNow - _terminalLandingStarted > TimeSpan.FromSeconds(45))
            {
                Status = $"terminal-foot-timeout; state={player.MovementState}, falling={player.IsFalling}, " +
                    $"staging-range={Vector3.Distance(player.Position, _terminalLandingStaging):0.0}m";
                return VehicleTransitionResult.Unavailable;
            }
            if (player.MovementState == MovementState.Fly)
            {
                _terminalGroundObservedAt = DateTime.MinValue;
                float stagingRange = Vector3.Distance(player.Position, _terminalLandingStaging);
                float verticalDelta = Math.Abs(player.Position.Y - _terminalLandingStaging.Y);
                if (stagingRange > 1.25f || verticalDelta > 1.25f)
                {
                    if (!SafeToTransition(player)) { Status = "terminal-descent-gated"; return VehicleTransitionResult.Blocked; }
                    _movement.SetDestination(MovementOwner.OutdoorTravel, _terminalLandingStaging);
                    Status = $"terminal-safe-descent; staging-range={stagingRange:0.0}m, vertical={verticalDelta:0.0}m";
                    return VehicleTransitionResult.Waiting;
                }
                _movement.Halt(MovementOwner.OutdoorTravel);
                if (Item.HasPendingUse || Spell.HasPendingCast)
                { Status = "terminal-dismount-waits-for-pending-action"; return VehicleTransitionResult.Waiting; }
                if (!_terminalDismountRequested)
                {
                    Item equippedVehicle = Inventory.Items.FirstOrDefault(x =>
                        x.Id == YalmahaXl29478ItemId && x.IsEquipped);
                    if (equippedVehicle != null)
                    {
                        if (Inventory.NumFreeSlots <= 0)
                        { Status = "terminal-dismount-no-free-inventory-slot"; return VehicleTransitionResult.Unavailable; }
                        equippedVehicle.MoveToInventory();
                        Status = "terminal-vehicle-unequip-requested-once; automatic-inventory-slot=111";
                        _say("Terminal foot requirement: safe landing staging verified; requested one explicit equipped-Yalmaha move to main inventory and waiting for live detachment proof.");
                    }
                    else
                    {
                        _movement.SetMovement(MovementOwner.OutdoorTravel, MovementAction.LeaveFly);
                        Status = "terminal-leave-fly-requested-once";
                        _say("Terminal foot requirement: safe landing staging verified; no equipped known Yalmaha was exposed, so sent one LeaveFly and waiting for live detachment proof.");
                    }
                    _terminalDismountRequested = true;
                }
                else Status = TerminalVehicleAttached(player) ?
                    "terminal-dismount-awaiting-live-detachment" : "terminal-dismount-awaiting-live-foot-state";
                return VehicleTransitionResult.Waiting;
            }
            _movement.Release(MovementOwner.OutdoorTravel);
            if (TerminalVehicleAttached(player) && !_terminalDismountRequested)
            {
                if (Item.HasPendingUse || Spell.HasPendingCast)
                { Status = "terminal-attached-vehicle-waits-for-pending-action"; return VehicleTransitionResult.Waiting; }
                Item equippedVehicle = Inventory.Items.FirstOrDefault(x =>
                    x.Id == YalmahaXl29478ItemId && x.IsEquipped);
                if (equippedVehicle == null || Inventory.NumFreeSlots <= 0)
                {
                    Status = equippedVehicle == null ? "terminal-attached-vehicle-not-resolvable" :
                        "terminal-dismount-no-free-inventory-slot";
                    return VehicleTransitionResult.Unavailable;
                }
                equippedVehicle.MoveToInventory();
                _terminalDismountRequested = true;
                Status = "terminal-vehicle-unequip-requested-once; automatic-inventory-slot=111";
                _say("Terminal foot requirement: AO reports non-Fly while a vehicle remains attached; requested one explicit equipped-Yalmaha move to main inventory and waiting for live detachment proof.");
                return VehicleTransitionResult.Waiting;
            }
            if (TerminalVehicleAttached(player) || player.IsFalling || Item.HasPendingUse || Spell.HasPendingCast)
            {
                _terminalGroundObservedAt = DateTime.MinValue;
                Status = TerminalVehicleAttached(player) ? "terminal-vehicle-detachment-pending" :
                    player.IsFalling ? "terminal-ground-settling" : "terminal-pending-action-settling";
                return VehicleTransitionResult.Waiting;
            }
            if (_terminalGroundObservedAt == DateTime.MinValue)
            {
                _terminalGroundObservedAt = DateTime.UtcNow;
                Status = "terminal-ground-verification";
                return VehicleTransitionResult.Waiting;
            }
            if (DateTime.UtcNow - _terminalGroundObservedAt < TimeSpan.FromMilliseconds(750))
                return VehicleTransitionResult.Waiting;
            Status = "terminal-foot-verified";
            return VehicleTransitionResult.Ready;
        }

        public void ResetTerminalRequirement()
        {
            _terminalLandingActive = _terminalDismountRequested = false;
            _terminalLandingStarted = _terminalGroundObservedAt = DateTime.MinValue;
            _terminalLandingStaging = Vector3.Zero;
        }

        // Dynel.VehiclePointer is AOSharp's persistent movement/position backing
        // object even on foot; it is not evidence that a vehicle is attached.
        // Equipment-page presence plus live Fly/non-Fly is authoritative here.
        private static bool TerminalVehicleAttached(SimpleChar player) =>
            Inventory.Items.Any(x => x.Id == YalmahaXl29478ItemId && x.IsEquipped);

        private bool SafeToTransition(SimpleChar player) => !Game.IsZoning && !Playfield.IsDungeon &&
            !player.IsAttacking && !player.IsFalling &&
            (_movement.Owner == MovementOwner.None || _movement.Owner == MovementOwner.OutdoorTravel);

        public void ResetJourney()
        {
            _observedOriginal = _activationRequested = _ownsFlight = false;
            ResetTerminalRequirement(); CompleteMissionBatchRemount(); Status = "unobserved";
        }
    }
}
