using System;
using System.Collections.Generic;
using System.Linq;
using AOSharp.Common.GameData;
using AOSharp.Core;
using AOSharp.Core.Inventory;
using SmokeLounge.AOtomation.Messaging.Messages;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;
using CharacterActionType = SmokeLounge.AOtomation.Messaging.GameData.CharacterActionType;

namespace RKmission
{
    // Managed objective identities survive native quest removal and action changes.
    internal sealed class MissionObjective
    {
        private const float ObjectiveUseRange = 2f;
        internal sealed class Step
        {
            public MissionAction Action;
            public Identity Target;
            public bool Pickup, Sent, Done, DeathObserved, Proof;
            public DateTime LastSent, ObservationStarted;
        }

        private readonly Action<string> _say;
        private readonly AcceptedMission _record;
        private readonly List<Step> _steps = new List<Step>();
        private readonly Dictionary<Identity, int> _rooms = new Dictionary<Identity, int>();
        private readonly HashSet<Identity> _pickedUpItems = new HashSet<Identity>();
        private readonly MissionCompletionTracker _completion = new MissionCompletionTracker();
        private DateTime _acknowledgementStarted, _approachStarted;
        private Identity _approaching = Identity.None;
        private bool _serverCompletion;
        public bool Finale { get; private set; }
        public bool FinalActionsAllowed { get; set; }
        public bool RewardConfirmed { get; private set; }
        public string Evidence { get; private set; }
        public string Failure { get; private set; }
        public bool Returning => _record.Kind == RkMissionKind.ReturnItem;
        public IEnumerable<int> Rooms => _rooms.Values.Distinct();
        public bool HasSteps => _steps.Count > 0;
        public bool CollectedReturnItem => Returning && ReturnItem != null;
        public Item ReturnItem => Inventory.Items.FirstOrDefault(item =>
            _pickedUpItems.Contains(item.UniqueIdentity) ||
            _steps.Any(step => step.Pickup && item.UniqueIdentity == step.Target) ||
            _steps.Any(step => step.Action is UseItemOnItemAction use &&
                use.Destination == _record.Source && item.UniqueIdentity == use.Source));

        public MissionObjective(AcceptedMission record, Action<string> say)
        { _record = record; _say = say; Refresh(null); }

        public void Refresh(DungeonLayout layout)
        {
            foreach (MissionAction action in _record.Actions ?? new List<MissionAction>())
            {
                Identity target = Target(action);
                // Find-item missions complete by selecting the item. Only a
                // return-item mission needs to collect it for a later hand-in.
                bool pickup = action is FindItemAction && Returning;
                if (action is UseItemOnItemAction use && (_record.Kind == RkMissionKind.ReturnItem ||
                    (use.Destination == _record.Source && _record.Source != Identity.None)))
                {
                    _record.Kind = RkMissionKind.ReturnItem;
                    target = use.Source;
                    pickup = true;
                }
                if (target == Identity.None) continue;
                Step existing = _steps.FirstOrDefault(step => step.Target == target && step.Action.Type == action.Type);
                if (existing == null)
                    _steps.Add(new Step { Action = action, Target = target, Pickup = pickup });
                else
                    existing.Pickup = pickup;
            }
            // A return hand-in action may arrive after its FindItemAction.
            foreach (Step step in _steps.Where(x => x.Action is FindItemAction))
                step.Pickup = Returning;
            if (layout == null) return;
            foreach (Step step in _steps)
            {
                Dynel dynel = DynelManager.GetDynel(step.Target);
                if (dynel == null) continue;
                Room room = Playfield.Rooms.FirstOrDefault(x => layout.ContainsDynel(x.Instance, dynel));
                if (room != null && (!_rooms.TryGetValue(step.Target, out int previous) || previous != room.Instance))
                {
                    _rooms[step.Target] = room.Instance;
                    _say($"Objective {step.Target} located in room {room.Instance}; reserve its interaction for the finale.");
                }
            }
        }

        private static Identity Target(MissionAction action)
        {
            if (action is KillPersonAction kill) return kill.Target;
            if (action is FindPersonAction person) return person.Target;
            if (action is FindItemAction item) return item.Target;
            if (action is UseItemOnItemAction use) return use.Destination;
            return Identity.None;
        }

        public bool IsObjective(Identity identity) => _steps.Any(x => x.Target == identity);
        public bool IsNonLootObjective(Identity identity) => _steps.Any(x =>
            x.Target == identity && !x.Pickup);
        public bool HoldEnemy(Identity identity) => _record.State != MissionProgress.CompletedByUser && _steps.Any(step => step.Target == identity &&
            (step.Action is KillPersonAction ? !(Finale && FinalActionsAllowed) :
                step.Action is FindPersonAction && (!RewardConfirmed || !FinalActionsAllowed)));
        public bool HoldLoot(Dynel dynel) => IsObjective(dynel.Identity) &&
            (IsNonLootObjective(dynel.Identity) || !(Finale && FinalActionsAllowed) ||
                dynel.Identity.Type == IdentityType.SimpleChar);

        public void BeginFinale()
        {
            if (Finale) return;
            Finale = true;
            _say("Other rooms and enemies cleared; ordinary loot processed or skipped. Starting the reserved objective finale.");
        }

        public void ObserveAcknowledgement()
        {
            if (RewardConfirmed || !Finale || _record.DeletedByUser || Returning) return;
            DateTime now = DateTime.UtcNow;
            foreach (Step step in _steps.Where(x => x.Sent))
            {
                Dynel dynel = DynelManager.GetDynel(step.Target);
                if (step.Action is KillPersonAction)
                {
                    if ((dynel != null && !new SimpleChar(dynel).IsAlive) ||
                        DynelManager.Corpses.Any(x => x.Identity.Instance == step.Target.Instance)) step.DeathObserved = true;
                    step.Proof |= step.DeathObserved;
                }
                else if (step.Action is FindPersonAction)
                {
                    if (Targeting.Target?.Identity != step.Target) step.ObservationStarted = DateTime.MinValue;
                    else if (step.ObservationStarted == DateTime.MinValue) step.ObservationStarted = now;
                    else if (now - step.ObservationStarted >= TimeSpan.FromSeconds(30)) step.Proof = true;
                }
                else if (step.Action is FindItemAction && !step.Pickup)
                    step.Proof |= Targeting.Target?.Identity == step.Target;
                else if (step.Pickup)
                    step.Proof |= Inventory.Items.Any(x => x.UniqueIdentity == step.Target || _pickedUpItems.Contains(x.UniqueIdentity));
                else if (step.Action is UseItemOnItemAction use)
                    step.Proof |= !Inventory.Items.Any(x => x.UniqueIdentity == use.Source);
            }
            // Disappearance alone is never completion. It must follow our final
            // target/use/kill, with no observed manual Delete command.
            bool ownedAction = _steps.Any(x => x.Sent &&
                now - x.LastSent <= TimeSpan.FromSeconds(60));
            bool objectiveProof = _steps.Count > 0 && _steps.All(x => x.Proof || x.Done);
            if (!_completion.Reconcile(_record.Id, ownedAction, objectiveProof,
                _serverCompletion, _record.DeletedByUser, out string evidence)) return;
            RewardConfirmed = true;
            Evidence = evidence;
            _say($"Mission {_record.Id.Instance}: objective acknowledged by removal of the bound quest after our finale action.");
        }

        public void ObserveMessage(N3Message message)
        {
            if (!Finale || !_steps.Any(x => x.Sent)) return;
            if (message is CharacterActionMessage action && action.Action == CharacterActionType.Death)
                foreach (Step step in _steps.Where(x => x.Sent && x.Action is KillPersonAction &&
                    (x.Target == action.Target || x.Target == action.Identity))) step.DeathObserved = true;
            if (message is ChatTextMessage chat && chat.Text != null &&
                (chat.Text.IndexOf("Mission accomplished", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 chat.Text.IndexOf("You have completed the mission", StringComparison.OrdinalIgnoreCase) >= 0))
                _serverCompletion = true;
        }

        public void ArmKill(SimpleChar enemy)
        {
            Step step = _steps.FirstOrDefault(x => x.Target == enemy.Identity && x.Action is KillPersonAction);
            if (step == null || !Finale) return;
            step.Sent = true;
            step.LastSent = DateTime.UtcNow; // Combat can take longer than the acknowledgement window.
        }

        public bool Tick(Room room, DungeonLayout layout, Action<Vector3> navigate,
            ManagerLoot.ManagerLoot loot)
        {
            foreach (Identity item in loot.MissionObjectiveItems) _pickedUpItems.Add(item);
            if (!Finale || !FinalActionsAllowed || RewardConfirmed || _record.State == MissionProgress.CompletedByUser) return false;
            ObserveAcknowledgement();
            if (RewardConfirmed) return false;
            if (_record.DeletedByUser)
            { Failure = "Bound mission was manually deleted; automatic completion is held."; return true; }
            foreach (Step step in _steps)
            {
                if (Returning && step.Action is UseItemOnItemAction && !step.Pickup) continue;
                if (step.Pickup && Inventory.Items.Any(x => x.UniqueIdentity == step.Target || _pickedUpItems.Contains(x.UniqueIdentity))) step.Done = true;
                if (step.Action is KillPersonAction) continue; // The guarded combat path owns this step.
                if (step.Done) continue;
                Dynel target = DynelManager.GetDynel(step.Target);
                if (target == null || !layout.ContainsDynel(room.Instance, target)) continue;
                if (_approaching != step.Target)
                { _approaching = step.Target; _approachStarted = DateTime.UtcNow; }
                if (DateTime.UtcNow - _approachStarted > TimeSpan.FromSeconds(60))
                {
                    string sourceState = step.Action is UseItemOnItemAction timeoutUse
                        ? $", source={timeoutUse.Source}, source in inventory={Inventory.Items.Any(x => x.UniqueIdentity == timeoutUse.Source)}"
                        : "";
                    Failure = $"Objective {step.Target} did not complete within 60 s " +
                        $"(distance={target.DistanceFrom(DynelManager.LocalPlayer):0.0}m{sourceState}); " +
                        "/rkm complete remains available after checking in game.";
                    return true;
                }
                float interactionRange = step.Pickup || step.Action is UseItemOnItemAction
                    ? ObjectiveUseRange : 4f;
                if (target.DistanceFrom(DynelManager.LocalPlayer) > interactionRange)
                { navigate(target.Position); return true; }
                MovementArbiter.Current.Halt(MovementOwner.Objective);
                if (Spell.HasPendingCast || Item.HasPendingUse || loot.IsProcessingMissionLoot) return true;
                if (DateTime.UtcNow - step.LastSent < TimeSpan.FromSeconds(3)) return true;
                if (step.Action is UseItemOnItemAction use && !step.Pickup)
                {
                    Item source = Inventory.Items.FirstOrDefault(x => x.UniqueIdentity == use.Source);
                    if (source == null && step.Sent && step.Proof) continue; // Consumed; await acknowledgement.
                    if (source == null) { Failure = "Mission repair/use item is missing from main inventory."; return true; }
                    source.UseOn(target.Identity);
                }
                else if (step.Pickup)
                {
                    if (Inventory.NumFreeSlots <= 1)
                    { Failure = "Free inventory space is needed for the objective item."; return true; }
                    if (target.Identity.Type == IdentityType.Container)
                    {
                        loot.BeginMissionObjectiveLoot(target.Identity);
                        MarkSent(step);
                        return false; // Manager.Loot opens/picks and transfers the objective contents.
                    }
                    // A ground mission item needs the game's pickup action.
                    // Generic Use interacts with the world dynel but need not
                    // transfer it into the character's inventory.
                    Network.Send(new PickUpMessage { Target = target.Identity });
                }
                else
                {
                    // Find item selects the exact object without opening/using
                    // it. Find person may need ~30 s of uninterrupted selection.
                    if (Targeting.Target?.Identity != target.Identity) target.Target();
                    if (step.Action is FindItemAction)
                        step.Proof |= Targeting.Target?.Identity == step.Target;
                }
                MarkSent(step);
                return true;
            }
            if (CollectedReturnItem) return false;
            if (PendingRoom.HasValue && PendingRoom.Value != room.Instance)
            { _acknowledgementStarted = DateTime.MinValue; return false; }
            if (_acknowledgementStarted == DateTime.MinValue) _acknowledgementStarted = DateTime.UtcNow;
            if (DateTime.UtcNow - _acknowledgementStarted > TimeSpan.FromSeconds(60))
                Failure = "Objective acknowledgement or remaining objective location is unavailable; check in game, then /rkm complete if appropriate.";
            return false;
        }

        private void MarkSent(Step step)
        {
            if (!step.Sent)
            {
                string detail = step.Action is UseItemOnItemAction use
                    ? $", source={use.Source}, destination={use.Destination}, collect={step.Pickup}"
                    : "";
                _say($"Final objective action: {step.Action.Type}, target={step.Target}{detail}.");
            }
            step.Sent = true;
            step.LastSent = DateTime.UtcNow;
        }

        public int? PendingRoom => _steps.Where(x => !x.Done && _rooms.ContainsKey(x.Target))
            .Select(x => (int?)_rooms[x.Target]).FirstOrDefault();
    }
}
