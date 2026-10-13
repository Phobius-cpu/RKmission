using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using AOSharp.Common.GameData;
using AOSharp.Common.GameData.UI;
using AOSharp.Core;
using AOSharp.Core.Inventory;
using AOSharp.Core.UI;
using Newtonsoft.Json;
using SmokeLounge.AOtomation.Messaging.GameData;
using SmokeLounge.AOtomation.Messaging.Messages;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;
using MissionIdentity = AOSharp.Common.GameData.Identity;

namespace ManagerLoot
{
    public enum ItemClassification { Protected, Keep, Reject, Unknown }

    // Hosted by RKmission's single AO# entry point.
    public class ManagerLoot
    {
        private const string PluginName = "ManagerLoot";
        private string _embeddedPluginDirectory;
        private string UiDirectory => _embeddedPluginDirectory;
        private MaliMissionRoller2.MainWindow? _rollerHost;
        private Func<bool>? _showRoller;
        private View? _managerView;

        public void RunEmbedded(string pluginDirectory, MaliMissionRoller2.MainWindow rollerHost, Func<bool> showRoller)
        {
            _embeddedPluginDirectory = pluginDirectory;
            _rollerHost = rollerHost;
            _showRoller = showRoller;
            _rollerHost.WindowReady += OnHostWindowReady;
            _rollerHost.EnsureManagerLootView = MainUI;
            Run();
        }

        private void OnHostWindowReady()
        {
            settingsWindow = null!;
            _managerView = null;
        }

        public void ShowSettingsTab()
        {
            if (_rollerHost == null) { MainUI(); return; }
            if (_rollerHost.Window?.IsValid != true)
                _showRoller?.Invoke();
            if (_rollerHost.Window?.IsValid == true)
            {
                if (_managerView == null) MainUI();
                _rollerHost.ShowManagerLootTab();
            }
        }

        private bool FindSettingsView<T>(string name, out T view) where T : View
        {
            if (_rollerHost != null)
            {
                if (_managerView != null) return _managerView.FindChild(name, out view);
                view = null!;
                return false;
            }
            if (settingsWindow != null) return settingsWindow.FindView(name, out view);
            view = null!;
            return false;
        }
        private readonly string Version_Number = "2.1.6";

        protected Settings _settings;
        private static readonly List<Settings> _settingsToSave = new List<Settings>();

        private Window settingsWindow;
        private Window _infoWindow;

        private static string EnableString;

        private static List<Rule> Rules;

        private readonly Dictionary<int, double> openedContainers = new Dictionary<int, double>();
        private readonly HashSet<MissionIdentity> _unreachableMissionLoot = new HashSet<MissionIdentity>();
        private readonly HashSet<MissionIdentity> _seenMissionLoot = new HashSet<MissionIdentity>();
        private readonly HashSet<MissionIdentity> _openedMissionLoot = new HashSet<MissionIdentity>();
        private readonly HashSet<MissionIdentity> _finishedMissionLoot = new HashSet<MissionIdentity>();
        private readonly Dictionary<MissionIdentity, long> _missionLootOccurrences =
            new Dictionary<MissionIdentity, long>();
        private readonly Dictionary<MissionIdentity, long> _openedMissionLootOccurrences =
            new Dictionary<MissionIdentity, long>();
        private readonly Dictionary<MissionIdentity, long> _finishedMissionLootOccurrences =
            new Dictionary<MissionIdentity, long>();
        private long _nextMissionLootOccurrence;
        private enum MissionSourceState
        {
            Discovered, PendingApproach, DeferredApproach, Opening, Lockpicking, Looting, Completed,
            SkippedUnreachable, SkippedInsufficientSkill, SkippedNoLockpick,
            SkippedRepeatedFailure, SkippedNoCapacity, CriticalBlocked
        }
        private readonly Dictionary<MissionIdentity, MissionSourceState> _missionSources =
            new Dictionary<MissionIdentity, MissionSourceState>();
        private readonly Dictionary<MissionIdentity, string> _missionSkipReasons =
            new Dictionary<MissionIdentity, string>();
        private const int MaxOrdinaryApproachAttempts = 2;
        private sealed class DeferredApproach
        {
            public int Attempts;
            public int RetryAfterTraversal;
        }
        private readonly Dictionary<MissionIdentity, DeferredApproach> _deferredApproaches =
            new Dictionary<MissionIdentity, DeferredApproach>();
        private int _missionRoomTraversals;
        public int MissionLootProgress { get; private set; }
        private int _observedContainerCount;
        private void SetMissionSource(MissionIdentity identity, MissionSourceState state)
        {
            if (MissionRoomId < 0 && !_missionSources.ContainsKey(identity)) return;
            if (_missionSources.TryGetValue(identity, out MissionSourceState old) && old == state) return;
            _missionSources[identity] = state;
            MissionLootProgress++;
        }
        private static bool Settled(MissionSourceState state) =>
            state == MissionSourceState.Completed || state == MissionSourceState.SkippedUnreachable ||
            state == MissionSourceState.SkippedInsufficientSkill || state == MissionSourceState.SkippedNoLockpick ||
            state == MissionSourceState.SkippedRepeatedFailure || state == MissionSourceState.SkippedNoCapacity;
        private readonly HashSet<MissionIdentity> _objectiveLootItems = new HashSet<MissionIdentity>();
        private const int MaxBackgroundBagMoveAttempts = 3;
        private const double BackgroundBagMoveConfirmationSeconds = 1.5;
        private sealed class BackgroundBagMove
        {
            public MissionIdentity SourceSlot;
            public MissionIdentity BagIdentity;
            public string ItemName;
            public string BagName;
            public int Id;
            public int LowId;
            public int HighId;
            public int QualityLevel;
            public int DestinationCountBefore;
            public int Attempts;
            public double SentAt;
        }
        private BackgroundBagMove _backgroundBagMove;
        private readonly HashSet<MissionIdentity> _deferredBackgroundBagSlots =
            new HashSet<MissionIdentity>();
        private sealed class MissionItemReservation
        {
            public MissionIdentity Owner;
            public MissionIdentity Item;
            public int LowId, HighId, QualityLevel;
            public string Name, Context;

            public bool Matches(Item item) => item != null && Item == item.UniqueIdentity &&
                LowId == item.LowId && HighId == item.HighId && QualityLevel == item.QualityLevel &&
                string.Equals(Name, item.Name, StringComparison.Ordinal);
        }
        private readonly List<MissionItemReservation> _missionItemReservations =
            new List<MissionItemReservation>();
        private readonly HashSet<MissionIdentity> _selectedItems = new HashSet<MissionIdentity>();
        private const double LootWindowDwellSeconds = 2.0;
        private const double ListedMoveConfirmationSeconds = 2.0;
        private const int MaxListedMoveAttempts = 2;
        private const int MaxKeepPlacementAttempts = 2;
        private const double KeepPlacementConfirmationSeconds = 2.0;
        private const int MaxManagedBagLoadAttempts = 3;
        private const double ManagedBagResponseSeconds = 2.5;
        private sealed class ManagedBagSnapshot
        {
            public int Attempts;
            public double SentAt;
            public bool Confirmed, Exhausted;
        }
        private readonly Dictionary<MissionIdentity, ManagedBagSnapshot> _managedBagSnapshots =
            new Dictionary<MissionIdentity, ManagedBagSnapshot>();
        private MissionIdentity _waitingForManagedBag = MissionIdentity.None;
        private MissionIdentity _keepPlacementBag = MissionIdentity.None;
        private bool _keepPlacementBagOpened;
        private sealed class ListedItemTransfer
        {
            public MissionIdentity Identity;
            public MissionIdentity SourceSlot;
            public MissionIdentity AcquiredSlot;
            public MissionIdentity KeepBag;
            public int Id, LowId, HighId, QualityLevel, InventoryCountBefore, Attempts;
            public int KeepBagCountBefore, KeepPlacementAttempts;
            public int KeepBagOpenAttempts;
            public string Name;
            public string KeepBagName;
            public Rule Rule;
            public bool Objective, Verified, NonActionable, AcquisitionRecorded, KeepPlacementPending;
            public double SentAt, KeepPlacementSentAt, KeepBagOpenSentAt;
            public readonly HashSet<MissionIdentity> ExistingInventorySlots = new HashSet<MissionIdentity>();
        }
        private sealed class SourceAcquisition
        {
            public MissionIdentity Source;
            public long Occurrence;
            public double OpenedAt;
            public readonly List<ListedItemTransfer> Items = new List<ListedItemTransfer>();
            public int Current;
            public bool DwellLogged, CapacityLogged, SkippedNoCapacity;
        }
        private SourceAcquisition _sourceAcquisition;
        private readonly List<ListedItemTransfer> _verifiedSelectedDetails = new List<ListedItemTransfer>();
        private readonly List<ListedItemTransfer> _verifiedNormalizedKeeps = new List<ListedItemTransfer>();
        private const int MaxMissionLockpickAttempts = 3;
        private const double LockpickRetryDelaySeconds = 1;
        private readonly Dictionary<MissionIdentity, LockpickAttempt> _missionLockpicks = new Dictionary<MissionIdentity, LockpickAttempt>();
        private enum LockpickOutcome { None, Success, NotLocked, NoLockPick, InsufficientSkill, TemporaryFailure, RepeatedFailure }
        private sealed class LockpickAttempt
        {
            public int Attempts;
            public double FirstAttempt;
            public double LastAttempt;
            public LockpickOutcome Outcome;
            public bool CriticalBlocked;
        }
        private readonly Dictionary<MissionIdentity, int> _missionLootRooms = new Dictionary<MissionIdentity, int>();
        private MissionIdentity _pendingMissionLoot = MissionIdentity.None;
        private long _pendingMissionLootOccurrence;
        // RKMission limits the original loot state machine to the room being cleared.
        public int MissionRoomId { get; private set; } = -1;
        public Func<Dynel, int, bool> MissionRoomContains { get; set; }
        public Func<int, IEnumerable<Dynel>> MissionRoomDynels { get; set; }
        public Func<Dynel, bool> MissionLootAllowed { get; set; }
        public Func<Dynel, bool> MissionInteractionReady { get; set; }
        // Independent of combat/phase permissions: an objective is final work,
        // even if it entered the discovery ledger before its metadata arrived.
        public Func<MissionIdentity, bool> MissionLootReserved { get; set; }
        public Func<Item, bool> MissionItemProtected { get; set; }
        public Func<MissionIdentity, bool> MissionReservationActive { get; set; }
        public MissionIdentity ActiveMissionOwner { get; set; } = MissionIdentity.None;
        public Func<Item, bool> OperationalItemProtected { get; set; }
        public MissionIdentity MissionObjectiveContainer { get; set; } = MissionIdentity.None;
        // RKMission temporarily owns the stationary recovery window. Preserve
        // original settings/process state and resume it after preparation.
        public bool MissionActionsPaused { get; set; }
        private bool _enabledForMission;

        public void BeginMissionRoom(int roomId)
        {
            if (_settings == null) return;
            MissionRoomId = roomId;
            _settings["Chests"] = true;
            if (!_settings["Enable"].AsBool())
            {
                Helper_Enable();
                _enabledForMission = true;
            }
        }

        public void ObserveMissionRoomTraversal()
        {
            _missionRoomTraversals++;
        }

        public void EndMissionRoom(bool restoreUserState = true)
        {
            MissionRoomId = -1;
            _pendingMissionLoot = MissionIdentity.None; // Explicitly release ownership on stop/exit.
            _pendingMissionLootOccurrence = 0;
            _keepPlacementBag = MissionIdentity.None;
            _keepPlacementBagOpened = false;
            if (restoreUserState && _enabledForMission && _settings != null && _settings["Enable"].AsBool())
                Helper_Enable();
            if (restoreUserState) _enabledForMission = false;
        }

        public void ResetMissionLootSkips()
        {
            // Selected-item ownership can outlive a mission because quantity rules may
            // already have been consumed. Only purge identities that can alias every
            // ordinary AO item; exact-detail selections remain intentionally available.
            _selectedItems.RemoveWhere(identity => !HasUsableIdentity(identity));
            _unreachableMissionLoot.Clear();
            _seenMissionLoot.Clear();
            _openedMissionLoot.Clear();
            _finishedMissionLoot.Clear();
            _missionLootOccurrences.Clear();
            _openedMissionLootOccurrences.Clear();
            _finishedMissionLootOccurrences.Clear();
            _nextMissionLootOccurrence = 0;
            _missionSources.Clear();
            _missionSkipReasons.Clear();
            _deferredApproaches.Clear();
            _missionRoomTraversals = 0;
            MissionLootProgress = 0;
            _observedContainerCount = 0;
            _objectiveLootItems.Clear();
            _missionLockpicks.Clear();
            _missionLootRooms.Clear();
            _pendingMissionLoot = MissionIdentity.None;
            _pendingMissionLootOccurrence = 0;
            _keepPlacementBag = MissionIdentity.None;
            _keepPlacementBagOpened = false;
            openedContainers.Clear();
            MissionObjectiveContainer = MissionIdentity.None;
            CurrentCorpse = null;
            CorpseContainer = null;
            _sourceAcquisition = null;
            CurrentProcess = ProcessState.Load_Backpacks;
        }
        private bool ReservedMissionLoot(MissionIdentity identity) => MissionLootReserved?.Invoke(identity) == true;
        public bool IsMissionCriticalLoot(MissionIdentity identity) =>
            ReservedMissionLoot(identity) || MissionObjectiveContainer == identity;
        public bool IgnoreOrdinaryMissionLoot { get; set; }
        public int SkippedMissionLootCount => IgnoreOrdinaryMissionLoot ? 0 : _missionSources.Count(x =>
            Settled(x.Value) && x.Value != MissionSourceState.Completed && !IsMissionCriticalLoot(x.Key));
        public int UnfinishedMissionLootCount => IgnoreOrdinaryMissionLoot ? 0 : _missionSources.Count(x =>
            !Settled(x.Value) && !IsMissionCriticalLoot(x.Key));
        public int ReservedPendingMissionLootCount => _missionSources.Count(x =>
            !Settled(x.Value) && ReservedMissionLoot(x.Key));
        public int CriticalBlockedMissionLootCount => _missionSources.Count(x =>
            x.Value == MissionSourceState.CriticalBlocked);
        public bool DeferredMissionLootWaitingForTraversal => _deferredApproaches.Any(x =>
            !IsMissionCriticalLoot(x.Key) &&
            x.Value.Attempts < MaxOrdinaryApproachAttempts &&
            _missionRoomTraversals < x.Value.RetryAfterTraversal);
        public bool HasReadyDeferredMissionLootInRoom(int roomId) => _deferredApproaches.Any(x =>
            !IsMissionCriticalLoot(x.Key) &&
            x.Value.Attempts < MaxOrdinaryApproachAttempts &&
            _missionRoomTraversals >= x.Value.RetryAfterTraversal &&
            _missionLootRooms.TryGetValue(x.Key, out int sourceRoom) && sourceRoom == roomId &&
            _missionSources.TryGetValue(x.Key, out MissionSourceState state) &&
            state == MissionSourceState.DeferredApproach &&
            !_finishedMissionLoot.Contains(x.Key) && !_unreachableMissionLoot.Contains(x.Key));
        public bool HasPendingMissionCorpse(int roomId) => !IgnoreOrdinaryMissionLoot &&
            (MissionRoomDynels?.Invoke(roomId) ?? DynelManager.AllDynels).Any(x =>
                x.Identity.Type == IdentityType.Corpse && IsInMissionRoom(x, roomId) &&
                !_finishedMissionLoot.Contains(x.Identity) && !_unreachableMissionLoot.Contains(x.Identity) &&
                (MissionLootAllowed?.Invoke(x) ?? true));
        public string MissionLootBlockers => $"ordinary skipped={SkippedMissionLootCount}, ordinary unfinished={UnfinishedMissionLootCount}, " +
            $"reserved objective entries={ReservedPendingMissionLootCount}, process={CurrentProcess}, pending={_pendingMissionLoot}; " +
            string.Join("; ", _missionSources.Where(x => !Settled(x.Value)).Take(8).Select(x =>
                $"{x.Key} state={x.Value} room={(_missionLootRooms.TryGetValue(x.Key, out int room) ? room.ToString() : "unknown")} " +
                (DynelManager.GetDynel(x.Key) == null ? "not currently visible" : "still visible")));
        public IEnumerable<MissionIdentity> MissionObjectiveItems => _objectiveLootItems;
        internal static bool UsableMissionItemIdentity(MissionIdentity identity) =>
            identity != MissionIdentity.None && identity.Type != IdentityType.None && identity.Instance != 0;
        public void BeginMissionReservations(MissionIdentity owner)
        {
            if (owner != MissionIdentity.None)
                _missionItemReservations.RemoveAll(x => x.Owner == owner);
        }
        public void ReleaseMissionReservations(MissionIdentity owner)
        {
            if (owner != MissionIdentity.None)
                _missionItemReservations.RemoveAll(x => x.Owner == owner);
        }
        public void ProtectPendingHandIn(MissionIdentity owner, Item item) =>
            ReserveMissionItem(owner, item, "pending Return Item hand-in");
        public void RememberMissionCriticalInventory(MissionIdentity owner)
        {
            foreach (Item item in Inventory.Items.Where(i => i.Slot.Type == IdentityType.Inventory))
                if (MissionItemProtected?.Invoke(item) ?? false)
                    ReserveMissionItem(owner, item, "active objective inventory item");
        }
        private void ReserveMissionItem(MissionIdentity owner, Item item, string context)
        {
            if (owner == MissionIdentity.None || item == null || !UsableMissionItemIdentity(item.UniqueIdentity))
                return;
            _missionItemReservations.RemoveAll(x => x.Owner == owner && x.Item == item.UniqueIdentity);
            _missionItemReservations.Add(new MissionItemReservation
            {
                Owner = owner,
                Item = item.UniqueIdentity,
                LowId = item.LowId,
                HighId = item.HighId,
                QualityLevel = item.QualityLevel,
                Name = item.Name,
                Context = context
            });
        }
        private MissionItemReservation ActiveMissionReservation(Item item)
        {
            _missionItemReservations.RemoveAll(x =>
                !(MissionReservationActive?.Invoke(x.Owner) ?? false));
            return _missionItemReservations.FirstOrDefault(x => x.Matches(item));
        }
        private bool ProtectedMissionItem(Item item) =>
            item.UniqueIdentity.Type == IdentityType.MissionKey ||
            ActiveMissionReservation(item) != null ||
            (MissionItemProtected?.Invoke(item) ?? false);

        internal static bool SameProtectedBagItem(Item item, Item stored)
        {
            if (item == null || stored == null) return false;
            return HasUsableIdentity(item.UniqueIdentity)
                ? stored.UniqueIdentity == item.UniqueIdentity
                : ReferenceEquals(stored, item);
        }

        private static bool ProtectedBagContains(Item item) => Inventory.Backpacks.Any(bag =>
            ManagedBagFamily.IsProtected(bag.Name) &&
            Inventory.GetContainerItems(bag.Identity).Any(stored => SameProtectedBagItem(item, stored)));

        internal string ProtectionReason(Item item)
        {
            if (item == null) return null;
            if (item.UniqueIdentity.Type == IdentityType.MissionKey) return "mission key";
            MissionItemReservation reservation = ActiveMissionReservation(item);
            if (reservation != null)
                return $"reserved mission item (mission {reservation.Owner.Instance}, {reservation.Context})";
            if (MissionItemProtected?.Invoke(item) ?? false)
                return ActiveMissionOwner != MissionIdentity.None
                    ? $"mission objective (mission {ActiveMissionOwner.Instance})"
                    : "mission objective";
            if (OperationalItemProtected?.Invoke(item) ?? false) return "operational item";
            if (ProtectedBagContains(item))
                return "RK Loot/legacy RKM Keep bag";
            return null;
        }

        // Classification is the only rule decision exposed to RKMission. A new
        // unselected inventory item has no permanent value solely because the
        // mission added it. No classification performs disposal.
        public ItemClassification Classify(Item item, bool newlyAcquiredDuringMission = false)
        {
            if (item == null) return ItemClassification.Unknown;
            if (ProtectionReason(item) != null)
                return ItemClassification.Protected;
            if (HasUsableIdentity(item.UniqueIdentity) && _selectedItems.Contains(item.UniqueIdentity))
                return ItemClassification.Keep;
            if (!HasUsableIdentity(item.UniqueIdentity) &&
                _verifiedNormalizedKeeps.Any(selected => item.Slot == selected.SourceSlot &&
                    SameItemDetails(item, selected))) return ItemClassification.Keep;
            if (!HasUsableIdentity(item.UniqueIdentity) &&
                _verifiedSelectedDetails.Any(selected => SameItemDetails(item, selected))) return ItemClassification.Keep;
            if (_settings == null || Rules == null) return ItemClassification.Unknown;
            bool listed = GetMatchingRule(item) != null;
            if (newlyAcquiredDuringMission) return listed ? ItemClassification.Keep : ItemClassification.Reject;
            return (_settings["Reverse"].AsBool() ? !listed : listed)
                ? ItemClassification.Keep : ItemClassification.Reject;
        }

        // RKMission owns post-mission movement, but ManagerLoot remains the
        // authority for the selected rule and its requested destination.
        internal static string ResolveKeepDestination(Rule rule) =>
            string.IsNullOrWhiteSpace(rule?.BagName) ? ManagedBagFamily.Keep : rule.BagName.Trim();

        public string KeepBagName(Item item)
        {
            Rule rule = GetMatchingRule(item);
            return rule == null ? null : ResolveKeepDestination(rule);
        }
        public bool HasUnprocessedMissionLoot(int roomId, Func<Dynel, bool> include = null) =>
            !IgnoreOrdinaryMissionLoot && (MissionRoomDynels?.Invoke(roomId) ?? DynelManager.AllDynels).Any(x =>
                (x.Identity.Type == IdentityType.Corpse || x.Identity.Type == IdentityType.Container) &&
                IsInMissionRoom(x, roomId) && !_finishedMissionLoot.Contains(x.Identity) &&
                !_unreachableMissionLoot.Contains(x.Identity) &&
                (include == null || include(x)));

        public long RegisterOutstandingMissionLoot(MissionIdentity identity, int roomId)
        {
            if (identity == MissionIdentity.None || MissionRoomId < 0) return 0;
            bool completedOccurrence = _missionLootRooms.TryGetValue(identity, out int previousRoom) &&
                previousRoom == roomId && _missionSources.TryGetValue(identity, out MissionSourceState state) &&
                state == MissionSourceState.Completed;
            TrackMissionLoot(identity, roomId, completedOccurrence);
            return _missionLootOccurrences[identity];
        }

        // A post-combat corpse may release room ownership only with both exact
        // ContainerOpened and exact processing-finished evidence.  Generic
        // "settled" includes policy skips and is deliberately not success.
        public bool IsMissionLootSuccessfullyProcessed(MissionIdentity identity, int roomId, long occurrence) =>
            occurrence != 0 &&
            _missionLootRooms.TryGetValue(identity, out int observedRoom) && observedRoom == roomId &&
            _missionLootOccurrences.TryGetValue(identity, out long currentOccurrence) && currentOccurrence == occurrence &&
            _openedMissionLoot.Contains(identity) && _finishedMissionLoot.Contains(identity) &&
            _openedMissionLootOccurrences.TryGetValue(identity, out long openedOccurrence) && openedOccurrence == occurrence &&
            _finishedMissionLootOccurrences.TryGetValue(identity, out long finishedOccurrence) && finishedOccurrence == occurrence &&
            _missionSources.TryGetValue(identity, out MissionSourceState state) &&
            state == MissionSourceState.Completed;

        public bool IsMissionLootSuccessfullyProcessed(MissionIdentity identity) =>
            _missionLootRooms.TryGetValue(identity, out int roomId) &&
            _missionLootOccurrences.TryGetValue(identity, out long occurrence) &&
            IsMissionLootSuccessfullyProcessed(identity, roomId, occurrence);

        public bool TryGetMissionLootSkipReason(MissionIdentity identity, out string reason)
        {
            reason = null;
            if (!_missionSources.TryGetValue(identity, out MissionSourceState state) || !Settled(state) ||
                state == MissionSourceState.Completed) return false;
            if (_missionSkipReasons.TryGetValue(identity, out reason) && !string.IsNullOrWhiteSpace(reason))
                return true;
            reason = state == MissionSourceState.SkippedUnreachable ? "approach retries exhausted" :
                state == MissionSourceState.SkippedInsufficientSkill ? "insufficient lockpick skill" :
                state == MissionSourceState.SkippedNoLockpick ? "no lockpick available" :
                state == MissionSourceState.SkippedRepeatedFailure ? "repeated lockpick failure" :
                state == MissionSourceState.SkippedNoCapacity ? "no safe main-inventory slot" :
                state.ToString();
            return true;
        }

        public void RetryVisibleOutstandingMissionLoot(MissionIdentity identity, int roomId, string priorReason)
        {
            if (identity == MissionIdentity.None || MissionRoomId < 0 ||
                IsMissionLootSuccessfullyProcessed(identity)) return;
            _unreachableMissionLoot.Remove(identity);
            _finishedMissionLoot.Remove(identity);
            _finishedMissionLootOccurrences.Remove(identity);
            _deferredApproaches.Remove(identity);
            _missionSkipReasons.Remove(identity);
            openedContainers.Remove(identity.Instance);
            TrackMissionLoot(identity, roomId);
            SetMissionSource(identity, MissionSourceState.Discovered);
            Chat.WriteLine($"RKMission: Reopened visible outstanding loot {identity} in room {roomId}; " +
                $"prior skip was '{priorReason}', but exact ContainerOpened plus processing-finished evidence is still absent.");
        }

        public void SkipUnreachableMissionLoot(MissionIdentity identity)
        {
            SetMissionSource(identity, IsMissionCriticalLoot(identity) ?
                MissionSourceState.CriticalBlocked : MissionSourceState.SkippedUnreachable);
            _unreachableMissionLoot.Add(identity);
            if (CurrentCorpse?.Identity == identity && CorpseContainer == null)
            {
                CurrentCorpse = null;
                CurrentProcess = ProcessState.Open_Corpse;
            }
            if (_pendingMissionLoot == identity && CorpseContainer == null)
            {
                _pendingMissionLoot = MissionIdentity.None;
                _pendingMissionLootOccurrence = 0;
            }
        }

        public bool DeferMissionLoot(MissionIdentity identity, string failureReason,
            out int attempt, out int maxAttempts)
        {
            maxAttempts = MaxOrdinaryApproachAttempts;
            if (IsMissionCriticalLoot(identity))
            {
                attempt = 1;
                SetMissionSource(identity, MissionSourceState.CriticalBlocked);
                return false;
            }
            if (!_deferredApproaches.TryGetValue(identity, out DeferredApproach deferred))
                _deferredApproaches[identity] = deferred = new DeferredApproach();
            attempt = ++deferred.Attempts;
            if (attempt >= MaxOrdinaryApproachAttempts)
            {
                SkipUnreachableMissionLoot(identity);
                _missionSkipReasons[identity] = $"approach retries exhausted: {failureReason}";
                return false;
            }
            deferred.RetryAfterTraversal = _missionRoomTraversals + 1;
            SetMissionSource(identity, MissionSourceState.DeferredApproach);
            if (CurrentCorpse?.Identity == identity && CorpseContainer == null)
            {
                CurrentCorpse = null;
                CurrentProcess = ProcessState.Open_Corpse;
            }
            if (_pendingMissionLoot == identity && CorpseContainer == null)
            {
                _pendingMissionLoot = MissionIdentity.None;
                _pendingMissionLootOccurrence = 0;
            }
            return true;
        }

        private bool DeferredApproachReady(MissionIdentity identity) =>
            IsMissionCriticalLoot(identity) ||
            !_deferredApproaches.TryGetValue(identity, out DeferredApproach deferred) ||
            _missionRoomTraversals >= deferred.RetryAfterTraversal;

        private LockpickAttempt MissionLockpick(MissionIdentity identity)
        {
            if (!_missionLockpicks.TryGetValue(identity, out LockpickAttempt attempt))
                _missionLockpicks[identity] = attempt = new LockpickAttempt();
            return attempt;
        }

        private bool LockpickReady(MissionIdentity identity) =>
            !_missionLockpicks.TryGetValue(identity, out LockpickAttempt attempt) ||
            (!attempt.CriticalBlocked && Time.AONormalTime >= attempt.LastAttempt + LockpickRetryDelaySeconds);

        private void FinishLockpickFailure(MissionIdentity identity, LockpickOutcome reason)
        {
            LockpickAttempt attempt = MissionLockpick(identity);
            attempt.Outcome = reason;
            if (ReservedMissionLoot(identity) || MissionObjectiveContainer == identity)
            {
                attempt.CriticalBlocked = true;
                SetMissionSource(identity, MissionSourceState.CriticalBlocked);
                Chat.WriteLine($"RKMission: Objective container {identity} cannot be lockpicked ({reason}); mission recovery required.");
                // Keep pending ownership so RKMission's existing loot watchdog stops
                // the mission instead of treating this objective as ordinary loot.
                CurrentProcess = ProcessState.PickingLock;
                Timeout = double.MaxValue;
                return;
            }
            SkipUnreachableMissionLoot(identity);
            SetMissionSource(identity, reason == LockpickOutcome.InsufficientSkill ?
                MissionSourceState.SkippedInsufficientSkill : reason == LockpickOutcome.NoLockPick ?
                MissionSourceState.SkippedNoLockpick : MissionSourceState.SkippedRepeatedFailure);
            _missionSkipReasons[identity] = $"lockpick failure: {reason} after {attempt.Attempts} attempt(s)";
            // A known unpickable ordinary chest is settled for room and final clearance.
            _finishedMissionLoot.Add(identity);
            Chat.WriteLine($"RKMission: Skipping locked loot {identity}: {reason} after {attempt.Attempts} attempt(s).");
            CurrentCorpse = null;
            CurrentProcess = ProcessState.Open_Corpse;
        }

        private void ObserveLockpickFeedback(string message)
        {
            if (MissionRoomId < 0 || CurrentProcess != ProcessState.PickingLock ||
                _pendingMissionLoot == MissionIdentity.None || string.IsNullOrWhiteSpace(message)) return;
            LockpickAttempt attempt = MissionLockpick(_pendingMissionLoot);
            if (attempt.CriticalBlocked || Time.AONormalTime - attempt.LastAttempt > 2.5) return;
            string text = message.ToLowerInvariant();
            bool lockContext = text.Contains("lock") || text.Contains("pick") ||
                text.Contains("breaking and entering") || text.Contains("breaking & entering");
            bool skillFailure = text.Contains("skill") &&
                (text.Contains("not enough") || text.Contains("too low") || text.Contains("insufficient") ||
                 text.Contains("lack") || (text.Contains("need") && text.Contains("more")) ||
                 text.Contains("not high enough"));
            if (lockContext && skillFailure)
                FinishLockpickFailure(_pendingMissionLoot, LockpickOutcome.InsufficientSkill);
        }

        public Dynel NextMissionLoot(int roomId)
        {
            if (IgnoreOrdinaryMissionLoot) return null;
            var candidates = (MissionRoomDynels?.Invoke(roomId) ?? DynelManager.AllDynels)
            .Where(x => (x.Identity.Type == IdentityType.Corpse || x.Identity.Type == IdentityType.Container)
                && IsInMissionRoom(x, roomId)
                && (MissionLootAllowed?.Invoke(x) ?? true)).ToList();
            foreach (Dynel candidate in candidates) TrackMissionLoot(candidate.Identity, roomId);
            Dynel selected = candidates.Where(x =>
                !_unreachableMissionLoot.Contains(x.Identity)
                && !_finishedMissionLoot.Contains(x.Identity)
                && DeferredApproachReady(x.Identity))
            .OrderBy(x => x.Identity == _pendingMissionLoot ? 0 : 1)
            .ThenBy(x => x.Identity.Type == IdentityType.Corpse ? 0 : 1)
            .ThenBy(x => x.DistanceFrom(DynelManager.LocalPlayer)).FirstOrDefault();
            if (selected != null && _missionSources.TryGetValue(selected.Identity, out MissionSourceState state) &&
                (state == MissionSourceState.Discovered || state == MissionSourceState.DeferredApproach))
                SetMissionSource(selected.Identity, MissionSourceState.PendingApproach);
            return selected;
        }

        private void FinishMissionContainer()
        {
            if (MissionRoomId >= 0 && CorpseContainer != null)
            {
                if (!_openedMissionLoot.Contains(CorpseContainer.Identity))
                {
                    Chat.WriteLine($"RKMission: Refusing completion for {CorpseContainer.Identity}: exact ContainerOpened evidence is absent.");
                    return;
                }
                _unreachableMissionLoot.Remove(CorpseContainer.Identity);
                long occurrence = _sourceAcquisition?.Occurrence ?? 0;
                if (occurrence == 0 || !_openedMissionLootOccurrences.TryGetValue(CorpseContainer.Identity, out long openedOccurrence) ||
                    openedOccurrence != occurrence) return;
                if (_finishedMissionLoot.Add(CorpseContainer.Identity))
                    Chat.WriteLine($"RKMission: Loot processing finished {CorpseContainer.Identity}.");
                _finishedMissionLootOccurrences[CorpseContainer.Identity] = occurrence;
                SetMissionSource(CorpseContainer.Identity, MissionSourceState.Completed);
                if (_pendingMissionLoot == CorpseContainer.Identity)
                {
                    _pendingMissionLoot = MissionIdentity.None;
                    _pendingMissionLootOccurrence = 0;
                }
            }
        }

        private static bool SameItemDetails(Item item, ListedItemTransfer selected) => item != null &&
            item.Id == selected.Id && item.LowId == selected.LowId && item.HighId == selected.HighId &&
            item.QualityLevel == selected.QualityLevel &&
            string.Equals(item.Name, selected.Name, StringComparison.Ordinal);

        private static bool SameItemDetails(Item left, Item right) => left != null && right != null &&
            left.Id == right.Id && left.LowId == right.LowId && left.HighId == right.HighId &&
            left.QualityLevel == right.QualityLevel &&
            string.Equals(left.Name, right.Name, StringComparison.Ordinal);

        private static bool BackgroundBagItemMatches(Item item, BackgroundBagMove move) => item != null &&
            item.Id == move.Id && item.LowId == move.LowId && item.HighId == move.HighId &&
            item.QualityLevel == move.QualityLevel &&
            string.Equals(item.Name, move.ItemName, StringComparison.Ordinal);

        private static bool HasUsableIdentity(MissionIdentity identity) =>
            identity != MissionIdentity.None && identity.Type != IdentityType.None && identity.Instance != 0;

        // Called only after RKMission has observed an exact one-item source
        // departure and destination arrival. None identities remain scoped to
        // the verified destination slot so identical unrelated items do not
        // inherit Keep ownership.
        internal bool PreserveVerifiedKeepOwnership(Item source, Item destination)
        {
            if (source == null || destination == null || source.Id != destination.Id ||
                source.LowId != destination.LowId || source.HighId != destination.HighId ||
                source.QualityLevel != destination.QualityLevel ||
                !string.Equals(source.Name, destination.Name, StringComparison.Ordinal)) return false;
            if (HasUsableIdentity(destination.UniqueIdentity))
            {
                RememberSelectedIdentity(destination.UniqueIdentity);
                return true;
            }
            _verifiedNormalizedKeeps.RemoveAll(existing => existing.SourceSlot == destination.Slot);
            _verifiedNormalizedKeeps.Add(new ListedItemTransfer
            {
                Identity = destination.UniqueIdentity,
                SourceSlot = destination.Slot,
                Id = destination.Id,
                LowId = destination.LowId,
                HighId = destination.HighId,
                QualityLevel = destination.QualityLevel,
                Name = destination.Name,
                Verified = true
            });
            return true;
        }

        private void RememberSelectedIdentity(MissionIdentity identity)
        {
            if (HasUsableIdentity(identity))
                _selectedItems.Add(identity);
        }

        private bool SameSelectedItem(Item item, ListedItemTransfer selected) => item != null &&
            (HasUsableIdentity(selected.Identity) && item.UniqueIdentity == selected.Identity ||
             !HasUsableIdentity(selected.Identity) && item.Slot == selected.SourceSlot && SameItemDetails(item, selected));

        private int MatchingInventoryCount(ListedItemTransfer selected) => Inventory.Items.Count(item =>
            item.Slot.Type == IdentityType.Inventory && item.UniqueIdentity.Type != IdentityType.Container &&
            SameItemDetails(item, selected));

        private Item AcquiredInventoryItem(ListedItemTransfer selected) => Inventory.Items.LastOrDefault(item =>
            item.Slot.Type == IdentityType.Inventory && item.UniqueIdentity.Type != IdentityType.Container &&
            (HasUsableIdentity(selected.Identity)
                ? item.UniqueIdentity == selected.Identity
                : !selected.ExistingInventorySlots.Contains(item.Slot) && SameItemDetails(item, selected)));

        private void BeginSourceAcquisition(Container container)
        {
            _sourceAcquisition = new SourceAcquisition
            {
                Source = container.Identity,
                Occurrence = _pendingMissionLootOccurrence,
                OpenedAt = Time.AONormalTime
            };
            var oneEachNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var ruleSelections = new Dictionary<Rule, int>();
            foreach (Item item in container.Items ?? Enumerable.Empty<Item>())
            {
                bool objective = container.Identity == MissionObjectiveContainer;
                Rule rule = GetMatchingRule(item);
                bool selected = objective || Classify(item) == ItemClassification.Keep;
                if (!selected) continue;

                if (!objective && rule != null)
                {
                    if (string.Equals(rule.OneEach, "true", StringComparison.OrdinalIgnoreCase) &&
                        !oneEachNames.Add(item.Name)) continue;
                    if (int.TryParse(rule.Quantity, out int quantity) && quantity != 999)
                    {
                        int alreadyOwned = Inventory.Items.Count(existing =>
                            existing.Slot.Type == IdentityType.Inventory && GetMatchingRule(existing) == rule);
                        int selectedForRule = ruleSelections.TryGetValue(rule, out int count) ? count : 0;
                        if (alreadyOwned + selectedForRule >= quantity) continue;
                        ruleSelections[rule] = selectedForRule + 1;
                    }
                }

                var transfer = new ListedItemTransfer
                {
                    Identity = item.UniqueIdentity,
                    SourceSlot = item.Slot,
                    Id = item.Id,
                    LowId = item.LowId,
                    HighId = item.HighId,
                    QualityLevel = item.QualityLevel,
                    Name = item.Name,
                    Rule = rule,
                    Objective = objective
                };
                transfer.InventoryCountBefore = MatchingInventoryCount(transfer);
                foreach (Item existing in Inventory.Items.Where(candidate =>
                    candidate.Slot.Type == IdentityType.Inventory && SameItemDetails(candidate, transfer)))
                    transfer.ExistingInventorySlots.Add(existing.Slot);
                _sourceAcquisition.Items.Add(transfer);
            }
            Chat.WriteLine($"RKMission: ManagerLoot selected {_sourceAcquisition.Items.Count}/{container.Items?.Count ?? 0} item(s) from {container.Identity}.");
        }

        private bool SourceStillContains(ListedItemTransfer selected) => CorpseContainer?.Items != null &&
            CorpseContainer.Items.Any(item => SameSelectedItem(item, selected));

        private Item AcquiredMainInventoryItem(ListedItemTransfer selected) => Inventory.Items.LastOrDefault(item =>
            item.Slot.Type == IdentityType.Inventory && item.UniqueIdentity.Type != IdentityType.Container &&
            (selected.AcquiredSlot != MissionIdentity.None
                ? item.Slot == selected.AcquiredSlot && SameItemDetails(item, selected)
                : HasUsableIdentity(selected.Identity) && item.UniqueIdentity == selected.Identity));

        private int MatchingBagCount(Container bag, ListedItemTransfer selected) =>
            (bag?.Items ?? Enumerable.Empty<Item>()).Count(item => SameItemDetails(item, selected));

        private bool ProcessKeepPlacement(ListedItemTransfer selected)
        {
            Container bag = Inventory.Backpacks.FirstOrDefault(candidate => candidate.Identity == selected.KeepBag);
            Item acquired = AcquiredMainInventoryItem(selected);
            if (!_keepPlacementBagOpened || _keepPlacementBag != selected.KeepBag)
            {
                if (acquired == null || bag == null)
                {
                    selected.KeepPlacementPending = false;
                    Chat.WriteLine($"RKMission: Keep placement deferred for '{selected.Name}'; destination '{selected.KeepBagName}' or the retained main-inventory item disappeared.");
                    return true;
                }
                if (Item.HasPendingUse || Time.AONormalTime < selected.KeepBagOpenSentAt + ManagedBagResponseSeconds)
                    return true;
                if (selected.KeepBagOpenAttempts >= MaxManagedBagLoadAttempts)
                {
                    selected.KeepPlacementPending = false;
                    Chat.WriteLine($"RKMission: Keep placement deferred for '{selected.Name}'; destination '{selected.KeepBagName}' did not provide ContainerOpened evidence after {selected.KeepBagOpenAttempts} bounded attempt(s), and the item remains safely owned in main inventory.");
                    return true;
                }
                Item bagItem = Inventory.Items.FirstOrDefault(item => item != null &&
                    item.UniqueIdentity == selected.KeepBag);
                if (bagItem == null)
                {
                    selected.KeepPlacementPending = false;
                    Chat.WriteLine($"RKMission: Keep placement deferred for '{selected.Name}'; destination '{selected.KeepBagName}' is not available in inventory, and the item remains safely owned in main inventory.");
                    return true;
                }
                _keepPlacementBag = selected.KeepBag;
                _keepPlacementBagOpened = false;
                selected.KeepBagOpenAttempts++;
                selected.KeepBagOpenSentAt = Time.AONormalTime;
                bagItem.Use();
                Chat.WriteLine($"RKMission: Opening Keep destination '{selected.KeepBagName}' before placement (attempt {selected.KeepBagOpenAttempts}/{MaxManagedBagLoadAttempts}).");
                return true;
            }
            bool leftMain = acquired == null;
            bool arrived = bag != null && (MatchingBagCount(bag, selected) > selected.KeepBagCountBefore ||
                HasUsableIdentity(selected.Identity) && bag.Items.Any(item => item.UniqueIdentity == selected.Identity));
            if (leftMain && arrived)
            {
                Item destination = bag.Items.LastOrDefault(item =>
                    HasUsableIdentity(selected.Identity) ? item.UniqueIdentity == selected.Identity : SameItemDetails(item, selected));
                if (destination != null && !HasUsableIdentity(destination.UniqueIdentity))
                {
                    _verifiedNormalizedKeeps.RemoveAll(existing => existing.SourceSlot == destination.Slot);
                    _verifiedNormalizedKeeps.Add(new ListedItemTransfer
                    {
                        Identity = destination.UniqueIdentity, SourceSlot = destination.Slot,
                        Id = destination.Id, LowId = destination.LowId, HighId = destination.HighId,
                        QualityLevel = destination.QualityLevel, Name = destination.Name, Verified = true
                    });
                }
                Chat.WriteLine($"RKMission: Keep placement confirmed for '{selected.Name}' in '{selected.KeepBagName}' after source-slot removal and destination arrival.");
                selected.KeepPlacementPending = false;
                MissionLootProgress++;
                return true;
            }

            if (Item.HasPendingUse || Time.AONormalTime < selected.KeepPlacementSentAt + KeepPlacementConfirmationSeconds)
                return true;
            if (selected.KeepPlacementAttempts >= MaxKeepPlacementAttempts)
            {
                if (acquired != null)
                {
                    Chat.WriteLine($"RKMission: Keep placement deferred after {selected.KeepPlacementAttempts} bounded attempt(s) for '{selected.Name}'; destination '{selected.KeepBagName}' was not confirmed and the item remains safely owned in main inventory.");
                    selected.KeepPlacementPending = false;
                    MissionLootProgress++;
                    return true;
                }
                SetMissionSource(_sourceAcquisition.Source, MissionSourceState.CriticalBlocked);
                if (!selected.NonActionable)
                    Chat.WriteLine($"RKMission: Keep placement failed for '{selected.Name}': neither main-inventory retention nor destination arrival could be confirmed after {selected.KeepPlacementAttempts} attempt(s); source settlement is held for safe recovery.");
                selected.NonActionable = true;
                return true;
            }
            if (acquired == null || bag == null)
            {
                SetMissionSource(_sourceAcquisition.Source, MissionSourceState.CriticalBlocked);
                if (!selected.NonActionable)
                    Chat.WriteLine($"RKMission: Keep placement cannot continue for '{selected.Name}'; the acquired item or destination bag disappeared, so source settlement is held.");
                selected.NonActionable = true;
                return true;
            }
            selected.KeepBagCountBefore = MatchingBagCount(bag, selected);
            selected.KeepPlacementAttempts++;
            selected.KeepPlacementSentAt = Time.AONormalTime;
            acquired.MoveToContainer(bag);
            Chat.WriteLine($"RKMission: Keep placement requested for '{selected.Name}' in '{selected.KeepBagName}' (attempt {selected.KeepPlacementAttempts}/{MaxKeepPlacementAttempts}); awaiting verified destination arrival.");
            return true;
        }

        private void RecordVerifiedTransfer(ListedItemTransfer selected)
        {
            if (!selected.AcquisitionRecorded)
            {
                selected.Verified = true;
                selected.AcquisitionRecorded = true;
                MissionLootProgress++;
                Item acquired = AcquiredInventoryItem(selected);
                if (acquired == null)
                {
                    SetMissionSource(_sourceAcquisition.Source, MissionSourceState.CriticalBlocked);
                    selected.NonActionable = true;
                    Chat.WriteLine($"RKMission: Listed-item acquisition evidence for '{selected.Name}' could not be bound to one inventory slot; source settlement is held to avoid a duplicate or misidentified Keep transfer.");
                    return;
                }
                else
                {
                    selected.AcquiredSlot = acquired.Slot;
                    RememberSelectedIdentity(acquired.UniqueIdentity);
                    if (!HasUsableIdentity(acquired.UniqueIdentity) &&
                        !_verifiedSelectedDetails.Any(existing => SameItemDetails(acquired, existing)))
                        _verifiedSelectedDetails.Add(selected);
                    if (selected.Objective && HasUsableIdentity(acquired.UniqueIdentity))
                        _objectiveLootItems.Add(acquired.UniqueIdentity);
                    if (!selected.Objective && selected.Rule != null)
                    {
                        selected.KeepBagName = ResolveKeepDestination(selected.Rule);
                        UpdateRule(selected.Rule);
                        Container bag = Inventory.Backpacks
                            .Where(candidate => ManagedBagFamily.Matches(candidate.Name, selected.KeepBagName))
                            .OrderBy(candidate => ManagedBagFamily.Order(candidate.Name, selected.KeepBagName))
                            .ThenBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
                            .FirstOrDefault(candidate => candidate.Items.Count < 21);
                        if (bag != null)
                        {
                            selected.KeepBag = bag.Identity;
                            selected.KeepPlacementPending = true;
                            selected.KeepPlacementSentAt = 0;
                        }
                        else
                            Chat.WriteLine($"RKMission: Keep destination '{selected.KeepBagName}' unavailable or full; leaving '{selected.Name}' safely in main inventory as Keep.");
                    }
                }
                Chat.WriteLine($"RKMission: Verified listed-item transfer '{selected.Name}', id={selected.Id}, QL={selected.QualityLevel} from {_sourceAcquisition.Source}.");
            }
            _sourceAcquisition.Current++;
        }

        private bool ProcessMissionSourceAcquisition()
        {
            if (_sourceAcquisition == null || CorpseContainer == null) return false;
            if (_sourceAcquisition.Current < _sourceAcquisition.Items.Count)
            {
                ListedItemTransfer selected = _sourceAcquisition.Items[_sourceAcquisition.Current];
                bool leftSource = !SourceStillContains(selected);
                bool appearedInInventory = MatchingInventoryCount(selected) > selected.InventoryCountBefore ||
                    HasUsableIdentity(selected.Identity) && Inventory.Items.Any(item => item.UniqueIdentity == selected.Identity);
                if (leftSource && appearedInInventory)
                {
                    RecordVerifiedTransfer(selected);
                    return true;
                }

                if (selected.Attempts == 0 && Inventory.NumFreeSlots <= 1)
                {
                    bool critical = selected.Objective || IsMissionCriticalLoot(_sourceAcquisition.Source);
                    if (critical)
                    {
                        SetMissionSource(_sourceAcquisition.Source, MissionSourceState.CriticalBlocked);
                        if (!_sourceAcquisition.CapacityLogged)
                            Chat.WriteLine($"RKMission: Critical loot {_sourceAcquisition.Source} remains held because the main inventory has no safe free slot.");
                        _sourceAcquisition.CapacityLogged = true;
                        return true;
                    }
                    _sourceAcquisition.SkippedNoCapacity = true;
                    SetMissionSource(_sourceAcquisition.Source, MissionSourceState.SkippedNoCapacity);
                    _missionSkipReasons[_sourceAcquisition.Source] = "no safe main-inventory slot after exact ContainerOpened";
                    foreach (ListedItemTransfer remaining in _sourceAcquisition.Items.Skip(_sourceAcquisition.Current))
                        remaining.NonActionable = true;
                    _sourceAcquisition.Current = _sourceAcquisition.Items.Count;
                    if (!_sourceAcquisition.CapacityLogged)
                        Chat.WriteLine($"RKMission: Optional loot {_sourceAcquisition.Source} safely skipped because the main inventory has no safe free slot.");
                    _sourceAcquisition.CapacityLogged = true;
                    return true;
                }

                if (selected.Attempts == 0 || Time.AONormalTime >= selected.SentAt + ListedMoveConfirmationSeconds)
                {
                    if (selected.Attempts >= MaxListedMoveAttempts)
                    {
                        bool critical = selected.Objective || IsMissionCriticalLoot(_sourceAcquisition.Source);
                        if (critical)
                        {
                            SetMissionSource(_sourceAcquisition.Source, MissionSourceState.CriticalBlocked);
                            if (!selected.NonActionable)
                                Chat.WriteLine($"RKMission: Listed-item transfer timed out for critical '{selected.Name}', id={selected.Id}, QL={selected.QualityLevel}; source remains held.");
                            selected.NonActionable = true;
                            return true;
                        }
                        selected.NonActionable = true;
                        Chat.WriteLine($"RKMission: Listed-item transfer remained ambiguous for '{selected.Name}', id={selected.Id}, QL={selected.QualityLevel}; safely skipping after {selected.Attempts} bounded attempt(s).");
                        _sourceAcquisition.Current++;
                        return true;
                    }

                    Item sourceItem = CorpseContainer.Items?.FirstOrDefault(item => SameSelectedItem(item, selected));
                    if (sourceItem == null)
                    {
                        if (Time.AONormalTime < _sourceAcquisition.OpenedAt + ListedMoveConfirmationSeconds)
                            return true;
                        bool critical = selected.Objective || IsMissionCriticalLoot(_sourceAcquisition.Source);
                        if (critical)
                        {
                            SetMissionSource(_sourceAcquisition.Source, MissionSourceState.CriticalBlocked);
                            if (!selected.NonActionable)
                                Chat.WriteLine($"RKMission: Critical listed item '{selected.Name}', id={selected.Id}, QL={selected.QualityLevel} disappeared before a verified move; source remains held.");
                            selected.NonActionable = true;
                            return true;
                        }
                        selected.NonActionable = true;
                        Chat.WriteLine($"RKMission: Listed item '{selected.Name}', id={selected.Id}, QL={selected.QualityLevel} disappeared without inventory evidence; safely classifying it non-actionable.");
                        _sourceAcquisition.Current++;
                        return true;
                    }
                    selected.Attempts++;
                    selected.SentAt = Time.AONormalTime;
                    Chat.WriteLine($"RKMission: Moving listed item '{selected.Name}', id={selected.Id}, QL={selected.QualityLevel} from {_sourceAcquisition.Source} (attempt {selected.Attempts}/{MaxListedMoveAttempts}).");
                    sourceItem.MoveToInventory();
                }
                return true;
            }

            if (Time.AONormalTime < _sourceAcquisition.OpenedAt + LootWindowDwellSeconds) return true;
            if (!_sourceAcquisition.DwellLogged)
            {
                _sourceAcquisition.DwellLogged = true;
                Chat.WriteLine($"RKMission: Loot-window visual dwell complete for {_sourceAcquisition.Source}.");
            }
            return false;
        }

        private void TrackMissionLoot(MissionIdentity identity, int roomId, bool newObservedOccurrence = false)
        {
            if (_missionLootRooms.TryGetValue(identity, out int previousRoom) &&
                (previousRoom != roomId || newObservedOccurrence))
            {
                _missionLootOccurrences[identity] = ++_nextMissionLootOccurrence;
                Chat.WriteLine($"RKMission: New loot occurrence {identity}: room {previousRoom} -> {roomId}, generation {_missionLootOccurrences[identity]}; stale open/finish evidence invalidated.");
                _openedMissionLoot.Remove(identity);
                _finishedMissionLoot.Remove(identity);
                _openedMissionLootOccurrences.Remove(identity);
                _finishedMissionLootOccurrences.Remove(identity);
                _unreachableMissionLoot.Remove(identity);
                _missionSkipReasons.Remove(identity);
                _deferredApproaches.Remove(identity);
                _missionLockpicks.Remove(identity);
                openedContainers.Remove(identity.Instance);
                _missionSources.Remove(identity);
                if (_pendingMissionLoot == identity)
                {
                    _pendingMissionLoot = MissionIdentity.None;
                    _pendingMissionLootOccurrence = 0;
                }
                if (_sourceAcquisition?.Source == identity || CorpseContainer?.Identity == identity)
                {
                    _sourceAcquisition = null;
                    CorpseContainer = null;
                    CurrentCorpse = null;
                    CurrentProcess = ProcessState.Open_Corpse;
                }
            }
            else if (!_missionLootOccurrences.ContainsKey(identity))
                _missionLootOccurrences[identity] = ++_nextMissionLootOccurrence;
            _seenMissionLoot.Add(identity);
            _missionLootRooms[identity] = roomId;
            if (!_missionSources.ContainsKey(identity)) SetMissionSource(identity, MissionSourceState.Discovered);
        }

        private void BindMissionLoot(Dynel dynel)
        {
            if (MissionRoomId < 0) return;
            _pendingMissionLoot = dynel.Identity;
            TrackMissionLoot(dynel.Identity, MissionRoomId);
            _pendingMissionLootOccurrence = _missionLootOccurrences[dynel.Identity];
            SetMissionSource(dynel.Identity, MissionSourceState.Opening);
        }

        public void BeginMissionObjectiveLoot(MissionIdentity identity)
        {
            if (MissionObjectiveContainer == identity) return;
            MissionObjectiveContainer = identity;
            // Earlier ordinary rules may have skipped it or left the quest
            // item behind before objective metadata arrived. Retry once as final work.
            _unreachableMissionLoot.Remove(identity);
            _deferredApproaches.Remove(identity);
            _finishedMissionLoot.Remove(identity);
            openedContainers.Remove(identity.Instance);
            _missionLockpicks.Remove(identity);
            SetMissionSource(identity, MissionSourceState.Discovered);
        }

        private bool IsInMissionRoom(Dynel dynel, int roomId) =>
            dynel.Room?.Instance == roomId ||
            MissionRoomContains?.Invoke(dynel, roomId) == true;

        public bool IsProcessingMissionLoot => _pendingMissionLoot != MissionIdentity.None || CorpseContainer != null ||
            CurrentProcess == ProcessState.Opening || CurrentProcess == ProcessState.PickingLock ||
            CurrentProcess == ProcessState.Move_To_Inventory ||
            CurrentProcess == ProcessState.Move_To_KeepBag ||
            CurrentProcess == ProcessState.Close_Corpse;
        public MissionIdentity ProcessingMissionLootIdentity => _pendingMissionLoot != MissionIdentity.None
            ? _pendingMissionLoot : CorpseContainer?.Identity ?? MissionIdentity.None;
        public MissionIdentity WaitingMissionLootIdentity =>
            MissionRoomId >= 0 && CurrentProcess == ProcessState.Opening &&
            CorpseContainer == null ? _pendingMissionLoot : MissionIdentity.None;
        public bool WaitingForOrdinaryMissionContainer(MissionIdentity identity) =>
            MissionRoomId >= 0 && identity.Type == IdentityType.Container &&
            _pendingMissionLoot == identity && CorpseContainer == null &&
            !IsMissionCriticalLoot(identity) &&
            (CurrentProcess == ProcessState.Opening || CurrentProcess == ProcessState.Open_Corpse);
        private Dynel CurrentCorpse;

        private readonly List<string> ErrorMessages = new List<string>();

        private double Timeout;
        private double ZoneDelay = 0.0;

        private ProcessState CurrentProcess;
        private ProcessState LastProcess;
        private Container CorpseContainer;
        //private bool Print;

        private string LocalFolderPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AOSharp", "ManagerLoot", DynelManager.LocalPlayer.Name);
        private string SharedFolderPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AOSharp", "ManagerLoot", "Shared");

        private string FolderPath => _settings != null && _settings["UseSharedFolder"].AsBool() ? SharedFolderPath : LocalFolderPath;
        private string RulesPath => Path.Combine(FolderPath, "Default.json");

        private string LastUsedPathFile => Path.Combine(LocalFolderPath, "LastUsedPath.txt");

        //private string NewName;

        //private ComboBox file_Path;

        private readonly List<int> reverseItems = new List<int>();

        public void Run()
        {
            try
            {
                if (Game.IsNewEngine)
                {
                    Chat.WriteLine("Does not work on this engine!");
                    return;
                }

                _settings = new Settings(PluginName);

                _settings.AddVariable("Enable", false);
                _settings.AddVariable("Delete", false);
                _settings.AddVariable("Reverse", false);
                _settings.AddVariable("Exact", false);
                _settings.AddVariable("OneOfEach", false);
                _settings.AddVariable("Disable", false);
                _settings.AddVariable("Chests", false);
                _settings.AddVariable("LootAll", false);
                _settings.AddVariable("UseSharedFolder", false);

                _settings.AddVariable("DisableIfEmptyList", false);

                _settings.AddVariable("MainWindowTopLeftX", 100f);
                _settings.AddVariable("MainWindowTopLeftY", 100f);

                _settings.AddVariable("Print", false);
                _settingsToSave.Add(_settings);

                EnableString = _settings["Enable"].AsBool() ? "Disable" : "Enable";

                Directory.CreateDirectory(LocalFolderPath);
                Directory.CreateDirectory(SharedFolderPath);

                LoadRules();

                Chat.RegisterCommand(PluginName, ManagerCommand);
                Chat.RegisterCommand("lm", ManagerLootCommand);

                Chat.RegisterCommand("printitems", (command, param, chatWindow) => { _settings["Print"] = !_settings["Print"].AsBool(); Chat.WriteLine($"print items {_settings["Print"].AsBool()}"); });

                UIController.WindowDeleted += Windowclosed;
                Network.N3MessageSent += N3MessageSent;

                CurrentProcess = ProcessState.Load_Backpacks;

                MainUI();

                Chat.WriteLine($"{PluginName} loaded!");
                Chat.WriteLine($"/{PluginName} opens its tab in RKMission Roller. /lm to enable/disable");
                Chat.WriteLine($"/macro mLoot /{PluginName}");

                string _ManagerLootEnable = _settings["Enable"].AsBool() ? "Enabled" : "Disabled";
                Chat.WriteLine($"{PluginName} {_ManagerLootEnable}");

                ZoneDelay = Time.AONormalTime + 6;

                if (_settings["Enable"].AsBool())
                {
                    Game.OnUpdate += OnUpdate;
                    Game.TeleportStarted += TeleportStarted;
                    Game.TeleportEnded += TeleportEnded;
                    DynelManager.DynelSpawned += DynelSpawned;
                    Network.N3MessageReceived += N3MessageReceived;
                    Inventory.ContainerOpened += ContainerOpened;
                }
            }
            catch (Exception ex)
            {
                ErrorCatch(ex);
            }
        }

        #region Events

        private void N3MessageSent(object sender, N3Message e)
        {
            if (e.N3MessageType != N3MessageType.CharacterAction) return;
            var charAction = (CharacterActionMessage)e;
            if (charAction.Action != CharacterActionType.Logout) return;

            _settings["Enable"] = false;
            EnableString = "Enable";

            if (settingsWindow?.IsValid == true && FindSettingsView("Enable_Disable_Button", out Button enableButton))
                enableButton.SetLabel(EnableString);

            Save();
            UnsubEvents();
            return;
        }

        private void TeleportStarted(object sender, EventArgs e)
        {
            ZoneDelay = Time.AONormalTime + 10000;
        }

        private void TeleportEnded(object sender, EventArgs e)
        {
            ZoneDelay = Time.AONormalTime + 6;
            openedContainers.Clear();
            _managedBagSnapshots.Clear();
            _waitingForManagedBag = MissionIdentity.None;
            _backgroundBagMove = null;
            _deferredBackgroundBagSlots.Clear();
            CurrentProcess = ProcessState.Load_Backpacks;
        }

        private void DynelSpawned(object sender, Dynel e)
        {
            if (!_settings["Enable"].AsBool()) return;

            switch (e.Identity.Type)
            {
                case IdentityType.Corpse:
                    if (!openedContainers.ContainsKey(e.Identity.Instance))
                        return;

                    if (Time.AONormalTime > openedContainers[e.Identity.Instance] + 200)
                        openedContainers.Remove(e.Identity.Instance);
                    break;
            }
        }

        private void N3MessageReceived(object sender, N3Message e)
        {
            if (!_settings["Enable"].AsBool()) return;

            // AO# exposes rendered server text through these existing N3 messages.
            // Numeric FeedbackMessage ids have no verified lockpick mapping here.
            if (e is ChatTextMessage chat) ObserveMissionFeedback(chat.Text);
            else if (e is FormatFeedbackMessage feedback) ObserveMissionFeedback(feedback.Message);

            switch (e.N3MessageType)
            {
                case N3MessageType.ContainerAddItem:
                    var contAddItem = (ContainerAddItem)e;
                    // target is the localplay identity, soruce is where is came from but corpses are also labeled as backpacks, Backpack:820000. so not useful.
                    //Chat.WriteLine($"ContainerAddItem -> Source={contAddItem.Source}, Target={contAddItem.Target}, Slot={contAddItem.Slot}");
                    if (contAddItem.Source == IdentityType.Inventory) return;
                    foreach (var item in Inventory.Items.Where(i => i.Slot.Type == IdentityType.Inventory)) //&& i.Slot.Instance == contAddItem.Slot slot is always 111 so it can not match the item slot
                    {
                        if (ProtectedMissionItem(item)) continue;
                        if (!_settings["Reverse"].AsBool())
                        {

                        }
                        else if (reverseItems != null && reverseItems.Count > 0)
                        {
                            foreach (var itemId in reverseItems)
                            {
                                if (item.Id != itemId) continue;
                                var bag = Inventory.Backpacks.Where(b => b.Name.Contains("loot")).OrderBy(b => b.Name).FirstOrDefault(b => b.Items.Count < 21);
                                if (bag == null) break;
                                item.MoveToContainer(bag);
                            }

                            reverseItems.Clear();
                        }
                    }
                    break;
                case N3MessageType.GenericCmd:
                    var cmd = (GenericCmdMessage)e;
                    if (CurrentProcess != ProcessState.Closing) return;
                    if (DynelManager.LocalPlayer.Identity != cmd.User) return;
                    if (cmd.Target.Type != IdentityType.Corpse) return;
                    if (cmd.Action != GenericCmdAction.Use) return;
                    if (cmd.Target != CurrentCorpse.Identity) return;
                    CurrentCorpse = null;
                    CurrentProcess = ProcessState.Open_Corpse;
                    break;
                case N3MessageType.Despawn:
                    var despawn = (DespawnMessage)e;

                    if (despawn.Identity.Type != IdentityType.Corpse) return;

                    if (CurrentCorpse != null && CurrentCorpse.Identity.Instance == despawn.Identity.Instance)
                    {
                        openedContainers.Remove(despawn.Identity.Instance);
                        CurrentCorpse = null;
                    }

                    if (openedContainers.ContainsKey(despawn.Identity.Instance))
                        openedContainers.Remove(despawn.Identity.Instance);

                    break;
            }
        }

        private void ObserveMissionFeedback(string message)
        {
            ObserveLockpickFeedback(message);
            if (MissionRoomId < 0 || CurrentProcess != ProcessState.Opening ||
                _pendingMissionLoot == MissionIdentity.None || string.IsNullOrWhiteSpace(message) ||
                message.IndexOf("too far away", StringComparison.OrdinalIgnoreCase) < 0) return;
            MissionIdentity identity = _pendingMissionLoot;
            _pendingMissionLoot = MissionIdentity.None;
            _pendingMissionLootOccurrence = 0;
            CurrentCorpse = null;
            CurrentProcess = ProcessState.Open_Corpse;
            SetMissionSource(identity, MissionSourceState.PendingApproach);
            Chat.WriteLine($"RKMission: Loot {identity} reported too far away; opening ownership released back to approach.");
        }

        private void ContainerOpened(object sender, Container container)
        {
            if (!_settings["Enable"].AsBool()) return;

            if (container != null && (_managedBagSnapshots.TryGetValue(container.Identity,
                out ManagedBagSnapshot managedSnapshot) || _keepPlacementBag == container.Identity))
            {
                if (managedSnapshot == null)
                    _managedBagSnapshots[container.Identity] = managedSnapshot = new ManagedBagSnapshot();
                managedSnapshot.Confirmed = true;
                if (_keepPlacementBag == container.Identity)
                    _keepPlacementBagOpened = true;
                if (_waitingForManagedBag == container.Identity)
                    _waitingForManagedBag = MissionIdentity.None;
                _deferredBackgroundBagSlots.Clear();
                return;
            }

            if (container.Identity.Type != IdentityType.Corpse && container.Identity.Type != IdentityType.Container) return;
            if (Inventory.Items.Any(i => i.UniqueIdentity == container.Identity)) return;

            if (MissionRoomId >= 0)
            {
                if (_pendingMissionLoot != container.Identity || _pendingMissionLootOccurrence == 0 ||
                    !_missionLootOccurrences.TryGetValue(container.Identity, out long currentOccurrence) ||
                    currentOccurrence != _pendingMissionLootOccurrence ||
                    !_missionLootRooms.TryGetValue(container.Identity, out int occurrenceRoom) ||
                    occurrenceRoom != MissionRoomId) return;
            }
            else if (CurrentProcess != ProcessState.Opening) return;

            if (Inventory.Backpacks.Any(b => b.Identity == container.Identity)) return;

            Chat.WriteLine($"ManagerLoot: ContainerOpened {container.Identity} " +
                (MissionRoomId >= 0 ? $"in RKMission room {MissionRoomId}." : "outside RKMission room ownership."));
            if (_settings["Print"].AsBool() && container.Identity.Type == IdentityType.Container)
                foreach (var item in container.Items)
                    Chat.WriteLine($"{item.Name}, {item.Id}, {item.QualityLevel}, {item.UniqueIdentity}");

            if (!openedContainers.ContainsKey(container.Identity.Instance))
                openedContainers.Add(container.Identity.Instance, Time.AONormalTime);
            else
                openedContainers[container.Identity.Instance] = Time.AONormalTime;

            CorpseContainer = container;
            if (MissionRoomId >= 0)
            {
                _openedMissionLoot.Add(container.Identity);
                _openedMissionLootOccurrences[container.Identity] = _pendingMissionLootOccurrence;
            }
            _observedContainerCount = container.Items?.Count ?? 0;
            CurrentCorpse = DynelManager.GetDynel(container.Identity);
            SetMissionSource(container.Identity, MissionSourceState.Looting);
            if (MissionRoomId >= 0) BeginSourceAcquisition(container);
            if (_missionLockpicks.TryGetValue(container.Identity, out LockpickAttempt lockpick))
                lockpick.Outcome = LockpickOutcome.Success;

            if (_settings["Print"].AsBool())
                foreach (var item in container.Items)
                {
                    if (CheckRules(item))
                        Chat.WriteLine(item.Name, ChatColor.Green);
                    else
                        Chat.WriteLine(item.Name, ChatColor.Red);
                }

            CurrentProcess = ProcessState.Move_To_Inventory;
        }

        private void Windowclosed(object sender, Window e)
        {
            switch (e.Name)
            {
                case PluginName:
                    Window_Closed_helper();
                    break;
            }
        }

        private void OnUpdate(object sender, float deltaTime)
        {
            try
            {
                if (Game.IsZoning) return;
                if (Time.AONormalTime < ZoneDelay) return;
                if (MissionActionsPaused) return;
                if (MissionLootAllowed != null && MissionRoomId < 0) return;

                if (MissionRoomId < 0 && _settings["DisableIfEmptyList"].AsBool() &&
                    Rules != null && Rules.Count == 0 && _settings["Enable"].AsBool())
                {
                    _settings["Enable"] = false;
                    _settings["DisableIfEmptyList"] = false;
                    EnableString = "Enable";

                    if (settingsWindow?.IsValid == true && FindSettingsView("Enable_Disable_Button", out Button enableButton))
                        enableButton.SetLabel(EnableString);

                    Chat.WriteLine($"{PluginName} disabled");

                    Game.OnUpdate -= OnUpdate;
                    Game.TeleportEnded -= TeleportEnded;
                    DynelManager.DynelSpawned -= DynelSpawned;
                    Network.N3MessageReceived -= N3MessageReceived;
                    Inventory.ContainerOpened -= ContainerOpened;
                    return;
                }

                if (_settings["Print"].AsBool() && CurrentProcess != LastProcess)
                {
                    Chat.WriteLine($"Process: {CurrentProcess}", ChatColor.Green);
                    LastProcess = CurrentProcess;
                }

                if (MissionRoomId >= 0 && CurrentProcess == ProcessState.Open_Corpse)
                {
                    // A stuck pending-use flag must not strand ordinary loot forever.
                    var expired = _missionLockpicks.FirstOrDefault(x => x.Value.Attempts > 0 &&
                        x.Value.Outcome == LockpickOutcome.TemporaryFailure &&
                        Time.AONormalTime - x.Value.FirstAttempt > 12 &&
                        !_finishedMissionLoot.Contains(x.Key));
                    if (expired.Value != null)
                    {
                        FinishLockpickFailure(expired.Key, LockpickOutcome.RepeatedFailure);
                        return;
                    }
                }

                switch (CurrentProcess)
                {
                    case ProcessState.Load_Backpacks:
                        // Chat.WriteLine("Loading backpack info.", ChatColor.Yellow);
                        InitializeBackpackInfo();
                        CurrentProcess = ProcessState.Open_Corpse;
                        break;
                    case ProcessState.Open_Corpse:
                        if (CorpseContainer != null) { CurrentProcess = ProcessState.Move_To_Inventory; return; }

                        if (Inventory.Items.Where(i => i.Slot.Type == IdentityType.Inventory && i.UniqueIdentity.Type != IdentityType.Container && !ProtectedMissionItem(i) &&
                            !_deferredBackgroundBagSlots.Contains(i.Slot)).Select(i => new { Item = i, Rule = GetRuleForItem(i) }).Any(x => x.Rule != null && x.Rule.BagName != "" &&
                            Inventory.Backpacks.Any(b => ManagedBagFamily.Matches(b.Name, x.Rule.BagName) && b.Items.Count < 21)))
                        { CurrentProcess = ProcessState.Move_To_BackPack; return; }

                        if (Spell.HasPendingCast || Item.HasPendingUse || PerkAction.List.Any(perk => perk.IsExecuting)) return;

                        var roomDynels = MissionRoomId >= 0
                            ? MissionRoomDynels?.Invoke(MissionRoomId) ?? DynelManager.AllDynels
                            : DynelManager.AllDynels;
                        var dynel = roomDynels.Where(c => !openedContainers.ContainsKey(c.Identity.Instance)
                        && (_pendingMissionLoot == MissionIdentity.None || c.Identity == _pendingMissionLoot)
                        && !_unreachableMissionLoot.Contains(c.Identity)
                        && (MissionRoomId < 0 || LockpickReady(c.Identity))
                        && (c.Identity.Type == IdentityType.Container || c.Identity.Type == IdentityType.Corpse)
                        && (MissionLootAllowed?.Invoke(c) ?? true)
                        && (MissionRoomId < 0 || !_finishedMissionLoot.Contains(c.Identity))
                        && (MissionRoomId < 0 || IsInMissionRoom(c, MissionRoomId)))
                            .OrderBy(d => MissionRoomId >= 0 && d.Identity.Type == IdentityType.Corpse ? 0 : 1)
                            .ThenBy(d => d.Position.DistanceFrom(DynelManager.LocalPlayer.Position))
                            .FirstOrDefault(c => MissionRoomId >= 0
                                ? MissionInteractionReady?.Invoke(c) == true
                                : DynelManager.LocalPlayer.Position.Distance2DFrom(c.Position) < 6);

                        if (dynel == null) return;
                        if (Spell.HasPendingCast || Item.HasPendingUse || PerkAction.List.Any(perk => perk.IsExecuting)) return;

                        if (dynel.Identity.Type == IdentityType.Corpse)
                        {
                            CurrentCorpse = dynel;
                            BindMissionLoot(dynel);
                            Timeout = Time.AONormalTime + 2;
                            CurrentProcess = ProcessState.Opening;
                            new Corpse(dynel).Use();
                            //Chat.WriteLine($"Opening corpse: {dynel.Name}", ChatColor.Yellow);
                        }
                        else if (dynel.Identity.Type == IdentityType.Container)
                        {
                            if (!_settings["Chests"].AsBool()) return;
                            if (DynelManager.LocalPlayer.IsAttacking || DynelManager.NPCs.Any(c => c.IsAttacking && c.FightingTarget?.Identity == DynelManager.LocalPlayer.Identity)) return;

                            var chest = new Chest(dynel);
                            if (chest.IsLocked && !RKmission.CarriedItems.BackpackSnapshotsReady)
                                return;
                            CurrentCorpse = dynel;
                            BindMissionLoot(dynel);
                            Timeout = Time.AONormalTime + 2;
                            if (chest.IsLocked)
                            {
                                var lockPick = RKmission.CarriedItems.FindLockpick();
                                if (MissionRoomId >= 0)
                                {
                                    if (lockPick == null)
                                    {
                                        FinishLockpickFailure(chest.Identity, LockpickOutcome.NoLockPick);
                                        return;
                                    }
                                    LockpickAttempt attempt = MissionLockpick(chest.Identity);
                                    if (attempt.Attempts == 0) attempt.FirstAttempt = Time.AONormalTime;
                                    attempt.Attempts++;
                                    attempt.LastAttempt = Time.AONormalTime;
                                    attempt.Outcome = LockpickOutcome.None;
                                }
                                CurrentProcess = ProcessState.PickingLock;
                                SetMissionSource(chest.Identity, MissionSourceState.Lockpicking);
                                lockPick?.UseOn(chest);
                                //Chat.WriteLine($"Picking lock on chest: {chest.Name}", ChatColor.Yellow);
                            }
                            else
                            {
                                if (MissionRoomId >= 0)
                                {
                                    LockpickAttempt attempt = MissionLockpick(chest.Identity);
                                    attempt.Outcome = attempt.Attempts > 0 ? LockpickOutcome.Success : LockpickOutcome.NotLocked;
                                }
                                CurrentProcess = ProcessState.Opening;
                                chest.Use();
                                //Chat.WriteLine($"Opening chest: {chest.Name}", ChatColor.Yellow);
                            }
                        }
                        break;

                    case ProcessState.PickingLock:
                        if (MissionRoomId >= 0 && Time.AONormalTime > Timeout &&
                            _pendingMissionLoot != MissionIdentity.None &&
                            _missionLockpicks.TryGetValue(_pendingMissionLoot, out LockpickAttempt current))
                        {
                            MissionIdentity identity = _pendingMissionLoot;
                            Dynel pending = DynelManager.GetDynel(identity);
                            if (pending != null && !new Chest(pending).IsLocked)
                                current.Outcome = LockpickOutcome.Success;
                            else if (current.Attempts >= MaxMissionLockpickAttempts ||
                                Time.AONormalTime - current.FirstAttempt > 12)
                            {
                                FinishLockpickFailure(identity, LockpickOutcome.RepeatedFailure);
                                break;
                            }
                            else
                                current.Outcome = LockpickOutcome.TemporaryFailure;
                            _pendingMissionLoot = MissionIdentity.None;
                            _pendingMissionLootOccurrence = 0;
                            CurrentCorpse = null;
                            CurrentProcess = ProcessState.Open_Corpse;
                        }
                        break;
                    case ProcessState.Opening:
                        if (Spell.HasPendingCast || Item.HasPendingUse || PerkAction.List.Any(perk => perk.IsExecuting)) return;
                        if (Time.AONormalTime > Timeout) CurrentProcess = ProcessState.Open_Corpse;
                        break;

                    case ProcessState.Move_To_Inventory:
                        if (Spell.HasPendingCast || Item.HasPendingUse || PerkAction.List.Any(perk => perk.IsExecuting)) return;
                        if (CorpseContainer == null) { CurrentProcess = ProcessState.Open_Corpse; break; }
                        if (MissionRoomId >= 0 && _sourceAcquisition != null)
                        {
                            if (ProcessMissionSourceAcquisition()) return;
                            CurrentProcess = ProcessState.Close_Corpse;
                            break;
                        }
                        var contents = CorpseContainer.Items;
                        if (contents == null) return; // Keep pending ownership until data is available.
                        if (contents.Count < _observedContainerCount) MissionLootProgress++;
                        _observedContainerCount = contents.Count;
                        if (contents.Count == 0)
                        {
                            FinishMissionContainer();
                            CurrentCorpse = null;
                            CorpseContainer = null;
                            CurrentProcess = ProcessState.Open_Corpse;
                            break;
                        }

                        if (Spell.HasPendingCast || Item.HasPendingUse || PerkAction.List.Any(perk => perk.IsExecuting)) return;

                        if (Inventory.NumFreeSlots <= 1)
                        {
                            if (MissionRoomId >= 0 && !IsMissionCriticalLoot(CorpseContainer.Identity))
                            {
                                MissionIdentity skipped = CorpseContainer.Identity;
                                _finishedMissionLoot.Add(skipped);
                                SetMissionSource(skipped, MissionSourceState.SkippedNoCapacity);
                                _missionSkipReasons[skipped] = "no safe main-inventory slot after exact ContainerOpened";
                                if (_pendingMissionLoot == skipped)
                                {
                                    _pendingMissionLoot = MissionIdentity.None;
                                    _pendingMissionLootOccurrence = 0;
                                }
                                Chat.WriteLine($"RKMission: Optional loot {skipped} deferred because the main inventory has no safe free slot.");
                                CurrentCorpse = null;
                                CorpseContainer = null;
                                CurrentProcess = ProcessState.Open_Corpse;
                                break;
                            }
                            CurrentProcess = ProcessState.Move_To_BackPack;
                            return;
                        }

                        var corpseItem = CorpseContainer.Items.FirstOrDefault(i =>
                            CorpseContainer.Identity == MissionObjectiveContainer ||
                            Classify(i) == ItemClassification.Keep);

                        if (corpseItem != null)
                        {
                            if (Classify(corpseItem) == ItemClassification.Keep)
                                RememberSelectedIdentity(corpseItem.UniqueIdentity);
                            if (CorpseContainer.Identity == MissionObjectiveContainer && HasUsableIdentity(corpseItem.UniqueIdentity))
                                _objectiveLootItems.Add(corpseItem.UniqueIdentity);
                            if (_settings["Reverse"].AsBool() && CorpseContainer.Identity != MissionObjectiveContainer) reverseItems.Add(corpseItem.Id);
                            corpseItem.MoveToInventory();

                            CurrentProcess = ProcessState.Move_To_BackPack;
                            return;
                        }

                        if (MissionRoomId < 0 && _settings["Delete"].AsBool() && CorpseContainer.Items.Count > 0)
                        {
                            var delItem = CorpseContainer.Items.FirstOrDefault();
                            if (delItem != null)
                            {
                                delItem.Delete();
                                return;
                            }
                        }

                        CurrentProcess = ProcessState.Close_Corpse;
                        break;

                    case ProcessState.Move_To_BackPack:
                        if (Spell.HasPendingCast || Item.HasPendingUse || PerkAction.List.Any(perk => perk.IsExecuting)) return;

                        // Mission-source acquisition must verify the item in main inventory
                        // before any optional rule-based background sorting can touch it.
                        if (MissionRoomId >= 0 && _sourceAcquisition != null)
                        {
                            _backgroundBagMove = null;
                            CurrentProcess = ProcessState.Move_To_Inventory;
                            break;
                        }

                        if (_backgroundBagMove != null)
                        {
                            Container pendingBag = Inventory.Backpacks.FirstOrDefault(b => b.Identity == _backgroundBagMove.BagIdentity);
                            bool sourceRemoved = !Inventory.Items.Any(i =>
                                i.Slot == _backgroundBagMove.SourceSlot && BackgroundBagItemMatches(i, _backgroundBagMove));
                            int destinationCount = pendingBag?.Items.Count(i => BackgroundBagItemMatches(i, _backgroundBagMove)) ?? 0;
                            if (sourceRemoved && destinationCount > _backgroundBagMove.DestinationCountBefore)
                            {
                                Chat.WriteLine($"RKMission: Background bag move verified '{_backgroundBagMove.ItemName}', id={_backgroundBagMove.Id}, QL={_backgroundBagMove.QualityLevel}, source={_backgroundBagMove.SourceSlot}, destination='{_backgroundBagMove.BagName}' {_backgroundBagMove.BagIdentity} after {_backgroundBagMove.Attempts} attempt(s).");
                                _backgroundBagMove = null;
                                CurrentProcess = ProcessState.Open_Corpse;
                                break;
                            }
                            if (Time.AONormalTime < _backgroundBagMove.SentAt + BackgroundBagMoveConfirmationSeconds)
                                return;
                            if (_backgroundBagMove.Attempts >= MaxBackgroundBagMoveAttempts || pendingBag == null)
                            {
                                Chat.WriteLine($"RKMission: Background bag move deferred '{_backgroundBagMove.ItemName}', id={_backgroundBagMove.Id}, QL={_backgroundBagMove.QualityLevel}, source={_backgroundBagMove.SourceSlot}, destination='{_backgroundBagMove.BagName}' {_backgroundBagMove.BagIdentity}; source removal/destination arrival was not verified after {_backgroundBagMove.Attempts} bounded attempt(s). Sorting will reacquire after a zone or bag refresh.");
                                _deferredBackgroundBagSlots.Add(_backgroundBagMove.SourceSlot);
                                _backgroundBagMove = null;
                                CurrentProcess = ProcessState.Open_Corpse;
                                break;
                            }
                            Item retryItem = Inventory.Items.FirstOrDefault(i =>
                                i.Slot == _backgroundBagMove.SourceSlot && BackgroundBagItemMatches(i, _backgroundBagMove));
                            if (retryItem == null || pendingBag.Items.Count >= 21)
                            {
                                Chat.WriteLine($"RKMission: Background bag move deferred '{_backgroundBagMove.ItemName}', source={_backgroundBagMove.SourceSlot}, destination='{_backgroundBagMove.BagName}' {_backgroundBagMove.BagIdentity}; source or destination changed before verification. Sorting will reacquire after a zone or bag refresh.");
                                _deferredBackgroundBagSlots.Add(_backgroundBagMove.SourceSlot);
                                _backgroundBagMove = null;
                                CurrentProcess = ProcessState.Open_Corpse;
                                break;
                            }
                            _backgroundBagMove.Attempts++;
                            _backgroundBagMove.SentAt = Time.AONormalTime;
                            retryItem.MoveToContainer(pendingBag);
                            return;
                        }

                        var invItemWithBag = Inventory.Items.Where(i => i.Slot.Type == IdentityType.Inventory &&
                            i.UniqueIdentity.Type != IdentityType.Container && !ProtectedMissionItem(i) &&
                            !_deferredBackgroundBagSlots.Contains(i.Slot))
                            .Select(i => new { Item = i, Rule = GetRuleForItem(i) })
                            .FirstOrDefault(x => x.Rule != null && x.Rule.BagName != "");

                        var invItemNoBag = Rules.FirstOrDefault(r => string.IsNullOrEmpty(r.BagName) && Inventory.Items.Count(i => i.Slot.Type == IdentityType.Inventory && i.UniqueIdentity.Type != IdentityType.Container && GetRuleForItem(i) == r) >= Convert.ToInt32(r.Quantity));

                        if (invItemWithBag != null)
                        {
                            if (invItemWithBag.Item != null)
                            {
                                var bag = Inventory.Backpacks
                                    .Where(b => ManagedBagFamily.Matches(b.Name, invItemWithBag.Rule.BagName))
                                    .OrderBy(b => ManagedBagFamily.Order(b.Name, invItemWithBag.Rule.BagName))
                                    .ThenBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
                                    .FirstOrDefault(b => b.Items.Count < 21);

                                if (bag != null)
                                {
                                    _backgroundBagMove = new BackgroundBagMove
                                    {
                                        SourceSlot = invItemWithBag.Item.Slot,
                                        BagIdentity = bag.Identity,
                                        ItemName = invItemWithBag.Item.Name,
                                        BagName = bag.Name,
                                        Id = invItemWithBag.Item.Id,
                                        LowId = invItemWithBag.Item.LowId,
                                        HighId = invItemWithBag.Item.HighId,
                                        QualityLevel = invItemWithBag.Item.QualityLevel,
                                        DestinationCountBefore = bag.Items.Count(i =>
                                            SameItemDetails(i, invItemWithBag.Item)),
                                        Attempts = 1,
                                        SentAt = Time.AONormalTime
                                    };
                                    invItemWithBag.Item.MoveToContainer(bag);
                                }
                                else
                                    CurrentProcess = ProcessState.Move_To_Inventory;
                            }
                            else
                            {
                                UpdateRule(invItemWithBag.Rule);
                                CurrentProcess = ProcessState.Move_To_Inventory;
                            }
                        }
                        else if (invItemNoBag != null)
                        {
                            foreach (Item selected in Inventory.Items.Where(i =>
                                i.Slot.Type == IdentityType.Inventory &&
                                GetMatchingRule(i) == invItemNoBag))
                                RememberSelectedIdentity(selected.UniqueIdentity);
                            Rules.Remove(invItemNoBag);
                            SaveRules();
                            RefreshList();

                            CurrentProcess = ProcessState.Move_To_Inventory;
                            break;
                        }
                        else
                            CurrentProcess = ProcessState.Move_To_Inventory;

                        break;
                    case ProcessState.Move_To_KeepBag:
                        if (Spell.HasPendingCast || Item.HasPendingUse || PerkAction.List.Any(perk => perk.IsExecuting)) return;
                        ListedItemTransfer pendingPlacement = _sourceAcquisition?.Items
                            .FirstOrDefault(item => item.KeepPlacementPending);
                        if (pendingPlacement == null)
                        {
                            _sourceAcquisition = null;
                            _keepPlacementBag = MissionIdentity.None;
                            _keepPlacementBagOpened = false;
                            CurrentProcess = ProcessState.Open_Corpse;
                            break;
                        }
                        if (_keepPlacementBag != pendingPlacement.KeepBag)
                        {
                            _keepPlacementBag = pendingPlacement.KeepBag;
                            _keepPlacementBagOpened = false;
                        }
                        ProcessKeepPlacement(pendingPlacement);
                        break;
                    case ProcessState.Close_Corpse:
                        if (Item.HasPendingUse) return;
                        if (MissionRoomId >= 0 && _sourceAcquisition != null &&
                            Time.AONormalTime < _sourceAcquisition.OpenedAt + LootWindowDwellSeconds) return;
                        int verifiedTransfers = _sourceAcquisition?.Items.Count(item => item.Verified) ?? 0;
                        int selectedTransfers = _sourceAcquisition?.Items.Count ?? 0;
                        int unselectedLeftovers = Math.Max(0, (CorpseContainer?.Items?.Count ?? 0) -
                            (_sourceAcquisition?.Items.Count(item => !item.Verified && !item.NonActionable) ?? 0));
                        if (MissionRoomId >= 0 && _sourceAcquisition != null)
                            Chat.WriteLine($"RKMission: Loot source {_sourceAcquisition.Source} settled after {verifiedTransfers}/{selectedTransfers} selected transfer(s); {unselectedLeftovers} unselected/non-actionable item(s) ignored.");
                        if (_sourceAcquisition?.SkippedNoCapacity == true)
                        {
                            _finishedMissionLoot.Add(_sourceAcquisition.Source);
                            _finishedMissionLootOccurrences[_sourceAcquisition.Source] = _sourceAcquisition.Occurrence;
                            if (_pendingMissionLoot == _sourceAcquisition.Source)
                            {
                                _pendingMissionLoot = MissionIdentity.None;
                                _pendingMissionLootOccurrence = 0;
                            }
                        }
                        else
                            FinishMissionContainer();
                        //Chat.WriteLine("Closing corpse and clearing references.", ChatColor.Yellow);
                        Dynel closingCorpse = CurrentCorpse;
                        CurrentCorpse = null;
                        CorpseContainer = null;
                        bool placeKeeps = _sourceAcquisition?.Items.Any(item => item.KeepPlacementPending) == true;
                        if (!placeKeeps) _sourceAcquisition = null;
                        CurrentProcess = placeKeeps ? ProcessState.Move_To_KeepBag : ProcessState.Open_Corpse;
                        if (closingCorpse != null && closingCorpse.Position.DistanceFrom(DynelManager.LocalPlayer.Position) < 6)
                            closingCorpse.Use();
                        break;
                }
            }
            catch (Exception ex)
            {
                ErrorCatch(ex);
            }
        }

        #endregion

        #region Handle Lists

        private void LoadRules()
        {
            try
            {
                Rules = new List<Rule>();

                string pathToLoad = RulesPath;

                if (File.Exists(LastUsedPathFile))
                {
                    string savedPath = File.ReadAllText(LastUsedPathFile);
                    if (File.Exists(savedPath))
                        pathToLoad = savedPath;
                }

                if (File.Exists(pathToLoad))
                {
                    List<Rule> scopedRules = new List<Rule>();
                    string rulesJson = File.ReadAllText(pathToLoad);
                    scopedRules = JsonConvert.DeserializeObject<List<Rule>>(rulesJson);

                    foreach (var rule in scopedRules)
                    {
                        if (string.IsNullOrEmpty(rule.Name))
                            rule.Name = "Unnamed";

                        if (string.IsNullOrEmpty(rule.Lql))
                            rule.Lql = "1";

                        if (string.IsNullOrEmpty(rule.Hql))
                            rule.Hql = "999";

                        if (string.IsNullOrEmpty(rule.Quantity))
                            rule.Quantity = "999";

                        if (string.IsNullOrEmpty(rule.Exact))
                            rule.Exact = "false";

                        if (string.IsNullOrEmpty(rule.OneEach))
                            rule.OneEach = "false";

                        if (string.IsNullOrEmpty(rule.BagName))
                            rule.BagName = "";

                        Rules.Add(rule);
                    }
                }
            }
            catch (Exception ex)
            {
                ErrorCatch(ex);
            }
        }

        private bool CheckRules(Item item) => GetMatchingRule(item) != null;

        private Rule GetRuleForItem(Item item) =>
            Classify(item) == ItemClassification.Keep ? GetMatchingRule(item) : null;

        internal static bool RuleNameMatches(string itemName, int itemId, string ruleName, bool exact)
        {
            if (string.IsNullOrWhiteSpace(itemName) || string.IsNullOrWhiteSpace(ruleName)) return false;
            if (int.TryParse(ruleName, out int id)) return itemId == id;
            return exact
                ? string.Equals(itemName, ruleName, StringComparison.OrdinalIgnoreCase)
                : ruleName.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .All(word => itemName.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private Rule GetMatchingRule(Item item)
        {
            if (item == null || Rules == null) return null;
            try
            {
                // First matching name/ID retains the original ordered-rule precedence.
                Rule rule = Rules.FirstOrDefault(r =>
                    RuleNameMatches(item.Name, item.Id, r.Name,
                        string.Equals(r.Exact, "true", StringComparison.OrdinalIgnoreCase)));
                if (rule == null || !int.TryParse(rule.Quantity, out int quantity) || quantity < 1 ||
                    !int.TryParse(rule.Lql, out int low) || !int.TryParse(rule.Hql, out int high) ||
                    item.QualityLevel < low || item.QualityLevel > high) return null;
                if (string.Equals(rule.OneEach, "true", StringComparison.OrdinalIgnoreCase))
                {
                    // An item already in the main inventory is allowed to be the
                    // first copy; later copies wait/reject while it is sorted.
                    bool alreadyInInventory = Inventory.Items.Any(existing =>
                        existing.UniqueIdentity == item.UniqueIdentity);
                    IEnumerable<Item> earlierItems = alreadyInInventory
                        ? Inventory.Items.TakeWhile(existing => existing.UniqueIdentity != item.UniqueIdentity)
                        : Inventory.Items;
                    if (earlierItems.Any(existing => existing.Slot.Type == IdentityType.Inventory &&
                        string.Equals(existing.Name, item.Name, StringComparison.OrdinalIgnoreCase))) return null;
                    if (!string.IsNullOrWhiteSpace(rule.BagName) && Inventory.Backpacks
                        .Where(bag => ManagedBagFamily.Matches(bag.Name, rule.BagName))
                        .Any(bag => Inventory.GetContainerItems(bag.Identity).Any(existing =>
                            existing.UniqueIdentity != item.UniqueIdentity &&
                            string.Equals(existing.Name, item.Name, StringComparison.OrdinalIgnoreCase)))) return null;
                }
                return rule;
            }
            catch (Exception ex) { ErrorCatch(ex); return null; }
        }

        private void UpdateRule(Rule rule)
        {
            try
            {
                if (string.IsNullOrEmpty(rule.Quantity)) return;

                if (Convert.ToInt32(rule.Quantity) == 999) return;

                rule.Quantity = (Convert.ToInt32(rule.Quantity) - 1).ToString();

                if (Convert.ToInt32(rule.Quantity) == 0)
                    Rules.Remove(rule);

                SaveRules();
                RefreshList();
            }
            catch (Exception ex)
            {
                ErrorCatch(ex);
            }
        }

        private void SaveRules()
        {
            try
            {
                List<Rule> ScopeRules = new List<Rule>();

                ScopeRules = Rules.ToList();

                string path = null;

                if (settingsWindow == null || !settingsWindow.IsValid)
                    return;

                if (FindSettingsView("SaveAs", out TextInputView text))
                {
                    if (text.Text == null || string.IsNullOrEmpty(text.Text))
                    {
                        if (FindSettingsView("filePath", out DropdownMenu filePath))
                            path = Path.Combine(FolderPath, $"{filePath.GetItemLabel(filePath.GetSelection())}.json");
                    }
                    else
                        path = Path.Combine(FolderPath, $"{text.Text}.json");
                }

                string rulesJson = JsonConvert.SerializeObject(ScopeRules);
                File.WriteAllText(path, rulesJson);
            }
            catch (Exception ex)
            {
                ErrorCatch(ex);
            }
        }

        private void RefreshList()
        {
            try
            {
                if (settingsWindow == null || !settingsWindow.IsValid)
                    return;

                FindSettingsView("ScrollListRoot", out MultiListView _multiListView);

                _multiListView.DeleteAllChildren();

                if (Rules == null || Rules.Count == 0) return;

                for (int i = 0; i < Rules.Count; i++)
                {
                    var r = Rules[i];

                    var entry = View.CreateFromXml(UiDirectory + "\\UI\\ManagerLoot\\ItemEntry.xml");

                    if (entry.FindChild("Index", out TextView index))
                        index.Text = (i + 1).ToString();

                    if (entry.FindChild("Range", out TextView range))
                        range.Text = $"[{r.Lql,3} - {r.Hql,3} ]";

                    if (entry.FindChild("Name", out TextView name))
                        name.Text = r.Name;

                    if (entry.FindChild("Quantity", out TextView qty))
                        qty.Text = r.Quantity.ToString();

                    if (entry.FindChild("Exact", out TextView exact))
                        exact.Text = r.Exact.ToString();

                    if (entry.FindChild("OneEach", out TextView oneEach))
                        oneEach.Text = r.OneEach.ToString();

                    if (entry.FindChild("Bag", out TextView bag))
                        bag.Text = r.BagName ?? "";

                    _multiListView.AddChild(entry, false);
                }
            }
            catch (Exception ex)
            {
                ErrorCatch(ex);
            }
        }

        #endregion

        #region Chat Commands

        private void ManagerCommand(string arg1, string[] arg2, ChatWindow window)
        {
            ShowSettingsTab();
        }

        private void ManagerLootCommand(string command, string[] param, ChatWindow chatWindow)
        {
            if (param.Length < 1)
            {
                Helper_Enable();
            }
        }

        #endregion

        #region Button Clicked

        private void Enable_Disable_Button_Clicked(object sender, ButtonBase e)
        {
            Helper_Enable();
        }

        private void HandleInfoViewClick(object s, ButtonBase button)
        {
            if (_infoWindow?.IsValid == true)
            {
                _infoWindow.Close();
                _infoWindow = null;
                return;
            }

            _infoWindow = Window.CreateFromXml("Info", UiDirectory + "\\UI\\ManagerLoot\\ManagerLootInfoView.xml", windowStyle: WindowStyle.Default, windowFlags: WindowFlags.AutoScale | WindowFlags.NoFade);
            _infoWindow.Show(true);
        }

        private void AddButtonClicked(object sender, ButtonBase e)
        {
            try
            {
                FindSettingsView("TextName", out TextInputView nameInput);
                FindSettingsView("_itemMinQL", out TextInputView minQlInput);
                FindSettingsView("_itemMaxQL", out TextInputView maxQlInput);
                FindSettingsView("_itemQuantity", out TextInputView quantityInput);
                FindSettingsView("BagName", out DropdownMenu bagMenu);
                FindSettingsView("ErrorMessage", out TextView errorMessage);

                string name = nameInput.Text.Trim();
                string minQlStr = minQlInput.Text;
                string maxQlStr = maxQlInput.Text;
                string quantityStr = quantityInput.Text;

                if (string.IsNullOrEmpty(name))
                {
                    errorMessage.Text = "Can't add an empty name";
                    return;
                }

                if (!int.TryParse(minQlStr, out int minQl) || !int.TryParse(maxQlStr, out int maxQl))
                {
                    errorMessage.Text = "Quality entries must be numbers!";
                    return;
                }

                if (minQl <= 0)
                {
                    errorMessage.Text = "Min Quality must be at least 1!";
                    return;
                }

                if (minQl > maxQl)
                {
                    errorMessage.Text = "Min Quality must be less or equal than the high quality!";
                    return;
                }

                if (maxQl > 500)
                {
                    errorMessage.Text = "Max Quality must be 500!";
                    return;
                }

                if (!int.TryParse(quantityStr, out int quantity))
                {
                    errorMessage.Text = "Quantity entries must be numbers!";
                    return;
                }

                if (quantity > 999)
                {
                    errorMessage.Text = "Max Quantity must be no more than 999!";
                    return;
                }


                Rules.Add(new Rule(name, minQlStr, maxQlStr, quantityStr, $"{_settings["Exact"]}", $"{_settings["OneOfEach"]}", string.Equals(bagMenu.GetItemLabel(bagMenu.GetSelection()),
                    "Inventory", StringComparison.OrdinalIgnoreCase) ? string.Empty : bagMenu.GetItemLabel(bagMenu.GetSelection())));

                nameInput.Text = "";
                minQlInput.Text = "1";
                maxQlInput.Text = "500";
                quantityInput.Text = "999";
                _settings["Exact"] = false;
                _settings["OneOfEach"] = false;
                errorMessage.Text = "";

                SaveRules();
                RefreshList();

            }
            catch (Exception ex)
            {
                ErrorCatch(ex);
            }
        }

        private void RemButtonClicked(object sender, ButtonBase e)
        {
            try {
            FindSettingsView("ScrollListRoot", out MultiListView list);

            FindSettingsView("RemoveIndex", out TextInputView removeIndex);
            FindSettingsView("ErrorMessage", out TextView errorMessage);

            if (removeIndex.Text.Trim() == "")
            {
                errorMessage.Text = "Cant remove an empty entry";
                return;
            }

            int index;
            try
            {
                index = Convert.ToInt32(removeIndex.Text) - 1;
            }
            catch
            {
                errorMessage.Text = "Entry must be a number!";
                return;
            }

            if (index < 0 || index >= Rules.Count)
            {
                errorMessage.Text = "Invalid entry!";
                return;
            }

            Rules.RemoveAt(index);

            errorMessage.Text = "";

            SaveRules();
            RefreshList();
            }
            catch (Exception ex)
            {
                ErrorCatch(ex);
            }
        }

        private void New_List_Button_Clicked(object sender, ButtonBase e)
        {
            try {
            if (settingsWindow == null || !settingsWindow.IsValid) return;

            if (FindSettingsView("SaveAs", out TextInputView text))
            {
                if (text.Text == null || string.IsNullOrEmpty(text.Text))
                {
                    Chat.WriteLine($"Enter new List Name!");
                    return;
                }

                File.WriteAllText(LastUsedPathFile, Path.Combine(FolderPath, $"{text.Text}.json"));
                Rules.Clear();

                SaveRules();
                RefreshList();

                Update_List_DropDown();

                text.Text = "";
            }
            }
            catch (Exception ex)
            {
                ErrorCatch(ex);
            }
        }

        private void LoadButtonClicked(object sender, ButtonBase e)
        {
            try {
            if (FindSettingsView("filePath", out DropdownMenu filePath))
            {
                string selectedFile = Path.Combine(FolderPath, $"{filePath.GetItemLabel(filePath.GetSelection())}.json");

                if (selectedFile != null)
                {
                    if (File.Exists(selectedFile))
                    {
                        string rulesJson = File.ReadAllText(selectedFile);
                        Rules = JsonConvert.DeserializeObject<List<Rule>>(rulesJson) ?? new List<Rule>();
                        RefreshList();
                        File.WriteAllText(LastUsedPathFile, selectedFile);
                        CurrentProcess = ProcessState.Load_Backpacks;
                    }
                }
            }
            }
            catch (Exception ex)
            {
                ErrorCatch(ex);
            }
        }

        private void Remove_Button_Clicked(object sender, ButtonBase e)
        {
            if (FindSettingsView("filePath", out DropdownMenu filePath))
            {
                string selectedFile = Path.Combine(FolderPath, $"{filePath.GetItemLabel(filePath.GetSelection())}.json");

                if (selectedFile == LastUsedPathFile) return;

                if (File.Exists(selectedFile))
                {
                    File.Delete(selectedFile);

                    Update_List_DropDown();
                }
            }
        }

        private void OpenTheLootFolder(object s, ButtonBase button)
        {
            Process.Start("explorer.exe", FolderPath);
        }

        private void ToggleFolderScopeClicked(object sender, ButtonBase e)
        {
            bool newValue = !_settings["UseSharedFolder"].AsBool();
            _settings["UseSharedFolder"] = newValue;

            if (FindSettingsView("ToggleFolderScope", out Button toggleBtn))
                toggleBtn.SetLabel($"Use Shared: {newValue}");

            Save();
        }

        private void Clear_List_Button_Clicked(object sender, ButtonBase e)
        {
            try {
            if (settingsWindow == null || !settingsWindow.IsValid)
                return;

            FindSettingsView("ScrollListRoot", out MultiListView _multiListView);

            _multiListView.DeleteAllChildren();

            Rules.Clear();

            SaveRules();
            }
            catch (Exception ex)
            {
                ErrorCatch(ex);
            }
        }

        #endregion

        #region Helpers

        private void MainUI()
        {
            try
            {
                if (_rollerHost == null && settingsWindow?.IsValid == true)
                {
                    Window_Closed_helper();

                    settingsWindow.Close();
                    settingsWindow = null;
                    return;
                }

                if (_rollerHost != null)
                {
                    if (_managerView != null && ReferenceEquals(settingsWindow, _rollerHost.Window) &&
                        settingsWindow?.IsValid == true) return;
                    settingsWindow = _rollerHost.Window;
                    if (settingsWindow?.IsValid != true) return;
                    _managerView = View.CreateFromXml(UiDirectory + "\\UI\\ManagerLoot\\ManagerLootSettingWindow.xml");
                    _rollerHost.AttachManagerLootView(_managerView);
                }
                else
                {
                    settingsWindow = Window.CreateFromXml(PluginName, UiDirectory + "\\UI\\ManagerLoot\\ManagerLootSettingWindow.xml", windowStyle: WindowStyle.Default, windowFlags: WindowFlags.AutoScale | WindowFlags.NoFade);
                    settingsWindow.MoveTo(_settings["MainWindowTopLeftX"].AsFloat(), _settings["MainWindowTopLeftY"].AsFloat());
                }

                if (FindSettingsView("InfoView", out Button infoView))
                    infoView.Clicked = HandleInfoViewClick;

                if (FindSettingsView("Enable_Disable_Button", out Button enableButton))
                {
                    enableButton.SetLabel(EnableString);
                    enableButton.Clicked = Enable_Disable_Button_Clicked;
                }

                if (FindSettingsView("BagName", out DropdownMenu bags))
                {
                    for (uint i = 0; i < 30; i++)
                        bags.DeleteItem(i);

                    bags.AppendItem("Inventory");
                    // The family remains selectable even when only numbered
                    // keep bags exist (or a bag will be added later).
                    bags.AppendItem(ManagedBagFamily.Keep);

                    foreach (var item in Inventory.Backpacks)
                    {
                        if (!string.IsNullOrWhiteSpace(item.Name) &&
                            !string.Equals(item.Name, ManagedBagFamily.Keep, StringComparison.OrdinalIgnoreCase))
                            bags.AppendItem(item.Name);
                    }
                }

                if (FindSettingsView("ScrollListRoot", out MultiListView _multiListView) && FindSettingsView("_itemMinQL", out TextInputView _itemMinQL)
                    && FindSettingsView("_itemMaxQL", out TextInputView _itemMaxQL) && FindSettingsView("_itemQuantity", out TextInputView _itemQuantity))
                {
                    _itemMinQL.Text = "1";
                    _itemMaxQL.Text = "500";
                    _itemQuantity.Text = "999";
                    _settings["Exact"] = false;
                    _settings["OneOfEach"] = false;

                    RefreshList();
                }

                if (FindSettingsView("buttonAdd", out Button addbut))
                    addbut.Clicked += AddButtonClicked;

                if (FindSettingsView("buttonDel", out Button rembut))
                    rembut.Clicked += RemButtonClicked;

                if (FindSettingsView("buttonNew", out Button newbut))
                    newbut.Clicked += New_List_Button_Clicked;

                if (FindSettingsView("filePath", out DropdownMenu filePath))
                    Update_List_DropDown();

                if (FindSettingsView("buttonLoad", out Button loadbut))
                    loadbut.Clicked += LoadButtonClicked;

                if (FindSettingsView("buttonRemove", out Button buttonRemove))
                    buttonRemove.Clicked += Remove_Button_Clicked;

                if (FindSettingsView("OpenLootFolder", out Button openLootFolder))
                    openLootFolder.Clicked = OpenTheLootFolder;

                if (FindSettingsView("ToggleFolderScope", out Button toggleFolderScope))
                {
                    toggleFolderScope.Clicked = ToggleFolderScopeClicked;
                    toggleFolderScope.SetLabel($"Use Shared: {_settings["UseSharedFolder"].AsBool()}");
                }

                if (FindSettingsView("ClearList", out Button clearButton))
                    clearButton.Clicked += Clear_List_Button_Clicked;

                if (FindSettingsView("VersionNumber", out TextView version))
                    version.Text = $"Version {Version_Number}";

                if (settingsWindow == null) { Chat.WriteLine("settingsWindow == nul"); return; }

                if (_rollerHost == null) settingsWindow.Show(true);

            }
            catch (Exception ex)
            {
                ErrorCatch(ex);
            }
        }

        private void Update_List_DropDown()
        {
            if (FindSettingsView("filePath", out DropdownMenu filePath))
            {
                if (!string.IsNullOrEmpty(FolderPath))
                {
                    var lists = Directory.GetFiles(FolderPath).Where(n => Path.GetFileNameWithoutExtension(n) != "Config"
                    && Path.GetFileNameWithoutExtension(n) != "log" && Path.GetFileName(n) != "LastUsedPath.txt").ToList();

                    if (lists.Count > 0)
                    {
                        for (uint i = (uint)lists.Count + 1; i > 0; i--)
                            filePath.DeleteItem(i - 1);

                        foreach (var name in lists)
                        {
                            if (name == null) continue;

                            filePath.AppendItem(Path.GetFileNameWithoutExtension(name));
                        }

                        if (File.Exists(LastUsedPathFile))
                        {
                            var lastfile = (uint)lists.IndexOf(File.ReadAllText(LastUsedPathFile));
                            filePath.SelectByIndex(lastfile, true);
                        }
                    }
                }
            }
        }

        public void Teardown()
        {
            if (_rollerHost != null)
            {
                _rollerHost.WindowReady -= OnHostWindowReady;
                _rollerHost.EnsureManagerLootView = null;
            }
            Save();
            UnsubEvents();
        }

        private void Save()
        {
            _settingsToSave.ForEach(settings => settings.Save());
        }
        private void InitializeBackpackInfo()
        {
            try {
            if (Time.AONormalTime >= ZoneDelay)
            {
                var managedBags = Inventory.Backpacks.Where(bag => bag != null &&
                    (ManagedBagFamily.IsProtected(bag.Name) || Rules.Any(rule =>
                        !string.IsNullOrWhiteSpace(rule.BagName) &&
                        ManagedBagFamily.Matches(bag.Name, rule.BagName)))).ToList();
                var carried = new HashSet<MissionIdentity>(managedBags.Select(bag => bag.Identity));
                foreach (MissionIdentity removed in _managedBagSnapshots.Keys
                    .Where(identity => !carried.Contains(identity)).ToList())
                    _managedBagSnapshots.Remove(removed);
                foreach (var bag in managedBags)
                {
                    if (!_managedBagSnapshots.TryGetValue(bag.Identity, out ManagedBagSnapshot snapshot))
                        _managedBagSnapshots[bag.Identity] = snapshot = new ManagedBagSnapshot();
                    if (!snapshot.Confirmed && Inventory.GetContainerItems(bag.Identity).Any())
                        snapshot.Confirmed = true;
                }

                double now = Time.AONormalTime;
                if (_waitingForManagedBag != MissionIdentity.None &&
                    _managedBagSnapshots.TryGetValue(_waitingForManagedBag, out ManagedBagSnapshot waiting) &&
                    !waiting.Confirmed && !waiting.Exhausted)
                {
                    if (now < waiting.SentAt + ManagedBagResponseSeconds) return;
                    if (waiting.Attempts >= MaxManagedBagLoadAttempts) waiting.Exhausted = true;
                    _waitingForManagedBag = MissionIdentity.None;
                }
                if (_managedBagSnapshots.Values.All(snapshot => snapshot.Confirmed || snapshot.Exhausted))
                { CurrentProcess = ProcessState.Open_Corpse; return; }
                if (Item.HasPendingUse) return;

                var next = _managedBagSnapshots.First(pair => !pair.Value.Confirmed && !pair.Value.Exhausted);
                Item bagItem = Inventory.Items.FirstOrDefault(item => item != null &&
                    item.UniqueIdentity == next.Key);
                if (bagItem == null) { next.Value.Exhausted = true; return; }
                bagItem.Use();
                next.Value.Attempts++;
                next.Value.SentAt = now;
                _waitingForManagedBag = next.Key;
            }
            }
            catch (Exception ex)
            {
                ErrorCatch(ex);
            }
        }

        private void Helper_Enable()
        {
            _settings["Enable"] = !_settings["Enable"].AsBool();
            EnableString = _settings["Enable"].AsBool() ? "Disable" : "Enable";

            if (settingsWindow?.IsValid == true && FindSettingsView("Enable_Disable_Button", out Button enableButton))
                enableButton.SetLabel(EnableString);

            if (_settings["Enable"].AsBool())
            {
                Chat.WriteLine($"{PluginName} Enable");
                Game.OnUpdate += OnUpdate;
                Game.TeleportEnded += TeleportEnded;
                DynelManager.DynelSpawned += DynelSpawned;
                Network.N3MessageReceived += N3MessageReceived;
                Inventory.ContainerOpened += ContainerOpened;
            }
            else
            {
                Chat.WriteLine($"{PluginName} disabled");
                Game.OnUpdate -= OnUpdate;
                Game.TeleportEnded -= TeleportEnded;
                DynelManager.DynelSpawned -= DynelSpawned;
                Network.N3MessageReceived -= N3MessageReceived;
                Inventory.ContainerOpened -= ContainerOpened;
            }

            Save();
        }

        private void Window_Closed_helper()
        {
            if (_rollerHost != null) return;
            if (settingsWindow?.IsValid == true)
            {
                Rect frame = settingsWindow.GetFrame();
                _settings["MainWindowTopLeftX"] = frame.MinX;
                _settings["MainWindowTopLeftY"] = frame.MinY;
                Save();
            }
        }

        private void UnsubEvents()
        {
            Game.OnUpdate -= OnUpdate;
            Game.TeleportEnded -= TeleportEnded;
            DynelManager.DynelSpawned -= DynelSpawned;
            Network.N3MessageReceived -= N3MessageReceived;
            Inventory.ContainerOpened -= ContainerOpened;
            UIController.WindowDeleted -= Windowclosed;
            Network.N3MessageSent -= N3MessageSent;
        }

        private enum ProcessState { Load_Backpacks, Open_Corpse, Opening, Move_To_Inventory, Move_To_BackPack, Move_To_KeepBag, Close_Corpse, Closing, PickingLock, Waiting }

        private void ErrorCatch(Exception ex)
        {
            var output = ex.Message + Environment.NewLine + "   at " + ex.TargetSite?.DeclaringType?.FullName + "." + ex.TargetSite?.Name;

            if (!ErrorMessages.Contains(output))
                ErrorMessages.Add(output);

            if (settingsWindow != null && settingsWindow.IsValid && FindSettingsView("Errors", out View errorView))
                PopulateErrorView(errorView);
        }

        private void PopulateErrorView(View errorView)
        {
            errorView.DeleteAllChildren();

            if (ErrorMessages != null && ErrorMessages.Count > 0)
            {
                foreach (var error in ErrorMessages)
                {
                    var parts = error.Split(new[] { "   at " }, StringSplitOptions.None);

                    View xmlRoot = View.CreateFromXml($"{UiDirectory}\\UI\\ManagerLoot\\ErrorRow.xml");
                    xmlRoot.FindChild("TextLabel", out TextView labelView);
                    labelView.Text = parts[0];
                    //labelView.SetColor(Color.Red);
                    errorView.AddChild(xmlRoot, true);

                    if (parts.Length > 1)
                    {
                        View methodRoot = View.CreateFromXml($"{UiDirectory}\\UI\\ManagerLoot\\ErrorRow.xml");
                        methodRoot.FindChild("TextLabel", out TextView methodLabel);
                        methodLabel.Text = "at " + parts[1];
                        errorView.AddChild(methodRoot, true);
                    }
                }
            }
        }

        #endregion
    }
}
