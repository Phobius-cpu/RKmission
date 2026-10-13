using System;
using System.Collections.Generic;
using System.Linq;
using AOSharp.Common.GameData;
using AOSharp.Common.GameData.UI;
using AOSharp.Core;
using AOSharp.Core.UI;

namespace RKmission
{
    internal sealed class NekoPanel : IDisposable
    {
        private readonly MissionEntranceResolver _resolver;
        private readonly Action _warp;
        private readonly Func<IEnumerable<AcceptedMission>> _missions;
        private readonly Action<int> _selectMission;
        private readonly string _entryPath;
        private Button _toggle;
        private TextView _mission, _keys, _diagnostics;
        private View _missionList;
        private readonly List<View> _missionEntries = new List<View>();
        private string _missionSignature;
        private int _selectedMissionId;
        private string _selectedMissionName;
        private int _selectedPlayfield;
        private DateTime _nextRefresh;
        public View Root { get; }

        public NekoPanel(string pluginDir, MissionEntranceResolver resolver, Action warp,
            Func<IEnumerable<AcceptedMission>> missions, Action<int> selectMission)
        {
            _resolver = resolver; _warp = warp; _missions = missions; _selectMission = selectMission;
            _entryPath = System.IO.Path.Combine(pluginDir, "Plugins", "MaliMissionRoller2", "UI", "Views", "NekoMissionEntry.xml");
            Root = View.CreateFromXml(System.IO.Path.Combine(pluginDir,
                "Plugins", "MaliMissionRoller2", "UI", "Views", "NekoView.xml"));
            Root.FindChild("NekoMission", out _mission);
            Root.FindChild("NekoKeys", out _keys);
            Root.FindChild("NekoDiagnostics", out _diagnostics);
            Root.FindChild("NekoMissionList", out _missionList);
            if (Root.FindChild("NekoWarp", out Button warpButton)) warpButton.Clicked = (_, _) => _warp();
            if (Root.FindChild("NekoToggle", out _toggle)) _toggle.Clicked = (_, _) =>
            { _resolver.ToggleEnabled(); Refresh(); };
            Game.OnUpdate += OnUpdate;
            Refresh();
        }

        private void OnUpdate(object sender, float elapsed)
        { if (DateTime.UtcNow >= _nextRefresh) { _nextRefresh = DateTime.UtcNow.AddMilliseconds(500); Refresh(); } }

        private void Refresh()
        {
            RefreshMissions();
            _toggle?.SetLabel(_resolver.Enabled
                ? "Key: <font color='#00FF00'>ON</font>"
                : "Key: <font color='#FF4040'>OFF</font>");
            if (_mission != null) _mission.Text = _selectedMissionId == 0
                ? "Selected mission: none"
                : $"Selected mission: {_selectedMissionName} [{_selectedMissionId}] - PF {_selectedPlayfield}";
            if (_keys != null) _keys.Text = "Detected keys: " + _resolver.KeySummary;
            if (_diagnostics != null) _diagnostics.Text = $"State: {_resolver.Status}\nCandidates: {_resolver.CandidateCount}\nCurrent attempt: {_resolver.AttemptNumber}";
        }

        private void RefreshMissions()
        {
            if (_missionList == null || _missions == null) return;
            List<AcceptedMission> missions = _missions().Where(x => x.Present && x.IsRubiKaDestination && !x.Completed)
                .OrderBy(x => x.Id.Instance).Take(3).ToList();
            string signature = string.Join("|", missions.Select(x => $"{x.Id.Instance}:{x.Name}:{x.PlayfieldId}:{x.Kind}"));
            if (signature == _missionSignature) return;
            foreach (View entry in _missionEntries) _missionList.RemoveChild(entry);
            _missionEntries.Clear();
            foreach (AcceptedMission mission in missions)
            {
                View entry = View.CreateFromXml(_entryPath);
                entry.FindChild("MissionIcon", out BitmapView icon);
                entry.FindChild("MissionLabel", out TextView label);
                entry.FindChild("MissionSelect", out Button select);
                icon?.SetBitmap(MissionIcon(mission.Kind).ToString());
                string name = string.IsNullOrWhiteSpace(mission.Name) ? $"Mission {mission.Id.Instance}" : mission.Name;
                if (name.Length > 16) name = name.Substring(0, 13) + "...";
                if (label != null) label.Text = $"{name}\nPF {mission.PlayfieldId}";
                if (select != null)
                {
                    select.Tag = mission.Id.Instance;
                    select.SetAlpha(mission.Id.Instance == _selectedMissionId ? 1f : 0.72f);
                    select.Clicked = (_, e) =>
                    {
                        if (!(e.Tag is int id)) return;
                        AcceptedMission chosen = missions.FirstOrDefault(x => x.Id.Instance == id);
                        if (chosen == null) return;
                        _selectedMissionId = chosen.Id.Instance;
                        _selectedMissionName = chosen.Name;
                        _selectedPlayfield = chosen.PlayfieldId;
                        _selectMission(id);
                        _missionSignature = null;
                        Refresh();
                    };
                }
                _missionList.AddChild(entry, false);
                _missionEntries.Add(entry);
            }
            _missionSignature = signature;
            _missionList.FitToContents(); Root.FitToContents();
        }

        private static int MissionIcon(RkMissionKind kind)
        {
            switch (kind)
            {
                case RkMissionKind.ReturnItem: return 11329;
                case RkMissionKind.KillPerson: return 11330;
                case RkMissionKind.FindPerson: return 11335;
                case RkMissionKind.FindItem: return 11337;
                case RkMissionKind.Repair: return 11342;
                default: return 11335;
            }
        }

        public void Dispose()
        {
            Game.OnUpdate -= OnUpdate;
            if (_missionList != null) foreach (View entry in _missionEntries) _missionList.RemoveChild(entry);
            _missionEntries.Clear();
        }
    }
}
