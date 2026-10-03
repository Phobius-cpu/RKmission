using AOSharp.Common.GameData.UI;
using AOSharp.Core.UI;

namespace MaliMissionRoller2
{
    // RKMission now owns the shared tool cluster, so the old roller-specific
    // help button documents the coordinator and its user-facing commands.
    public class HelpWindow
    {
        public Window StartupWindow;

        public HelpWindow()
        {
            StartupWindow = Window.CreateFromXml("RKMissionHelp", $"{Main.PluginDir}\\UI\\Windows\\HelpWindow.xml",
                WindowStyle.Default, WindowFlags.AutoScale | WindowFlags.NoFade);

            if (StartupWindow.FindView("Purpose", out TextView purpose))
            {
                purpose.Text =
                    "RKMission is an AO# plugin designed to automate\n" +
                    "Rubi-Ka missions from start to finish.\n\n" +
                    "It combines mission rolling, travel, navigation, combat,\n" +
                    "looting, and mission completion into one coordinated system,\n" +
                    "with the goal of reliable long-term autonomous operation.";
            }

            SetText("MissionCommands",
                "/rkm auto\n" +
                "/rkm start\n" +
                "/rkm local\n" +
                "/rkm stop\n" +
                "/rkm status\n" +
                "/rkm missions\n" +
                "/rkm complete [id]");
            SetText("MissionDescriptions",
                "Full automatic mission cycle\n" +
                "Arm local mission takeover\n" +
                "Local-only; user controls long travel\n" +
                "Stop RKMission automation\n" +
                "Show automation/travel/dungeon status\n" +
                "List accepted mission state\n" +
                "Confirm the bound mission manually");

            SetText("RollerCommands",
                "/rkm zone <id|all>\n" +
                "/rkm rolls <count>\n" +
                "/rkm limit <count|off>\n" +
                "/mmr maxitems <count>\n" +
                "/mmr shopvalue <value>");
            SetText("RollerDescriptions",
                "Restrict rolling destination\n" +
                "Maximum offers per rolling attempt\n" +
                "Accepted missions per cycle\n" +
                "Roller displayed-item limit\n" +
                "Roller shop-value factor");

            SetText("TravelCommands",
                "/rkm travel auto|ground|flying\n" +
                "/rkm fgrid [scan|nav]\n" +
                "/rkm nav\n" +
                "/rkm nav record [name]\n" +
                "/rkm nav stop\n" +
                "/rkm nav list");
            SetText("TravelDescriptions",
                "Select local movement mode\n" +
                "Surveyed exits / mesh status\n" +
                "Open Navigation diagnostics\n" +
                "Start recording a safe route\n" +
                "Save the active recording\n" +
                "Print saved routes");

            SetText("ToolCommands",
                "/rkm loot\n" +
                "/ManagerLoot\n" +
                "/rkm map\n" +
                "/mapsettings\n" +
                "/rkm settings\n" +
                "/lm\n" +
                "/printitems");
            SetText("ToolDescriptions",
                "Open ManagerLoot tab\n" +
                "Open ManagerLoot tab\n" +
                "Open Dungeon Map tab\n" +
                "Open Dungeon Map tab\n" +
                "Open Roller settings view\n" +
                "Toggle ManagerLoot enable state\n" +
                "Toggle ManagerLoot item printing");

            if (StartupWindow.FindView("Close", out Button close))
                close.Clicked = (_, _) =>
                {
                    Midi.Play("Click");
                    StartupWindow.Close();
                };

            StartupWindow.MoveToCenter();
        }

        private void SetText(string name, string text)
        {
            if (StartupWindow.FindView(name, out TextView view))
                view.Text = text;
        }
    }
}
