using AOSharp.Common.GameData;
using AOSharp.Common.GameData.UI;
using AOSharp.Core;
using AOSharp.Core.Misc;
using AOSharp.Core.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MalisDungeonMap2
{
    public class MainWindow : AOSharpWindow
    {
        private SliderView _offsetXSliderView;
        private SliderView _offsetYSliderView;
        private SliderView _scaleSliderView;
        private SliderView _distanceSliderView;
        private SliderView _outlineOffsetSliderView;
        private Checkbox _showConfigOnStart;
        private TextInputView _wallColor;
        private Config _config;
        private View _colorEntryRoot;
        private View _lineEntryRoot;
        private View _shapeEntryRoot;
        private TextInputView _blacklistTextView;
        private AutoResetInterval _uiUpdateTick;

        private Dictionary<Entry, ColorView> _colorViews;
        private Dictionary<Entry, EntryView> _lineEntryViews;
        private Dictionary<Entry, EntryView> _shapeEntryViews;
        private List<Entry> _colorOrdering = new List<Entry>
        {      Entry.Wall,
               Entry.ActiveRoom,
               Entry.Door,
               Entry.LocalPlayer,
               Entry.Player,
               Entry.Npc,
               Entry.Terminal,
               Entry.MissionObjective,
               Entry.UnopenedChest,
               Entry.OpenedChest,
        };
        private string _colorViewPath;
        private string _entryViewPath;

        public MainWindow(string name, string windowPath, string colorViewPath, string entryViewPath, Config config, WindowStyle windowStyle = WindowStyle.Default, WindowFlags flags = WindowFlags.AutoScale | WindowFlags.NoFade) : base(name, windowPath, windowStyle, flags)
        {
            _config = config;
            _colorViewPath = colorViewPath;
            _entryViewPath = entryViewPath;
            _uiUpdateTick = new AutoResetInterval(100);
            _colorViews = new Dictionary<Entry, ColorView>();
            _lineEntryViews = new Dictionary<Entry, EntryView>();
            _shapeEntryViews = new Dictionary<Entry, EntryView>();
        }

        public void Activate()
        {
            if (Window == null || !Window.IsValid || !Window.IsVisible)
                Game.OnUpdate += OnUpdate;

            Show();
        }

        public void Dispose()
        {
            foreach (var colorView in _colorViews.Values)
                _colorEntryRoot.RemoveChild(colorView.Root);

            foreach (var lineEntry in _lineEntryViews.Values)
                _lineEntryRoot.RemoveChild(lineEntry.Root);

            foreach (var shapeEntry in _shapeEntryViews.Values)
                _shapeEntryRoot.RemoveChild(shapeEntry.Root);

            Window.Close();
        }

        protected override void OnWindowCreating()
        {
            try
            {
                if (Window.FindView("OffsetX", out _offsetXSliderView))
                    _offsetXSliderView.Value = _config.MapConfig.Offset.X;

                if (Window.FindView("OffsetY", out _offsetYSliderView))
                    _offsetYSliderView.Value = _config.MapConfig.Offset.Y;

                if (Window.FindView("Scale", out _scaleSliderView))
                    _scaleSliderView.Value = _config.MapConfig.Scale;

                if (Window.FindView("ViewDistance", out _distanceSliderView))
                    _distanceSliderView.SetValue(_config.MapConfig.ViewDistance);

                if (Window.FindView("OutlineOffset", out _outlineOffsetSliderView))
                    _outlineOffsetSliderView.SetValue(_config.MapConfig.OutlineOffset);

                if (Window.FindView("BlacklistView", out _blacklistTextView))
                    _blacklistTextView.Text = _config.MapConfig.BlacklistIdsRaw;

                if (Window.FindView("SaveConfigButton", out Button saveButton))
                    saveButton.Clicked = OnSaveClick;

                if (Window.FindView("ShowConfigOnStart", out _showConfigOnStart))
                    _showConfigOnStart.SetValue(_config.MapConfig.ShowConfigOnStartup);

                if (Window.FindView("ColorRoot", out   _colorEntryRoot))
                {
                }

                if (Window.FindView("ShapeEntryRoot", out _shapeEntryRoot))
                {
                }

                if (Window.FindView("LineEntryRoot", out _lineEntryRoot))
                {
                }

                var orderedColors = _colorOrdering
                    .Where(key => _config.MapConfig.Colors.ContainsKey(key))
                    .ToDictionary(key => key, key => _config.MapConfig.Colors[key]);

                foreach (var colorEntries in orderedColors)
                {
                    ColorView colorView = new ColorView(_colorViewPath, colorEntries.Key.GetDescription());
                    colorView.ColorInputView.Text = Utils.Vector3ToHex(colorEntries.Value);
                    _colorViews.Add(colorEntries.Key, colorView);
                    _colorEntryRoot.AddChild(colorView.Root, true);
                }

                foreach (var lineEntry in _config.MapConfig.LineEntries)
                {
                    EntryView entryView = new EntryView(_entryViewPath, lineEntry.Key.GetDescription());
                    entryView.Show.SetValue(lineEntry.Value.Show);
                    entryView.Outline.SetValue(lineEntry.Value.Outline);
                    _lineEntryViews.Add(lineEntry.Key, entryView);
                    _lineEntryRoot.AddChild(entryView.Root, true);
                }

                foreach (var shapeEntries in _config.MapConfig.ShapeEntries)
                {
                    EntryView entryView = new EntryView(_entryViewPath, shapeEntries.Key.GetDescription());
                    entryView.Show.SetValue(shapeEntries.Value.Show);
                    entryView.Outline.SetValue(shapeEntries.Value.Outline);
                    _shapeEntryViews.Add(shapeEntries.Key, entryView);
                    _shapeEntryRoot.AddChild(entryView.Root, true);
                }
            }
            catch (Exception e)
            {
                Chat.WriteLine(e);
            }
        }

        private void OnSaveClick(object sender, ButtonBase e)
        {
            Chat.WriteLine("Map config saved!");
            _config.Save();
        }

        private void OnUpdate(object sender, float e)
        {
            if (!_uiUpdateTick.Elapsed)
                return;

            if (Window == null || !Window.IsValid || !Window.IsVisible)
            {
                Game.OnUpdate -= OnUpdate;
                _config.Save();
                Window = null;
                return;
            }

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
    }
}