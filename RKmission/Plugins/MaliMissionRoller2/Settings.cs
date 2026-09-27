using AOSharp.Common.GameData;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MaliMissionRoller2
{
    public class Settings
    {
        public Dictionary<string, bool> Types;
        public Dictionary<string, bool> Extras;
        public Dictionary<string, float> Sliders;
        public Dictionary<string, bool> Database;
        public Dictionary<string, int> Dev;

        public Dictionary<string, LocationModel> Locations;
        public Vector2 Frame;

        public class LocationModel
        {
            public Bounds Bounds;
            public bool State;
        }

        public Settings()
        {
        }

        public void Save()
        {
            foreach (PlayfieldEntryView locationEntry in Main.Window.SettingsView.Locations.Entries)
                Locations[locationEntry.Name.Text] = new LocationModel
                {
                    State = (bool)locationEntry.Toggle.Tag,
                    Bounds = new Bounds
                    {
                        Coord1 = locationEntry.Bounds.Coord1,
                        Coord2 = locationEntry.Bounds.Coord2,
                    }
                };

            Sliders["EasyHard"] = Main.Window.SettingsView.Sliders.EasyHard.Value;
            Sliders["GoodBad"] = Main.Window.SettingsView.Sliders.GoodBad.Value;
            Sliders["OrderChaos"] = Main.Window.SettingsView.Sliders.OrderChaos.Value;
            Sliders["OpenHidden"] = Main.Window.SettingsView.Sliders.OpenHidden.Value;
            Sliders["PhysicalMystical"] = Main.Window.SettingsView.Sliders.PhysicalMystical.Value;
            Sliders["HeadonStealth"] = Main.Window.SettingsView.Sliders.HeadonStealth.Value;
            Sliders["CreditsXp"] = Main.Window.SettingsView.Sliders.CreditsXp.Value;

            Types["ReturnItem"] = (bool)Main.Window.SettingsView.MissionTypes.ReturnItem.Tag;
            Types["KillTarget"] = (bool)Main.Window.SettingsView.MissionTypes.KillTarget.Tag;
            Types["FindTarget"] = (bool)Main.Window.SettingsView.MissionTypes.FindTarget.Tag;
            Types["FindItem"] = (bool)Main.Window.SettingsView.MissionTypes.FindItem.Tag;
            Types["UseItem"] = (bool)Main.Window.SettingsView.MissionTypes.UseItem.Tag;

            Extras["PlayAlertSound"] = (bool)Main.Window.SettingsView.ExtraOptions.PlayAlertSound.Tag;
            Extras["AutoAdjustQl"] = (bool)Main.Window.SettingsView.ExtraOptions.AutoAdjustQl.Tag;
            Extras["RemoveRoll"] = (bool)Main.Window.SettingsView.ExtraOptions.RemoveRoll.Tag;
            Extras["AutoAccept"] = (bool)Main.Window.SettingsView.ExtraOptions.AutoAccept.Tag;
            Extras["ShowBounds"] = (bool)Main.Window.SettingsView.ExtraOptions.ShowBounds.Tag;
            Extras["StartHelp"] = false;


            Database["Implants"] = (bool)Main.Window.SettingsView.ItemDisplay.Implants.Tag;
            Database["Refined"] = (bool)Main.Window.SettingsView.ItemDisplay.Refined.Tag;
            Database["Clusters"] = (bool)Main.Window.SettingsView.ItemDisplay.Clusters.Tag;
            Database["Nanos"] = (bool)Main.Window.SettingsView.ItemDisplay.Nanos.Tag;
            Database["Rest"] = (bool)Main.Window.SettingsView.ItemDisplay.Rest.Tag;


            Frame.X = Main.Window.Window.GetFrame().MinX;
            Frame.Y = Main.Window.Window.GetFrame().MinY;

            File.WriteAllText($"{Main.PluginDir}\\JSON\\Settings.json", JsonConvert.SerializeObject(this));

            List<RollEntryViewModel> rollModels = Main.Window.SettingsView.ItemDisplay.RollEntryViews.Select(x => x.RollEntryModel).ToList();
            File.WriteAllText($"{Main.PluginDir}\\JSON\\RollList.json", JsonConvert.SerializeObject(rollModels));
        }
    }
}