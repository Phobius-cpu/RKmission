using AOSharp.Common.GameData;
using AOSharp.Core;
using AOSharp.Core.Misc;
using AOSharp.Core.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MalisDungeonMap2
{
    // Embedded form of Mali's original settings window. It uses the same XML,
    // controls, config and update behavior; only the outer AO# Window is supplied
    // by RKMission's tab host.
    internal sealed class EmbeddedMainView : IDisposable
    {
        public View Root { get; }

        private SliderView _offsetXSliderView;
        private SliderView _offsetYSliderView;
        private SliderView _scaleSliderView;
        private SliderView _distanceSliderView;
        private SliderView _outlineOffsetSliderView;
        private Checkbox _showConfigOnStart;
        private readonly Config _config;
        private View _colorEntryRoot;
        private View _lineEntryRoot;
        private View _shapeEntryRoot;
        private TextInputView _blacklistTextView;
        private readonly AutoResetInterval _uiUpdateTick;
        private readonly Dictionary<Entry, ColorView> _colorViews = new Dictionary<Entry, ColorView>();
        private readonly Dictionary<Entry, EntryView> _lineEntryViews = new Dictionary<Entry, EntryView>();
        private readonly Dictionary<Entry, EntryView> _shapeEntryViews = new Dictionary<Entry, EntryView>();
        private readonly string _colorViewPath;
        private readonly string _entryViewPath;
        private bool _disposed;

        private static readonly List<Entry> ColorOrdering = new List<Entry>
        {
            Entry.Wall, Entry.ActiveRoom, Entry.Door, Entry.LocalPlayer, Entry.Player,
            Entry.Npc, Entry.Terminal, Entry.MissionObjective, Entry.UnopenedChest, Entry.OpenedChest
        };

        public EmbeddedMainView(string windowPath, string colorViewPath, string entryViewPath, Config config)
        {
            _config = config;
            _colorViewPath = colorViewPath;
            _entryViewPath = entryViewPath;
            _uiUpdateTick = new AutoResetInterval(100);
            Root = View.CreateFromXml(windowPath);
            Initialize();
            Game.OnUpdate += OnUpdate;
        }

        private void Initialize()
        {
            if (Root.FindChild("OffsetX", out _offsetXSliderView))
                _offsetXSliderView.Value = _config.MapConfig.Offset.X;
            if (Root.FindChild("OffsetY", out _offsetYSliderView))
                _offsetYSliderView.Value = _config.MapConfig.Offset.Y;
            if (Root.FindChild("Scale", out _scaleSliderView))
                _scaleSliderView.Value = _config.MapConfig.Scale;
            if (Root.FindChild("ViewDistance", out _distanceSliderView))
                _distanceSliderView.SetValue(_config.MapConfig.ViewDistance);
            if (Root.FindChild("OutlineOffset", out _outlineOffsetSliderView))
                _outlineOffsetSliderView.SetValue(_config.MapConfig.OutlineOffset);
            if (Root.FindChild("BlacklistView", out _blacklistTextView))
                _blacklistTextView.Text = _config.MapConfig.BlacklistIdsRaw;
            if (Root.FindChild("ShowConfigOnStart", out _showConfigOnStart))
                _showConfigOnStart.SetValue(_config.MapConfig.ShowConfigOnStartup);
            if (Root.FindChild("SaveConfigButton", out Button saveButton))
                saveButton.Clicked = OnSaveClick;

            Root.FindChild("ColorRoot", out _colorEntryRoot);
            Root.FindChild("ShapeEntryRoot", out _shapeEntryRoot);
            Root.FindChild("LineEntryRoot", out _lineEntryRoot);

            if (_colorEntryRoot != null)
            {
                foreach (Entry key in ColorOrdering.Where(key => _config.MapConfig.Colors.ContainsKey(key)))
                {
                    ColorView colorView = new ColorView(_colorViewPath, key.GetDescription());
                    colorView.ColorInputView.Text = Utils.Vector3ToHex(_config.MapConfig.Colors[key]);
                    _colorViews.Add(key, colorView);
                    _colorEntryRoot.AddChild(colorView.Root, true);
                }
            }

            if (_lineEntryRoot != null)
            {
                foreach (var lineEntry in _config.MapConfig.LineEntries)
                {
                    EntryView entryView = new EntryView(_entryViewPath, lineEntry.Key.GetDescription());
                    entryView.Show.SetValue(lineEntry.Value.Show);
                    entryView.Outline.SetValue(lineEntry.Value.Outline);
                    _lineEntryViews.Add(lineEntry.Key, entryView);
                    _lineEntryRoot.AddChild(entryView.Root, true);
                }
            }

            if (_shapeEntryRoot != null)
            {
                foreach (var shapeEntry in _config.MapConfig.ShapeEntries)
                {
                    EntryView entryView = new EntryView(_entryViewPath, shapeEntry.Key.GetDescription());
                    entryView.Show.SetValue(shapeEntry.Value.Show);
                    entryView.Outline.SetValue(shapeEntry.Value.Outline);
                    _shapeEntryViews.Add(shapeEntry.Key, entryView);
                    _shapeEntryRoot.AddChild(entryView.Root, true);
                }
            }

            Root.FitToContents();
        }

        private void OnSaveClick(object sender, ButtonBase e)
        {
            Apply();
            _config.Save();
            Chat.WriteLine("Map config saved!");
        }

        private void OnUpdate(object sender, float e)
        {
            if (_disposed || !_uiUpdateTick.Elapsed) return;
            Apply();
        }

        private void Apply()
        {
            if (_offsetXSliderView == null || _offsetYSliderView == null ||
                _scaleSliderView == null || _distanceSliderView == null ||
                _outlineOffsetSliderView == null || _showConfigOnStart == null ||
                _blacklistTextView == null)
                return;

            var offset = new Vector2(_offsetXSliderView.GetValue(), _offsetYSliderView.GetValue());
            _config.MapConfig.Offset = new Vector2((float)Math.Round(offset.X, 1), (float)Math.Round(offset.Y, 1));
            _config.MapConfig.ShowConfigOnStartup = _showConfigOnStart.IsChecked;
            _config.MapConfig.ViewDistance = _distanceSliderView.GetValue();
            _config.MapConfig.Scale = Math.Max((float)Math.Round(_scaleSliderView.GetValue(), 3), 0.001f);
            _config.MapConfig.OutlineOffset = Math.Max((float)Math.Round(_outlineOffsetSliderView.GetValue(), 3), 0.001f);
            _config.MapConfig.BlacklistIdsRaw = _blacklistTextView.Text;
            _config.MapConfig.ParseBlacklist();

            foreach (var colorView in _colorViews)
                _config.MapConfig.Colors[colorView.Key] = Utils.HexToVector3(colorView.Value.ColorInputView.Text);
            foreach (var entryView in _shapeEntryViews)
            {
                _config.MapConfig.ShapeEntries[entryView.Key].Show = entryView.Value.Show.IsChecked;
                _config.MapConfig.ShapeEntries[entryView.Key].Outline = entryView.Value.Outline.IsChecked;
            }
            foreach (var entryView in _lineEntryViews)
            {
                _config.MapConfig.LineEntries[entryView.Key].Show = entryView.Value.Show.IsChecked;
                _config.MapConfig.LineEntries[entryView.Key].Outline = entryView.Value.Outline.IsChecked;
            }

            DungeonMapRenderer.MapConfig = _config.MapConfig;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Game.OnUpdate -= OnUpdate;
            Apply();
            _config.Save();
            if (_colorEntryRoot != null)
                foreach (ColorView colorView in _colorViews.Values) _colorEntryRoot.RemoveChild(colorView.Root);
            if (_lineEntryRoot != null)
                foreach (EntryView entryView in _lineEntryViews.Values) _lineEntryRoot.RemoveChild(entryView.Root);
            if (_shapeEntryRoot != null)
                foreach (EntryView entryView in _shapeEntryViews.Values) _shapeEntryRoot.RemoveChild(entryView.Root);
        }
    }
}
