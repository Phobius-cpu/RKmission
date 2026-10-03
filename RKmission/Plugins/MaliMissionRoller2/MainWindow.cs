using AOSharp.Common.GameData;
using AOSharp.Common.GameData.UI;
using AOSharp.Core;
using AOSharp.Core.Inventory;
using AOSharp.Core.UI;
using Newtonsoft.Json;
using SmokeLounge.AOtomation.Messaging.GameData;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MaliMissionRoller2
{
    public class MainWindow: AOSharpWindow
    {
        public static MissionTerminal CurrentTerminal;
        internal HeaderView HeaderView;
        internal MissionView MissionView;
        internal SettingsView SettingsView;
        internal bool InSettings;
        private View? _tabContent;
        private View? _rollerContent;
        private View? _managerLootContent;
        private View? _dungeonMapContent;
        private View? _settingsContent;
        private View? _hostRoot;
        private Button? _rollerTab;
        private Button? _managerLootTab;
        private Button? _dungeonMapTab;
        private Button? _settingsTab;
        private enum HostedTab { Roller, ManagerLoot, DungeonMap, Settings }
        private HostedTab _activeTab = HostedTab.Roller;
        public bool IsManagerLootTabActive => _activeTab == HostedTab.ManagerLoot;
        public bool IsDungeonMapTabActive => _activeTab == HostedTab.DungeonMap;
        public bool IsSettingsTabActive => _activeTab == HostedTab.Settings;
        public event Action? WindowReady;
        public Action? EnsureManagerLootView { get; set; }
        public Action? EnsureDungeonMapView { get; set; }
        private bool _isRolling;
        private bool _rkAutoMode;
        // RKMission drives the original request/response UI. A zero zone uses
        // the enabled Rubi-Ka locations in the roller settings.
        public int AutoZoneId { get; private set; }
        public bool IsAutoRolling => _rkAutoMode && _isRolling;
        public string LastAutoError { get; private set; }
        public int PendingAutoMissionId { get; private set; }
        public int PendingAutoPlayfieldId { get; private set; }
        public string? PendingAutoTitle { get; private set; }
        public Vector3 PendingAutoLocation { get; private set; }
        public HashSet<int> PendingAutoPreviousQuestIds { get; private set; } = new HashSet<int>();
        public DateTime AutoAcceptRequestedAtUtc { get; private set; }
        public DateTime LastAutoOfferAtUtc { get; private set; }
        public Func<int, int>? AutoAcceptedCountForPlayfield { get; set; }
        public int EnabledAutoDestinationCount => SettingsView?.Locations?.Entries?.Count(x =>
            (bool)x.Toggle.Tag && RKmission.AcceptedMissions.IsRubiKaPlayfield(x.PfId)) ?? 0;
        public bool HasEnabledAutoDestination => SettingsView?.Locations?.Entries?.Any(x =>
            (bool)x.Toggle.Tag && RKmission.AcceptedMissions.IsRubiKaPlayfield(x.PfId)) == true;

        public bool StartZoneRolling(int zoneId)
        {
            if (Window?.IsValid != true)
            {
                LastAutoError = "Roller window is closed; reopen it and restart /rkm auto.";
                return false;
            }

            AutoZoneId = zoneId;
            _rkAutoMode = true;
            _isRolling = true;
            LastAutoError = null;
            PendingAutoMissionId = 0;
            PendingAutoPlayfieldId = 0;
            PendingAutoTitle = null;
            PendingAutoPreviousQuestIds.Clear();
            AutoAcceptRequestedAtUtc = DateTime.MinValue;
            LastAutoOfferAtUtc = DateTime.UtcNow;
            _requestTimer = 1.5f;
            return RequestMission();
        }

        public void StopZoneRolling()
        {
            AutoZoneId = 0;
            _rkAutoMode = false;
            _isRolling = false;
            PendingAutoMissionId = 0;
            PendingAutoPlayfieldId = 0;
            PendingAutoTitle = null;
            PendingAutoPreviousQuestIds.Clear();
            AutoAcceptRequestedAtUtc = DateTime.MinValue;
        }
        private float _requestTimer;
        private int _missionLevel;
        private readonly List<List<int>> MissionLvls = JsonConvert.DeserializeObject<List<List<int>>>(File.ReadAllText($"{Main.PluginDir}\\JSON\\MissionLevels.json"));

        public MainWindow(string name, string path, WindowStyle windowStyle = WindowStyle.Default, WindowFlags flags = WindowFlags.AutoScale | WindowFlags.NoFade) : base(name, path, windowStyle, flags)
        {
            Extensions.LoadCustomTextures($"{Main.PluginDir}\\UI\\Textures\\", 1000035);
        }

        protected override void OnWindowCreating()
        {
            try
            {
                HelpWindow _helpWindow = new HelpWindow();

                Window.FindView("HostRoot", out _hostRoot);
                Window.FindView("TabContent", out _tabContent);
                Window.FindView("RollerContent", out _rollerContent);
                Window.FindView("SettingsContent", out _settingsContent);
                Window.FindView("RollerTab", out _rollerTab);
                Window.FindView("ManagerLootTab", out _managerLootTab);
                Window.FindView("DungeonMapTab", out _dungeonMapTab);
                Window.FindView("SettingsTab", out _settingsTab);
                _managerLootContent = null;
                _dungeonMapContent = null;
                _activeTab = HostedTab.Roller;
                if (_rollerTab != null) _rollerTab.Clicked += (_, _) => ShowRollerTab();
                if (_managerLootTab != null) _managerLootTab.Clicked += (_, _) => ShowManagerLootTab();
                if (_dungeonMapTab != null) _dungeonMapTab.Clicked += (_, _) => ShowDungeonMapTab();
                if (_settingsTab != null) _settingsTab.Clicked += (_, _) => ShowSettingsTab();

                if (Window.FindView("HeaderRoot", out View headerRoot))
                {
                    InSettings = false;
                    _isRolling = false;
                    _requestTimer = 0.9f;
                    HeaderView = new HeaderView(headerRoot);
                    HeaderView.Help.Clicked += HelpClick;
                    HeaderView.Start.Clicked += StartClick;
                    HeaderView.Settings.Tag = InSettings;
                    HeaderView.Settings.Clicked += SettingsClick;
                    HeaderView.Request.Clicked += RequestClick;
                }

                if (Window.FindView("MissionRoot", out View missionRoot))
                {
                    MissionView = new MissionView(missionRoot);
                    MissionView.Hide();
                }

                if (Window.FindView("SettingsRoot", out View settingsRoot))
                {
                    SettingsView = new SettingsView(settingsRoot);
                    SettingsView.Locations.BoundsCheck();
                }
                // SettingsContent must exist during XML/view construction, but the
                // Roller remains the default visible tab.
                if (_tabContent != null && _settingsContent != null)
                    _tabContent.RemoveChild(_settingsContent);
                FitActiveTab();
                UpdateTabAppearance();
                WindowReady?.Invoke();
            }
            catch (Exception e)
            {
                Chat.WriteLine(e);
            }
        }

        public void AttachManagerLootView(View view)
        {
            if (_tabContent == null || view == null) return;
            if (_activeTab == HostedTab.ManagerLoot && _managerLootContent != null)
                _tabContent.RemoveChild(_managerLootContent);
            _managerLootContent = view;
            if (_activeTab == HostedTab.ManagerLoot)
                _tabContent.AddChild(_managerLootContent, false);
            FitActiveTab();
        }

        public void AttachDungeonMapView(View view)
        {
            if (_tabContent == null || view == null) return;
            if (_activeTab == HostedTab.DungeonMap && _dungeonMapContent != null)
                _tabContent.RemoveChild(_dungeonMapContent);
            _dungeonMapContent = view;
            if (_activeTab == HostedTab.DungeonMap)
                _tabContent.AddChild(_dungeonMapContent, false);
            FitActiveTab();
        }

        public void ShowRollerTab()
        {
            if (_tabContent == null || _rollerContent == null || _activeTab == HostedTab.Roller) return;
            RemoveActiveContent();
            _tabContent.AddChild(_rollerContent, false);
            _activeTab = HostedTab.Roller;
            InSettings = false;
            if (HeaderView?.Settings != null) HeaderView.Settings.Tag = false;
            FitActiveTab();
            UpdateTabAppearance();
        }

        public void ShowManagerLootTab()
        {
            if (_managerLootContent == null) EnsureManagerLootView?.Invoke();
            if (_tabContent == null || _rollerContent == null || _managerLootContent == null ||
                _activeTab == HostedTab.ManagerLoot) return;
            RemoveActiveContent();
            _tabContent.AddChild(_managerLootContent, false);
            _activeTab = HostedTab.ManagerLoot;
            InSettings = false;
            if (HeaderView?.Settings != null) HeaderView.Settings.Tag = false;
            FitActiveTab();
            UpdateTabAppearance();
        }

        public void ShowDungeonMapTab()
        {
            if (_dungeonMapContent == null) EnsureDungeonMapView?.Invoke();
            if (_tabContent == null || _rollerContent == null || _dungeonMapContent == null ||
                _activeTab == HostedTab.DungeonMap) return;
            RemoveActiveContent();
            _tabContent.AddChild(_dungeonMapContent, false);
            _activeTab = HostedTab.DungeonMap;
            InSettings = false;
            if (HeaderView?.Settings != null) HeaderView.Settings.Tag = false;
            FitActiveTab();
            UpdateTabAppearance();
        }

        public void ShowSettingsTab()
        {
            if (_tabContent == null || _settingsContent == null || _activeTab == HostedTab.Settings) return;
            RemoveActiveContent();
            _tabContent.AddChild(_settingsContent, false);
            _activeTab = HostedTab.Settings;
            InSettings = true;
            if (HeaderView?.Settings != null) HeaderView.Settings.Tag = true;
            FitActiveTab();
            UpdateTabAppearance();
        }

        private void RemoveActiveContent()
        {
            if (_tabContent == null) return;
            if (_activeTab == HostedTab.Roller && _rollerContent != null)
                _tabContent.RemoveChild(_rollerContent);
            else if (_activeTab == HostedTab.ManagerLoot && _managerLootContent != null)
                _tabContent.RemoveChild(_managerLootContent);
            else if (_activeTab == HostedTab.DungeonMap && _dungeonMapContent != null)
                _tabContent.RemoveChild(_dungeonMapContent);
            else if (_activeTab == HostedTab.Settings && _settingsContent != null)
                _tabContent.RemoveChild(_settingsContent);
        }

        private void FitActiveTab()
        {
            _tabContent?.FitToContents();
            _hostRoot?.FitToContents();
        }

        private void UpdateTabAppearance()
        {
            _rollerTab?.SetAlpha(_activeTab == HostedTab.Roller ? 1f : 0.65f);
            _managerLootTab?.SetAlpha(_activeTab == HostedTab.ManagerLoot ? 1f : 0.65f);
            _dungeonMapTab?.SetAlpha(_activeTab == HostedTab.DungeonMap ? 1f : 0.65f);
            _settingsTab?.SetAlpha(_activeTab == HostedTab.Settings ? 1f : 0.65f);
        }

        private void HelpClick(object sender, ButtonBase e)
        {
            Midi.Play("Click");

            HelpWindow helpWindow = new HelpWindow();
            helpWindow.StartupWindow.Show(true);
        }

        private void StartClick(object sender, ButtonBase e)
        {
            Midi.Play("Click");

            _isRolling = !_isRolling;
            _rkAutoMode = false;
            AutoZoneId = 0;
            PendingAutoMissionId = 0;
            Chat.WriteLine($"Auto Rolling Toggled.");
        }

        private void SettingsClick(object sender, ButtonBase e)
        {
            Midi.Play("Click");
            ShowSettingsTab();
        }

        public void SwapViews()
        {
            Midi.Play("Click");
            ShowRollerTab();
            MissionView.Show();
            InSettings = false;
            HeaderView.Settings.Tag = false;
        }

        private void RequestClick(object sender, ButtonBase e)
        {
            Midi.Play("Click");

            SwapViews();
            RequestMission();
        }

        internal bool RequestMission()
        {
            if (Window?.IsValid != true)
            {
                LastAutoError = "Roller window is closed; reopen it and restart /rkm auto.";
                _isRolling = false;
                return false;
            }

            if (CurrentTerminal == null)
            {
                LastAutoError = "Mission terminal is unavailable; stand beside it and restart /rkm auto.";
                Chat.WriteLine(LastAutoError);
                _isRolling = false;
                return false;
            }

            if (Inventory.NumFreeSlots < 2)
            {
                LastAutoError = "You need at least 2 free inventory slots to roll.";
                Chat.WriteLine(LastAutoError);
                _isRolling = false;
                return false;
            }

            List<RollEntryView> rollEntries = SettingsView.ItemDisplay.RollEntryViews;

            if (rollEntries.Count == 0 && _isRolling && !_rkAutoMode)
            {
                _isRolling = false;
                Chat.WriteLine("Roll List is empty!");
                Chat.WriteLine("Auto Rolling set to: FALSE");
                return false;
            }

            // RKMission destination rolling does not require an item roll list.
            // The item's auto-level processor reports an empty list as fatal.
            if ((bool)SettingsView.ExtraOptions.AutoAdjustQl.Tag && _isRolling &&
                !_rkAutoMode)
            {
                var rollProcessor = new RollEntryProcessor(MissionLvls);

                var result = rollProcessor.ProcessRollEntry(SettingsView.ItemDisplay.RollEntryViews);

                if (result.NoValidEntry)
                {
                    Midi.Play("Alert");
                    Chat.WriteLine(
                        "Remaining roll items outside characters level reach.\n" +
                        "If you think this is wrong, disable the 'Auto Adjust Level Slider'\n" +
                        "temporarily and contact me so I can update the mission level table!\n" +
                        "(press Help in the roller window for details)");
                    _isRolling = false;
                }
                else if (result.IsSpecialCredit)
                {
                    Chat.WriteLine($"Rolling for missions with combined credit reward >= {result.CreditReward}");
                }
                else
                {
                    SettingsView.Sliders.EasyHard.Value = result.SliderValue;
                    Chat.WriteLine($"Mission level set to: {result.MissionLevel}\nUnique items to roll in this range: {result.UniqueItemCount}");
                }
            }

            MissionSliders sliders = SettingsView.Sliders.GetSliderValues();

            CurrentTerminal.RequestMissions(
                sliders.Difficulty,
                sliders.GoodBad,
                sliders.OrderChaos,
                sliders.OpenHidden,
                sliders.PhysicalMystical,
                sliders.HeadonStealth,
                sliders.CreditsXp
                );
            return true;
        }

        private bool IsAutoEligible(MissionInfo mission)
        {
            int playfield = mission.Playfield.Instance;
            if (!RKmission.AcceptedMissions.IsRubiKaPlayfield(playfield) ||
                (AutoZoneId > 0 && playfield != AutoZoneId)) return false;
            PlayfieldEntryView location = SettingsView.Locations.Entries.FirstOrDefault(x => x.PfId == playfield);
            if (AutoZoneId == 0 && (location == null || !(bool)location.Toggle.Tag)) return false;
            if (location != null && location.Bounds.Coord1.X != 0 &&
                location.Bounds.Coord2.X != 0 && !location.Bounds.Contains(mission.Location)) return false;
            var types = SettingsView.MissionTypes;
            switch (mission.MissionIcon)
            {
                case 11329: return (bool)types.ReturnItem.Tag;
                case 11330: return (bool)types.KillTarget.Tag;
                case 11335: return (bool)types.FindTarget.Tag;
                case 11337: return (bool)types.FindItem.Tag;
                case 11342: return (bool)types.UseItem.Tag;
                default: return false;
            }
        }

        internal void RollMatchCheck(MissionInfo[] missionList)
        {
            MissionView.Update(missionList);
            if (PendingAutoMissionId > 0) return;
            if (_rkAutoMode)
            {
                LastAutoOfferAtUtc = DateTime.UtcNow;
                int current = AOSharp.Core.Playfield.ModelIdentity.Instance;
                Vector3 origin = DynelManager.LocalPlayer.Position;
                MissionInfo nearest = missionList
                    .Where(IsAutoEligible)
                    .OrderBy(x => AutoAcceptedCountForPlayfield?.Invoke(x.Playfield.Instance) ?? 0)
                    .ThenBy(x => x.Playfield.Instance == current ? 1 : 0)
                    .ThenBy(x => x.Playfield.Instance)
                    .ThenBy(x => x.Playfield.Instance == current
                        ? Vector3.Distance(x.Location, origin) : 0f)
                    .FirstOrDefault();
                if (nearest != null)
                {
                    _isRolling = false;
                    _rkAutoMode = false;
                    AutoZoneId = 0;
                    PendingAutoMissionId = nearest.MissionIdentity.Instance;
                    PendingAutoPlayfieldId = nearest.Playfield.Instance;
                    PendingAutoTitle = nearest.Title;
                    PendingAutoLocation = nearest.Location;
                    PendingAutoPreviousQuestIds = new HashSet<int>((Mission.List ?? new List<Mission>())
                        .Select(x => x.Identity.Instance));
                    AutoAcceptRequestedAtUtc = DateTime.UtcNow;
                    MissionView.AcceptMission(nearest.MissionIdentity);
                    return;
                }
                _requestTimer = 1.5f;
                return;
            }
            int missionIndex = -1;

            foreach (MissionInfo missionInfo in missionList)
            {
                missionIndex++;
                RollEntryView rollEntry = SettingsView.ItemDisplay.RollEntryViews
                    .Where(rollList => missionInfo.MissionItemData.Any(missionEntry =>
                                missionEntry.HighId == rollList.RollEntryModel.HighId && missionEntry.Ql == rollList.RollEntryModel.Ql ||
                                rollList.RollEntryModel.LowId == missionEntry.LowId && missionEntry.LowId == missionEntry.HighId && missionEntry.Ql == rollList.RollEntryModel.Ql) ||
                                missionInfo.Description.Contains(rollList.RollEntryModel.Name) && new[] { "Nano Crystal", "NanoCrystal" }.Any(rollList.RollEntryModel.Name.Contains) ||
                                missionInfo.Description.Contains(rollList.RollEntryModel.Name) && rollList.RollEntryModel.Ql == _missionLevel ||
                                rollList.RollEntryModel.LowId == 297315 && rollList.RollEntryModel.Ql <= MissionView.CombinedItemValue[missionIndex])
                    .FirstOrDefault();

                if (rollEntry == null)
                    continue;

                if (!(bool)SettingsView.MissionTypes.ReturnItem.Tag && missionInfo.MissionIcon == 11329)
                {
                    Chat.WriteLine($"Match found '{rollEntry.RollEntryModel.Name}', skipping due to 'Return Item' being disabled.");
                    continue;
                }
                if (!(bool)SettingsView.MissionTypes.KillTarget.Tag && missionInfo.MissionIcon == 11330)
                {
                    Chat.WriteLine($"Match found '{rollEntry.RollEntryModel.Name}', skipping due to 'Kill Target' being disabled.");
                    continue;
                }
                if (!(bool)SettingsView.MissionTypes.FindTarget.Tag && missionInfo.MissionIcon == 11335)
                {
                    Chat.WriteLine($"Match found '{rollEntry.RollEntryModel.Name}', skipping due to 'Find Target' being disabled.");
                    continue;
                }
                if (!(bool)SettingsView.MissionTypes.FindItem.Tag && missionInfo.MissionIcon == 11337)
                {
                    Chat.WriteLine($"Match found '{rollEntry.RollEntryModel.Name}', skipping due to 'Find Item' being disabled.");
                    continue;
                }
                if (!(bool)SettingsView.MissionTypes.UseItem.Tag && missionInfo.MissionIcon == 11342)
                {
                    Chat.WriteLine($"Match found '{rollEntry.RollEntryModel.Name}', skipping due to 'Use Item' being disabled.");
                    continue;
                }

                PlayfieldEntryView locEntry = SettingsView.Locations.Entries.Where(x => (bool)x.Toggle.Tag && x.PfId == missionInfo.Playfield.Instance).FirstOrDefault();

                if (locEntry == null)
                {
                    Chat.WriteLine($"Match found '{rollEntry.RollEntryModel.Name}', skipping due to '{Extensions.GetZoneName(missionInfo.Playfield.Instance)}' being disabled.");
                    continue;
                }
                if (locEntry.Bounds.Coord1.X != 0 && locEntry.Bounds.Coord2.X != 0 && !locEntry.Bounds.Contains(missionInfo.Location))
                {
                    Chat.WriteLine($"Match found '{rollEntry.RollEntryModel.Name}', skipping due to mission location in '{Extensions.GetZoneName(missionInfo.Playfield.Instance)}' being out of set bounds.");
                    continue;
                }

                SettingsView.ItemDisplay.UpdateRollEntry(rollEntry, (bool)SettingsView.ExtraOptions.RemoveRoll.Tag);
                Main.Settings.Save();

                if ((bool)SettingsView.ExtraOptions.AutoAccept.Tag)
                {
                    MissionView.AcceptMission(missionInfo.MissionIdentity, (bool)SettingsView.ExtraOptions.PlayAlertSound.Tag);
                }
                else
                {
                    _isRolling = false;
                    Chat.WriteLine($"Match found '{rollEntry.RollEntryModel.Name}', due to 'Auto Accept' being disabled, waiting for user input.");
                }
                return;
            }
            _requestTimer = 0.9f;
        }

        public void Update(float e)
        {
            _requestTimer -= e;

            MissionView.UpdateDistance();
            SettingsView.UpdateUI((bool)SettingsView.ExtraOptions.ShowBounds.Tag);

            if (_isRolling && _requestTimer < 0)
            {
                RequestMission();
                _requestTimer = 1.5f;
            }
        }

        public void UpdateTerminal(MissionTerminal terminal)
        {
            CurrentTerminal = terminal;
            if (_activeTab == HostedTab.Roller)
                MissionView.Show();
        }
    }
}
