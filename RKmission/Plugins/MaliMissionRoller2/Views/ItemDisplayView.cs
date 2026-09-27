using AOSharp.Common.GameData;
using AOSharp.Core;
using AOSharp.Core.UI;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MaliMissionRoller2
{
    public class ItemDisplayView
    {
        private View _root;
        private View _scrollListRoot;
        private View _dbRoot;
        private BitmapView _background;
        private List<BrowserEntryView> _browserEntryViews;
        internal List<RollEntryView> RollEntryViews;
        private TextView _searchBarNameInput;
        private TextView _searchBarModsInput;
        private Button BrowserMode;
        private Button RollMode;
        public Button Implants;
        public Button Refined;
        public Button Clusters;
        public Button Nanos;
        public Button Rest;
        private string _cachedSearchText;
        private int _cachedDisplayItems;
        private bool _inRollMode;
        private readonly Dictionary<Stat, string[]> ModTags = JsonConvert.DeserializeObject<Dictionary<Stat, string[]>>(File.ReadAllText($"{Main.PluginDir}\\JSON\\ModTags.json"));
        private readonly string defaultText;

        public ItemDisplayView(View root)
        {
            View _view = View.CreateFromXml($"{Main.PluginDir}\\UI\\Views\\ItemDisplayView.xml");
            _root = root;
            _browserEntryViews = new List<BrowserEntryView>();
            RollEntryViews = new List<RollEntryView>();

            _view.FindChild("ScrollListRoot", out _scrollListRoot);
            _view.FindChild("Background", out _background);
            _background.SetBitmap("SearchWindowBg2");
            _view.FindChild("DbRoot", out _dbRoot);
            _dbRoot.LimitMaxSize(new Vector2(0, 15));
            _dbRoot.SetAlpha(0);
            _view.FindChild("Implants", out Implants);
            Implants.Tag = Main.Settings.Database["Implants"];
            DbUpdate(Implants);
            _view.FindChild("Refined", out Refined);
            Refined.Tag = Main.Settings.Database["Refined"];
            DbUpdate(Refined);
            _view.FindChild("Clusters", out Clusters);
            Clusters.Tag = Main.Settings.Database["Clusters"];
            DbUpdate(Clusters);
            _view.FindChild("Nanos", out Nanos);
            Nanos.Tag = Main.Settings.Database["Nanos"];
            DbUpdate(Nanos);
            _view.FindChild("Rest", out Rest);
            Rest.Tag = Main.Settings.Database["Rest"];
            DbUpdate(Rest);
            _view.FindChild("SearchBarMods", out _searchBarModsInput);
            if (Main.Settings.Extras["StartHelp"])
                _searchBarModsInput.Text = "Type item mods here.. str,stam";
            else
                _searchBarModsInput.Text = "";
            _searchBarModsInput.SetAlpha(0);
            _view.FindChild("SearchBarName", out _searchBarNameInput);
            if (Main.Settings.Extras["StartHelp"])
                _searchBarNameInput.Text = "Type item name here.. Nano Beh";
            else
                _searchBarNameInput.Text = "";
            defaultText =  _searchBarNameInput.Text + _searchBarModsInput.Text;
            _searchBarNameInput.SetAlpha(0);
             _view.FindChild("BrowserMode", out BrowserMode);
            _view.FindChild("RollMode", out RollMode);
            Extensions.ButtonSetGfx(RollMode, 1000036);
            RollMode.Clicked = RollModeClick;
            Extensions.ButtonSetGfx(BrowserMode, 1000046);
            BrowserMode.Clicked = BrowserModeClick;
            _inRollMode = true;
            //pregenerated views for browser entries
            FormatBrowserEntries();
            //loading saved rolllist

            List<RollEntryViewModel> rollList = new List<RollEntryViewModel>();

            if (File.Exists($"{Main.PluginDir}\\JSON\\RollList.json"))
                rollList = JsonConvert.DeserializeObject<List<RollEntryViewModel>>(File.ReadAllText($"{Main.PluginDir}\\JSON\\RollList.json"));

            if (rollList != null)
            foreach (RollEntryViewModel rollEntry in rollList)
                FormatRollEntry(rollEntry);

            foreach (var item in RollEntryViews)
                _scrollListRoot.AddChild(item.Root, false);

            _root.AddChild(_view, false);
        }

        internal void FormatBrowserEntries()
        {
            DeleteBrowserEntries();

            _browserEntryViews.Clear();

            for (int i = 0; i < Main.Settings.Dev["MaxItems"]; i++)
            {
                BrowserEntryView itemView = new BrowserEntryView();

                itemView.Root = View.CreateFromXml($"{Main.PluginDir}\\UI\\Views\\BrowserEntryView.xml");
                itemView.Root.FindChild("Name", out itemView.Name);
                itemView.Root.FindChild("Range", out itemView.Range);
                itemView.Root.FindChild("Ql", out itemView.Ql);
                itemView.Root.FindChild("Bitmap", out itemView.Bitmap);
                itemView.Bitmap.SetBitmap("ItemPreviewBg");
                itemView.Root.FindChild("Button", out itemView.Button);
                Extensions.ButtonSetGfx(itemView.Button, 1000037);
                itemView.Root.FindChild("Preview", out View preview);
                itemView.MultiListView = ItemListViewBase.Create(new Rect(20, 20, 20, 20), 0, 0);
                itemView.MultiListView.SetGridIconSpacing(new Vector2(6000, 6000));
                itemView.MultiListView.SetGridIconSize(3);
                itemView.MultiListView.SetLayoutMode(0);
                itemView.MultiListView.SetViewCellCounts(IPoint.Zero, IPoint.Zero);
                preview.AddChild(itemView.MultiListView, false);
                _browserEntryViews.Add(itemView);
            }
        }
        private void FormatRollEntry(RollEntryViewModel rollEntryModel,bool newEntry = false)
        {
            RollEntryView rollEntry = new RollEntryView();
            rollEntry.RollEntryModel = rollEntryModel;

            if (newEntry)
                rollEntryModel.Count = 1;

            rollEntry.Root = View.CreateFromXml($"{Main.PluginDir}\\UI\\Views\\RollEntryView.xml");
            rollEntry.Root.FindChild("Name", out rollEntry.Name);
            rollEntry.Root.FindChild("Bitmap", out rollEntry.Bitmap);
            rollEntry.Root.FindChild("Range", out rollEntry.Range);
            rollEntry.Root.FindChild("Count", out rollEntry.Count);
            rollEntry.Count.Text = $"{rollEntryModel.Count.ToString().PadLeft(2, '0')}";
            rollEntry.Root.FindChild("Plus", out rollEntry.Plus);
            Extensions.ButtonSetGfx(rollEntry.Plus, 1000037);
            rollEntry.Root.FindChild("Minus", out rollEntry.Minus);
            Extensions.ButtonSetGfx(rollEntry.Minus, 1000047);
            rollEntry.Root.FindChild("Preview", out View preview);
            rollEntry.Name.Text = rollEntryModel.Name;
            rollEntry.Bitmap.SetBitmap("ItemPreviewBg2");
            rollEntry.Range.Text = $"{rollEntryModel.Ql.ToString().PadLeft(3, '0')}";
            rollEntry.Plus.Tag = rollEntry;
            rollEntry.Plus.Clicked = RollEntryPlusClick;
            rollEntry.Minus.Tag = rollEntry;
            rollEntry.Minus.Clicked = RollEntryMinusClick;
            rollEntry.MultiListView = ItemListViewBase.Create(new Rect(20, 20, 20, 20), 0, 0);
            rollEntry.MultiListView.SetGridIconSpacing(new Vector2(6000, 6000));
            rollEntry.MultiListView.SetGridIconSize(3);
            rollEntry.MultiListView.SetLayoutMode(0);
            rollEntry.MultiListView.SetViewCellCounts(IPoint.Zero, IPoint.Zero);

            if (DummyItem.CreateDummyItemID(rollEntryModel.LowId, rollEntryModel.HighId, rollEntryModel.Ql, out Identity dummyItemId))
            {
                if (rollEntry.MultiListViewItem != null)
                    rollEntry.MultiListView.RemoveItem(rollEntry.MultiListViewItem);

                rollEntry.MultiListViewItem = InventoryListViewItem.Create(0, dummyItemId, true);
                rollEntry.MultiListView.AddItem(rollEntry.MultiListView.GetFirstFreePos(), rollEntry.MultiListViewItem, true);
            }

            preview.SetLocalAlpha(0);
            preview.AddChild(rollEntry.MultiListView, false);

            RollEntryViews.Add(rollEntry);
            RollEntryViews = RollEntryViews.OrderBy(x => x.RollEntryModel.Ql).ToList();
        }

        private void RollEntryPlusClick(object sender, ButtonBase e)
        {
            Midi.Play("Click");
            RollEntryView rollEntryView = (RollEntryView)e.Tag;
            rollEntryView.RollEntryModel.Count += 1;
            rollEntryView.Count.Text = $"{rollEntryView.RollEntryModel.Count.ToString().PadLeft(2, '0')}";
            Main.Settings.Save();
        }

        private void RollEntryMinusClick(object sender, ButtonBase e)
        {
            Midi.Play("Click");
            RollEntryView rollEntryView = (RollEntryView)e.Tag;
            UpdateRollEntry(rollEntryView);
            Main.Settings.Save();
        }

        internal void UpdateRollEntry(RollEntryView rollEntryView, bool updateView = true)
        {
            if (!updateView)
                return;

            rollEntryView.RollEntryModel.Count -= 1;

            if (rollEntryView.RollEntryModel.Count == 0)
            {
                RollEntryViews.Remove(rollEntryView);
                if (_inRollMode)
                    _scrollListRoot.RemoveChild(rollEntryView.Root);
                //  _cachedDisplayItems -= 1;
            }
            else
            {
                rollEntryView.Count.Text = $"{rollEntryView.RollEntryModel.Count.ToString().PadLeft(2, '0')}";
            }
            _scrollListRoot.FitToContents();
        }

        public void Update()
        {
            if (_searchBarNameInput.Text.Length < 3 && _searchBarModsInput.Text.Length < 3)
            {
                if (_cachedSearchText != "")
                {
                    DeleteBrowserEntries();
                    _cachedSearchText = _searchBarNameInput.Text + _searchBarModsInput.Text;
                }

                return;
            }

            if (_cachedSearchText == _searchBarNameInput.Text + _searchBarModsInput.Text)
                return;

            SearchQuery();
        }

        private unsafe void SearchQuery()
        {
            DeleteBrowserEntries();

            List<string> itemName = _searchBarNameInput.Text
                .ToLower()
                .Split(' ')
                .ToList();

            List<List<string>> searchTagsList = _searchBarModsInput.Text
                    .ToLower()
                    .Split(',')
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Split(' ').Where(y => !string.IsNullOrWhiteSpace(y)).ToList())
                    .ToList();

            int itemIndex = 0;

            foreach (var itemDb in Main.ItemDb)
            {
                int counter = 0;
                int itemId = 0;
                //item name query
                foreach (string searchText in itemName)
                {
                    foreach (string keywords in itemDb.Key.Tags)
                    {
                        if (keywords.Contains(searchText))
                        {
                            counter++;
                            break;
                        }
                    }

                    if (itemName.Count == 1)
                    {
                        if (int.TryParse(itemName[0], out int id))
                        {
                            if (id == itemDb.Key.LowId || id == itemDb.Key.HighId)
                                itemId = id;
                        }
                    }
                }

                if (itemName.Count() != counter && itemId == 0)
                    continue;

                //item mods query
                if (searchTagsList != null && searchTagsList.Count > 0)
                {
                    if (itemDb.Value.Count == 0)
                        continue;

                    List<Stat> queryStats = new List<Stat>();

                    foreach (Stat stat in itemDb.Value)
                        foreach (List<string> searchList in searchTagsList)
                            if (ModTags[stat].Where(x => searchList.Any(y => x.StartsWith(y))).Count() == searchList.Count)
                                queryStats.Add(stat);

                    if (queryStats.Count < searchTagsList.Count)
                        continue;

                    if (!queryStats.Any(itemDb.Value.Contains))
                        continue;
                }

                FormatBrowserEntry(_browserEntryViews[itemIndex], itemDb);

                _scrollListRoot.AddChild(_browserEntryViews[itemIndex].Root, false);
                itemIndex++;

                if (itemIndex == Main.Settings.Dev["MaxItems"])
                    break;
            }

            _cachedDisplayItems = itemIndex;
            _cachedSearchText = _searchBarNameInput.Text + _searchBarModsInput.Text;


            if (_cachedSearchText != defaultText)
            {
                if (itemIndex == Main.Settings.Dev["MaxItems"])
                    Chat.WriteLine($"Items found: {Main.Settings.Dev["MaxItems"]}+ (listing first {Main.Settings.Dev["MaxItems"]})");
                else
                    Chat.WriteLine($"Items found: {itemIndex}");
            }
            _scrollListRoot.FitToContents();
        }

        //browser mode stuff
        private void FormatBrowserEntry(BrowserEntryView browserEntry, KeyValuePair<ItemInfo, List<Stat>> itemDb)
        {
            browserEntry.Name.Text = itemDb.Key.Name;
            browserEntry.Range.Text = $"{itemDb.Key.LowQl.ToString().PadLeft(3, '0')} \n{itemDb.Key.HighQl.ToString().PadLeft(3, '0')}";
            browserEntry.Button.Tag = new BrowserEntryViewModel { ItemDb = itemDb, Ql = browserEntry.Ql };
            browserEntry.Button.Clicked = BrowserEntryClick;
            browserEntry.Ql.Text = itemDb.Key.LowQl == itemDb.Key.HighQl ? itemDb.Key.LowQl.ToString() : "";

            if (DummyItem.CreateDummyItemID(itemDb.Key.LowId, itemDb.Key.HighId, itemDb.Key.HighQl, out Identity dummyItemId))
            {
                if (browserEntry.MultiListViewItem != null)
                    browserEntry.MultiListView.RemoveItem(browserEntry.MultiListViewItem);

                browserEntry.MultiListViewItem = InventoryListViewItem.Create(0, dummyItemId, true);
                browserEntry.MultiListView.AddItem(browserEntry.MultiListView.GetFirstFreePos(), browserEntry.MultiListViewItem, true);
            }
        }

        private void BrowserEntryClick(object sender, ButtonBase e)
        {
            Midi.Play("Click");
            BrowserEntryViewModel browserEntryModel = (BrowserEntryViewModel)e.Tag;
            KeyValuePair<ItemInfo, List<Stat>> itemDb = browserEntryModel.ItemDb;
            string textInput = browserEntryModel.Ql.Text == "" ? "0" : browserEntryModel.Ql.Text;

            int ql;

            if (itemDb.Key.LowId == 297315)
            {
                if (!int.TryParse(textInput, out ql))
                {
                    Chat.WriteLine($"Please provide a reward value.", ChatColor.Red);
                    return;
                }
                else
                    Chat.WriteLine($"New Roll List Entry:\n Rolling Missions with Credits Value >= {ql}");
            }
            else if (itemDb.Key.LowQl == itemDb.Key.HighQl)
            {
                ql = itemDb.Key.LowQl;
            }
            else if (!int.TryParse(textInput, out ql) || ql < itemDb.Key.LowQl || ql > itemDb.Key.HighQl)
            {
                Chat.WriteLine($"Please provide a item QL next to its name in the browser.", ChatColor.Red);
                return;
            }

            if (itemDb.Key.LowId != 297315)
                Chat.WriteLine($"New Roll List Entry:\n Name: {itemDb.Key.Name} QL: {ql}");

            RollEntryViewModel rollEntryModel = new RollEntryViewModel
            {
                LowId = itemDb.Key.LowId,
                HighId = itemDb.Key.HighId,
                HighQl = itemDb.Key.HighQl,
                LowQl = itemDb.Key.LowQl,
                Name = itemDb.Key.Name,
                Ql = ql,
                Count = 1
            };

            RollEntryView rollListEntry = RollEntryViews.Where(x =>
                x.RollEntryModel.HighQl == rollEntryModel.HighQl &&
                x.RollEntryModel.LowId == rollEntryModel.LowId &&
                x.RollEntryModel.LowQl == rollEntryModel.LowQl &&
                x.RollEntryModel.HighId == rollEntryModel.HighId &&
                x.RollEntryModel.Ql == ql).FirstOrDefault();

            if (rollListEntry == null)
            {
                FormatRollEntry(rollEntryModel, true);
            }
            else
            {
                rollListEntry.RollEntryModel.Count += 1;
                rollListEntry.Count.Text = $"{rollListEntry.RollEntryModel.Count.ToString().PadLeft(2, '0')}";
            }

            Main.Settings.Save();
            _scrollListRoot.FitToContents();
        }

        private void BrowserModeClick(object sender, ButtonBase e)
        {
            if (!_inRollMode)
                return;

            Midi.Play("Click");
            Extensions.ButtonSetGfx(BrowserMode, 1000036);
            Extensions.ButtonSetGfx(RollMode, 1000046);
            _searchBarNameInput.LimitMaxSize(new Vector2(225, 14));
            _searchBarNameInput.SetAlpha(1);
            _searchBarModsInput.LimitMaxSize(new Vector2(225, 14));
            _searchBarModsInput.SetAlpha(1);
            _dbRoot.SetAlpha(1);
            _dbRoot.LimitMaxSize(new Vector2(235, 15));
            _background.SetBitmap("SearchWindowBg");

            foreach (var item in RollEntryViews)
                _scrollListRoot.RemoveChild(item.Root);

            if (_cachedSearchText != "" && _cachedSearchText.Length > 2 && _cachedSearchText != defaultText)
            {
                SearchQuery();
            }

            _inRollMode = false;
            _scrollListRoot.FitToContents();
        }

        private void RollModeClick(object sender, ButtonBase e)
        {
            if (_inRollMode)
                return;

            Midi.Play("Click");
            Extensions.ButtonSetGfx(RollMode, 1000036);
            Extensions.ButtonSetGfx(BrowserMode, 1000046);
            _searchBarNameInput.SetAlpha(0);
            _searchBarNameInput.LimitMaxSize(new Vector2(0, 14));
            _searchBarModsInput.LimitMaxSize(new Vector2(0, 14));
            _searchBarModsInput.SetAlpha(0);
            _dbRoot.SetAlpha(0);
            _dbRoot.LimitMaxSize(new Vector2(0, 15));
            _background.SetBitmap("SearchWindowBg2");
            DeleteBrowserEntries();

            foreach (var item in RollEntryViews)
                _scrollListRoot.AddChild(item.Root, false);

            _inRollMode = true;
            _scrollListRoot.FitToContents();
        }

        internal void DeleteBrowserEntries()
        {
            if (_cachedDisplayItems > 0)
                for (int i = 0; i < _cachedDisplayItems; i++)
                    _scrollListRoot.RemoveChild(_browserEntryViews[i].Root);

            _cachedDisplayItems = 0;
            _scrollListRoot.FitToContents();
        }

        private void DbUpdate(ButtonBase button)
        {
            bool on = (bool)button.Tag;

            if (on)
                Extensions.ButtonSetGfx((Button)button, 1000036);
            else
                Extensions.ButtonSetGfx((Button)button, 1000046);

            button.Clicked += FormatItemDb;
        }

        private void FormatItemDb(object sender, ButtonBase e)
        {
            bool on = (bool)e.Tag;

            if (!on)
                Extensions.ButtonSetGfx((Button)e, 1000036);
            else
                Extensions.ButtonSetGfx((Button)e, 1000046);

            e.Tag = !(bool)e.Tag;

            Extensions.FormatItemDb((bool)Implants.Tag, (bool)Refined.Tag, (bool)Clusters.Tag, (bool)Nanos.Tag,(bool)Rest.Tag, true);

            _cachedSearchText = "";
        }
    }
    public class BrowserEntryView
    {
        public View Root;
        public BitmapView Bitmap;
        public Button Button;
        public MultiListView MultiListView;
        public InventoryListViewItem MultiListViewItem;
        public TextView Name;
        public TextView Range;
        public TextView Ql;
    }
    public class BrowserEntryViewModel
    {
        public KeyValuePair<ItemInfo, List<Stat>> ItemDb;
        public TextView Ql;
    }
    public class RollEntryView
    {
        public View Root;
        public BitmapView Bitmap;
        public Button Plus;
        public Button Minus;
        public TextView Count;
        public MultiListView MultiListView;
        public InventoryListViewItem MultiListViewItem;
        public TextView Name;
        public TextView Range;
        public RollEntryViewModel RollEntryModel;
    }
    public class RollEntryViewModel
    {
        public int LowId;
        public int HighId;
        public int LowQl;
        public int HighQl;
        public int Count;
        public string Name;
        public int Ql;
    }
}
