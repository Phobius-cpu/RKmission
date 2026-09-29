using System;
using AOSharp.Common.GameData;
using AOSharp.Core;

namespace RKmission
{
    // MissionDungeon chooses the enemy. This class owns approach, range/LOS,
    // attack initiation and the engagement deadline; profession handlers remain
    // responsible for nanos, perks, pets and heals.
    internal sealed class CombatDriver
    {
        private readonly Action<string> _say;
        private Identity _target = Identity.None;
        private DateTime _lastAttackOpportunity;

        public CombatDriver(Action<string> say) { _say = say; }
        public void Reset() { _target = Identity.None; _lastAttackOpportunity = DateTime.MinValue; }

        public bool Tick(SimpleChar enemy, int roomId, Action<Vector3> navigate,
            Action onTargetChanged, float engagementRange)
        {
            if (enemy == null) { Reset(); return true; }
            var player = DynelManager.LocalPlayer;
            if (_target != enemy.Identity)
            {
                _target = enemy.Identity;
                _lastAttackOpportunity = DateTime.UtcNow;
                onTargetChanged();
                MovementArbiter.Current.Halt(MovementOwner.CombatPosition);
                _say($"Targeting {enemy.Name} ({enemy.Identity}) in room {roomId} at " +
                    $"{enemy.DistanceFrom(player):0.0}m (new engagement range {engagementRange:0}m)" +
                    (enemy.IsPet ? " (spawned entity)." : "."));
            }
            if (DateTime.UtcNow - _lastAttackOpportunity > TimeSpan.FromSeconds(20))
            {
                _say($"Enemy {enemy.Name} could not be reached in room {roomId}.");
                return false;
            }
            if (enemy.IsInLineOfSight && enemy.IsInAttackRange(true))
            {
                _lastAttackOpportunity = DateTime.UtcNow;
                MovementArbiter.Current.Halt(MovementOwner.CombatPosition);
                if (!player.IsAttackPending &&
                    (!player.IsAttacking || player.FightingTarget?.Identity != enemy.Identity))
                    player.Attack(enemy);
            }
            else navigate(enemy.Position);
            return true;
        }
    }
}
