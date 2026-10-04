using System;
using System.Linq;
using AOSharp.Common.GameData;
using AOSharp.Core;
using AOSharp.Core.Inventory;

namespace RKmission
{
    internal enum HandInResult { Waiting, Confirmed, Blocked }

    // Only the exact return action of the bound quest may authorize a hand-in.
    // Sending UseOn is not success: the source must leave inventory and the
    // bound quest must remain absent after the action.
    internal sealed class ReturnItemHandIn
    {
        private readonly MissionCompletionTracker _completion = new MissionCompletionTracker();
        private Identity _mission = Identity.None;
        private Identity _item = Identity.None;
        private DateTime _started, _lastSent;
        private int _attempts;
        public string Failure { get; private set; }

        public void Reset()
        {
            _mission = _item = Identity.None;
            _started = _lastSent = DateTime.MinValue;
            _attempts = 0;
            Failure = null;
        }

        public HandInResult Tick(AcceptedMission record)
        {
            if (_mission != record.Id)
            {
                Reset();
                _mission = record.Id;
                _started = DateTime.UtcNow;
            }
            if (record.DeletedByUser)
                return Block("Bound return-item quest was deleted by the user.");
            UseItemOnItemAction[] actions = (record.Actions?.OfType<UseItemOnItemAction>() ??
                    Enumerable.Empty<UseItemOnItemAction>())
                .Where(x => record.Source != Identity.None && x.Destination == record.Source)
                .ToArray();
            if (actions.Length != 1)
                return Block("No unambiguous return-item action to the bound mission source is available.");
            UseItemOnItemAction action = actions[0];
            if (_item == Identity.None) _item = action.Source;
            if (_item != action.Source)
                return Block("Return-item identity changed after hand-in began.");
            Item item = Inventory.Items.FirstOrDefault(x =>
                x.Slot.Type == IdentityType.Inventory && x.UniqueIdentity == _item);
            bool itemConsumed = item == null && _lastSent != DateTime.MinValue;
            if (_completion.Reconcile(record.Id, _lastSent != DateTime.MinValue &&
                DateTime.UtcNow - _lastSent < TimeSpan.FromMinutes(4), itemConsumed,
                false, record.DeletedByUser, out _)) return HandInResult.Confirmed;
            if (DateTime.UtcNow - _started > TimeSpan.FromMinutes(4))
                return Block("Return-item hand-in was not confirmed within four minutes.");
            if (item == null)
                return _lastSent == DateTime.MinValue
                    ? Block("Bound return item is missing before hand-in.") : HandInResult.Waiting;
            if (Mission.List == null) return HandInResult.Waiting;
            if (!Mission.List.Any(x => x.Identity == record.Id))
                return Block("Bound quest is absent before a verified hand-in.");
            Dynel target = DynelManager.GetDynel(action.Destination);
            if (target == null) return HandInResult.Waiting;
            if (Vector3.Distance(DynelManager.LocalPlayer.Position, target.Position) > 2.5f)
                return HandInResult.Waiting;
            if (Item.HasPendingUse || Spell.HasPendingCast ||
                DateTime.UtcNow - _lastSent < TimeSpan.FromSeconds(10)) return HandInResult.Waiting;
            if (_attempts >= 3)
                return Block("Three exact return-item uses produced no confirmed hand-in.");
            item.UseOn(action.Destination);
            _lastSent = DateTime.UtcNow;
            _attempts++;
            return HandInResult.Waiting;
        }

        private HandInResult Block(string reason)
        {
            Failure = reason;
            return HandInResult.Blocked;
        }
    }
}
