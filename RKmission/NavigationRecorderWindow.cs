using System;
using AOSharp.Core;
using AOSharp.Core.UI;

namespace RKmission
{
    // Temporary/diagnostic UI for recording FGrid and other evidence-based routes.
    // It is deliberately independent from the permanent Roller/Loot/Dungeon/Settings cluster.
    internal sealed class NavigationRecorderWindow : IDisposable
    {
        private readonly NavigationRouteRecorder _recorder;
        private readonly Action<string> _say;
        private readonly string _xmlPath;
        private Window _window;
        private TextInputView _routeName;
        private TextView _status;
        private TextView _routes;
        private DateTime _nextRefresh;

        public NavigationRecorderWindow(string pluginDir, NavigationRouteRecorder recorder, Action<string> say)
        {
            _recorder = recorder;
            _say = say;
            _xmlPath = System.IO.Path.Combine(pluginDir, "Plugins", "MaliMissionRoller2", "UI", "Windows", "NavigationWindow.xml");
            Game.OnUpdate += OnUpdate;
        }

        public void Show()
        {
            if (_window?.IsValid == true)
            {
                _window.Show(true);
                Refresh();
                return;
            }

            try
            {
                _window = Window.CreateFromXml("RKMissionNavigation", _xmlPath,
                    WindowStyle.Default, WindowFlags.AutoScale | WindowFlags.NoFade);

                _window.FindView("RouteName", out _routeName);
                _window.FindView("Status", out _status);
                _window.FindView("Routes", out _routes);

                if (_window.FindView("StartRecording", out Button start))
                    start.Clicked = (_, _) =>
                    {
                        _recorder.Start(_routeName?.Text);
                        Refresh();
                    };

                if (_window.FindView("StopRecording", out Button stop))
                    stop.Clicked = (_, _) =>
                    {
                        _recorder.Stop();
                        Refresh();
                    };

                if (_window.FindView("Refresh", out Button refresh))
                    refresh.Clicked = (_, _) => Refresh();

                if (_window.FindView("Close", out Button close))
                    close.Clicked = (_, _) => _window.Close();

                _window.MoveToCenter();
                _window.Show(true);
                Refresh();
            }
            catch (Exception ex)
            {
                _say("Navigation diagnostics window could not be opened: " + ex.Message);
            }
        }

        private void OnUpdate(object sender, float elapsed)
        {
            if (_window?.IsValid != true || !_window.IsVisible || DateTime.UtcNow < _nextRefresh)
                return;
            _nextRefresh = DateTime.UtcNow.AddMilliseconds(500);
            Refresh();
        }

        private void Refresh()
        {
            if (_window?.IsValid != true) return;
            if (_status != null)
            {
                string player = DynelManager.LocalPlayer == null ? "player unavailable" :
                    $"PF {Playfield.ModelIdentity.Instance}, pos=({DynelManager.LocalPlayer.Position.X:0.0}, " +
                    $"{DynelManager.LocalPlayer.Position.Y:0.0}, {DynelManager.LocalPlayer.Position.Z:0.0})";
                _status.Text = $"Recorder: {_recorder.Status}\nCurrent: {player}";
            }

            if (_routes != null)
            {
                var summaries = _recorder.RouteSummaries;
                _routes.Text = summaries.Count == 0
                    ? "No saved routes. Stand at one safe endpoint, enter a name, Start Recording, walk the actual safe path, then Stop Recording."
                    : string.Join("\n", summaries);
            }
        }

        public void Dispose()
        {
            Game.OnUpdate -= OnUpdate;
            if (_window?.IsValid == true) _window.Close();
            _window = null;
        }
    }
}
