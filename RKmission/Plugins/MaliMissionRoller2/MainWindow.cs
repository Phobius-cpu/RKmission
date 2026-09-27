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
        private bool _isRolling;
        // RKMission drives the original request/response UI with a zone target.
        public int AutoZoneId { get; private set; }

        public void StartZoneRolling(int zoneId)
        {
            AutoZoneId = zoneId;
            _isRolling = true;
            _requestTimer = 1.5f;
            RequestMission();
        }

        public void StopZoneRolling()
        {
            AutoZoneId = 0;
            _isRolling = false;
        }
        private float _requestTimer;
        private int _missionLevel;
        private readonly List<List<int>> MissionLvls = JsonConvert.DeserializeObject<List<List<int>>>(File.ReadAllText($"{Main.PluginDir}\\JSON\\MissionLevels.json"));

        public MainWindow(string name, string path, WindowStyle windowStyle = WindowStyle.Popup, WindowFlags flags = WindowFlags.AutoScale | WindowFlags.NoFade) : base(name, path, windowStyle, flags)
        {
            Extensions.LoadCustomTextures($"{Main.PluginDir}\\UI\\Textures\\", 1000035);
        }

        protected override void OnWindowCreating()
        {
            try
            {
                HelpWindow _helpWindow = new HelpWindow();

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
                    SettingsView.Hide();
                }
            }
            catch (Exception e)
            {
                Chat.WriteLine(e);
            }
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
            Chat.WriteLine($"Auto Rolling Toggled.");
        }

        private void SettingsClick(object sender, ButtonBase e)
        {
            Midi.Play("Click");

            if (InSettings)
            {
                Extensions.ButtonSetGfx(HeaderView.Settings, 1000050);
                MissionView.Show();
                SettingsView.Hide();
            }
            else
            {
                Extensions.ButtonSetGfx(HeaderView.Settings, 1000043);
                MissionView.Hide();
                SettingsView.Show();
            }
            InSettings = !InSettings;
        }

        public void SwapViews()
        {
            Midi.Play("Click");

            if (InSettings)
            {
                MissionView.Show();
                SettingsView.Hide();
                InSettings = false;
            }
            else
            {
                MissionView.Show();
            }

            Extensions.ButtonSetGfx(HeaderView.Settings, 1000050);
        }

        private void RequestClick(object sender, ButtonBase e)
        {
            Midi.Play("Click");

            SwapViews();
            RequestMission();
        }

        internal void RequestMission()
        {
            if (CurrentTerminal == null)
            {
                Chat.WriteLine("Invalid terminal. (right click or use a mission terminal)");
                _isRolling = false;
                return;
            }

            if (Inventory.NumFreeSlots < 2)
            {
                Chat.WriteLine("You need at least 2 free inventory slots to roll.");
                _isRolling = false;
                return;
            }

            List<RollEntryView> rollEntries = SettingsView.ItemDisplay.RollEntryViews;

            if (rollEntries.Count == 0 && _isRolling && AutoZoneId == 0)
            {
                _isRolling = false;
                Chat.WriteLine("Roll List is empty!");
                Chat.WriteLine("Auto Rolling set to: FALSE");
                return;
            }

            if ((bool)SettingsView.ExtraOptions.AutoAdjustQl.Tag && _isRolling)
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
                        "(press '?' in the top right corner for contact details)");
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

            CurrentTerminal?.RequestMissions(
                sliders.Difficulty,
                sliders.GoodBad,
                sliders.OrderChaos,
                sliders.OpenHidden,
                sliders.PhysicalMystical,
                sliders.HeadonStealth,
                sliders.CreditsXp
                );
        }
        internal void RollMatchCheck(MissionInfo[] missionList)
        {
            MissionView.Update(missionList);
            if (AutoZoneId != 0)
            {
                var origin = AOSharp.Core.Playfield.ModelIdentity.Instance == AutoZoneId
                    ? DynelManager.LocalPlayer.Position : Vector3.Zero;
                MissionInfo nearest = missionList
                    .Where(x => x.Playfield.Instance == AutoZoneId)
                    .OrderBy(x => Vector3.Distance(x.Location, origin))
                    .FirstOrDefault();
                if (nearest != null)
                {
                    _isRolling = false;
                    AutoZoneId = 0;
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
            if ((bool)HeaderView.Settings.Tag == true)
                MissionView.Show();
        }
    }
}
