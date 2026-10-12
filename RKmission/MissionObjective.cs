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
        private const float CloseRangeOverride = 1.5f;
        private const float InteractionRayPlayerHeight = 1.1f;
        private const float InteractionRayTargetHeight = 0.25f;
        private static readonly TimeSpan ApproachTimeout = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan AcknowledgementTimeout = TimeSpan.FromSeconds(75);
        private static readonly TimeSpan FindPersonObservationConfirmation = TimeSpan.FromSeconds(5);
        internal enum StepState { Pending, Approaching, ReadyToInteract, ActionSent, AwaitingProof, ProofObserved, Completed, Blocked }
        internal sealed class Step
        {
            public MissionAction Action;
            public Identity Target, InventoryItem;
            public string RoleEvidence;
            public bool Pickup, Sent, Done, DeathObserved, Proof;
            public StepState State;
            public DateTime LastSent, ActionStarted, ObservationStarted, ApproachStarted, ReadyStarted, LastGateLog;
            public float BestDistance = float.MaxValue;
            public string LastGateReason;
        }

        private readonly Action<string> _say;
        private readonly AcceptedMission _record;
        private readonly List<Step> _steps = new List<Step>();
        private readonly Dictionary<Identity, int> _rooms = new Dictionary<Identity, int>();
        private readonly Dictionary<Identity, Vector3> _lastPositions = new Dictionary<Identity, Vector3>();
        private readonly HashSet<Identity> _pickedUpItems = new HashSet<Identity>();
        private readonly MissionCompletionTracker _completion = new MissionCompletionTracker();
        private DateTime _acknowledgementStarted;
        private bool _serverCompletion;
        private string _roleResolutionFailure;
        public bool Finale { get; private set; }
        public bool FinalActionsAllowed { get; set; }
        public bool RewardConfirmed { get; private set; }
        public string Evidence { get; private set; }
        public string Failure { get; private set; }
        public bool Returning => _record.Kind == RkMissionKind.ReturnItem;
        public IEnumerable<int> Rooms => _rooms.Values.Distinct();
        public bool HasSteps => _steps.Count > 0;
        public bool HasKillTarget => _steps.Any(x => x.Action is KillPersonAction);
        public bool HasFindPersonTarget => _steps.Any(x => x.Action is FindPersonAction);
        public Identity FindPersonTarget => _steps.Where(x => !x.Done && x.Action is FindPersonAction)
            .Select(x => x.Target).FirstOrDefault();
        public Identity KillTarget => _steps.Where(x => !x.Done && x.Action is KillPersonAction)
            .Select(x => x.Target).FirstOrDefault();
        public bool CollectedReturnItem => Returning && ReturnItem != null;
        public string StepSummary => string.Join(", ", _steps.Select(x =>
            $"{x.Action.Type}:{x.Target}={x.State}" +
            (x.InventoryItem != Identity.None ? $" item={x.InventoryItem}" : "")));
        public Item ReturnItem => Inventory.Items.FirstOrDefault(item =>
            _pickedUpItems.Contains(item.UniqueIdentity) ||
            _steps.Any(step => step.Pickup && item.UniqueIdentity == step.Target) ||
            _steps.Any(step => step.Action is UseItemOnItemAction &&
                step.InventoryItem != Identity.None && item.UniqueIdentity == step.InventoryItem));

        public MissionObjective(AcceptedMission record, Action<string> say)
        { _record = record; _say = say; Refresh(null); }

        public void Refresh(DungeonLayout layout)
        {
            string roleFailure = null;
            foreach (MissionAction action in _record.Actions ?? new List<MissionAction>())
            {
                Identity target = Target(action);
                Identity inventoryItem = Identity.None;
                string roleEvidence = null;
                // Find-item missions complete by selecting the item. Only a
                // return-item mission needs to collect it for a later hand-in.
                bool pickup = action is FindItemAction && Returning;
                if (action is UseItemOnItemAction use)
                {
                    if (!UseItemRoleResolver.TryResolve(_record, use, out UseItemRoles roles, out string reason))
                    {
                        roleFailure = reason;
                        continue;
                    }
                    target = roles.WorldTarget;
                    inventoryItem = roles.InventoryItem;
                    roleEvidence = roles.Evidence;
                    if (_record.Kind == RkMissionKind.ReturnItem ||
                        (use.Destination == _record.VerifiedIssuingTerminal &&
                            _record.VerifiedIssuingTerminal != Identity.None))
                    {
                        _record.Kind = RkMissionKind.ReturnItem;
                        target = roles.InventoryItem;
                        pickup = true;
                    }
                }
                if (target == Identity.None) continue;
                Step existing = _steps.FirstOrDefault(step => step.Target == target && step.Action.Type == action.Type);
                if (existing == null)
                    _steps.Add(new Step { Action = action, Target = target, InventoryItem = inventoryItem,
                        RoleEvidence = roleEvidence, Pickup = pickup });
                else
                {
                    existing.Pickup = pickup;
                    existing.InventoryItem = inventoryItem;
                    existing.RoleEvidence = roleEvidence;
                }
            }
            _roleResolutionFailure = roleFailure;
            // A return hand-in action may arrive after its FindItemAction.
            foreach (Step step in _steps.Where(x => x.Action is FindItemAction))
                step.Pickup = Returning;
            if (layout == null) return;
            foreach (Step step in _steps)
            {
                Dynel dynel = DynelManager.GetDynel(step.Target);
                if (dynel == null) continue;
                _lastPositions[step.Target] = dynel.Position;
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
            if (action is UseItemOnItemAction) return Identity.None;
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
            _say("Other rooms checked and ordinary enemies cleared; starting the reserved objective finale. Any loot deferred by combat will be collected before exit.");
            if (HasFindPersonTarget)
                _say($"FindPerson finale: exact target={FindPersonTarget}, mapped room=" +
                    $"{RoomFor(FindPersonTarget)?.ToString() ?? "unknown"}; target remains protected during observation.");
        }

        public bool PreflightFinale(out string reason)
        {
            reason = null;
            if (_roleResolutionFailure != null)
            { reason = _roleResolutionFailure; return false; }
            if (_steps.Count == 0) { reason = "No supported objective step is bound to this mission."; return false; }
            if (_record.DeletedByUser) { reason = "Bound mission was manually deleted."; return false; }
            if (_steps.Any(x => x.Pickup && !x.Done) && Inventory.NumFreeSlots <= 1)
            { reason = "Free main-inventory space is needed for the objective item."; return false; }
            foreach (Step step in _steps.Where(x => x.Action is UseItemOnItemAction && !x.Pickup))
            {
                if (step.InventoryItem != Identity.None &&
                    (Inventory.Items.Any(x => x.UniqueIdentity == step.InventoryItem) ||
                    _steps.Any(x => x.Pickup && x.Target == step.InventoryItem))) continue;
                reason = $"Resolved objective inventory item {step.InventoryItem} is absent before finale " +
                    $"(world target={step.Target}, roles={step.RoleEvidence}).";
                return false;
            }
            return true;
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
                    if (Targeting.Target?.Identity != step.Target)
                    {
                        if (step.ObservationStarted != DateTime.MinValue)
                            _say($"FindPerson observation reset: exact target {step.Target} is no longer selected.");
                        step.ObservationStarted = DateTime.MinValue;
                    }
                    else if (step.ObservationStarted == DateTime.MinValue)
                    {
                        step.ObservationStarted = now;
                        _say($"FindPerson observation started for exact target {step.Target}; " +
                            $"{FindPersonObservationConfirmation.TotalSeconds:0} seconds of uninterrupted selection required.");
                    }
                    else if (!step.Proof && now - step.ObservationStarted >= FindPersonObservationConfirmation)
                    {
                        int seconds = (int)(now - step.ObservationStarted).TotalSeconds;
                        step.Proof = true;
                        _say($"FindPerson observation complete for exact target {step.Target} after {seconds} seconds; " +
                            "awaiting mission acknowledgement.");
                    }
                }
                else if (step.Action is FindItemAction && !step.Pickup)
                    step.Proof |= Targeting.Target?.Identity == step.Target;
                else if (step.Pickup)
                    step.Proof |= Inventory.Items.Any(x => x.UniqueIdentity == step.Target || _pickedUpItems.Contains(x.UniqueIdentity));
                else if (step.Action is UseItemOnItemAction)
                    step.Proof |= !Inventory.Items.Any(x => x.UniqueIdentity == step.InventoryItem);
                if (step.Proof) step.State = StepState.ProofObserved;
            }
            // Disappearance alone is never completion. It must follow our final
            // target/use/kill, with no observed manual Delete command.
            // Sent is durable for this exact bound finale. A dynel may vanish
            // immediately after AO accepts our action, and kill combat may run
            // longer than an arbitrary recency window; neither may erase ownership.
            bool ownedAction = _steps.Any(x => x.Sent);
            bool objectiveProof = _steps.Count > 0 && _steps.All(x => x.Proof || x.Done);
            if (!_completion.Reconcile(_record.Id, ownedAction, objectiveProof,
                _serverCompletion, _record.DeletedByUser, out string evidence)) return;
            RewardConfirmed = true;
            Evidence = evidence;
            foreach (Step step in _steps) step.State = StepState.Completed;
            _say($"Mission {_record.Id.Instance}: objective proof accepted; evidence={evidence}.");
            Identity releasedFindPerson = _steps.Where(x => x.Action is FindPersonAction)
                .Select(x => x.Target).FirstOrDefault();
            if (releasedFindPerson != Identity.None)
                _say($"FindPerson completion release: exact target {releasedFindPerson} is no longer held and may enter normal combat/corpse processing.");
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
            step.State = StepState.ActionSent;
            step.ActionStarted = step.LastSent = DateTime.UtcNow; // Combat owns the action; proof is still observed here.
        }

        public bool Tick(Room room, DungeonLayout layout, Func<Vector3, bool> navigate,
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
                if (step.Done) { step.State = StepState.Completed; continue; }
                if (step.Action is KillPersonAction) continue; // The guarded combat path owns this step.
                if (step.Proof)
                {
                    step.State = StepState.ProofObserved;
                    continue; // Await the bound quest's server acknowledgement.
                }
                if (step.Sent)
                {
                    step.State = StepState.AwaitingProof;
                    if (DateTime.UtcNow - step.ActionStarted > AcknowledgementTimeout)
                    {
                        step.State = StepState.Blocked;
                        Failure = $"Objective action {step.Action.Type} for {step.Target} was sent, but supported proof/acknowledgement " +
                            $"did not arrive within {AcknowledgementTimeout.TotalSeconds:0} s; disappearance alone is not accepted. " +
                            "/rkm complete remains available only after checking the mission in game.";
                        return true;
                    }
                }
                Dynel target = DynelManager.GetDynel(step.Target);
                if (target == null || !layout.ContainsDynel(room.Instance, target)) continue;
                DateTime now = DateTime.UtcNow;
                float distance = target.DistanceFrom(DynelManager.LocalPlayer);
                if (step.ApproachStarted == DateTime.MinValue)
                {
                    step.ApproachStarted = now;
                    step.BestDistance = distance;
                }
                else if (distance + 0.25f < step.BestDistance)
                {
                    step.BestDistance = distance;
                    step.ApproachStarted = now; // Meaningful movement is approach progress, not a stale timeout.
                }
                step.State = StepState.Approaching;
                float interactionRange = step.Pickup || step.Action is UseItemOnItemAction
                    ? ObjectiveUseRange : 4f;
                Vector3 player = DynelManager.LocalPlayer.Position;
                bool rawRay = LocalRoutePlanner.ClearInteractionSegment(player, target.Position);
                bool elevatedRay = LocalRoutePlanner.ClearInteractionSegment(
                    player + Vector3.Up * InteractionRayPlayerHeight,
                    target.Position + Vector3.Up * InteractionRayTargetHeight);
                bool exactMappedRoom = _rooms.TryGetValue(step.Target, out int mappedRoom) &&
                    mappedRoom == room.Instance;
                bool closeOverride = exactMappedRoom && distance <= CloseRangeOverride;
                bool inRange = distance <= interactionRange;
                bool rayAllowsInteraction = elevatedRay || closeOverride;
                if (!inRange || !rayAllowsInteraction)
                {
                    string blocker = !inRange ? "outside interaction range" : "elevated interaction ray blocked";
                    LogGate(step, room, target, distance, interactionRange, rawRay, elevatedRay,
                        closeOverride, blocker, loot);
                    if (now - step.ApproachStarted > ApproachTimeout)
                    {
                        step.State = StepState.Blocked;
                        Failure = $"Objective action was never sent for {step.Action.Type}:{step.Target}: {blocker} for " +
                            $"{ApproachTimeout.TotalSeconds:0} s without meaningful approach progress " +
                            $"(room={room.Instance}, distance={distance:0.0}m, range={interactionRange:0.0}m, " +
                            $"raw ray={rawRay}, elevated ray={elevatedRay}, close override={closeOverride}).";
                        return true;
                    }
                    if (!navigate(target.Position))
                    {
                        step.State = StepState.Blocked;
                        Failure = $"No complete mapped route to objective {step.Target} in room {room.Instance}.";
                    }
                    return true;
                }
                step.State = StepState.ReadyToInteract;
                if (step.ReadyStarted == DateTime.MinValue) step.ReadyStarted = now;
                MovementArbiter.Current.Halt(MovementOwner.Objective);
                if (Spell.HasPendingCast || Item.HasPendingUse || loot.IsProcessingMissionLoot)
                {
                    string blocker = Spell.HasPendingCast ? "pending spell" :
                        Item.HasPendingUse ? "pending item use" : "ManagerLoot mission processing";
                    LogGate(step, room, target, distance, interactionRange, rawRay, elevatedRay,
                        closeOverride, blocker, loot);
                    if (!step.Sent && now - step.ReadyStarted > ApproachTimeout)
                    {
                        step.State = StepState.Blocked;
                        Failure = $"Objective action was never sent for {step.Action.Type}:{step.Target}: {blocker} " +
                            $"persisted for {ApproachTimeout.TotalSeconds:0} s after interaction readiness.";
                    }
                    return true;
                }
                if (now - step.LastSent < TimeSpan.FromSeconds(3)) return true;
                if (step.Action is UseItemOnItemAction && !step.Pickup)
                {
                    Item source = Inventory.Items.FirstOrDefault(x => x.UniqueIdentity == step.InventoryItem);
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
                    ? $", source={use.Source}, destination={use.Destination}, resolved item={step.InventoryItem}, " +
                      $"resolved world target={step.Target}, roles={step.RoleEvidence}, collect={step.Pickup}"
                    : "";
                _say($"Final objective action: {step.Action.Type}, target={step.Target}{detail}.");
            }
            step.Sent = true;
            step.State = StepState.ActionSent;
            if (step.ActionStarted == DateTime.MinValue) step.ActionStarted = DateTime.UtcNow;
            step.LastSent = DateTime.UtcNow;
        }

        private void LogGate(Step step, Room room, Dynel target, float distance,
            float interactionRange, bool rawRay, bool elevatedRay, bool closeOverride,
            string reason, ManagerLoot.ManagerLoot loot)
        {
            DateTime now = DateTime.UtcNow;
            if (step.LastGateReason == reason && now - step.LastGateLog < TimeSpan.FromSeconds(5)) return;
            step.LastGateReason = reason;
            step.LastGateLog = now;
            string source = step.Action is UseItemOnItemAction
                ? $", resolved item={step.InventoryItem}, source available=" +
                  $"{Inventory.Items.Any(x => x.UniqueIdentity == step.InventoryItem)}, " +
                  $"world target={step.Target}, roles={step.RoleEvidence}"
                : "";
            _say($"Objective gate: reason={reason}, target={step.Target}, type={step.Action.Type}, " +
                $"room={room.Instance}, same-room={MappedToRoom(room, target)}, distance={distance:0.0}m, " +
                $"range={interactionRange:0.0}m, raw ray={rawRay}, elevated ray={elevatedRay}, " +
                $"close override={closeOverride}, pending spell={Spell.HasPendingCast}, pending item={Item.HasPendingUse}, " +
                $"ManagerLoot processing={loot.IsProcessingMissionLoot}, free slots={Inventory.NumFreeSlots}{source}.");
        }

        private bool MappedToRoom(Room room, Dynel target) =>
            _rooms.TryGetValue(target.Identity, out int mappedRoom) && mappedRoom == room.Instance;

        public int? PendingRoom => _steps.Where(x => !x.Done && _rooms.ContainsKey(x.Target))
            .Select(x => (int?)_rooms[x.Target]).FirstOrDefault();

        public int? RoomFor(Identity target) => _rooms.TryGetValue(target, out int room)
            ? (int?)room : null;

        public bool ProtectsMissionItem(Identity identity) => identity != Identity.None && !RewardConfirmed &&
            _record.State != MissionProgress.CompletedAutomatically &&
            _record.State != MissionProgress.CompletedByUser && _steps.Any(step =>
                !step.Proof && !step.Done && (step.InventoryItem == identity ||
                (step.Action is FindItemAction && step.Target == identity)));
    }
}
