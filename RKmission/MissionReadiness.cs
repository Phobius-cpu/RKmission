using System;
using System.Collections.Generic;
using System.Linq;
using AOSharp.Common.GameData;
using AOSharp.Core;
using AOSharp.Core.Combat;
using AOSharp.Core.Inventory;
using AOSharp.Pathfinding;
using SmokeLounge.AOtomation.Messaging.Messages;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;
using SmokeLounge.AOtomation.Messaging.GameData;
using Identity = AOSharp.Common.GameData.Identity;

namespace RKmission
{
    // Holds new room actions while stationary preparation can run. Never replaces
    // the user's CombatHandler or attempts recovery while player/pets have aggro.
    internal sealed class MissionReadiness
    {
        private readonly Action<string> _say;
        private readonly MissionReadinessSettings _settings;
        private readonly Dictionary<string, DateTime> _retryAfter = new Dictionary<string, DateTime>();
        private bool _active, _requested, _wasInCombat, _seated;
        private DateTime _started, _lastProgress, _quietAt, _busyUntil, _nextAction;
        private float _observedHealth, _observedNano;
        private string _buffSignature, _reason;

        public bool IsWaiting { get; private set; }
        public bool InCombat { get; private set; }
        public string Failure { get; private set; }
        public string Status => Failure ?? (IsWaiting ? "preparing/recovering" : "ready");

        public MissionReadiness(Action<string> say, MissionReadinessSettings settings)
        {
            _say = say;
            _settings = settings;
        }

        public void Start()
        {
            Stop();
            _active = _requested = true;
            _reason = "mission entry";
        }

        public void Stop()
        {
            Stand();
            _active = _requested = _wasInCombat = IsWaiting = InCombat = false;
            _started = _busyUntil = _nextAction = DateTime.MinValue;
            _retryAfter.Clear();
            Failure = null;
        }

        public void ObserveCombat()
        {
            var player = DynelManager.LocalPlayer;
            if (!_active || player == null) return;
            var pets = DynelManager.Characters.Where(x => x.IsPet &&
                x.PetOwnerId == player.Identity.Instance && x.IsAlive).ToList();
            var defended = new HashSet<Identity>(pets.Select(x => x.Identity)) { player.Identity };
            InCombat = player.IsAttackPending ||
                (player.IsAttacking && player.FightingTarget?.IsAlive != false) ||
                pets.Any(x => x.IsAttacking && x.FightingTarget?.IsAlive != false) ||
                DynelManager.Characters.Any(x => x.IsAlive && x.Identity != player.Identity &&
                    !defended.Contains(x.Identity) && x.FightingTarget != null &&
                    defended.Contains(x.FightingTarget.Identity));
            if (InCombat)
            {
                if (IsWaiting) _say("Preparation interrupted by combat; defending before recovery.");
                Stand(true);
                _wasInCombat = true;
                _requested = true;
                IsWaiting = false;
                _started = DateTime.MinValue;
            }
            else if (_wasInCombat)
            {
                _wasInCombat = false;
                _requested = true;
                _reason = "after combat";
                _started = DateTime.MinValue;
            }
        }

        public void ObserveAction(object sender, N3Message message)
        {
            if (!_active || Game.IsZoning || !(message is CharacterActionMessage action) ||
                action.Action != CharacterActionType.CastNano) return;
            // HasPendingCast is only a short send window in AO#. Retain the
            // program's cast/recharge horizon so a long buff is not interrupted.
            double seconds = 2;
            if (Spell.Find(action.Parameter2, out Spell spell))
                seconds += Math.Max(0, spell.GetStat(Stat.AttackDelay) / 100f) +
                    Math.Max(0, spell.GetStat(Stat.RechargeDelay) / 100f);
            DateTime until = DateTime.UtcNow.AddSeconds(Math.Min(120, seconds));
            if (until > _busyUntil) _busyUntil = until;
            _quietAt = DateTime.UtcNow;
        }

        // Called outside an active doorway transition, before objectives/loot or
        // acquiring a new enemy. ObserveCombat still runs during doorway movement.
        public bool Hold()
        {
            var player = DynelManager.LocalPlayer;
            if (!_active || InCombat || player == null) return false;
            DateTime now = DateTime.UtcNow;
            var missingBuffs = MissingBuffs().ToList();
            bool healthLow = player.MaxHealth > 0 && player.HealthPercent < _settings.HealthPercent;
            bool nanoLow = player.MaxNano > 0 && (player.NanoPercent < _settings.NanoPercent ||
                missingBuffs.Any(id => Spell.Find(id, out Spell buff) && buff.Cost > player.Nano && buff.Cost <= player.MaxNano));
            bool pending = PendingAction() || now < _busyUntil;
            if (!_requested && !IsWaiting && !healthLow && !nanoLow && !pending && missingBuffs.Count == 0)
                return false;

            if (_started == DateTime.MinValue)
            {
                _started = _lastProgress = _quietAt = now;
                _observedHealth = player.HealthPercent;
                _observedNano = player.MaxNano > 0 ? player.NanoPercent : 100;
                _buffSignature = BuffSignature();
                // Give a configured external handler one stationary observation
                // window. A healthy character with no handler/buff work proceeds.
                bool handlerWindow = _requested && CombatHandler.Instance != null;
                _requested = false;
                if (!healthLow && !nanoLow && !pending && missingBuffs.Count == 0 && !handlerWindow)
                {
                    _started = DateTime.MinValue;
                    _reason = null;
                    return false;
                }
                IsWaiting = true;
                SMovementController.Halt();
                _say($"Readiness pause ({_reason ?? "before next room action"}): " +
                    $"HP={player.HealthPercent:0.0}%, nano={(player.MaxNano > 0 ? player.NanoPercent : 100):0.0}%.");
            }

            string buffs = BuffSignature();
            if (player.HealthPercent > _observedHealth ||
                (player.MaxNano > 0 && player.NanoPercent > _observedNano) || buffs != _buffSignature)
            {
                _lastProgress = now;
            }
            if (buffs != _buffSignature) _quietAt = now;
            _observedHealth = player.HealthPercent;
            _observedNano = player.MaxNano > 0 ? player.NanoPercent : 100;
            _buffSignature = buffs;
            if (pending) _quietAt = _lastProgress = now;
            bool handlerStarting = CombatHandler.Instance != null &&
                now - _started < TimeSpan.FromSeconds(_settings.HandlerStartSeconds);
            if (now - _started >= TimeSpan.FromSeconds(_settings.TimeoutSeconds) ||
                (!pending && now - _lastProgress >= TimeSpan.FromSeconds(_settings.NoProgressSeconds)))
            {
                Failure = $"Readiness unresolved: HP={player.HealthPercent:0.0}%, " +
                    $"nano={(player.MaxNano > 0 ? player.NanoPercent : 100):0.0}%, " +
                    $"missing configured buffs={string.Join(",", missingBuffs)}. " +
                    "Check recovery supplies, cooldowns, skills, NCU and combat-handler settings; /rkm start to retry.";
                Stand();
                return true;
            }
            if (pending || handlerStarting || now < _nextAction) return true;

            if (!healthLow && !nanoLow && missingBuffs.Count == 0)
            {
                if (now - _quietAt < TimeSpan.FromSeconds(_settings.QuietSeconds)) return true;
                if (Stand(true)) return true; // Confirm standing before resuming movement.
                IsWaiting = false;
                _started = DateTime.MinValue;
                _reason = null;
                _say("Readiness complete; resuming mission actions.");
                return false;
            }

            // Let an external handler finish its next buff before consuming a kit.
            if (now - _quietAt < TimeSpan.FromSeconds(_settings.QuietSeconds)) return true;
            if (TryRecovery(healthLow, nanoLow, now)) return true;
            if (!healthLow && !nanoLow && missingBuffs.Count > 0)
            {
                if (Stand(true)) return true;
                foreach (int id in missingBuffs)
                {
                    if (!Spell.Find(id, out Spell buff) || !buff.IsReady ||
                        buff.Cost > player.Nano || !player.MovementStatePermitsCasting ||
                        !buff.MeetsUseReqs(player) || !CanTry("buff:" + id, now)) continue;
                    _retryAfter["buff:" + id] = now.AddSeconds(15);
                    buff.Cast(player, true);
                    _nextAction = now.AddSeconds(2);
                    _quietAt = now;
                    return true;
                }
            }
            else if (!_seated && player.MovementState != MovementState.Sit)
            {
                // Sitting enables treatment kits and natural regeneration. Buffs
                // and standing-only heals regain priority on the following tick.
                SMovementController.SetMovement(MovementAction.SwitchToSit);
                _seated = true;
                _nextAction = now.AddSeconds(1);
            }
            return true;
        }

        private bool TryRecovery(bool healthLow, bool nanoLow, DateTime now)
        {
            var player = DynelManager.LocalPlayer;
            var actions = new List<RecoveryAction>();
            foreach (Item item in Inventory.Items.Where(x => x.Slot.Type == IdentityType.Inventory &&
                x.UniqueIdentity.Type != IdentityType.Container))
                AddRecovery(actions, item, "item:" + item.Id + ":" + item.QualityLevel,
                    healthLow, nanoLow, now);
            foreach (PerkAction perk in PerkAction.List.Where(x => x.IsAvailable && !x.IsPending && !x.IsExecuting))
                AddRecovery(actions, perk, "perk:" + perk.Id, healthLow, nanoLow, now);
            foreach (Spell spell in Spell.List.Where(x => x.IsReady && x.Cost <= player.Nano))
                AddRecovery(actions, spell, "spell:" + spell.Id, healthLow, nanoLow, now);

            foreach (RecoveryAction action in actions.OrderByDescending(x => x.Score))
            {
                if (!action.Item.MeetsUseReqs(player) || SkillLocked(action.Item)) continue;
                if (action.Item is Spell && !player.MovementStatePermitsCasting)
                {
                    if (player.MovementState == MovementState.Sit && Stand(true)) return true;
                    continue;
                }
                bool sent = true;
                if (action.Item is Item item) item.Use(player, true);
                else if (action.Item is Spell spell) spell.Cast(player, true);
                else if (action.Item is PerkAction perk) sent = perk.Use(player, true);
                if (!sent) continue;
                _retryAfter[action.Key] = now.AddSeconds(10);
                _nextAction = now.AddSeconds(2);
                _quietAt = now;
                _say($"Recovery: {action.Item.Name} on self.");
                return true;
            }
            return false;
        }

        private sealed class RecoveryAction
        {
            public DummyItem Item;
            public string Key;
            public int Score;
        }

        private void AddRecovery(List<RecoveryAction> actions, DummyItem item, string key,
            bool healthLow, bool nanoLow, DateTime now)
        {
            if (!CanTry(key, now)) return;
            // Inspect positive HP/nano effects, including programs applied by
            // kits/perks. Names alone do not authorize drains or hostile actions.
            var effects = Effects(item, new HashSet<int>(), 0).ToList();
            if (effects.Any(x => x.Function == SpellFunction.Taunt || x.Function == SpellFunction.AoeDmg ||
                x.Function == SpellFunction.DrainDmg || x.Function == SpellFunction.Teleport ||
                x.Function == SpellFunction.TeleportLastSave || x.Function == SpellFunction.TeleportPerk ||
                x.Function == SpellFunction.SpawnMonster || x.Function == SpellFunction.SummonPet)) return;
            if (item is Spell activeHot && effects.Any(x => IsHit(x) &&
                Property(x, SpellPropertyOperator.Duration) > 1) && DynelManager.LocalPlayer.Buffs.Any(x =>
                    x.Id == activeHot.Id && x.RemainingTime > 5)) return;
            if (effects.Any(x => IsHit(x) && Property(x, SpellPropertyOperator.Stat) == (int)Stat.Health &&
                (Property(x, SpellPropertyOperator.Min) < 0 || Property(x, SpellPropertyOperator.Max) < 0))) return;
            int healthGain = effects.Where(IsHit).Where(x => Property(x, SpellPropertyOperator.Stat) == (int)Stat.Health)
                .Sum(x => Math.Max(0, Property(x, SpellPropertyOperator.Min)));
            int nanoGain = effects.Where(IsHit).Where(x => Property(x, SpellPropertyOperator.Stat) == (int)Stat.CurrentNano)
                .Sum(x => Math.Max(0, Property(x, SpellPropertyOperator.Min)));
            if (item is Spell recoverySpell) nanoGain = Math.Max(0, nanoGain - recoverySpell.Cost);
            int score = (healthLow ? healthGain : 0) + (nanoLow ? nanoGain : 0);
            if (score > 0) actions.Add(new RecoveryAction { Item = item, Key = key, Score = score });
        }

        private static IEnumerable<SpellData> Effects(DummyItem item, HashSet<int> visited, int depth)
        {
            if (depth > 3 || !visited.Add(item.Id)) yield break;
            foreach (SpellData effect in item.UseModifiers)
            {
                yield return effect;
                if (effect.Function != SpellFunction.CastNano) continue;
                int id = Property(effect, SpellPropertyOperator.TargetInstance);
                if (id <= 0) id = Property(effect, SpellPropertyOperator.ItemInstance);
                if (id > 0 && DummyItem.TryGet(new Identity(IdentityType.NanoProgram, id), out NanoItem nano))
                    foreach (SpellData child in Effects(nano, visited, depth + 1)) yield return child;
            }
        }

        private static bool IsHit(SpellData effect) =>
            effect.Function == SpellFunction.Hit || effect.Function == SpellFunction.HitPerk;

        private static int Property(SpellData effect, SpellPropertyOperator property) =>
            effect.Properties.TryGetValue(property, out int value) ? value : 0;

        private static bool SkillLocked(DummyItem item) => item.UseModifiers.Any(x =>
        {
            if (x.Function != SpellFunction.LockSkill) return false;
            int stat = Property(x, SpellPropertyOperator.Stat);
            if (stat <= 0) stat = Property(x, SpellPropertyOperator.SkillType);
            return stat > 0 && DynelManager.LocalPlayer.Cooldowns.ContainsKey((Stat)stat);
        });

        private bool CanTry(string key, DateTime now) =>
            !_retryAfter.TryGetValue(key, out DateTime until) || now >= until;

        private IEnumerable<int> MissingBuffs()
        {
            var buffs = DynelManager.LocalPlayer.Buffs;
            foreach (int id in _settings.BuffNanoIds)
            {
                if (buffs.Any(x => x.Id == id && x.RemainingTime > 5)) continue;
                if (Spell.Find(id, out Spell spell) && (int)spell.Nanoline > 0 && buffs.Any(x =>
                    x.Nanoline == spell.Nanoline && x.StackingOrder >= spell.StackingOrder && x.RemainingTime > 5)) continue;
                yield return id;
            }
        }

        private static string BuffSignature() =>
            string.Join(",", DynelManager.LocalPlayer.Buffs.Select(x => x.Id).OrderBy(x => x));

        private static bool PendingAction() => Spell.HasPendingCast || Item.HasPendingUse ||
            PerkAction.List.Any(x => x.IsPending || x.IsExecuting);

        private bool Stand(bool allowExternalSeat = false)
        {
            if (!_seated && !allowExternalSeat) return false;
            if (Game.IsZoning || DynelManager.LocalPlayer == null) { _seated = false; return false; }
            if (DynelManager.LocalPlayer.MovementState == MovementState.Sit)
            {
                SMovementController.SetMovement(MovementAction.LeaveSit);
                _nextAction = DateTime.UtcNow.AddSeconds(1);
                return true;
            }
            _seated = false;
            return false;
        }
    }
}
