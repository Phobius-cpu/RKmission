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
                    "RKMission coordinates Rubi-Ka mission rolling, accepted-mission travel, exact mission entry, " +
                    "dungeon exploration/combat/loot, objective completion and return/exit handling. " +
                    "The Roller, ManagerLoot, Dungeon Map and Roller Settings share one tabbed tool window; " +
                    "Navigation recording remains a separate diagnostic window.";
            }

            if (StartupWindow.FindView("Commands", out TextView commands))
            {
                commands.Text =
                    "MISSION CONTROL\n" +
                    "/rkm auto                 Full automatic mission cycle\n" +
                    "/rkm start                Arm local mission takeover\n" +
                    "/rkm local                Local-only mode; user controls long travel\n" +
                    "/rkm stop                 Stop RKMission automation\n" +
                    "/rkm status               Current automation/travel/dungeon status\n" +
                    "/rkm missions             List accepted mission state\n" +
                    "/rkm complete [id]        Confirm the bound mission manually\n\n" +
                    "ROLLER / DESTINATIONS\n" +
                    "/rkm zone <id|all>        Restrict automatic rolling destination\n" +
                    "/rkm rolls <count>        Maximum offers per rolling attempt\n" +
                    "/rkm limit <count|off>    Accepted missions per automatic cycle\n" +
                    "/mmr maxitems <count>     Roller displayed-item limit\n" +
                    "/mmr shopvalue <value>    Roller shop-value factor\n\n" +
                    "TRAVEL / FGRID\n" +
                    "/rkm travel auto|ground|flying\n" +
                    "/rkm fgrid [scan]         FGrid status / surveyed exits\n" +
                    "/rkm nav                  Open Navigation diagnostics window\n" +
                    "/rkm nav record [name]    Start recording a safe route\n" +
                    "/rkm nav stop             Save the active recording\n" +
                    "/rkm nav list             Print saved routes\n\n" +
                    "WINDOWS / TOOLS\n" +
                    "/rkm loot                 Open ManagerLoot tab\n" +
                    "/ManagerLoot              Open ManagerLoot tab\n" +
                    "/rkm map                  Open Dungeon Map tab\n" +
                    "/mapsettings              Open Dungeon Map tab\n" +
                    "/rkm settings             Open Roller Settings tab\n" +
                    "/lm                       Toggle ManagerLoot enable state\n" +
                    "/printitems               Toggle ManagerLoot item printing";
            }

            if (StartupWindow.FindView("Close", out Button close))
                close.Clicked = (_, _) =>
                {
                    Midi.Play("Click");
                    StartupWindow.Close();
                };

            StartupWindow.MoveToCenter();
        }
    }
}
