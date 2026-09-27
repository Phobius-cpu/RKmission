using AOSharp.Common.GameData;
using AOSharp.Common.GameData.UI;
using AOSharp.Common.Helpers;
using AOSharp.Common.Unmanaged.Imports;
using AOSharp.Common.Unmanaged.Interfaces;
using AOSharp.Core;
using AOSharp.Core.UI;
using SmokeLounge.AOtomation.Messaging.GameData;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MaliMissionRoller2
{
    public class ExtrasView
    {
        internal View Root;
        private BitmapView Background;
        internal Button PlayAlertSound;
        internal Button AutoAdjustQl;
        internal Button RemoveRoll;
        internal Button AutoAccept;
        internal Button ShowBounds;
        internal Button Dev;
        public ExtrasView(View root)
        {
            Root = root;
            View _view = View.CreateFromXml($"{Main.PluginDir}\\UI\\Views\\ExtrasView.xml");
            _view.FindChild("Background", out Background);
            Background.SetBitmap("ExtrasBg");
            _view.FindChild("PlayAlertSound", out PlayAlertSound);
            SetupChild(PlayAlertSound, "PlayAlertSound");
            _view.FindChild("AutoAdjustQl", out AutoAdjustQl);
            SetupChild(AutoAdjustQl, "AutoAdjustQl");
            _view.FindChild("RemoveRoll", out RemoveRoll);
            SetupChild(RemoveRoll, "RemoveRoll");
            _view.FindChild("AutoAccept", out AutoAccept);
            SetupChild(AutoAccept, "AutoAccept");
            _view.FindChild("ShowBounds", out ShowBounds);
            SetupChild(ShowBounds, "ShowBounds");
            _view.FindChild("Dev", out Dev);
            Extensions.ButtonSetGfx(Dev, 1000070);
            Dev.Clicked += DevClick;
            Root.AddChild(_view, false);
        }

        private void DevClick(object sender, ButtonBase e)
        {
            Midi.Play("Click");
            Chat.WriteLine("\n/mmr maxitems 'number' - change maximum browser display items\n" +
                           "  (Warning, higher numbers will increase lag while searching)\n\n" +
                           "/mmr shopvalue 'number' - change shop value (for accurate reward display)\n"+
                           "  8 - omni trade shop\n  7 - clan/neut trade shop\n  6 - omni shop\n  4 - other shops\n\n" +
                           "In order to roll for credits, type in 'credits' or 'rewards' in the DB browser\n"+
                           "In the QL section of the item 'Reward + Item', type in your desired minimum cash reward\n" +
                           "Roller will pickup missions with the reward + item reward being >= than the entered\n" +
                           "NOTE: It will always prioritise picking up other items / nanos in the roll list", ChatColor.Green);
        }

        private void SetupChild(Button button, string settingsName)
        {
            button.Tag = Main.Settings.Extras[settingsName];

            if (Main.Settings.Extras[settingsName])
                Extensions.ButtonSetGfx(button, 1000036);
            else
                Extensions.ButtonSetGfx(button, 1000046);

            button.Clicked = ExtrasClick;
        }

        private void ExtrasClick(object sender, ButtonBase e)
        {
            Midi.Play("Click");
            bool on = (bool)e.Tag;

            if (!on)
                Extensions.ButtonSetGfx((Button)e, 1000036);
            else
                Extensions.ButtonSetGfx((Button)e, 1000046);

            e.Tag = !on;
            Main.Settings.Save();
        }
    }
}