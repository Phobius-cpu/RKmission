using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AOSharp.Common.GameData;
using AOSharp.Core;
using AOSharp.Core.Inventory;
using Newtonsoft.Json;

namespace RKmission
{
    internal enum InventorySettlement { Moving, Ready, NeedsSpace, Blocked }

    // Only optional corpse/chest work is suppressed; objective interactions
    // continue through MissionObjective and retain their own free-slot checks.
    internal sealed class InventoryPolicy : IDisposable
    {
        private const string DestinationBagOwner = "PostMissionSettlement";
        private const int MaximumDestinationSnapshotUses = 3;
        // Exact AO template id for Yalmaha XL - 29478. Do not generalize this
        // to vehicle stats/names: those also match implants and recompilers.
        private const int YalmahaXl29478ItemId = 117322;
        private static readonly TimeSpan DestinationSnapshotResponse = TimeSpan.FromSeconds(2.5);
        private readonly HashSet<int> _consumerItemIds = new HashSet<int>();
        private readonly HashSet<string> _consumerItemModels = new HashSet<string>();
        public int MinimumFreeSlots { get; set; } = 3;
        public int AutomaticSellTriggerItems { get; set; } = 15;
        public int AutomaticLogisticsItemLimit { get; set; } = 20;
        // Explicit user overrides in RKMissionData/inventory-policy.json.
        public List<int> ProtectedItemIds { get; set; } = new List<int>();
        public List<string> ProtectedNameFragments { get; set; } = new List<string>();
        [JsonIgnore] public bool SkipOptionalLoot { get; private set; }
        private sealed class InventoryOccurrence
        {
            public Identity Slot;
            public Identity Item;
            public int LowId;
            public int HighId;
            public int QualityLevel;
            public string Name;

            public bool Matches(Item item) => item != null && Slot == item.Slot &&
                MatchesDetails(item);

            public bool MatchesDetails(Item item) => item != null &&
                Item == item.UniqueIdentity && LowId == item.LowId && HighId == item.HighId &&
                QualityLevel == item.QualityLevel &&
                string.Equals(Name, item.Name ?? string.Empty, StringComparison.Ordinal);
        }

        private List<InventoryOccurrence>? _inventoryAtDungeonEntry;
        private Identity _pendingItem = Identity.None, _pendingBag = Identity.None;
        private Identity _pendingSourceSlot = Identity.None;
        private bool _movePending;
        private DateTime _moveSent;
        private DateTime _settlementStarted;
        private int _moveAttempts;
        private int _pendingBagMatchCount;
        private int _pendingLowId, _pendingHighId, _pendingQualityLevel;
        private string _pendingName;
        private int _stagedRejects;
        private string _pendingDestination;
        private Identity _snapshotBag = Identity.None;
        private DateTime _snapshotUseSent;
        private int _snapshotUses;
        private bool _snapshotConfirmed, _snapshotEventsAttached;
        [JsonIgnore] public string? SettlementFailure { get; private set; }
        [JsonIgnore] public bool AutomaticLogisticsRequired { get; private set; }
        [JsonIgnore] public string? AutomaticLogisticsReason { get; private set; }
        [JsonIgnore] public int ObservedSellRejects { get; private set; }

        public void Initialize()
        {
            if (_snapshotEventsAttached) return;
            Inventory.ContainerOpened += OnContainerOpened;
            _snapshotEventsAttached = true;
        }

        // Handlers may register verified item IDs that they actually consume.
        // This does not turn a useful-looking name or arbitrary Use action into
        // an operational item.
        public void RegisterOperationalItemId(int itemId)
        {
            if (itemId > 0) _consumerItemIds.Add(itemId);
        }

        // Equipment lifecycle consumers register only models they have actually
        // verified or must restore. Slot compatibility alone never protects an item.
        public void RegisterOperationalItem(Item item)
        {
            if (item == null) return;
            if (item.Id > 0) _consumerItemIds.Add(item.Id);
            _consumerItemModels.Add(OperationalModel(item));
        }

        public void UnregisterOperationalItem(Item item)
        {
            if (item == null) return;
            _consumerItemModels.Remove(OperationalModel(item));
            if (item.Id > 0 && !_consumerItemModels.Any(x => x.StartsWith(item.Id + ":", StringComparison.Ordinal)))
                _consumerItemIds.Remove(item.Id);
        }

        private static string OperationalModel(Item item) =>
            $"{item.Id}:{item.LowId}:{item.HighId}:{item.QualityLevel}";

        // ManagerLoot calls this before its Keep/Reject decision, including
        // for items already in RK Junk (including legacy RKM Sell aliases).
        public bool IsOperationalItem(Item item)
        {
            if (item == null) return false;
            if (item.UniqueIdentity.Type == IdentityType.Container) return true;
            if (item.Id == YalmahaXl29478ItemId) return true;
            if (ProtectedItemIds.Contains(item.Id)) return true;
            string name = item.Name?.Trim() ?? string.Empty;
            if (ProtectedNameFragments.Any(x => !string.IsNullOrWhiteSpace(x) &&
                name.IndexOf(x.Trim(), StringComparison.OrdinalIgnoreCase) >= 0)) return true;
            return _consumerItemIds.Contains(item.Id) || _consumerItemModels.Contains(OperationalModel(item)) ||
                IsBuiltInOperationalName(name) ||
                MissionReadiness.IsRecoveryItem(item);
        }

        internal static bool IsBuiltInOperationalName(string name)
        {
            // Both RKMission's locked-door path and ManagerLoot's chest path
            // request this exact name. Other consumables require effect data
            // or a consumer registration, not a matching substring.
            return CarriedItems.IsLockpickName(name);
        }

        public void BeginMissionInventorySnapshot()
        {
            _pendingItem = _pendingBag = _pendingSourceSlot = Identity.None;
            _movePending = false;
            ResetDestinationSnapshot();
            _moveAttempts = 0;
            _stagedRejects = 0;
            _settlementStarted = DateTime.MinValue;
            SettlementFailure = null;
            AutomaticLogisticsRequired = false;
            AutomaticLogisticsReason = null;
            ObservedSellRejects = 0;
            _inventoryAtDungeonEntry = Inventory.Items
                .Where(item => item.Slot.Type == IdentityType.Inventory &&
                    item.UniqueIdentity.Type != IdentityType.Container)
                .Select(item => new InventoryOccurrence
                {
                    Slot = item.Slot,
                    Item = item.UniqueIdentity,
                    LowId = item.LowId,
                    HighId = item.HighId,
                    QualityLevel = item.QualityLevel,
                    Name = item.Name ?? string.Empty
                }).ToList();
        }

        // Reuse Manager.Loot's value decision and the AOSharp item transfer.
        // Only newly collected rejects enter a pre-existing RK Junk family bag. This
        // staging move is checked against both inventories; it is not a sale.
        public InventorySettlement TickAfterVerifiedExit(ManagerLoot.ManagerLoot loot, Action<string> say)
        {
            if (_settlementStarted == DateTime.MinValue)
                _settlementStarted = DateTime.UtcNow;
            if (DateTime.UtcNow - _settlementStarted > TimeSpan.FromMinutes(2))
            {
                SettlementFailure = "Post-exit item staging made no safe completion within two minutes.";
                return InventorySettlement.Blocked;
            }
            if (_movePending)
            {
                bool sourceStillMatches = Inventory.Items.Any(x =>
                    x.Slot == _pendingSourceSlot && PendingDetailsMatch(x));
                int bagMatchCount = Inventory.GetContainerItems(_pendingBag)
                    .Count(PendingDetailsMatch);
                if (!sourceStillMatches && bagMatchCount > _pendingBagMatchCount)
                {
                    Identity verifiedItem = _pendingItem;
                    Identity verifiedSlot = _pendingSourceSlot;
                    string verifiedDestination = _pendingDestination;
                    _pendingItem = _pendingBag = _pendingSourceSlot = Identity.None;
                    _movePending = false;
                    _moveAttempts = 0;
                    if (ManagerLoot.ManagedBagFamily.Matches(verifiedDestination, ManagerLoot.ManagedBagFamily.Sell))
                        _stagedRejects++;
                    say($"Verified post-mission inventory move for '{_pendingName}', id={_pendingLowId}/{_pendingHighId}, " +
                        $"QL={_pendingQualityLevel}, item={verifiedItem}, source slot={verifiedSlot}, " +
                        $"into '{verifiedDestination}' by source-slot removal and destination-count increase.");
                    _pendingDestination = null;
                }
                else if (DateTime.UtcNow - _moveSent < TimeSpan.FromSeconds(5))
                    return InventorySettlement.Moving;
                else if (!sourceStillMatches || _moveAttempts >= 2)
                {
                    SettlementFailure = $"Move to RK Junk was not verified for '{_pendingName}', " +
                        $"id={_pendingLowId}/{_pendingHighId}, QL={_pendingQualityLevel}, item={_pendingItem}, " +
                        $"source slot={_pendingSourceSlot}: sourcePresent={sourceStillMatches}, " +
                        $"destinationMatches={bagMatchCount} (before={_pendingBagMatchCount}).";
                    return InventorySettlement.Blocked;
                }
            }
            if (_inventoryAtDungeonEntry != null)
            {
                foreach (Item item in Inventory.Items.Where(x =>
                    x.Slot.Type == IdentityType.Inventory &&
                    x.UniqueIdentity.Type != IdentityType.Container &&
                    IsNewMissionOccurrence(x) &&
                    (!_movePending || (x.Slot == _pendingSourceSlot && PendingDetailsMatch(x)))))
                {
                    bool newlyAcquired = IsNewMissionOccurrence(item);
                    ManagerLoot.ItemClassification classification = loot.Classify(item, newlyAcquired);
                    string requested = classification == ManagerLoot.ItemClassification.Keep
                        ? loot.KeepBagName(item) :
                        CanAutoSell(item, loot, false, newlyAcquired, out _)
                            ? ManagerLoot.ManagedBagFamily.Sell : null;
                    if (string.IsNullOrWhiteSpace(requested)) continue;
                    var bag = Inventory.Backpacks
                        .Where(x => ManagerLoot.ManagedBagFamily.Matches(x.Name, requested) &&
                            Inventory.GetContainerItems(x.Identity).Count < 21)
                        .OrderBy(x => ManagerLoot.ManagedBagFamily.Order(x.Name, requested))
                        .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
                    if (bag == null) break;
                    if (!EnsureDestinationSnapshot(bag, say, out bool snapshotBlocked))
                        return snapshotBlocked ? InventorySettlement.Blocked : InventorySettlement.Moving;
                    if (!_movePending || item.Slot != _pendingSourceSlot ||
                        !PendingDetailsMatch(item))
                    {
                        _pendingItem = item.UniqueIdentity;
                        _moveAttempts = 0;
                    }
                    _movePending = true;
                    _pendingBag = bag.Identity;
                    _pendingSourceSlot = item.Slot;
                    _pendingDestination = bag.Name;
                    _pendingLowId = item.LowId;
                    _pendingHighId = item.HighId;
                    _pendingQualityLevel = item.QualityLevel;
                    _pendingName = item.Name ?? string.Empty;
                    _pendingBagMatchCount = Inventory.GetContainerItems(bag.Identity)
                        .Count(PendingDetailsMatch);
                    if (Item.HasPendingUse || Spell.HasPendingCast)
                        return InventorySettlement.Moving;
                    say($"Post-mission staging candidate: '{_pendingName}', id={_pendingLowId}/{_pendingHighId}, " +
                        $"QL={_pendingQualityLevel}, item={_pendingItem}, source slot={_pendingSourceSlot}, " +
                        $"ManagerLoot={classification}, destination='{_pendingDestination}', " +
                        $"destination matches before move={_pendingBagMatchCount}.");
                    item.MoveToContainer(bag);
                    _moveSent = DateTime.UtcNow;
                    _moveAttempts++;
                    return InventorySettlement.Moving;
                }
            }
            if (_movePending)
            {
                SettlementFailure = $"No available destination bag to verify/retry item {_pendingItem}.";
                return InventorySettlement.Blocked;
            }
            if (_stagedRejects > 0)
                say($"Verified {_stagedRejects} newly collected reject item(s) staged in RK Junk bag(s); no sale was performed.");
            bool enough = ClassifyAfterVerifiedExit(loot, say);
            ManagedBagOpenOwnership.ReleaseAll(DestinationBagOwner);
            return enough ? InventorySettlement.Ready : InventorySettlement.NeedsSpace;
        }

        private bool EnsureDestinationSnapshot(Backpack bag, Action<string> say, out bool blocked)
        {
            blocked = false;
            if (_snapshotBag != bag.Identity)
            {
                _snapshotBag = bag.Identity;
                _snapshotConfirmed = false;
                _snapshotUses = 0;
                _snapshotUseSent = DateTime.MinValue;
            }
            int observed = Inventory.GetContainerItems(bag.Identity).Count(item => item != null);
            if (_snapshotConfirmed || observed > 0)
            {
                if (!_snapshotConfirmed)
                    say($"Post-mission destination snapshot for '{bag.Name}' confirmed by {observed} observed item(s).");
                _snapshotConfirmed = true;
                return true;
            }
            if (Item.HasPendingUse || Spell.HasPendingCast ||
                _snapshotUseSent != DateTime.MinValue &&
                    DateTime.UtcNow - _snapshotUseSent < DestinationSnapshotResponse)
                return false;
            if (_snapshotUses >= MaximumDestinationSnapshotUses)
            {
                SettlementFailure = $"Destination bag '{bag.Name}' ({bag.Identity}) did not provide positive " +
                    $"ContainerOpened or populated-snapshot evidence after {_snapshotUses} bounded open request(s); no item move was sent.";
                blocked = true;
                return false;
            }
            Item bagItem = bag.Item ?? Inventory.Items.FirstOrDefault(item =>
                item != null && item.UniqueIdentity == bag.Identity);
            if (bagItem == null)
            {
                SettlementFailure = $"Destination bag '{bag.Name}' ({bag.Identity}) has no matching inventory item to open; no item move was sent.";
                blocked = true;
                return false;
            }
            ManagedBagOpenOwnership.RecordOpenUse(DestinationBagOwner, bag);
            bagItem.Use();
            _snapshotUses++;
            _snapshotUseSent = DateTime.UtcNow;
            say($"Post-mission destination snapshot requested for '{bag.Name}' ({bag.Identity}); " +
                $"bounded open request {_snapshotUses}/{MaximumDestinationSnapshotUses}. " +
                "An empty bag requires ContainerOpened evidence before staging.");
            return false;
        }

        private void OnContainerOpened(object sender, Container container)
        {
            if (container == null || container.Identity != _snapshotBag) return;
            _snapshotConfirmed = true;
        }

        private void ResetDestinationSnapshot()
        {
            if (_snapshotBag != Identity.None)
                ManagedBagOpenOwnership.Release(DestinationBagOwner, _snapshotBag);
            _snapshotBag = Identity.None;
            _snapshotUseSent = DateTime.MinValue;
            _snapshotUses = 0;
            _snapshotConfirmed = false;
        }

        private bool IsNewMissionOccurrence(Item item)
        {
            if (_inventoryAtDungeonEntry == null || item == null ||
                _inventoryAtDungeonEntry.Any(entry => entry.Matches(item))) return false;
            List<InventoryOccurrence> baseline = _inventoryAtDungeonEntry
                .Where(entry => entry.MatchesDetails(item)).ToList();
            List<Item> current = Inventory.Items.Where(candidate =>
                    candidate.Slot.Type == IdentityType.Inventory &&
                    candidate.UniqueIdentity.Type != IdentityType.Container &&
                    SameOccurrenceDetails(candidate, item)).ToList();
            int added = current.Count - baseline.Count;
            if (added <= 0) return false;
            // If an old equal item changed slot, the new copy cannot be identified
            // without risking a pre-mission Reject. Only treat the unmatched copies
            // as new when every baseline copy is still proven in its original slot.
            List<Item> unmatched = current.Where(candidate =>
                !baseline.Any(entry => entry.Matches(candidate))).ToList();
            return unmatched.Count == added && unmatched.Any(candidate =>
                candidate.Slot == item.Slot && SameOccurrenceDetails(candidate, item));
        }

        private static bool SameOccurrenceDetails(Item left, Item right) =>
            left != null && right != null && left.UniqueIdentity == right.UniqueIdentity &&
            left.LowId == right.LowId && left.HighId == right.HighId &&
            left.QualityLevel == right.QualityLevel &&
            string.Equals(left.Name ?? string.Empty, right.Name ?? string.Empty,
                StringComparison.Ordinal);

        private bool PendingDetailsMatch(Item item) => item != null &&
            item.UniqueIdentity == _pendingItem && item.LowId == _pendingLowId &&
            item.HighId == _pendingHighId && item.QualityLevel == _pendingQualityLevel &&
            string.Equals(item.Name ?? string.Empty, _pendingName ?? string.Empty, StringComparison.Ordinal);

        public bool CanAutoSell(Item item, ManagerLoot.ManagerLoot loot, bool inSellBag,
            bool newlyAcquiredMain, out string reason)
        {
            reason = null;
            if (item == null) { reason = "item is unavailable"; return false; }
            ManagerLoot.ItemClassification classification = loot.Classify(item, newlyAcquiredMain);
            if (classification != ManagerLoot.ItemClassification.Reject)
            { reason = $"classification is {classification}"; return false; }
            if (IsOperationalItem(item) || loot.ProtectionReason(item) != null)
            { reason = "item is mission-critical, protected, or operational"; return false; }
            if (!inSellBag && !newlyAcquiredMain)
            { reason = "pre-existing main-inventory Reject was not explicitly staged"; return false; }
            return true;
        }

        public bool IsBankEligible(Item item, ManagerLoot.ManagerLoot loot)
        {
            if (item == null || loot.Classify(item) != ManagerLoot.ItemClassification.Keep) return false;
            string requested = loot.KeepBagName(item);
            if (string.IsNullOrWhiteSpace(requested)) return true;
            // A usable rule destination wins. If it does not exist or every
            // matching bag is full, retaining the confirmed Keep in main for
            // verified bank storage is the safe fallback.
            return !Inventory.Backpacks.Any(bag =>
                ManagerLoot.ManagedBagFamily.Matches(bag.Name, requested) && bag.Items.Count < 21);
        }

        public static InventoryPolicy Load(string pluginDir, Action<string> say)
        {
            string path = Path.Combine(pluginDir, "RKMissionData", "inventory-policy.json");
            InventoryPolicy policy = new InventoryPolicy();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                if (File.Exists(path))
                    policy = JsonConvert.DeserializeObject<InventoryPolicy>(File.ReadAllText(path)) ?? policy;
                else File.WriteAllText(path, JsonConvert.SerializeObject(policy, Formatting.Indented));
            }
            catch (Exception ex) { say("Inventory policy unavailable; using defaults: " + ex.Message); }
            policy.MinimumFreeSlots = Math.Max(1, Math.Min(20, policy.MinimumFreeSlots));
            policy.AutomaticSellTriggerItems = Math.Max(1, Math.Min(21,
                policy.AutomaticSellTriggerItems));
            policy.AutomaticLogisticsItemLimit = Math.Max(1, Math.Min(20,
                policy.AutomaticLogisticsItemLimit));
            policy.ProtectedItemIds = policy.ProtectedItemIds?.Where(x => x > 0).Distinct().ToList() ??
                new List<int>();
            policy.ProtectedNameFragments = policy.ProtectedNameFragments?
                .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>();
            try { File.WriteAllText(path, JsonConvert.SerializeObject(policy, Formatting.Indented)); }
            catch (Exception ex) { say("Inventory policy loaded but normalized settings could not be saved: " + ex.Message); }
            return policy;
        }

        public void Update(Action<string> say)
        {
            bool skip = Inventory.NumFreeSlots < MinimumFreeSlots;
            if (skip == SkipOptionalLoot) return;
            SkipOptionalLoot = skip;
            say(skip
                ? $"Only {Inventory.NumFreeSlots} free inventory slots remain (minimum {MinimumFreeSlots}); optional corpse/chest loot is suspended. Mission objectives and exit continue."
                : $"Inventory recovered to {Inventory.NumFreeSlots} free slots; optional loot resumed.");
        }

        // Called after verified staging, if any. No bank/vendor action occurs.
        public bool ClassifyAfterVerifiedExit(ManagerLoot.ManagerLoot loot, Action<string> say)
        {
            var classes = Inventory.Items
                .Where(item => item.Slot.Type == IdentityType.Inventory &&
                    item.UniqueIdentity.Type != IdentityType.Container)
                .Select(item => loot.Classify(item,
                    newlyAcquiredDuringMission: IsNewMissionOccurrence(item))).ToList();
            _inventoryAtDungeonEntry = null;
            bool enoughCapacity = Inventory.NumFreeSlots >= MinimumFreeSlots;
            ObservedSellRejects = Inventory.Backpacks
                .Where(bag => ManagerLoot.ManagedBagFamily.Matches(bag.Name,
                    ManagerLoot.ManagedBagFamily.Sell))
                .SelectMany(bag => Inventory.GetContainerItems(bag.Identity))
                .Where(item => item != null)
                .Count(item => loot.Classify(item) == ManagerLoot.ItemClassification.Reject);
            bool sellPressure = ObservedSellRejects >= AutomaticSellTriggerItems;
            AutomaticLogisticsRequired = !enoughCapacity || sellPressure;
            AutomaticLogisticsReason = !enoughCapacity
                ? $"main inventory has {Inventory.NumFreeSlots} free slot(s), below the {MinimumFreeSlots}-slot safety margin"
                : sellPressure
                    ? $"RK Junk has {ObservedSellRejects} observed Reject item(s), reaching the {AutomaticSellTriggerItems}-item recycle threshold"
                    : null;
            say($"Post-exit inventory classification: protected={classes.Count(x => x == ManagerLoot.ItemClassification.Protected)}, " +
                $"keep={classes.Count(x => x == ManagerLoot.ItemClassification.Keep)}, " +
                $"reject={classes.Count(x => x == ManagerLoot.ItemClassification.Reject)}, " +
                $"unknown={classes.Count(x => x == ManagerLoot.ItemClassification.Unknown)}, " +
                $"RK Junk Reject={ObservedSellRejects}/{AutomaticSellTriggerItems}, " +
                $"free slots={Inventory.NumFreeSlots}. " +
                (enoughCapacity ? "Capacity is sufficient for the next mission." :
                    "LogisticsRequired: capacity is low; automatic terminal logistics will be queued."));
            return enoughCapacity;
        }

        public bool NeedsAutomaticLogistics(out string reason)
        {
            if (Inventory.NumFreeSlots < MinimumFreeSlots)
            {
                reason = $"main inventory has {Inventory.NumFreeSlots} free slot(s), below the " +
                    $"{MinimumFreeSlots}-slot safety margin";
                return true;
            }
            reason = AutomaticLogisticsReason;
            return AutomaticLogisticsRequired;
        }

        public void MarkAutomaticLogisticsCompleted()
        {
            AutomaticLogisticsRequired = false;
            AutomaticLogisticsReason = null;
            ObservedSellRejects = 0;
        }

        public void Dispose()
        {
            ManagedBagOpenOwnership.ReleaseAll(DestinationBagOwner);
            if (!_snapshotEventsAttached) return;
            Inventory.ContainerOpened -= OnContainerOpened;
            _snapshotEventsAttached = false;
            ResetDestinationSnapshot();
        }
    }
}
