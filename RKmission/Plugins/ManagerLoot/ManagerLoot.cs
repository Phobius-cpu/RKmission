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
        private readonly HashSet<MissionIdentity> _finishedMissionLoot = new HashSet<MissionIdentity>();
        private enum MissionSourceState
        {
            Discovered, PendingApproach, Opening, Lockpicking, Looting, Completed,
            SkippedUnreachable, SkippedInsufficientSkill, SkippedNoLockpick,
            SkippedRepeatedFailure, SkippedNoCapacity, CriticalBlocked
        }
        private readonly Dictionary<MissionIdentity, MissionSourceState> _missionSources =
            new Dictionary<MissionIdentity, MissionSourceState>();
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
        private readonly HashSet<MissionIdentity> _rememberedMissionItems = new HashSet<MissionIdentity>();
        private readonly HashSet<MissionIdentity> _selectedItems = new HashSet<MissionIdentity>();
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
        // RKMission limits the original loot state machine to the room being cleared.
        public int MissionRoomId { get; private set; } = -1;
        public Func<Dynel, int, bool> MissionRoomContains { get; set; }
        public Func<int, IEnumerable<Dynel>> MissionRoomDynels { get; set; }
        public Func<Dynel, bool> MissionLootAllowed { get; set; }
        // Independent of combat/phase permissions: an objective is final work,
        // even if it entered the discovery ledger before its metadata arrived.
        public Func<MissionIdentity, bool> MissionLootReserved { get; set; }
        public Func<Item, bool> MissionItemProtected { get; set; }
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

        public void EndMissionRoom()
        {
            MissionRoomId = -1;
            _pendingMissionLoot = MissionIdentity.None; // Explicitly release ownership on stop/exit.
            if (_enabledForMission && _settings != null && _settings["Enable"].AsBool())
                Helper_Enable();
            _enabledForMission = false;
        }

        public void ResetMissionLootSkips()
        {
            _unreachableMissionLoot.Clear();
            _seenMissionLoot.Clear();
            _finishedMissionLoot.Clear();
            _missionSources.Clear();
            MissionLootProgress = 0;
            _observedContainerCount = 0;
            _objectiveLootItems.Clear();
            _missionLockpicks.Clear();
            _missionLootRooms.Clear();
            _pendingMissionLoot = MissionIdentity.None;
            openedContainers.Clear();
            MissionObjectiveContainer = MissionIdentity.None;
            CurrentCorpse = null;
            CorpseContainer = null;
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
        public void ProtectPendingHandIn(Item item)
        {
            if (item != null) _rememberedMissionItems.Add(item.UniqueIdentity);
        }
        public void RememberMissionCriticalInventory()
        {
            foreach (Item item in Inventory.Items.Where(i => i.Slot.Type == IdentityType.Inventory))
                if (ProtectedMissionItem(item)) _rememberedMissionItems.Add(item.UniqueIdentity);
        }
        private bool ProtectedMissionItem(Item item) =>
            item.UniqueIdentity.Type == IdentityType.MissionKey ||
            _rememberedMissionItems.Contains(item.UniqueIdentity) ||
            (MissionItemProtected?.Invoke(item) ?? false);

        // Classification is the only rule decision exposed to RKMission. Reward origin
        // does not confer permanent value; it only explains why an unlisted item may
        // already be in inventory. No classification performs disposal.
        public ItemClassification Classify(Item item, bool acquiredMissionReward = false)
        {
            if (item == null) return ItemClassification.Unknown;
            if (ProtectedMissionItem(item) || Inventory.Backpacks.Any(bag =>
                ManagedBagFamily.IsProtected(bag.Name) &&
                Inventory.GetContainerItems(bag.Identity).Any(stored =>
                    stored.UniqueIdentity == item.UniqueIdentity)))
                return ItemClassification.Protected;
            if (_selectedItems.Contains(item.UniqueIdentity)) return ItemClassification.Keep;
            if (_settings == null || Rules == null) return ItemClassification.Unknown;
            bool listed = GetMatchingRule(item) != null;
            if (acquiredMissionReward) return listed ? ItemClassification.Keep : ItemClassification.Reject;
            return (_settings["Reverse"].AsBool() ? !listed : listed)
                ? ItemClassification.Keep : ItemClassification.Reject;
        }
        public bool HasUnprocessedMissionLoot(int roomId, Func<Dynel, bool> include = null) =>
            !IgnoreOrdinaryMissionLoot && (MissionRoomDynels?.Invoke(roomId) ?? DynelManager.AllDynels).Any(x =>
                (x.Identity.Type == IdentityType.Corpse || x.Identity.Type == IdentityType.Container) &&
                IsInMissionRoom(x, roomId) && !_finishedMissionLoot.Contains(x.Identity) &&
                !_unreachableMissionLoot.Contains(x.Identity) &&
                (include == null || include(x)));

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
            if (_pendingMissionLoot == identity && CorpseContainer == null) _pendingMissionLoot = MissionIdentity.None;
        }

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
                && !_finishedMissionLoot.Contains(x.Identity))
            .OrderBy(x => x.Identity == _pendingMissionLoot ? 0 : 1)
            .ThenBy(x => x.Identity.Type == IdentityType.Corpse ? 0 : 1)
            .ThenBy(x => x.DistanceFrom(DynelManager.LocalPlayer)).FirstOrDefault();
            if (selected != null && _missionSources.TryGetValue(selected.Identity, out MissionSourceState state) &&
                state == MissionSourceState.Discovered)
                SetMissionSource(selected.Identity, MissionSourceState.PendingApproach);
            return selected;
        }

        private void FinishMissionContainer()
        {
            if (MissionRoomId >= 0 && CorpseContainer != null)
            {
                _unreachableMissionLoot.Remove(CorpseContainer.Identity);
                if (_finishedMissionLoot.Add(CorpseContainer.Identity))
                    Chat.WriteLine($"RKMission: Loot processing finished {CorpseContainer.Identity}.");
                SetMissionSource(CorpseContainer.Identity, MissionSourceState.Completed);
                if (_pendingMissionLoot == CorpseContainer.Identity) _pendingMissionLoot = MissionIdentity.None;
            }
        }

        private void TrackMissionLoot(MissionIdentity identity, int roomId)
        {
            _seenMissionLoot.Add(identity);
            _missionLootRooms[identity] = roomId;
            if (!_missionSources.ContainsKey(identity)) SetMissionSource(identity, MissionSourceState.Discovered);
        }

        private void BindMissionLoot(Dynel dynel)
        {
            if (MissionRoomId < 0) return;
            _pendingMissionLoot = dynel.Identity;
            TrackMissionLoot(dynel.Identity, MissionRoomId);
            SetMissionSource(dynel.Identity, MissionSourceState.Opening);
        }

        public void BeginMissionObjectiveLoot(MissionIdentity identity)
        {
            if (MissionObjectiveContainer == identity) return;
            MissionObjectiveContainer = identity;
            // Earlier ordinary rules may have skipped it or left the quest
            // item behind before objective metadata arrived. Retry once as final work.
            _unreachableMissionLoot.Remove(identity);
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
            CurrentProcess == ProcessState.Move_To_Inventory || CurrentProcess == ProcessState.Move_To_BackPack ||
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
            if (e is ChatTextMessage chat) ObserveLockpickFeedback(chat.Text);
            else if (e is FormatFeedbackMessage feedback) ObserveLockpickFeedback(feedback.Message);

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

        private void ContainerOpened(object sender, Container container)
        {
            if (!_settings["Enable"].AsBool()) return;

            if (container.Identity.Type != IdentityType.Corpse && container.Identity.Type != IdentityType.Container) return;
            if (Inventory.Items.Any(i => i.UniqueIdentity == container.Identity)) return;

            if (MissionRoomId >= 0)
            {
                // A late response still belongs to this exact pending identity,
                // even after timeout or the world corpse disappearing.
                if (_pendingMissionLoot != container.Identity) return;
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
            _observedContainerCount = container.Items?.Count ?? 0;
            CurrentCorpse = DynelManager.GetDynel(container.Identity);
            SetMissionSource(container.Identity, MissionSourceState.Looting);
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

                        if (Inventory.Items.Where(i => i.Slot.Type == IdentityType.Inventory && i.UniqueIdentity.Type != IdentityType.Container && !ProtectedMissionItem(i)).Select(i => new { Item = i, Rule = GetRuleForItem(i) }).Any(x => x.Rule != null && x.Rule.BagName != "" &&
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
                            .ThenBy(d => d.Position.DistanceFrom(DynelManager.LocalPlayer.Position)).FirstOrDefault(c => DynelManager.LocalPlayer.Position.Distance2DFrom(c.Position) < 6);

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
                            CurrentCorpse = dynel;
                            BindMissionLoot(dynel);
                            Timeout = Time.AONormalTime + 2;
                            if (chest.IsLocked)
                            {
                                var lockPick = Inventory.Items.FirstOrDefault(p => p.Name == "Lock Pick");
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
                                if (_pendingMissionLoot == skipped) _pendingMissionLoot = MissionIdentity.None;
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
                                _selectedItems.Add(corpseItem.UniqueIdentity);
                            if (CorpseContainer.Identity == MissionObjectiveContainer) _objectiveLootItems.Add(corpseItem.UniqueIdentity);
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

                        var invItemWithBag = Inventory.Items.Where(i => i.Slot.Type == IdentityType.Inventory && i.UniqueIdentity.Type != IdentityType.Container && !ProtectedMissionItem(i)).Select(i => new { Item = i, Rule = GetRuleForItem(i) }).FirstOrDefault(x => x.Rule != null && x.Rule.BagName != "");

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
                                _selectedItems.Add(selected.UniqueIdentity);
                            Rules.Remove(invItemNoBag);
                            SaveRules();
                            RefreshList();

                            CurrentProcess = ProcessState.Move_To_Inventory;
                            break;
                        }
                        else
                            CurrentProcess = ProcessState.Move_To_Inventory;

                        break;
                    case ProcessState.Close_Corpse:
                        if (Item.HasPendingUse) return;
                        FinishMissionContainer();
                        //Chat.WriteLine("Closing corpse and clearing references.", ChatColor.Yellow);
                        Dynel closingCorpse = CurrentCorpse;
                        CurrentCorpse = null;
                        CorpseContainer = null;
                        CurrentProcess = ProcessState.Open_Corpse;
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

        private Rule GetMatchingRule(Item item)
        {
            if (item == null || Rules == null) return null;
            try
            {
                // First matching name/ID retains the original ordered-rule precedence.
                Rule rule = Rules.FirstOrDefault(r =>
                    !string.IsNullOrWhiteSpace(r.Name) &&
                    (int.TryParse(r.Name, out int id) ? item.Id == id :
                    string.Equals(r.Exact, "true", StringComparison.OrdinalIgnoreCase)
                        ? string.Equals(item.Name, r.Name, StringComparison.OrdinalIgnoreCase)
                        : r.Name.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                            .All(word => item.Name.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)));
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

                    foreach (var item in Inventory.Backpacks)
                    {
                        if (!string.IsNullOrWhiteSpace(item.Name))
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
                var lootBags = Inventory.Backpacks.Where(bag =>
                    ManagedBagFamily.IsProtected(bag.Name) ||
                    Rules.Any(r => ManagedBagFamily.Matches(bag.Name, r.BagName))).ToList();

                foreach (var item in Inventory.Items)
                {
                    if (lootBags.Any(bag => bag.Identity.Instance == item.UniqueIdentity.Instance))
                    {
                        item?.Use(); // Open
                        item?.Use(); // Close
                    }
                }
                CurrentProcess = ProcessState.Open_Corpse;
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

        private enum ProcessState { Load_Backpacks, Open_Corpse, Opening, Move_To_Inventory, Move_To_BackPack, Close_Corpse, Closing, PickingLock, Waiting }

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
