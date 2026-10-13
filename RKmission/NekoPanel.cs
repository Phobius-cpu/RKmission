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
        private Button _toggle;
        private TextView _mission, _keys, _diagnostics;
        private readonly Button[] _missionButtons = new Button[15];
        private readonly BitmapView[] _missionIcons = new BitmapView[15];
        private readonly TextView[] _missionLabels = new TextView[15];
        private string _missionSignature;
        private int _selectedMissionId;
        private string _selectedMissionName;
        private int _selectedPlayfield;
        private int _trackedMissionCount, _targetableMissionCount, _shownMissionCount;
        private DateTime _nextRefresh;
        public View Root { get; }

        public NekoPanel(string pluginDir, MissionEntranceResolver resolver, Action warp,
            Func<IEnumerable<AcceptedMission>> missions, Action<int> selectMission)
        {
            _resolver = resolver; _warp = warp; _missions = missions; _selectMission = selectMission;
            Root = View.CreateFromXml(System.IO.Path.Combine(pluginDir,
                "Plugins", "MaliMissionRoller2", "UI", "Views", "NekoView.xml"));
            Root.FindChild("NekoMission", out _mission);
            Root.FindChild("NekoKeys", out _keys);
            Root.FindChild("NekoDiagnostics", out _diagnostics);
            for (int index = 0; index < _missionButtons.Length; index++)
            {
                int slot = index + 1;
                Root.FindChild("NekoMission" + slot, out _missionButtons[index]);
                Root.FindChild("NekoMissionIcon" + slot, out _missionIcons[index]);
                Root.FindChild("NekoMissionLabel" + slot, out _missionLabels[index]);
                if (_missionButtons[index] != null) _missionButtons[index].Clicked = SelectMission;
            }
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
            if (_diagnostics != null) _diagnostics.Text =
                $"Missions: AO live {Mission.List?.Count ?? 0}, tracked {_trackedMissionCount}, targetable {_targetableMissionCount}, shown {_shownMissionCount}/15\n" +
                $"State: {_resolver.Status}\nCandidates: {_resolver.CandidateCount}\nCurrent attempt: {_resolver.AttemptNumber}";
        }

        private void RefreshMissions()
        {
            if (_missionButtons.Any(x => x == null) || _missions == null) return;
            List<AcceptedMission> tracked = _missions().Where(x => x.Present && !x.Completed).ToList();
            _trackedMissionCount = tracked.Count;
            List<AcceptedMission> targetable = tracked
                .Where(x => x.IsRubiKaDestination).ToList();
            _targetableMissionCount = targetable.Count;
            List<AcceptedMission> missions = targetable
                .OrderBy(x => x.Id.Instance).Take(15).ToList();
            _shownMissionCount = missions.Count;
            string signature = string.Join("|", missions.Select(x => $"{x.Id.Instance}:{x.Name}:{x.PlayfieldId}:{x.Kind}"));
            if (signature == _missionSignature && missions.Any(x => x.Id.Instance == _selectedMissionId)) return;
            for (int index = 0; index < _missionButtons.Length; index++)
            {
                if (index >= missions.Count)
                {
                    _missionButtons[index].Tag = null;
                    _missionButtons[index].SetAlpha(0f);
                    _missionIcons[index]?.SetAlpha(0f);
                    _missionLabels[index]?.SetAlpha(0f);
                    continue;
                }
                AcceptedMission mission = missions[index];
                _missionIcons[index]?.SetBitmap(MissionIcon(mission.Kind).ToString());
                _missionIcons[index]?.SetAlpha(1f);
                string name = string.IsNullOrWhiteSpace(mission.Name) ? $"Mission {mission.Id.Instance}" : mission.Name;
                if (name.Length > 42) name = name.Substring(0, 39) + "...";
                string description = !string.IsNullOrWhiteSpace(mission.Objectives)
                    ? mission.Objectives
                    : (!string.IsNullOrWhiteSpace(mission.QuestShortInfo) ? mission.QuestShortInfo : mission.Kind.ToString());
                if (description.Length > 52) description = description.Substring(0, 49) + "...";
                _missionLabels[index].Text = $"{name}\nPF {mission.PlayfieldId} - {description}";
                _missionLabels[index].SetAlpha(1f);
                _missionButtons[index].Tag = mission.Id.Instance;
                _missionButtons[index].SetAlpha(mission.Id.Instance == _selectedMissionId ? 1f : 0.72f);
            }
            _missionSignature = signature;
        }

        private void SelectMission(object sender, ButtonBase button)
        {
            if (!(button.Tag is int id)) return;
            AcceptedMission chosen = _missions().FirstOrDefault(x => x.Id.Instance == id && x.Present && x.IsRubiKaDestination && !x.Completed);
            if (chosen == null) return;
            _selectedMissionId = chosen.Id.Instance;
            _selectedMissionName = chosen.Name;
            _selectedPlayfield = chosen.PlayfieldId;
            _selectMission(id);
            _missionSignature = null;
            Refresh();
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
        }
    }
}
