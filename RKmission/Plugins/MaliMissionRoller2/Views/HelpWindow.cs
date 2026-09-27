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
    public class HelpWindow
    {
        public Window StartupWindow;
        private Window _graphicalWindow;

        public HelpWindow()
        {
            StartupWindow = Window.CreateFromXml("MaliMissionRollerHelp", $"{Main.PluginDir}\\UI\\Windows\\HelpWindow.xml",
                WindowStyle.Popup, WindowFlags.AutoScale | WindowFlags.NoFade);

            _graphicalWindow = Window.CreateFromXml("MaliMissionRollerGraphicalHelp", $"{Main.PluginDir}\\UI\\Windows\\GraphicalHelpWindow.xml",
                WindowStyle.Popup, WindowFlags.AutoScale | WindowFlags.NoFade);

            if (StartupWindow.FindView("Text", out TextView textView))
            {
                textView.Text = $"\n\n " +
                $"- Take advantage over continuous rolls\n " +
                $"- Roll with multiple clients simultaneously\n " +
                $"- Never skip a good roll due to server lag\n " +
                $"- Built in item db with filtering options\n " +
                $"- Use the 'Auto Adjust Lvl Slider' option \n " +
                $"  to automatically adjust the lvl slider\n " +
                $"  inbetween rolls for full automation\n " +
                $"- Press the Dev button next to 'Extras'\n " +
                $"  for some bonus / in dev features\n " +
                $"- Press the '?' in the UI to reopen me\n\n" +
                $"* QUICK ITEM BROWSER GUIDE *\n " +
                $"- You can search for items based on\n " +
                $"  their name or modifications or both\n " +
                $"  Use the two blue textboxes provided\n " +
                $"  after switching to 'DB Browser'\n " +
                $"  Textbox1 example: eye imp ref\n " +
                $"  Textbox2 example: assa rif,tutor'";
            }

            if (StartupWindow.FindView("Text2", out TextView textView2))
            {
                textView2.Text =$"\n"+
                $"- For bugs / glitches / requests:\n " +
                $"  Discord:  Pixelmania#0349\n\n " +
                $"       ~ Made with AOSharp SDK";
            }

            if (StartupWindow.FindView("Close", out Button _closeHelp))
            {
                Extensions.ButtonSetGfx(_closeHelp, 1000064);
                _closeHelp.Clicked = CloseHelpClick;
            }

            if (StartupWindow.FindView("GraphicalGuide", out Button _graphicalGuide))
            {
                Extensions.ButtonSetGfx(_graphicalGuide, 1000073);
                _graphicalGuide.Clicked = GraphicalGuideClick;
            }

            if (StartupWindow.FindView("Logo", out BitmapView _logo))
            {
                _logo.SetBitmap("BigLogo");
            }

            if (Main.Settings.Extras["StartHelp"])
            {
                StartupWindow.MoveToCenter();
                StartupWindow.Show(true);
            }
        }

        private void GraphicalGuideClick(object sender, ButtonBase e)
        {
            Midi.Play("Click");

            _graphicalWindow = Window.CreateFromXml("MaliMissionRollerGraphicalHelp", $"{Main.PluginDir}\\UI\\Windows\\GraphicalHelpWindow.xml",
                  WindowStyle.Popup, WindowFlags.AutoScale | WindowFlags.NoFade);

            if (_graphicalWindow.FindView("Close", out Button _closeGraphical))
            {
                Extensions.ButtonSetGfx(_closeGraphical, 1000064);
                _closeGraphical.Clicked = CloseGraphicalClick;
            }

            if (_graphicalWindow.FindView("Image", out BitmapView _graphical))
            {
                _graphical.SetBitmap("GraphicalGuide");
            }

            _graphicalWindow.MoveToCenter();
            _graphicalWindow.Show(true);
        }

        private void CloseHelpClick(object sender, ButtonBase e)
        {
            Midi.Play("Click");
            StartupWindow.Close();
        }

        private void CloseGraphicalClick(object sender, ButtonBase e)
        {
            Midi.Play("Click");
            _graphicalWindow.Close();
        }
    }
}