using AOBotBase.BTViewer;
using AOSharp.Common.GameData;
using AOSharp.Common.GameData.UI;
using AOSharp.Common.Unmanaged.DataTypes;
using AOSharp.Core;
using AOSharp.Core.UI;
using InfernoMission.Bot;
using Serilog.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AIMission.Bot.Views
{
    internal class MainWindow : AOSharpWindow
    {
        internal int MissionsCompleted = 0;
        internal double TotalRuntime = 0;

        private ViewCache _viewCache;
        private InfernoMissionBot _bot;
        private Logger _logger;

        public MainWindow(string name, string path, InfernoMissionBot bot) : base(name, path, WindowStyle.Popup)
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
            Window.FindView("AvgMissionTime", out _viewCache.AvgMissionTime);

            if (Window.FindView("SideSelector", out _viewCache.SideSelector))
                _viewCache.SideSelector.SetValue(Variant.Create((int)Side.Neutral));

            if (Window.FindView("DifficultySelector", out _viewCache.DifficultySelector))
                _viewCache.DifficultySelector.SetValue(Variant.Create((int)MissionDifficulty.Easy));
        }

        private void StartStopButtonClicked()
        {
            if (!_bot.Enabled)
            {
                if (!Team.IsInTeam)
                {
                    _logger.Error($"You must be in a team to start.", ChatColor.Red);
                    return;
                }
                else if (!Team.IsLeader)
                {
                    _logger.Error($"Only the team leader can start.", ChatColor.Red);
                    return;
                }
            }

            if (_bot.Enabled)
                if (Team.IsLeader)
                    _bot.StopAll();
                else
                    _bot.Stop();
            else
                _bot.StartAll();
        }

        public void UpdateButton()
        {
            _viewCache.StartStopButton.SetLabel(_bot.Enabled ? "Stop" : "Start");
        }

        public void MissionFinished(double duration)
        {
            MissionsCompleted++;
            TotalRuntime += duration;

            _viewCache.MissionCounterLabel.Text = MissionsCompleted.ToString();

            var avg = TotalRuntime / MissionsCompleted;
            _viewCache.AvgMissionTime.Text = $"{TimeSpan.FromSeconds(avg).ToString(@"hh\:mm\:ss")}";
        }

        public Side GetSelectedMissionSide()
        {
            Variant value = _viewCache.SideSelector.GetValue();
            return (Side)value.AsInt32();
        }

        public MissionDifficulty GetSelectedMissionDifficulty()
        {
            Variant value = _viewCache.DifficultySelector.GetValue();
            return (MissionDifficulty)value.AsInt32();
        }

        public class ViewCache
        {
            public TextView AvgMissionTime;
            public TextView MissionCounterLabel;
            public Button StartStopButton;
            public RadioButtonGroup SideSelector;
            public RadioButtonGroup DifficultySelector;
        }
    }
}
