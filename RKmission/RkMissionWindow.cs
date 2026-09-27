using System;
using System.IO;
using AOSharp.Common.GameData;
using AOSharp.Common.GameData.UI;
using AOSharp.Core.UI;

namespace RKmission
{
    // AO# window adapted from Mali's roller and Manager.Loot control layouts.
    internal sealed class RkMissionWindow
    {
        private readonly string _xmlPath;
        private readonly MissionRoller _roller;
        private readonly LootRules _rules;
        private readonly Action<int> _setZone;
        private readonly Action _start;
        private readonly Action _stop;
        private readonly Func<string> _status;
        private readonly Action<string> _say;
        private Window _window;

        public RkMissionWindow(string pluginDir, MissionRoller roller, LootRules rules,
            Action<int> setZone, Action start, Action stop, Func<string> status, Action<string> say)
        {
            _xmlPath = Path.Combine(pluginDir, "UI", "RKmissionWindow.xml");
            _roller = roller;
            _rules = rules;
            _setZone = setZone;
            _start = start;
            _stop = stop;
            _status = status;
            _say = say;
        }

        public void Show()
        {
            try
            {
                if (_window == null || !_window.IsValid)
                {
                    _window = Window.CreateFromXml("RKMission", _xmlPath,
                        windowSize: new Rect(0, 0, 500, 390),
                        windowStyle: WindowStyle.Default,
                        windowFlags: WindowFlags.AutoScale | WindowFlags.NoFade);
                    if (_window == null)
                    {
                        _say("Could not open RKMission window. Check the UI XML in the plugin folder.");
                        return;
                    }
                    Bind();
                    SetText("zoneInput", _roller.ZoneId.ToString());
                    SetText("rollsInput", _roller.MaxRolls.ToString());
                    SetText("difficultyInput", _roller.Difficulty.ToString());
                    SetText("minQlInput", "1");
                    SetText("maxQlInput", "500");
                    SetText("quantityInput", "999");
                }
                _window.Show(true);
                Refresh();
            }
            catch (Exception ex)
            {
                _say("Could not open RKMission window: " + ex.Message);
            }
        }

        public void Close()
        {
            if (_window != null && _window.IsValid)
                _window.Close();
            _window = null;
        }

        public void Refresh()
        {
            if (_window == null || !_window.IsValid)
                return;
            if (_window.FindView("statusText", out TextView status))
                status.Text = _status();
            if (_window.FindView("lootList", out TextView loot))
                loot.Text = _rules.Describe().Replace("; ", "\n");
        }

        private void Bind()
        {
            if (_window.FindView("startButton", out Button start))
                start.Clicked += StartClicked;
            if (_window.FindView("stopButton", out Button stop))
                stop.Clicked += StopClicked;
            if (_window.FindView("addButton", out Button add))
                add.Clicked += AddClicked;
            if (_window.FindView("removeButton", out Button remove))
                remove.Clicked += RemoveClicked;
        }

        private void StartClicked(object sender, ButtonBase button)
        {
            if (!TryReadInt("zoneInput", out int zone) || zone <= 0 ||
                !TryReadInt("rollsInput", out int rolls) || rolls <= 0 ||
                !TryReadInt("difficultyInput", out int difficulty) ||
                difficulty < 0 || difficulty > 255)
            {
                Notice("Enter a positive playfield ID and roll limit, and a difficulty from 0 to 255.");
                return;
            }
            _setZone(zone);
            _roller.MaxRolls = rolls;
            _roller.Difficulty = (byte)difficulty;
            _start();
            Refresh();
        }

        private void StopClicked(object sender, ButtonBase button)
        {
            _stop();
            Refresh();
        }

        private void AddClicked(object sender, ButtonBase button)
        {
            if (!_window.FindView("nameInput", out TextInputView name) ||
                !TryReadInt("minQlInput", out int low) ||
                !TryReadInt("maxQlInput", out int high) ||
                !TryReadInt("quantityInput", out int quantity))
            {
                Notice("Enter an item name or ID, QL range and quantity.");
                return;
            }
            bool exact = _window.FindView("exactCheck", out Checkbox exactCheck) && exactCheck.IsChecked;
            bool oneEach = _window.FindView("oneEachCheck", out Checkbox oneEachCheck) && oneEachCheck.IsChecked;
            if (!_rules.Add(name.Text, low, high, quantity, exact, oneEach))
            {
                Notice("Name is required. QL must be 1-500 and quantity 1-999.");
                return;
            }
            name.Text = "";
            Notice("Loot rule added.");
            Refresh();
        }

        private void RemoveClicked(object sender, ButtonBase button)
        {
            if (!TryReadInt("removeInput", out int index) || !_rules.Remove(index))
            {
                Notice("Enter the number of a listed loot rule.");
                return;
            }
            Notice("Loot rule removed.");
            Refresh();
        }

        private bool TryReadInt(string viewName, out int value)
        {
            value = 0;
            return _window.FindView(viewName, out TextInputView input) &&
                int.TryParse(input.Text, out value);
        }

        private void SetText(string viewName, string value)
        {
            if (_window.FindView(viewName, out TextInputView input))
                input.Text = value;
        }

        private void Notice(string message)
        {
            if (_window.FindView("noticeText", out TextView notice))
                notice.Text = message;
        }
    }
}
