using System;
using System.Collections.Generic;
using System.Linq;
using AOSharp.Common.GameData;
using AOSharp.Core;
using AOSharp.Core.Inventory;

namespace RKmission
{
    internal enum VehicleTransitionResult { Ready, Waiting, Unavailable, Blocked }

    // HUD1 is a managed role. Slot metadata supplies candidates; only the live
    // equipped slot plus MovementState.Fly can classify a candidate as a vehicle.
    internal sealed class VehicleLifecycle
    {
        private sealed class ItemReference
        {
            public Identity Identity;
            public int Id, LowId, HighId, QualityLevel;
            public string Name;
            public static ItemReference From(Item x) => x == null ? null : new ItemReference
            { Identity=x.UniqueIdentity, Id=x.Id, LowId=x.LowId, HighId=x.HighId,
              QualityLevel=x.QualityLevel, Name=x.Name ?? string.Empty };
            public bool Matches(Item x) => x != null && x.Id == Id && x.LowId == LowId &&
                x.HighId == HighId && x.QualityLevel == QualityLevel &&
                (Identity == Identity.None || x.UniqueIdentity == Identity) &&
                string.Equals(x.Name ?? string.Empty, Name ?? string.Empty, StringComparison.Ordinal);
            public override string ToString() =>
                $"'{Name}', id={Id}, templates={LowId}/{HighId}, QL={QualityLevel}, identity={Identity}";
        }

        private static readonly TimeSpan TransitionTimeout = TimeSpan.FromSeconds(8);
        private readonly MovementArbiter _movement;
        private readonly InventoryPolicy _inventoryPolicy;
        private readonly Hud1EquipmentProfiles _profiles;
        private readonly Action<string> _say;
        private readonly HashSet<string> _rejectedCandidates = new HashSet<string>();
        private bool _observedOriginal, _originalFlying, _ownsHud1;
        private int _characterInstance;
        private string _characterName;
        private ItemReference _verifiedVehicle, _indoorItem, _pendingTarget;
        private Hud1EquipmentProfile _profile;
        private string _pendingRole;
        private DateTime _requestedAt;
        private bool _terminalLandingActive, _terminalDismountRequested;
        private DateTime _terminalLandingStarted, _terminalGroundObservedAt;
        private Vector3 _terminalLandingStaging;
        public string Status { get; private set; } = "unobserved";

        public VehicleLifecycle(MovementArbiter movement, InventoryPolicy inventoryPolicy,
            Hud1EquipmentProfiles profiles, Action<string> say)
        { _movement = movement; _inventoryPolicy = inventoryPolicy; _profiles = profiles; _say = say; }

        public void ObserveOriginal()
        {
            SimpleChar player = DynelManager.LocalPlayer;
            if (player == null) return;
            if (_characterInstance != 0 && (_characterInstance != player.Identity.Instance ||
                !string.Equals(_characterName, player.Name ?? string.Empty, StringComparison.OrdinalIgnoreCase))) ResetJourney();
            _characterInstance = player.Identity.Instance;
            _characterName = player.Name ?? string.Empty;
            if (_observedOriginal) return;
            _profile = _profiles.ForCharacter(player.Identity.Instance, player.Name);
            Item savedVehicle = FindModel(_profile.Vehicle, false);
            Item savedIndoor = FindModel(_profile.Indoor, false);
            if (savedVehicle != null) { _verifiedVehicle = ItemReference.From(savedVehicle); _inventoryPolicy.RegisterOperationalItem(savedVehicle); }
            if (savedIndoor != null) { _indoorItem = ItemReference.From(savedIndoor); _inventoryPolicy.RegisterOperationalItem(savedIndoor); }
            _observedOriginal = true;
            _originalFlying = player.MovementState == MovementState.Fly;
            Item hud1 = Hud1Item();
            if (_originalFlying && hud1 != null)
            {
                _verifiedVehicle = ItemReference.From(hud1);
                _inventoryPolicy.RegisterOperationalItem(hud1);
                _ownsHud1 = true;
                PersistVehicle(hud1);
            }
            if (!_originalFlying && hud1 != null)
            {
                _indoorItem = ItemReference.From(hud1);
                _inventoryPolicy.RegisterOperationalItem(hud1);
                PersistIndoor(hud1);
                _say("HUD1 lifecycle observed original item = " + _indoorItem + ".");
            }
            Status = _originalFlying ? "vehicle/original-flight-preserved" : HudRole(hud1);
            _say($"Vehicle lifecycle: observed original mode={(_originalFlying ? "Fly" : "Foot")}; RKMission will restore only transitions it owns.");
        }

        public VehicleTransitionResult RequireFlight(bool routeBenefits)
        {
            ObserveOriginal();
            SimpleChar player = DynelManager.LocalPlayer;
            if (player == null) return VehicleTransitionResult.Waiting;
            Item liveHud1 = Hud1Item();
            if (_pendingRole == "candidate-cleanup")
            {
                if (_pendingTarget?.Matches(liveHud1) == true)
                { ClearPending(); Status = "vehicle/rejected-candidate-cleanup-verified"; }
                else if (DateTime.UtcNow - _requestedAt < TransitionTimeout)
                    return VehicleTransitionResult.Waiting;
                else { ClearPending(); Status = "vehicle/rejected-candidate-cleanup-unverified"; return VehicleTransitionResult.Unavailable; }
            }
            else if (_pendingRole == "candidate-cleanup-empty")
            {
                if (liveHud1 == null)
                { ClearPending(); Status = "vehicle/rejected-candidate-removal-verified"; }
                else if (DateTime.UtcNow - _requestedAt < TransitionTimeout)
                    return VehicleTransitionResult.Waiting;
                else { ClearPending(); Status = "vehicle/rejected-candidate-removal-unverified"; return VehicleTransitionResult.Unavailable; }
            }
            if (player.MovementState == MovementState.Fly)
            {
                Item hud1 = Hud1Item();
                if (_pendingRole == "vehicle" && _pendingTarget?.Matches(hud1) == true)
                {
                    _verifiedVehicle = ItemReference.From(hud1);
                    _inventoryPolicy.RegisterOperationalItem(hud1);
                    _ownsHud1 = true;
                    PersistVehicle(hud1);
                    ClearPending();
                    _say("Verified flying vehicle " + _verifiedVehicle + " from live HUD1 and MovementState.Fly.");
                }
                Status = _ownsHud1 ? "vehicle/verified-owned" : "vehicle/existing-flight-preserved";
                return VehicleTransitionResult.Ready;
            }
            if (!routeBenefits) { Status = "indoor-or-foot/route-does-not-benefit"; return VehicleTransitionResult.Unavailable; }
            if (!SafeToTransition(player, false)) { Status = GateStatus("vehicle", player, false); return VehicleTransitionResult.Blocked; }
            if (_pendingTarget != null)
            {
                if (DateTime.UtcNow - _requestedAt < TransitionTimeout)
                { Status = "vehicle/awaiting-hud1-and-fly-verification"; return VehicleTransitionResult.Waiting; }
                _rejectedCandidates.Add(CandidateKey(_pendingTarget));
                _say("Vehicle candidate " + _pendingTarget + " rejected: no Fly verification within bounded timeout.");
                ClearPending();
                Item rejected = Hud1Item();
                Item indoorCleanup = FindItem(_indoorItem, true);
                if (indoorCleanup != null && indoorCleanup.MeetsUseReqs(player))
                    return DispatchEquip(indoorCleanup, "candidate-cleanup", "Restoring displaced HUD1 after rejected vehicle candidate");
                if (rejected != null && Inventory.NumFreeSlots > 0)
                {
                    rejected.MoveToInventory();
                    _pendingRole = "candidate-cleanup-empty"; _requestedAt = DateTime.UtcNow;
                    Status = "vehicle/rejected-candidate-removal-requested";
                    return VehicleTransitionResult.Waiting;
                }
                Status = "vehicle/rejected-candidate-cleanup-blocked";
                return VehicleTransitionResult.Unavailable;
            }
            Item vehicle = FindItem(_verifiedVehicle, true);
            if (vehicle != null && _rejectedCandidates.Contains(CandidateKey(ItemReference.From(vehicle))))
                vehicle = null;
            if (vehicle == null)
            {
                List<Item> candidates = Inventory.Items.Where(IsHud1InventoryCandidate)
                    .Where(x => !_rejectedCandidates.Contains(CandidateKey(ItemReference.From(x))))
                    .OrderBy(x => x.Id).ThenBy(x => x.QualityLevel).ToList();
                _say($"HUD1 lifecycle scanning {candidates.Count} HUD1-compatible candidate(s).");
                vehicle = candidates.FirstOrDefault();
            }
            if (vehicle == null) { Status = "vehicle/no-verified-or-eligible-candidate"; return VehicleTransitionResult.Unavailable; }
            if (!vehicle.MeetsUseReqs(player))
            {
                _rejectedCandidates.Add(CandidateKey(ItemReference.From(vehicle)));
                Status = "vehicle/candidate-requirements-not-met";
                return VehicleTransitionResult.Waiting;
            }
            Item current = Hud1Item();
            if (!_ownsHud1 && !_originalFlying && current != null &&
                (_verifiedVehicle == null || !_verifiedVehicle.Matches(current)))
            {
                _indoorItem = ItemReference.From(current);
                _inventoryPolicy.RegisterOperationalItem(current);
                PersistIndoor(current);
                _say("Displaced indoor HUD1 item remembered: " + _indoorItem + ".");
            }
            return DispatchEquip(vehicle, "vehicle", "Trying vehicle candidate");
        }

        public VehicleTransitionResult RequireIndoorHud1()
        {
            ObserveOriginal();
            SimpleChar player = DynelManager.LocalPlayer;
            if (player == null) return VehicleTransitionResult.Waiting;
            if (Game.IsZoning) { Status = GateStatus("indoor", player, true); return VehicleTransitionResult.Waiting; }
            if (!SafeToTransition(player, true)) { Status = GateStatus("indoor", player, true); return VehicleTransitionResult.Blocked; }
            Item current = Hud1Item();
            if (!_ownsHud1)
            { Status = _originalFlying ? "vehicle/original-flight-preserved" : HudRole(current); return VehicleTransitionResult.Ready; }
            if (_pendingRole == "indoor")
            {
                if (_pendingTarget?.Matches(current) == true && player.MovementState != MovementState.Fly)
                {
                    ClearPending(); Status = "indoor/restore-verified";
                    _indoorItem = ItemReference.From(current);
                    _inventoryPolicy.RegisterOperationalItem(current);
                    PersistIndoor(current);
                    _say("Indoor HUD1 restore verified: " + _indoorItem + ".");
                    return VehicleTransitionResult.Ready;
                }
                if (DateTime.UtcNow - _requestedAt < TransitionTimeout) return VehicleTransitionResult.Waiting;
                ClearPending(); Status = "indoor/restore-unverified"; return VehicleTransitionResult.Unavailable;
            }
            if (_pendingRole == "empty")
            {
                if (current == null && player.MovementState != MovementState.Fly)
                { ClearPending(); Status = "indoor/empty-verified"; return VehicleTransitionResult.Ready; }
                if (DateTime.UtcNow - _requestedAt < TransitionTimeout) return VehicleTransitionResult.Waiting;
                ClearPending(); Status = "indoor/vehicle-removal-unverified"; return VehicleTransitionResult.Unavailable;
            }
            if (_verifiedVehicle == null || !_verifiedVehicle.Matches(current))
            {
                _ownsHud1 = false; ClearPending(); Status = "indoor/manual-hud1-change-preserved";
                _say("HUD1 lifecycle detected a manual slot change; stale owned restoration was abandoned.");
                return VehicleTransitionResult.Ready;
            }
            Item indoor = FindItem(_indoorItem, true);
            if (indoor != null && !indoor.MeetsUseReqs(player)) indoor = null;
            if (indoor == null)
            {
                List<Item> candidates = Inventory.Items.Where(IsHud1InventoryCandidate)
                    .Where(x => _verifiedVehicle == null || !_verifiedVehicle.Matches(x))
                    .Where(x => x.MeetsUseReqs(player))
                    .OrderBy(x => x.Id).ThenBy(x => x.QualityLevel).ToList();
                if (candidates.Count == 1)
                {
                    indoor = candidates[0];
                    _indoorItem = ItemReference.From(indoor);
                    _inventoryPolicy.RegisterOperationalItem(indoor);
                    _say("Unique usable indoor HUD1 candidate learned: " + _indoorItem + ".");
                }
                else if (candidates.Count > 1)
                {
                    _say($"Exact mission entered; {candidates.Count} usable non-vehicle HUD1 candidates remain, so RKMission will not guess: " +
                        string.Join("; ", candidates.Select(x => $"'{x.Name}', id={x.Id}, templates={x.LowId}/{x.HighId}, QL={x.QualityLevel}")) + ".");
                    return RemoveOwnedVehicle(current, "ambiguous indoor HUD1 candidates");
                }
            }
            if (indoor != null && indoor.MeetsUseReqs(player))
            {
                _say("Exact mission entered; restoring indoor HUD1 " + _indoorItem + ".");
                return DispatchEquip(indoor, "indoor", "Restoring indoor HUD1 item");
            }
            return RemoveOwnedVehicle(current, "no usable indoor HUD1 candidate exists");
        }

        public bool RestoreOriginal()
        {
            SimpleChar player = DynelManager.LocalPlayer;
            if (!_ownsHud1 || player == null || player.MovementState != MovementState.Fly) return true;
            if (_originalFlying) return true;
            if (!SafeToTransition(player, false)) { Status = GateStatus("restore", player, false); return false; }
            _movement.SetMovement(MovementOwner.OutdoorTravel, MovementAction.LeaveFly);
            Status = "restore-requested"; return false;
        }

        public void RestoreOwnedEquipmentOnStop()
        {
            SimpleChar player = DynelManager.LocalPlayer;
            Item current = Hud1Item();
            if (_originalFlying || !_ownsHud1 || player == null || _verifiedVehicle?.Matches(current) != true ||
                !SafeToTransition(player, Playfield.IsDungeon)) return;
            Item indoor = FindItem(_indoorItem, true);
            try
            {
                if (indoor != null && indoor.MeetsUseReqs(player))
                { indoor.Equip(EquipSlot.Weap_Hud1); Status = "stop/owned-indoor-restore-requested"; }
                else if (Inventory.NumFreeSlots > 0)
                { current.MoveToInventory(); Status = "stop/owned-vehicle-removal-requested"; }
            }
            catch (Exception ex) { Status = "stop/restore-dispatch-failed:" + ex.GetType().Name; }
        }

        public VehicleTransitionResult RequireFlightAfterMissionBatch() => RequireFlightAfterMissionBatch(true);
        public VehicleTransitionResult RequireFlightAfterMissionBatch(bool requireFlight)
        {
            if (!requireFlight)
            {
                SimpleChar player = DynelManager.LocalPlayer;
                if (player == null || Game.IsZoning) return VehicleTransitionResult.Waiting;
                if (player.MovementState != MovementState.Fly && !player.IsFalling &&
                    !Item.HasPendingUse && !Spell.HasPendingCast)
                { Status = "batch-departure-foot-route-verified"; return VehicleTransitionResult.Ready; }
                Status = "batch-departure-foot-route-unready"; return VehicleTransitionResult.Blocked;
            }
            return RequireFlight(true);
        }
        public void CompleteMissionBatchRemount() { }

        public VehicleTransitionResult RequireFootForTerminal(Vector3 landingStaging)
        {
            SimpleChar player = DynelManager.LocalPlayer;
            if (player == null || Game.IsZoning) return VehicleTransitionResult.Waiting;
            if (!_terminalLandingActive || Vector3.Distance(_terminalLandingStaging, landingStaging) > 0.5f)
            {
                _terminalLandingActive = true; _terminalDismountRequested = false;
                _terminalGroundObservedAt = DateTime.MinValue; _terminalLandingStarted = DateTime.UtcNow;
                _terminalLandingStaging = landingStaging; Status = "terminal-landing-staging";
            }
            if (DateTime.UtcNow - _terminalLandingStarted > TimeSpan.FromSeconds(45))
            { Status = "terminal-foot-timeout"; return VehicleTransitionResult.Unavailable; }
            Item attached = EquippedVerifiedVehicle();
            if (player.MovementState == MovementState.Fly)
            {
                _terminalGroundObservedAt = DateTime.MinValue;
                float range = Vector3.Distance(player.Position, _terminalLandingStaging);
                float vertical = Math.Abs(player.Position.Y - _terminalLandingStaging.Y);
                if (range > 1.25f || vertical > 1.25f)
                {
                    if (!SafeToTransition(player, false)) { Status = GateStatus("terminal-descent", player, false); return VehicleTransitionResult.Blocked; }
                    _movement.SetDestination(MovementOwner.OutdoorTravel, _terminalLandingStaging);
                    Status = "terminal-safe-descent"; return VehicleTransitionResult.Waiting;
                }
                _movement.Halt(MovementOwner.OutdoorTravel);
                if (Item.HasPendingUse || Spell.HasPendingCast) return VehicleTransitionResult.Waiting;
                if (!_terminalDismountRequested)
                {
                    if (attached != null && Inventory.NumFreeSlots > 0) attached.MoveToInventory();
                    else _movement.SetMovement(MovementOwner.OutdoorTravel, MovementAction.LeaveFly);
                    _terminalDismountRequested = true; Status = "terminal-dismount-requested-once";
                }
                return VehicleTransitionResult.Waiting;
            }
            _movement.Release(MovementOwner.OutdoorTravel);
            if (attached != null && !_terminalDismountRequested)
            {
                if (Inventory.NumFreeSlots <= 0) return VehicleTransitionResult.Unavailable;
                attached.MoveToInventory(); _terminalDismountRequested = true;
                Status = "terminal-vehicle-unequip-requested-once"; return VehicleTransitionResult.Waiting;
            }
            if (attached != null || player.IsFalling || Item.HasPendingUse || Spell.HasPendingCast)
            { _terminalGroundObservedAt = DateTime.MinValue; return VehicleTransitionResult.Waiting; }
            if (_terminalGroundObservedAt == DateTime.MinValue)
            { _terminalGroundObservedAt = DateTime.UtcNow; return VehicleTransitionResult.Waiting; }
            if (DateTime.UtcNow - _terminalGroundObservedAt < TimeSpan.FromMilliseconds(750)) return VehicleTransitionResult.Waiting;
            Status = "terminal-foot-verified"; return VehicleTransitionResult.Ready;
        }

        public void ResetTerminalRequirement()
        {
            _terminalLandingActive = _terminalDismountRequested = false;
            _terminalLandingStarted = _terminalGroundObservedAt = DateTime.MinValue;
            _terminalLandingStaging = Vector3.Zero;
        }

        private VehicleTransitionResult DispatchEquip(Item item, string role, string diagnostic)
        {
            if (Item.HasPendingUse || Spell.HasPendingCast)
            { Status = role + "/pending-action"; return VehicleTransitionResult.Waiting; }
            ItemReference target = ItemReference.From(item);
            try { item.Equip(EquipSlot.Weap_Hud1); }
            catch (Exception ex)
            {
                _rejectedCandidates.Add(CandidateKey(target));
                Status = role + "/equip-dispatch-failed:" + ex.GetType().Name;
                return VehicleTransitionResult.Unavailable;
            }
            _pendingTarget = target; _pendingRole = role; _requestedAt = DateTime.UtcNow;
            Status = role + "/equip-requested-awaiting-live-verification";
            _say(diagnostic + " " + target + "; Equip(HUD1) dispatched once, awaiting live slot/state proof.");
            return VehicleTransitionResult.Waiting;
        }

        private static Item Hud1Item() => Inventory.Items.FirstOrDefault(x => x.IsEquipped &&
            x.Slot.Type == IdentityType.WeaponPage && x.Slot.Instance == (int)EquipSlot.Weap_Hud1);
        private Item EquippedVerifiedVehicle()
        { Item item = Hud1Item(); return _verifiedVehicle?.Matches(item) == true ? item : null; }
        private static bool IsHud1InventoryCandidate(Item x) => x != null && !x.IsEquipped &&
            x.Slot.Type == IdentityType.Inventory && x.UniqueIdentity.Type != IdentityType.Container &&
            x.EquipSlots != null && x.EquipSlots.Contains(EquipSlot.Weap_Hud1);
        private static string CandidateKey(ItemReference x) => x == null ? "none" :
            $"{x.Identity}:{x.Id}:{x.LowId}:{x.HighId}:{x.QualityLevel}";
        private static string HudRole(Item x) => x == null ? "indoor/empty" : "indoor/equipped:" + (x.Name ?? x.Id.ToString());
        private static Item FindItem(ItemReference reference, bool inventoryOnly) => reference == null ? null :
            Inventory.Items.FirstOrDefault(x => reference.Matches(x) &&
                (!inventoryOnly || x.Slot.Type == IdentityType.Inventory && !x.IsEquipped));
        private static Item FindModel(Hud1EquipmentModel model, bool inventoryOnly) => model == null ? null :
            Inventory.Items.FirstOrDefault(x => model.Matches(x) &&
                (!inventoryOnly || x.Slot.Type == IdentityType.Inventory && !x.IsEquipped));
        private bool SafeToTransition(SimpleChar player, bool allowExactDungeon) => !Game.IsZoning &&
            (allowExactDungeon || !Playfield.IsDungeon) && !player.IsAttacking && !player.IsFalling &&
            !Item.HasPendingUse && !Spell.HasPendingCast &&
            (_movement.Owner == MovementOwner.None || _movement.Owner == MovementOwner.OutdoorTravel);
        private string GateStatus(string role, SimpleChar player, bool allowExactDungeon) =>
            $"{role}/transition-gated; owner={_movement.Owner}, zoning={Game.IsZoning}, " +
            $"dungeon={Playfield.IsDungeon}, exactDungeonAllowed={allowExactDungeon}, attacking={player?.IsAttacking == true}, " +
            $"falling={player?.IsFalling == true}, pendingItem={Item.HasPendingUse}, pendingSpell={Spell.HasPendingCast}";
        private VehicleTransitionResult RemoveOwnedVehicle(Item current, string reason)
        {
            if (current == null || Inventory.NumFreeSlots <= 0)
            { Status = "indoor/no-space-to-remove-vehicle"; return VehicleTransitionResult.Unavailable; }
            current.MoveToInventory();
            _pendingTarget = _verifiedVehicle; _pendingRole = "empty"; _requestedAt = DateTime.UtcNow;
            Status = reason.StartsWith("no usable", StringComparison.Ordinal) ?
                "indoor/no-candidate-vehicle-removal-requested" : "indoor/ambiguous-vehicle-removal-requested";
            _say("Exact mission entered; " + reason +
                ", so the owned vehicle was requested back to inventory and HUD1 will remain empty.");
            return VehicleTransitionResult.Waiting;
        }
        private void PersistVehicle(Item item)
        {
            if (_profile == null || item == null) return;
            _profile.Vehicle = Hud1EquipmentModel.From(item);
            _profiles.Save();
        }
        private void PersistIndoor(Item item)
        {
            if (_profile == null || item == null) return;
            _profile.Indoor = Hud1EquipmentModel.From(item);
            _profiles.Save();
        }
        private void ClearPending() { _pendingTarget = null; _pendingRole = null; _requestedAt = DateTime.MinValue; }

        public void ResetJourney()
        {
            if (_indoorItem != null)
            {
                Item item = FindItem(_indoorItem, false);
                if (item != null) _inventoryPolicy.UnregisterOperationalItem(item);
            }
            _observedOriginal = _originalFlying = _ownsHud1 = false;
            _characterInstance = 0; _characterName = null; _verifiedVehicle = _indoorItem = null; _profile = null;
            _rejectedCandidates.Clear(); ClearPending(); ResetTerminalRequirement(); Status = "unobserved";
        }
    }
}
