using AOBotBase.BTViewer;
using AOSharp.Common.GameData;
using AOSharp.Common.GameData.UI;
using AOSharp.Common.Unmanaged.DataTypes;
using AOSharp.Core;
using AOSharp.Core.UI;
using Serilog.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AIMission.Bot.Views
{
    internal class AIMissionBotWindow : AOSharpWindow
    {
        private ViewCache _viewCache;
        private AIMissionBot _bot;
        private Logger _logger;

        public AIMissionBotWindow(string name, string path, AIMissionBot bot) : base(name, path, WindowStyle.Popup)
        {
            _viewCache = new ViewCache();
            _bot = bot;
            _logger = bot.Logger;
        }

        protected override void OnWindowCreating()
        {
            if (Window.FindView("StartStopButton", out _viewCache.StartStopButton))
                _viewCache.StartStopButton.Clicked += (o, e) => StartStopButtonClicked();

            Window.FindView("MissionCounter", out _viewCache.MissionCounterLabel);

            if (Window.FindView("DifficultySelector", out _viewCache.DifficultySelector))
                _viewCache.DifficultySelector.SetValue(Variant.Create((int)AIMissionBot.Config.MissionDifficulty));

            foreach(var boss in AIMissionBot.Config.Bosses)
            {
                if (!Window.FindView(boss.Key, out Checkbox bossCheckBox))
                    continue;

                bossCheckBox.SetValue(boss.Value);
                _viewCache.BossCheckBoxes[boss.Key] = bossCheckBox;
            }

            if (Window.FindView("ClearCoccoonsCheckbox", out _viewCache.ClearCoccoonsCheckBox))
                _viewCache.ClearCoccoonsCheckBox.SetValue(AIMissionBot.Config.ClearCoccoons);
        }

        private void StartStopButtonClicked()
        {
            if (!_bot.Enabled)
            {
                if (!Team.IsInTeam)
                {
                    _logger.Information($"You must be in a team to start.", ChatColor.Red);
                    return;
                }
                else if (!Team.IsLeader)
                {
                    _logger.Information($"Only the team leader can start.", ChatColor.Red);
                    return;
                }
            }

            if (_bot.Enabled)
            {
                if (Team.IsLeader)
                    _bot.StopAll();
                else
                    _bot.Stop();
            }
            else
            {
                UpdateConfig();
                _bot.StartAll();
            }
        }

        private void UpdateConfig()
        {
            AIMissionBot.Config.MissionDifficulty = GetSelectedMissionDifficulty();
            AIMissionBot.Config.ClearCoccoons = _viewCache.ClearCoccoonsCheckBox.IsChecked;

            foreach(var bossCheckBox in _viewCache.BossCheckBoxes)
                AIMissionBot.Config.Bosses[bossCheckBox.Key] = bossCheckBox.Value.IsChecked;

            AIMissionBot.Config.Save();
        }

        public void UpdateButton()
        {
            _viewCache.StartStopButton.SetLabel(_bot.Enabled ? "Stop" : "Start");
        }

        public void UpdateMissionCounter()
        {
            _viewCache.MissionCounterLabel.Text = _bot.MissionsCompleted.ToString();
        }

        private MissionDifficulty GetSelectedMissionDifficulty()
        {
            Variant value = _viewCache.DifficultySelector.GetValue();
            return (MissionDifficulty)value.AsInt32();
        }

        public class ViewCache
        {
            public TextView MissionCounterLabel;
            public Button StartStopButton;
            public RadioButtonGroup DifficultySelector;
            public Dictionary<string, Checkbox> BossCheckBoxes = new Dictionary<string, Checkbox>();
            public Checkbox ClearCoccoonsCheckBox;
        }
    }
}
