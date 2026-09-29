using System;
using System.Linq;
using AOSharp.Common.GameData;
using AOSharp.Core;

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

        public DeathRecoveryController(MissionReadiness readiness, MovementArbiter movement, Action<string> say)
        { _readiness = readiness; _movement = movement; _say = say; }

        public void Begin()
        {
            _started = DateTime.UtcNow;
            _aliveAt = _lastExitUse = DateTime.MinValue;
            _reclaimPlayfield = 0;
            _preparing = false;
            _movement.StopAll();
        }

        public DeathRecoveryResult Tick()
        {
            if (DateTime.UtcNow - _started > TimeSpan.FromMinutes(4))
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
                return DeathRecoveryResult.Waiting;
            }
            if (DateTime.UtcNow - _aliveAt < TimeSpan.FromSeconds(3))
                return DeathRecoveryResult.Waiting;
            if (!_preparing)
            {
                _preparing = true;
                _readiness.Start();
                _say("Reclaim settled; restoring health, nano and configured readiness before travel.");
            }
            _readiness.ObserveCombat();
            if (_readiness.Hold())
                return _readiness.Failure == null ? DeathRecoveryResult.Waiting : DeathRecoveryResult.Failed;
            if (DynelManager.LocalPlayer.IsFalling ||
                DynelManager.LocalPlayer.GetStat(Stat.TemporarySkillReduction) > 1)
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
            return DeathRecoveryResult.Replan;
        }

        public void Stop()
        {
            _readiness.Stop();
            _movement.Release(MovementOwner.Recovery);
        }
    }
}
