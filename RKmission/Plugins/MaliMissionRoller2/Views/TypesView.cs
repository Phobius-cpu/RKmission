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
    public class TypesView
    {
        internal View Root;
        internal Button FindTarget;
        internal Button KillTarget;
        internal Button FindItem;
        internal Button UseItem;
        internal Button ReturnItem;

        public TypesView(View root)
        {
            Root = root;
            View _view = View.CreateFromXml($"{Main.PluginDir}\\UI\\Views\\TypesView.xml");
            _view.FindChild("FindTarget", out FindTarget);
            SetupChild(FindTarget, "FindTarget");
            _view.FindChild("KillTarget", out KillTarget);
            SetupChild(KillTarget, "KillTarget");
            _view.FindChild("FindItem", out FindItem);
            SetupChild(FindItem, "FindItem");
            _view.FindChild("UseItem", out UseItem);
            SetupChild(UseItem, "UseItem");
            _view.FindChild("ReturnItem", out ReturnItem);
            SetupChild(ReturnItem, "ReturnItem");

            Root.AddChild(_view, false);
        }

        private void SetupChild(Button button, string settingsName)
        {
            button.Tag = Main.Settings.Types[settingsName];

            if (Main.Settings.Types[settingsName])
                Extensions.SetButtonSymbol(button, "X");
            else
                Extensions.SetButtonSymbol(button, " ");

            button.Clicked = MissionTypeClick;
        }

        private void MissionTypeClick(object sender, ButtonBase e)
        {
            Midi.Play("Click");

            bool on = (bool)e.Tag;

            if (!on)
                Extensions.SetButtonSymbol((Button)e, "X");
            else
                Extensions.SetButtonSymbol((Button)e, " ");

            e.Tag = !on;
            Main.Settings.Save();
        }
    }
}
