using AOSharp.Common.GameData;
using AOSharp.Core;
using AOSharp.Core.UI;
using Newtonsoft.Json;
using SmokeLounge.AOtomation.Messaging.Messages;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;
using System;
using System.Collections.Generic;
using System.IO;

namespace MaliMissionRoller2
{
    // Hosted by RKmission's single AO# entry point; original public UI remains intact.
    public class Main
    {
        public static string PluginDir;
        public static MainWindow Window;
        public static Settings Settings;
        public static List<KeyValuePair<ItemInfo, List<Stat>>> ItemDb;
        public event EventHandler RollerWindowClosed;
        private bool _rollerWindowClosed;

        public bool ShowRoller()
        {
            if (Window == null)
                return false;

            bool reopening = Window.Window?.IsValid != true;
            Window.Show();
            if (Window?.Window?.IsValid != true)
                return false;

            _rollerWindowClosed = false;
            if (reopening)
            {
                var screenSize = AOSharp.Core.UI.Window.GetScreenSize();
                if (Settings.Frame.X > screenSize.X || Settings.Frame.Y > screenSize.Y)
                    Window.Window.MoveToCenter();
                else
                    Window.Window.MoveTo(Settings.Frame.X, Settings.Frame.Y);
                Settings.CaptureUiState();
            }
            return true;
        }

        public unsafe void Run(string pluginDir)
        {
            Chat.WriteLine("- Mali's Mission Roller 2.0 -", ChatColor.Gold);

            PluginDir = pluginDir;
            string fileName = File.Exists($"{pluginDir}\\JSON\\Settings.json") ? "" : "Default_";
            Settings = JsonConvert.DeserializeObject<Settings>(File.ReadAllText($"{pluginDir}\\JSON\\{fileName}Settings.json"));
            Extensions.FormatItemDb(Settings.Database["Implants"], Settings.Database["Refined"], Settings.Database["Clusters"], Settings.Database["Nanos"], Settings.Database["Rest"]);
            Window = new MainWindow("RKMission Roller", $"{pluginDir}\\UI\\Windows\\MainWindow.xml");
            ShowRoller();

            Game.OnUpdate += Update;
            UIController.WindowDeleted += OnWindowDeleted;
            Mission.RollListChanged += RollListChanged;
            Network.N3MessageReceived += N3Message_Received;
            Game.TeleportEnded += Game_OnTeleportEnded;
            Game.TeleportStarted += Game_OnTeleportStarted;

            Midi.Play("Alert");
            Chat.RegisterCommand("mmr", (string command, string[] param, ChatWindow chatWindow) => DevCmd(param));
        }

        private void DevCmd(string[] param)
        {
            if (param.Length < 2)
                return;

            string userInput = param[0].ToLower();

            if (!int.TryParse(param[1], out int result))
                return;

            switch (userInput)
            {
                case "maxitems":
                    Settings.Dev["MaxItems"] = result;
                    if (!_rollerWindowClosed && Window?.Window?.IsValid == true)
                    {
                        Window.SettingsView.ItemDisplay.DeleteBrowserEntries();
                        Window.SettingsView.ItemDisplay.FormatBrowserEntries();
                    }
                    Chat.WriteLine($"Max Display items set to: {Settings.Dev["MaxItems"]}",ChatColor.Red);
                    break;
                case "shopvalue":
                    Settings.Dev["ShopValue"] = result;
                    Chat.WriteLine($"Shop Value Factor set to: {Settings.Dev["ShopValue"]}", ChatColor.Red);
                    break;
            }
        }

        private void OnWindowDeleted(object sender, AOSharp.Core.UI.Window closedWindow)
        {
            if (!ReferenceEquals(closedWindow, Window?.Window))
                return;

            // AOSharp raises this event before invalidating the native window.
            // Save the live controls and frame now; Teardown must not read them later.
            _rollerWindowClosed = true;
            Window.StopZoneRolling();
            MainWindow.CurrentTerminal = null;
            RollerWindowClosed?.Invoke(this, EventArgs.Empty);
            try
            {
                Settings.Save(false);
            }
            catch (Exception e)
            {
                Chat.WriteLine($"Roller settings could not be saved on close: {e.Message}");
            }
        }

        private void Game_OnTeleportStarted(object sender, EventArgs e)
        {
            if (_rollerWindowClosed || Window?.Window?.IsValid != true)
                return;

            MainWindow.CurrentTerminal = null;
            Window.MissionView.Hide();
        }

        private void Game_OnTeleportEnded(object sender, EventArgs e)
        {
            if (_rollerWindowClosed || Window?.Window?.IsValid != true)
                return;

            Window.SettingsView.Locations.BoundsCheck();
        }

        private void N3Message_Received(object sender, N3Message n3Msg)
        {
            if (!(n3Msg is GenericCmdMessage genCmdMsg))
                return;

            if (n3Msg.Identity != DynelManager.LocalPlayer.Identity)
                return;

            if (genCmdMsg.Action != GenericCmdAction.Use)
                return;

            if (genCmdMsg.Target.Type != IdentityType.MissionTerminal)
                return;

            if (_rollerWindowClosed || Window?.Window?.IsValid != true)
            {
                return;
            }

            var terminal = DynelManager.GetDynel(genCmdMsg.Target);

            if (terminal == null)
            {
                Chat.WriteLine("This shouldn't happen");
                return;
            }

            MainWindow.CurrentTerminal = new MissionTerminal(terminal);
            Window.MissionView.ShopValue = Math.Round(Settings.Dev["ShopValue"] * (1 + (float)DynelManager.LocalPlayer.GetStat(AOSharp.Common.GameData.Stat.ComputerLiteracy) / (40 * 100)) / 100, 3);
            Window.SwapViews();
        }

        private void RollListChanged(object sender, RollListChangedArgs rollListChanged)
        {
            if (_rollerWindowClosed || Window?.Window?.IsValid != true)
                return;

            Window.RollMatchCheck(rollListChanged.MissionDetails);
        }

        private void Update(object sender, float e)
        {
            if (_rollerWindowClosed || Window?.Window?.IsValid != true)
                return;

            Window.Update(e);
            if (!_rollerWindowClosed && Window?.Window?.IsValid == true)
                Settings.CaptureUiState(false);
        }

        public void Teardown()
        {
            Game.OnUpdate -= Update;
            UIController.WindowDeleted -= OnWindowDeleted;
            Mission.RollListChanged -= RollListChanged;
            Network.N3MessageReceived -= N3Message_Received;
            Game.TeleportEnded -= Game_OnTeleportEnded;
            Game.TeleportStarted -= Game_OnTeleportStarted;
            Midi.TearDown();
            if (!_rollerWindowClosed && Window?.Window?.IsValid == true)
                Settings.Save();
            MainWindow.CurrentTerminal = null;
        }
    }
}
