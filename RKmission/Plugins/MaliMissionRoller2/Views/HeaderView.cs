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
    public class HeaderView
    {
        public Button Help;
        public Button Start;
        public Button Request;
        public Button Settings;
        private View _root;

        public HeaderView(View root)
        {
            View _view = View.CreateFromXml($"{Main.PluginDir}\\UI\\Views\\HeaderView.xml");

            _root = root;
            _view.FindChild("Help", out Help);
            _view.FindChild("Start", out Start);
            _view.FindChild("Request", out Request);
            _view.FindChild("Settings", out Settings);
            bool isInSettings = false;
            Settings.Tag = isInSettings;

            _root.AddChild(_view, false);
        }

        internal void Hide()
        {
            _root.LimitMaxSize(new Vector2(0, 0));
        }

        internal void Show()
        {
            _root.LimitMaxSize(_root.CalculatePreferredSize());
        }
    }
}
