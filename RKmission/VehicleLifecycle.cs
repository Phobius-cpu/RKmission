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
        private bool _terminalLandingActive, _terminalLeaveFlyRequested;
        private DateTime _terminalLandingStarted, _terminalGroundObservedAt;
        private Vector3 _terminalLandingStaging;
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
                _terminalLeaveFlyRequested = false;
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
                if (!_terminalLeaveFlyRequested)
                {
                    _movement.SetMovement(MovementOwner.OutdoorTravel, MovementAction.LeaveFly);
                    _terminalLeaveFlyRequested = true;
                    Status = "terminal-dismount-requested-once";
                    _say("Terminal foot requirement: safe landing staging verified; sent one LeaveFly and waiting for authoritative live foot state.");
                }
                else Status = "terminal-dismount-awaiting-live-state";
                return VehicleTransitionResult.Waiting;
            }
            _movement.Release(MovementOwner.OutdoorTravel);
            if (player.IsFalling || Item.HasPendingUse || Spell.HasPendingCast)
            {
                _terminalGroundObservedAt = DateTime.MinValue;
                Status = player.IsFalling ? "terminal-ground-settling" : "terminal-pending-action-settling";
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
            _terminalLandingActive = _terminalLeaveFlyRequested = false;
            _terminalLandingStarted = _terminalGroundObservedAt = DateTime.MinValue;
            _terminalLandingStaging = Vector3.Zero;
        }

        private bool SafeToTransition(SimpleChar player) => !Game.IsZoning && !Playfield.IsDungeon &&
            !player.IsAttacking && !player.IsFalling &&
            (_movement.Owner == MovementOwner.None || _movement.Owner == MovementOwner.OutdoorTravel);

        public void ResetJourney()
        {
            _observedOriginal = _activationRequested = _ownsFlight = false;
            ResetTerminalRequirement(); Status = "unobserved";
        }
    }
}
