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

        private bool SafeToTransition(SimpleChar player) => !Game.IsZoning && !Playfield.IsDungeon &&
            !player.IsAttacking && !player.IsFalling &&
            (_movement.Owner == MovementOwner.None || _movement.Owner == MovementOwner.OutdoorTravel);

        public void ResetJourney()
        { _observedOriginal = _activationRequested = _ownsFlight = false; Status = "unobserved"; }
    }
}
