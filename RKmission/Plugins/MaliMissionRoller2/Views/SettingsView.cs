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
    public class SettingsView
    {
        public View Root;
        public ItemDisplayView ItemDisplay;
        public SlidersView Sliders;
        public PlayfieldView Locations;
        public TypesView MissionTypes;
        public ExtrasView ExtraOptions;
        private View _settingsView;

        public SettingsView(View root)
        {
            Root = root;

            _settingsView = View.CreateFromXml($"{Main.PluginDir}\\UI\\Views\\SettingsView.xml");

            if (_settingsView.FindChild("ItemDisplayRoot", out View itemDisplayRoot))
                ItemDisplay = new ItemDisplayView(itemDisplayRoot);

            if (_settingsView.FindChild("SliderRoot", out View sliderRoot))
                Sliders = new SlidersView(sliderRoot);

            if (_settingsView.FindChild("LocationRoot", out View locationRoot))
                Locations = new PlayfieldView(locationRoot);

            if (_settingsView.FindChild("TypesRoot", out View typesRoot))
                MissionTypes = new TypesView(typesRoot);

            if (_settingsView.FindChild("ExtrasRoot", out View extrasRoot))
                ExtraOptions = new ExtrasView(extrasRoot);

            Root.AddChild(_settingsView, false);
        }

        internal void UpdateUI(bool drawBounds)
        {
            ItemDisplay.Update();

            if (drawBounds)
                Locations.DrawBounds();
        }

        internal void Hide()
        {
            if (!Root.FindChild("SettingsViewRoot", out View settingsRoot))
                return;

            Root.RemoveChild(_settingsView);
            Root.FitToContents();
        }

        internal void Show()
        {
            Root.AddChild(_settingsView, false);
            Root.FitToContents();
        }
    }
}