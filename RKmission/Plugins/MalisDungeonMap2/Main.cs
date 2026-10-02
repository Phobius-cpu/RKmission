using AOSharp.Core;
using AOSharp.Core.UI;
using System;

namespace MalisDungeonMap2
{
    // Hosted by RKmission's single AO# entry point.
    public class DungeonMap
    {
        public static string PluginDir;
        private Config _config;
        private MainWindow _window;
        private EmbeddedMainView _embeddedView;
        private DungeonMapRenderer _renderer;
        private MaliMissionRoller2.MainWindow _host;
        private Func<bool> _showHost;

        public void Run(string pluginDir)
        {
            RunCore(pluginDir);

            if (_config.MapConfig.ShowConfigOnStartup)
                ToggleWindow();
        }

        public void RunEmbedded(string pluginDir, MaliMissionRoller2.MainWindow host, Func<bool> showHost)
        {
            _host = host;
            _showHost = showHost;
            _host.WindowReady += OnHostWindowReady;
            _host.EnsureDungeonMapView = EnsureEmbeddedView;
            RunCore(pluginDir);

            if (_config.MapConfig.ShowConfigOnStartup)
                ToggleWindow();
        }

        private void RunCore(string pluginDir)
        {
            Chat.WriteLine("- Mali's Dungeon Map 2.0 -\n" +
                "Note: If map is not rendering, change graphic setting in launcher to 'Direct 3D T&L HAL'\n" +
                "/mapsettings");

            PluginDir = pluginDir;
            _renderer = DungeonMapRenderer.Start();
            _config = Config.TryLoad($"{pluginDir}\\config.json");

            DungeonMapRenderer.MapConfig = _config.MapConfig;
            Chat.RegisterCommand("mapsettings", (string command, string[] param, ChatWindow chatWindow) =>
            {
                ToggleWindow();
            });
        }

        private void OnHostWindowReady()
        {
            _embeddedView?.Dispose();
            _embeddedView = null;
        }

        private void EnsureEmbeddedView()
        {
            if (_host?.Window?.IsValid != true) return;
            if (_embeddedView == null)
            {
                _embeddedView = new EmbeddedMainView(
                    $"{PluginDir}\\UI\\MainWindow.xml",
                    $"{PluginDir}\\UI\\ColorView.xml",
                    $"{PluginDir}\\UI\\EntryView.xml",
                    _config);
                _host.AttachDungeonMapView(_embeddedView.Root);
            }
        }

        public void ToggleWindow()
        {
            try
            {
                if (_host != null)
                {
                    if (_host.Window?.IsValid != true)
                        _showHost?.Invoke();
                    if (_host.Window?.IsValid == true)
                    {
                        EnsureEmbeddedView();
                        _host.ShowDungeonMapTab();
                    }
                    return;
                }

                if (_window?.Window != null)
                {
                    _window.Dispose();
                }
                else
                {
                    _window = new MainWindow("Mali's Dungeon Map 2", $"{PluginDir}\\UI\\MainWindow.xml",
                        $"{PluginDir}\\UI\\ColorView.xml", $"{PluginDir}\\UI\\EntryView.xml", _config);
                    _window.Activate();
                }
            }
            catch (Exception ex)
            {
                Chat.WriteLine(ex.Message);
            }
        }

        public void Teardown()
        {
            _renderer?.Stop();
            if (_host != null)
            {
                _host.WindowReady -= OnHostWindowReady;
                _host.EnsureDungeonMapView = null;
            }
            _embeddedView?.Dispose();
            _embeddedView = null;
            if (_window?.Window != null) _window.Dispose();
        }
    }
}
