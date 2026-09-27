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
        private BitmapView _background;
        private BitmapView _icon;
        private TextView _title;

        public HeaderView(View root)
        {
            View _view = View.CreateFromXml($"{Main.PluginDir}\\UI\\Views\\HeaderView.xml");

            _root = root;
            _view.FindChild("Title", out _title);
            _title.Text = "- Mali's Mission Roller 2.0 -";
            _view.FindChild("Icon", out _icon);
            _icon.SetBitmap("HeaderIcon");
            _view.FindChild("Help", out Help);
            Extensions.ButtonSetGfx(Help, 1000040);
            _view.FindChild("Background", out _background);
            _background.SetBitmap("WindowBg");
            _view.FindChild("Start", out Start);
            Extensions.ButtonSetGfx(Start, 1000053);
            _view.FindChild("Request", out Request);
            Extensions.ButtonSetGfx(Request, 1000048);
            _view.FindChild("Settings", out Settings);
            Extensions.ButtonSetGfx(Settings, 1000050);
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