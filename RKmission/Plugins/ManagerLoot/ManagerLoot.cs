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
    // Hosted by RKmission's single AO# entry point.
    public class ManagerLoot
    {
        private const string PluginName = "ManagerLoot";
        private string _embeddedPluginDirectory;
        private string UiDirectory => _embeddedPluginDirectory;

        public void RunEmbedded(string pluginDirectory)
        {
            _embeddedPluginDirectory = pluginDirectory;
            Run();
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
        // RKMission limits the original loot state machine to the room being cleared.
        public int MissionRoomId { get; private set; } = -1;
        public Func<Dynel, int, bool> MissionRoomContains { get; set; }
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
            if (_enabledForMission && _settings != null && _settings["Enable"].AsBool())
                Helper_Enable();
            _enabledForMission = false;
        }

        public void ResetMissionLootSkips() => _unreachableMissionLoot.Clear();
        public int SkippedMissionLootCount => _unreachableMissionLoot.Count;

        public void SkipUnreachableMissionLoot(MissionIdentity identity)
        {
            _unreachableMissionLoot.Add(identity);
            if (CurrentCorpse?.Identity == identity && CorpseContainer == null)
            {
                CurrentCorpse = null;
                CurrentProcess = ProcessState.Open_Corpse;
            }
        }

        public Dynel NextMissionLoot(int roomId) => DynelManager.AllDynels
            .Where(x => (x.Identity.Type == IdentityType.Corpse || x.Identity.Type == IdentityType.Container)
                && IsInMissionRoom(x, roomId)
                && !_unreachableMissionLoot.Contains(x.Identity)
                && !openedContainers.ContainsKey(x.Identity.Instance))
            .OrderBy(x => x.DistanceFrom(DynelManager.LocalPlayer)).FirstOrDefault();

        private bool IsInMissionRoom(Dynel dynel, int roomId) =>
            dynel.Room?.Instance == roomId ||
            MissionRoomContains?.Invoke(dynel, roomId) == true;

        public bool IsProcessingMissionLoot => CorpseContainer != null ||
            CurrentProcess == ProcessState.Opening || CurrentProcess == ProcessState.PickingLock ||
            CurrentProcess == ProcessState.Move_To_Inventory || CurrentProcess == ProcessState.Move_To_BackPack ||
            CurrentProcess == ProcessState.Close_Corpse;
        public MissionIdentity WaitingMissionLootIdentity =>
            MissionRoomId >= 0 && CurrentProcess == ProcessState.Opening &&
            CorpseContainer == null ? CurrentCorpse?.Identity ?? MissionIdentity.None : MissionIdentity.None;
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
                Chat.WriteLine($"/{PluginName} to open/close UI. /lm to enable/disable");
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

            if (settingsWindow?.IsValid == true && settingsWindow.FindView("Enable_Disable_Button", out Button enableButton))
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

            switch (e.N3MessageType)
            {
                case N3MessageType.ContainerAddItem:
                    var contAddItem = (ContainerAddItem)e;
                    // target is the localplay identity, soruce is where is came from but corpses are also labeled as backpacks, Backpack:820000. so not useful.
                    //Chat.WriteLine($"ContainerAddItem -> Source={contAddItem.Source}, Target={contAddItem.Target}, Slot={contAddItem.Slot}");
                    if (contAddItem.Source == IdentityType.Inventory) return;
                    foreach (var item in Inventory.Items.Where(i => i.Slot.Type == IdentityType.Inventory)) //&& i.Slot.Instance == contAddItem.Slot slot is always 111 so it can not match the item slot
                    {
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

            Chat.WriteLine($"Container = {container.Identity.Type} ");

            if (_settings["Print"].AsBool())
                if (container.Identity.Type == IdentityType.Container)
                {
                    foreach (var item in container.Items)
                    {
                        Chat.WriteLine($"{item.Name}, {item.Id}, {item.QualityLevel}, {item.UniqueIdentity}");
                    }
                }

            if (CurrentProcess != ProcessState.Opening) return;

            if (Inventory.Backpacks.Any(b => b.Identity == container.Identity)) return;

            if (!openedContainers.ContainsKey(container.Identity.Instance))
                openedContainers.Add(container.Identity.Instance, Time.AONormalTime);
            else
                openedContainers[container.Identity.Instance] = Time.AONormalTime;

            CorpseContainer = container;
            CurrentCorpse = DynelManager.AllDynels.FirstOrDefault(x => x.Identity.Instance == container.Identity.Instance);

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

                if (MissionRoomId < 0 && _settings["DisableIfEmptyList"].AsBool() &&
                    Rules != null && Rules.Count == 0 && _settings["Enable"].AsBool())
                {
                    _settings["Enable"] = false;
                    _settings["DisableIfEmptyList"] = false;
                    EnableString = "Enable";

                    if (settingsWindow?.IsValid == true && settingsWindow.FindView("Enable_Disable_Button", out Button enableButton))
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

                switch (CurrentProcess)
                {
                    case ProcessState.Load_Backpacks:
                        // Chat.WriteLine("Loading backpack info.", ChatColor.Yellow);
                        InitializeBackpackInfo();
                        CurrentProcess = ProcessState.Open_Corpse;
                        break;
                    case ProcessState.Open_Corpse:
                        if (CorpseContainer != null) { CurrentProcess = ProcessState.Move_To_Inventory; return; }

                        if (Inventory.Items.Where(i => i.Slot.Type == IdentityType.Inventory && i.UniqueIdentity.Type != IdentityType.Container).Select(i => new { Item = i, Rule = GetRuleForItem(i) }).FirstOrDefault(x => x.Rule != null && x.Rule.BagName != "")?.Item != null)
                        { CurrentProcess = ProcessState.Move_To_BackPack; return; }

                        if (Spell.HasPendingCast || Item.HasPendingUse || PerkAction.List.Any(perk => perk.IsExecuting)) return;

                        var dynel = DynelManager.AllDynels.Where(c => !openedContainers.ContainsKey(c.Identity.Instance)
                        && !_unreachableMissionLoot.Contains(c.Identity)
                        && (c.Identity.Type == IdentityType.Container || c.Identity.Type == IdentityType.Corpse)
                        && (MissionRoomId < 0 || IsInMissionRoom(c, MissionRoomId)))
                            .OrderBy(d => d.Position.DistanceFrom(DynelManager.LocalPlayer.Position)).FirstOrDefault(c => DynelManager.LocalPlayer.Position.Distance2DFrom(c.Position) < 6);

                        if (dynel == null) return;
                        if (Spell.HasPendingCast || Item.HasPendingUse || PerkAction.List.Any(perk => perk.IsExecuting)) return;

                        if (dynel.Identity.Type == IdentityType.Corpse)
                        {
                            CurrentCorpse = dynel;
                            new Corpse(dynel).Use();
                            //Chat.WriteLine($"Opening corpse: {dynel.Name}", ChatColor.Yellow);
                            Timeout = Time.AONormalTime + 2;
                            CurrentProcess = ProcessState.Opening;
                        }
                        else if (dynel.Identity.Type == IdentityType.Container)
                        {
                            if (!_settings["Chests"].AsBool()) return;
                            if (DynelManager.LocalPlayer.IsAttacking || DynelManager.NPCs.Any(c => c.IsAttacking && c.FightingTarget?.Identity == DynelManager.LocalPlayer.Identity)) return;

                            var chest = new Chest(dynel);
                            if (chest.IsLocked)
                            {
                                var lockPick = Inventory.Items.FirstOrDefault(p => p.Name == "Lock Pick");
                                lockPick?.UseOn(chest);
                                //Chat.WriteLine($"Picking lock on chest: {chest.Name}", ChatColor.Yellow);
                                Timeout = Time.AONormalTime + 2;
                                CurrentProcess = ProcessState.PickingLock;
                            }
                            else
                            {
                                chest.Use();
                                //Chat.WriteLine($"Opening chest: {chest.Name}", ChatColor.Yellow);
                                Timeout = Time.AONormalTime + 2;
                                CurrentProcess = ProcessState.Opening;
                            }
                        }
                        break;

                    case ProcessState.Opening:
                    case ProcessState.PickingLock:
                        if (Spell.HasPendingCast || Item.HasPendingUse || PerkAction.List.Any(perk => perk.IsExecuting)) return;
                        if (Time.AONormalTime > Timeout) CurrentProcess = ProcessState.Open_Corpse;
                        break;

                    case ProcessState.Move_To_Inventory:
                        if (CorpseContainer == null || CorpseContainer.Items == null || CorpseContainer.Items.Count == 0)
                        {
                            CurrentCorpse = null;
                            CorpseContainer = null;
                            CurrentProcess = ProcessState.Open_Corpse;
                            break;
                        }

                        if (Spell.HasPendingCast || Item.HasPendingUse || PerkAction.List.Any(perk => perk.IsExecuting)) return;

                        if (Inventory.NumFreeSlots <= 1)
                        {
                            CurrentProcess = ProcessState.Move_To_BackPack;
                            return;
                        }

                        var corpseItem = CorpseContainer.Items.FirstOrDefault(i => (!_settings["Reverse"].AsBool() && CheckRules(i)) || (_settings["Reverse"].AsBool() && !CheckRules(i)));

                        if (corpseItem != null)
                        {
                            if (_settings["Reverse"].AsBool()) reverseItems.Add(corpseItem.Id);
                            corpseItem.MoveToInventory();

                            CurrentProcess = ProcessState.Move_To_BackPack;
                            return;
                        }

                        if (_settings["Delete"].AsBool() && CorpseContainer.Items.Count > 0)
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

                        var invItemWithBag = Inventory.Items.Where(i => i.Slot.Type == IdentityType.Inventory && i.UniqueIdentity.Type != IdentityType.Container).Select(i => new { Item = i, Rule = GetRuleForItem(i) }).FirstOrDefault(x => x.Rule != null && x.Rule.BagName != "");

                        var invItemNoBag = Rules.FirstOrDefault(r => string.IsNullOrEmpty(r.BagName) && Inventory.Items.Count(i => i.Slot.Type == IdentityType.Inventory && i.UniqueIdentity.Type != IdentityType.Container && GetRuleForItem(i) == r) >= Convert.ToInt32(r.Quantity));

                        if (invItemWithBag != null)
                        {
                            if (invItemWithBag.Item != null)
                            {
                                var bag = Inventory.Backpacks.OrderBy(b => b.Name).FirstOrDefault(b => b.Name == invItemWithBag.Rule.BagName && b.Items.Count < 21);

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
                        //Chat.WriteLine("Closing corpse and clearing references.", ChatColor.Yellow);
                        if (CurrentCorpse != null && CurrentCorpse.Position.DistanceFrom(DynelManager.LocalPlayer.Position) < 6)
                        {
                            CurrentCorpse?.Use();
                            CurrentCorpse = null;
                            CorpseContainer = null;
                        }

                        CurrentProcess = ProcessState.Open_Corpse;
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

        private bool CheckRules(Item item)
        {
            try
            {
                var rule = Rules.FirstOrDefault(r =>
                {
                    if (string.IsNullOrEmpty(r.Name))
                        return false;

                    if (int.TryParse(r.Name, out int id))
                        return item.Id == id;

                    if (!string.IsNullOrEmpty(r.Exact) && r.Exact == "true")
                        return string.Equals(item.Name, r.Name, StringComparison.OrdinalIgnoreCase);

                    string[] words = r.Name.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (string word in words)
                    {
                        if (item.Name.IndexOf(word, StringComparison.OrdinalIgnoreCase) < 0)
                            return false;
                    }

                    return true;
                });

                if (rule == null)
                    return false;

                if (string.IsNullOrEmpty(rule.Quantity))
                    return false;

                if (Convert.ToInt32(rule.Quantity) < 1)
                    return false;

                if (string.IsNullOrEmpty(rule.Lql) || string.IsNullOrEmpty(rule.Hql))
                    return false;

                if (item.QualityLevel < Convert.ToInt32(rule.Lql) || item.QualityLevel > Convert.ToInt32(rule.Hql))
                    return false;

                if (!string.IsNullOrEmpty(rule.OneEach) && rule.OneEach == "true")
                {
                    foreach (var invItem in Inventory.Items.Where(c => c.Slot.Type == IdentityType.Inventory))
                    {
                        if (string.Equals(invItem.Name, item.Name, StringComparison.OrdinalIgnoreCase))
                            return false;
                    }

                    if (!string.IsNullOrEmpty(rule.BagName))
                    {
                        foreach (var backpack in Inventory.Backpacks.Where(b => b.Name.Contains(rule.BagName)))
                        {
                            foreach (var containerItem in Inventory.GetContainerItems(backpack.Identity))
                            {
                                if (string.Equals(containerItem.Name, item.Name, StringComparison.OrdinalIgnoreCase))
                                    return false;
                            }
                        }
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                ErrorCatch(ex);
                return false;
            }
        }

        private Rule GetRuleForItem(Item item)
        {
            try
            {
                var rule = Rules.FirstOrDefault(r =>
                {
                    if (string.IsNullOrEmpty(r.Name))
                        return false;

                    if (int.TryParse(r.Name, out int id))
                        return item.Id == id;

                    if (!string.IsNullOrEmpty(r.Exact) && r.Exact == "true")
                        return string.Equals(item.Name, r.Name, StringComparison.OrdinalIgnoreCase);

                    if (item.Name.IndexOf(r.Name, StringComparison.OrdinalIgnoreCase) < 0)
                        return false;


                    return true;
                });

                if (rule == null)
                    return null;

                if (string.IsNullOrEmpty(rule.Quantity))
                    return null;

                if (Convert.ToInt32(rule.Quantity) < 1)
                    return null;

                if (string.IsNullOrEmpty(rule.Lql) || string.IsNullOrEmpty(rule.Hql))
                    return null;

                if (item.QualityLevel < Convert.ToInt32(rule.Lql) || item.QualityLevel > Convert.ToInt32(rule.Hql))
                    return null;

                if (!string.IsNullOrEmpty(rule.OneEach) && rule.OneEach == "true")
                {
                    foreach (var invItem in Inventory.Items.Where(c => c.Slot.Type == IdentityType.Inventory))
                    {
                        if (string.Equals(invItem.Name, item.Name, StringComparison.OrdinalIgnoreCase))
                            return null;
                    }

                    if (!string.IsNullOrEmpty(rule.BagName))
                    {
                        foreach (var backpack in Inventory.Backpacks.Where(b => b.Name.Contains(rule.BagName)))
                        {
                            foreach (var containerItem in Inventory.GetContainerItems(backpack.Identity))
                            {
                                if (string.Equals(containerItem.Name, item.Name, StringComparison.OrdinalIgnoreCase))
                                    return null;
                            }
                        }
                    }
                }

                return rule;
            }
            catch (Exception ex)
            {
                ErrorCatch(ex);
                return null;
            }
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

                if (settingsWindow.FindView("SaveAs", out TextInputView text))
                {
                    if (text.Text == null || string.IsNullOrEmpty(text.Text))
                    {
                        if (settingsWindow.FindView("filePath", out DropdownMenu filePath))
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

                settingsWindow.FindView("ScrollListRoot", out MultiListView _multiListView);

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
            MainUI();
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
                settingsWindow.FindView("TextName", out TextInputView nameInput);
                settingsWindow.FindView("_itemMinQL", out TextInputView minQlInput);
                settingsWindow.FindView("_itemMaxQL", out TextInputView maxQlInput);
                settingsWindow.FindView("_itemQuantity", out TextInputView quantityInput);
                settingsWindow.FindView("BagName", out DropdownMenu bagMenu);
                settingsWindow.FindView("ErrorMessage", out TextView errorMessage);

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
            settingsWindow.FindView("ScrollListRoot", out MultiListView list);

            settingsWindow.FindView("RemoveIndex", out TextInputView removeIndex);
            settingsWindow.FindView("ErrorMessage", out TextView errorMessage);

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

            if (settingsWindow.FindView("SaveAs", out TextInputView text))
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
            if (settingsWindow.FindView("filePath", out DropdownMenu filePath))
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
            if (settingsWindow.FindView("filePath", out DropdownMenu filePath))
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

            if (settingsWindow.FindView("ToggleFolderScope", out Button toggleBtn))
                toggleBtn.SetLabel($"Use Shared: {newValue}");

            Save();
        }

        private void Clear_List_Button_Clicked(object sender, ButtonBase e)
        {
            try {
            if (settingsWindow == null || !settingsWindow.IsValid)
                return;

            settingsWindow.FindView("ScrollListRoot", out MultiListView _multiListView);

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
                if (settingsWindow?.IsValid == true)
                {
                    Window_Closed_helper();

                    settingsWindow.Close();
                    settingsWindow = null;
                    return;
                }

                settingsWindow = Window.CreateFromXml(PluginName, UiDirectory + "\\UI\\ManagerLoot\\ManagerLootSettingWindow.xml", windowStyle: WindowStyle.Default, windowFlags: WindowFlags.AutoScale | WindowFlags.NoFade);
                settingsWindow.MoveTo(_settings["MainWindowTopLeftX"].AsFloat(), _settings["MainWindowTopLeftY"].AsFloat());

                if (settingsWindow.FindView("InfoView", out Button infoView))
                    infoView.Clicked = HandleInfoViewClick;

                if (settingsWindow.FindView("Enable_Disable_Button", out Button enableButton))
                {
                    enableButton.SetLabel(EnableString);
                    enableButton.Clicked = Enable_Disable_Button_Clicked;
                }

                if (settingsWindow.FindView("BagName", out DropdownMenu bags))
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

                if (settingsWindow.FindView("ScrollListRoot", out MultiListView _multiListView) && settingsWindow.FindView("_itemMinQL", out TextInputView _itemMinQL)
                    && settingsWindow.FindView("_itemMaxQL", out TextInputView _itemMaxQL) && settingsWindow.FindView("_itemQuantity", out TextInputView _itemQuantity))
                {
                    _itemMinQL.Text = "1";
                    _itemMaxQL.Text = "500";
                    _itemQuantity.Text = "999";
                    _settings["Exact"] = false;
                    _settings["OneOfEach"] = false;

                    RefreshList();
                }

                if (settingsWindow.FindView("buttonAdd", out Button addbut))
                    addbut.Clicked += AddButtonClicked;

                if (settingsWindow.FindView("buttonDel", out Button rembut))
                    rembut.Clicked += RemButtonClicked;

                if (settingsWindow.FindView("buttonNew", out Button newbut))
                    newbut.Clicked += New_List_Button_Clicked;

                if (settingsWindow.FindView("filePath", out DropdownMenu filePath))
                    Update_List_DropDown();

                if (settingsWindow.FindView("buttonLoad", out Button loadbut))
                    loadbut.Clicked += LoadButtonClicked;

                if (settingsWindow.FindView("buttonRemove", out Button buttonRemove))
                    buttonRemove.Clicked += Remove_Button_Clicked;

                if (settingsWindow.FindView("OpenLootFolder", out Button openLootFolder))
                    openLootFolder.Clicked = OpenTheLootFolder;

                if (settingsWindow.FindView("ToggleFolderScope", out Button toggleFolderScope))
                {
                    toggleFolderScope.Clicked = ToggleFolderScopeClicked;
                    toggleFolderScope.SetLabel($"Use Shared: {_settings["UseSharedFolder"].AsBool()}");
                }

                if (settingsWindow.FindView("ClearList", out Button clearButton))
                    clearButton.Clicked += Clear_List_Button_Clicked;

                if (settingsWindow.FindView("VersionNumber", out TextView version))
                    version.Text = $"Version {Version_Number}";

                if (settingsWindow == null) { Chat.WriteLine("settingsWindow == nul"); return; }

                settingsWindow.Show(true);

            }
            catch (Exception ex)
            {
                ErrorCatch(ex);
            }
        }

        private void Update_List_DropDown()
        {
            if (settingsWindow.FindView("filePath", out DropdownMenu filePath))
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
                var lootBags = Inventory.Backpacks.Where(bag => Rules.Any(r => !string.IsNullOrWhiteSpace(r.BagName) && bag.Name.StartsWith(r.BagName))).ToList();

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

            if (settingsWindow?.IsValid == true && settingsWindow.FindView("Enable_Disable_Button", out Button enableButton))
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

            if (settingsWindow != null && settingsWindow.IsValid && settingsWindow.FindView("Errors", out View errorView))
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
