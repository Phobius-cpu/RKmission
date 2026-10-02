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
        private DateTime _lastCombatTick;
        private TimeSpan _withoutAttackOpportunity;
        public Identity Target => _target;

        public CombatDriver(Action<string> say) { _say = say; }
        public void Reset()
        {
            _target = Identity.None;
            _lastCombatTick = DateTime.MinValue;
            _withoutAttackOpportunity = TimeSpan.Zero;
        }
        public void Pause() { if (_target != Identity.None) _lastCombatTick = DateTime.UtcNow; }

        public bool Tick(SimpleChar enemy, int roomId, Func<SimpleChar, bool> approach,
            Action onTargetChanged, float engagementRange)
        {
            if (enemy == null || !enemy.IsValid || !enemy.IsAlive) { Reset(); return true; }
            var player = DynelManager.LocalPlayer;
            DateTime now = DateTime.UtcNow;
            if (_target != enemy.Identity)
            {
                _target = enemy.Identity;
                _lastCombatTick = now;
                _withoutAttackOpportunity = TimeSpan.Zero;
                onTargetChanged();
                MovementArbiter.Current.Halt(MovementOwner.CombatPosition);
                _say($"Targeting {enemy.Name} ({enemy.Identity}) in room {roomId} at " +
                    $"{enemy.DistanceFrom(player):0.0}m (new engagement range {engagementRange:0}m)" +
                    (enemy.IsPet ? " (spawned entity)." : "."));
            }
            // Count active combat updates, excluding post-combat readiness holds.
            TimeSpan elapsed = now - _lastCombatTick;
            if (elapsed > TimeSpan.Zero)
                _withoutAttackOpportunity += elapsed > TimeSpan.FromSeconds(1)
                    ? TimeSpan.FromSeconds(1) : elapsed;
            _lastCombatTick = now;
            bool inSight = enemy.IsInLineOfSight;
            bool inRange = enemy.IsInAttackRange(true);
            if (inSight && inRange)
            {
                _withoutAttackOpportunity = TimeSpan.Zero;
                MovementArbiter.Current.Halt(MovementOwner.CombatPosition);
                if (!player.IsAttackPending &&
                    (!player.IsAttacking || player.FightingTarget?.Identity != enemy.Identity))
                    player.Attack(enemy);
            }
            else if (_withoutAttackOpportunity > TimeSpan.FromSeconds(45))
            {
                _say($"Enemy {enemy.Name} {enemy.Identity} could not be reached in room {roomId}: " +
                    $"distance={enemy.DistanceFrom(player):0.0}m, line of sight={inSight}, weapon range={inRange}, " +
                    $"health={enemy.Health}, player={player.Position}, enemy={enemy.Position}.");
                return false;
            }
            else if (!approach(enemy))
            {
                _say($"Enemy {enemy.Name} {enemy.Identity} could not establish a mapped combat approach in room {roomId}: " +
                    $"distance={enemy.DistanceFrom(player):0.0}m, line of sight={inSight}, weapon range={inRange}, " +
                    $"movement owner={MovementArbiter.Current.Owner}, player={player.Position}, enemy={enemy.Position}.");
                return false;
            }
            return true;
        }
    }
}
