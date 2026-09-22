using AOBotBase.BTViewer;
using AOSharp.Common.GameData;
using AOSharp.Common.GameData.UI;
using AOSharp.Common.Unmanaged.DataTypes;
using AOSharp.Core;
using AOSharp.Core.UI;
using S13.Bot;
using Serilog.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace S13bot.UI
{
    internal class S13BotWindow : AOSharpWindow
    {
        private ViewCache _viewCache;
        private S13Bot _bot;
        private Logger _logger;

        public S13BotWindow(string name, string path, S13Bot bot) : base(name, path, WindowStyle.Popup)
        {
            _viewCache = new ViewCache();
            _bot = bot;
            _logger = bot.Logger;
        }

        protected override void OnWindowCreating()
        {
            if (Window.FindView("StartStopButton", out _viewCache.StartStopButton))
                _viewCache.StartStopButton.Clicked += (o, e) => StartStopButtonClicked();

            Window.FindView("Counter", out _viewCache.CounterLabel);

            if (Window.FindView("PullStyleSelector", out _viewCache.PullStyleSelector))
                _viewCache.PullStyleSelector.SetValue(Variant.Create((int)PullStyle.Safe));
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

        public void UpdateInstanceCounter()
        {
            _viewCache.CounterLabel.Text = _bot.InstancesCompleted.ToString();
        }

        public PullStyle GetSelectedPullStyle()
        {
            Variant value = _viewCache.PullStyleSelector.GetValue();
            return (PullStyle)value.AsInt32();
        }

        public class ViewCache
        {
            public TextView CounterLabel;
            public Button StartStopButton;
            public RadioButtonGroup PullStyleSelector;
        }
    }
}
