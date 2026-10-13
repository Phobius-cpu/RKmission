using System;
using System.Linq;
using AOSharp.Common.GameData;
using AOSharp.Core;
using AOSharp.Core.Inventory;

namespace RKmission
{
    internal enum DeathRecoveryResult { Waiting, Replan, Failed }

    // Reconcile against the live character after reclaim. MissionReadiness owns
    // safe kit/nano recovery; this controller handles the reclaim exit and only
    // then returns control to the travel planner.
    internal sealed class DeathRecoveryController
    {
        private readonly MissionReadiness _readiness;
        private readonly MovementArbiter _movement;
        private readonly Action<string> _say;
        private DateTime _started, _aliveAt, _lastExitUse;
        private int _reclaimPlayfield;
        private bool _preparing;
        private bool _canAttempted, _sicknessReported;
        public string Phase { get; private set; } = "Idle";

        public DeathRecoveryController(MissionReadiness readiness, MovementArbiter movement, Action<string> say)
        { _readiness = readiness; _movement = movement; _say = say; }

        public void Begin()
        {
            _started = DateTime.UtcNow;
            _aliveAt = _lastExitUse = DateTime.MinValue;
            _reclaimPlayfield = 0;
            _preparing = false;
            _canAttempted = _sicknessReported = false;
            Phase = "AwaitingReclaim";
            _movement.StopAll();
        }

        public DeathRecoveryResult Tick()
        {
            if (DateTime.UtcNow - _started > TimeSpan.FromMinutes(20))
            { Stop(); return DeathRecoveryResult.Failed; }
            if (Game.IsZoning || DynelManager.LocalPlayer == null ||
                !DynelManager.LocalPlayer.IsAlive)
            {
                _aliveAt = DateTime.MinValue;
                return DeathRecoveryResult.Waiting;
            }
            if (_aliveAt == DateTime.MinValue)
            {
                _aliveAt = DateTime.UtcNow;
                _reclaimPlayfield = Playfield.ModelIdentity.Instance;
                Phase = "SettlingAlive";
                return DeathRecoveryResult.Waiting;
            }
            if (DateTime.UtcNow - _aliveAt < TimeSpan.FromSeconds(3))
                return DeathRecoveryResult.Waiting;
            if (!AcceptedMissions.IsRubiKaPlayfield(_reclaimPlayfield))
            {
                Phase = "FailedOutsideRubiKa";
                _say($"Death recovery stopped: reclaim playfield {_reclaimPlayfield} is not Rubi-Ka. Manual recovery is required; no garden or Shadowlands route will be attempted.");
                Stop();
                return DeathRecoveryResult.Failed;
            }
            int sickness = DynelManager.LocalPlayer.GetStat(Stat.TemporarySkillReduction);
            if (sickness > 1)
            {
                Phase = "ResurrectionSickness";
                if (!_canAttempted && !Item.HasPendingUse && !Spell.HasPendingCast)
                {
                    _canAttempted = true;
                    Item can = Inventory.Items.FirstOrDefault(x => x.Name != null &&
                        x.Name.IndexOf("Resurrection Sickness Removal", StringComparison.OrdinalIgnoreCase) >= 0);
                    if (can != null)
                    {
                        can.Use();
                        _say($"Death recovery: used configured/known '{can.Name}' and will verify TemporarySkillReduction clears before travel.");
                        return DeathRecoveryResult.Waiting;
                    }
                }
                if (!_sicknessReported)
                {
                    _sicknessReported = true;
                    _say("Death recovery: Resurrection Sickness Removal Nano Can is absent/unusable; waiting for the full TemporarySkillReduction cycle. Warp travel remains blocked.");
                }
                return DeathRecoveryResult.Waiting;
            }
            if (!_preparing)
            {
                _preparing = true;
                Phase = "Readiness";
                _readiness.Start();
                _say("Rubi-Ka reclaim and sickness clearance verified; restoring health, nano and configured readiness before Scotty return.");
            }
            _readiness.ObserveCombat();
            if (_readiness.Hold())
                return _readiness.Failure == null ? DeathRecoveryResult.Waiting : DeathRecoveryResult.Failed;
            if (DynelManager.LocalPlayer.IsFalling)
                return DeathRecoveryResult.Waiting;
            Dynel exit = DynelManager.AllDynels.FirstOrDefault(x =>
                x.Name != null && x.Name.IndexOf("Garden Exit", StringComparison.OrdinalIgnoreCase) >= 0);
            if (exit != null && Playfield.ModelIdentity.Instance == _reclaimPlayfield)
            {
                if (Vector3.Distance(DynelManager.LocalPlayer.Position, exit.Position) > 3f)
                    _movement.SetDestination(MovementOwner.Recovery, exit.Position);
                else if (DateTime.UtcNow - _lastExitUse > TimeSpan.FromSeconds(5))
                {
                    _movement.Halt(MovementOwner.Recovery);
                    exit.Use();
                    _lastExitUse = DateTime.UtcNow;
                }
                return DeathRecoveryResult.Waiting;
            }
            Stop();
            Phase = "ReadyForSavedCityReturn";
            return DeathRecoveryResult.Replan;
        }

        public void Stop()
        {
            _readiness.Stop();
            _movement.Release(MovementOwner.Recovery);
            if (Phase != "ReadyForSavedCityReturn" && Phase != "FailedOutsideRubiKa") Phase = "Idle";
        }
    }
}
