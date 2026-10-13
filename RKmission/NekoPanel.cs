using System;
using AOSharp.Common.GameData.UI;
using AOSharp.Core;
using AOSharp.Core.UI;

namespace RKmission
{
    internal sealed class NekoPanel : IDisposable
    {
        private readonly MissionEntranceResolver _resolver;
        private readonly Action _warp;
        private readonly Action _help;
        private TextView _enabled, _mission, _keys, _diagnostics;
        private DateTime _nextRefresh;
        public View Root { get; }

        public NekoPanel(string pluginDir, MissionEntranceResolver resolver, Action warp, Action help)
        {
            _resolver = resolver; _warp = warp; _help = help;
            Root = View.CreateFromXml(System.IO.Path.Combine(pluginDir,
                "Plugins", "MaliMissionRoller2", "UI", "Views", "NekoView.xml"));
            Root.FindChild("NekoEnabled", out _enabled);
            Root.FindChild("NekoMission", out _mission);
            Root.FindChild("NekoKeys", out _keys);
            Root.FindChild("NekoDiagnostics", out _diagnostics);
            if (Root.FindChild("NekoWarp", out Button warpButton)) warpButton.Clicked = (_, _) => _warp();
            if (Root.FindChild("NekoHelp", out Button helpButton)) helpButton.Clicked = (_, _) => _help();
            if (Root.FindChild("NekoToggle", out Button toggle)) toggle.Clicked = (_, _) =>
            { _resolver.Enabled = !_resolver.Enabled; if (!_resolver.Enabled) _resolver.Reset(); Refresh(); };
            Game.OnUpdate += OnUpdate;
            Refresh();
        }

        private void OnUpdate(object sender, float elapsed)
        { if (DateTime.UtcNow >= _nextRefresh) { _nextRefresh = DateTime.UtcNow.AddMilliseconds(500); Refresh(); } }

        private void Refresh()
        {
            if (_enabled != null) _enabled.Text = _resolver.Enabled ? "<font color='#00FF00'>ON</font>" : "<font color='#FF4040'>OFF</font>";
            if (_mission != null) _mission.Text = "Selected mission: " + _resolver.SelectedMission;
            if (_keys != null) _keys.Text = "Detected keys: " + _resolver.KeySummary;
            if (_diagnostics != null) _diagnostics.Text = $"State: {_resolver.Status}\nCandidates: {_resolver.CandidateCount}\nCurrent attempt: {_resolver.AttemptNumber}";
        }

        public void Dispose() { Game.OnUpdate -= OnUpdate; }
    }
}
