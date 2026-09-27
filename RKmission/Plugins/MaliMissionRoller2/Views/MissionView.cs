using AOSharp.Common.GameData;
using AOSharp.Core;
using AOSharp.Core.UI;
using SmokeLounge.AOtomation.Messaging.GameData;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;
using System.Collections.Generic;

namespace MaliMissionRoller2
{
    public class MissionView
    {
        private View _root;
        private List<MissionModel> _missionViews;
        private const float _maxDistanceToTerminal = 7.480928f;
        private const float _numOfMissions = 5;
        internal double ShopValue;
        internal int[] CombinedItemValue;

        public MissionView(View root)
        {
            _missionViews = new List<MissionModel>();
            _root = root;

            for (int i = 0; i < _numOfMissions; i++)
            {
                MissionModel missionModel = new MissionModel();
                missionModel.Root = View.CreateFromXml($"{Main.PluginDir}\\UI\\Views\\MissionView.xml");
                missionModel.Root.FindChild("Background", out missionModel.Bitmap);
                missionModel.Root.FindChild("Icon", out missionModel.Icon);
                missionModel.Bitmap.SetBitmap("MissionSlotBg");
                missionModel.Root.FindChild("Ping", out missionModel.Ping);
                Extensions.ButtonSetGfx(missionModel.Ping, 1000045);
                missionModel.Ping.Clicked = PingClick;
                missionModel.Root.FindChild("Accept", out missionModel.Accept);
                Extensions.ButtonSetGfx(missionModel.Accept, 1000035);
                missionModel.Accept.Clicked = AcceptClick;
                missionModel.Root.FindChild("Title", out missionModel.Title);
                missionModel.Root.FindChild("Playfield", out missionModel.Playfield);
                missionModel.Root.FindChild("Credits", out missionModel.Credits);
                missionModel.Root.FindChild("Experience", out missionModel.Experience);
                missionModel.MultiListView = ItemListViewBase.Create(new Rect(20, 20, 20, 20), 0, 0);
                missionModel.MultiListView.SetGridIconSpacing(new Vector2(4, 4));
                missionModel.MultiListView.SetGridIconSize(3);
                missionModel.MultiListView.SetLayoutMode(0);
                missionModel.Root.FindChild("Preview", out View preview);
                preview.AddChild(missionModel.MultiListView, false);

                _missionViews.Add(missionModel);
                _root.AddChild(missionModel.Root,false);
            }

            MissionViewSetAlpha(0);
        }

        private void PingClick(object sender, ButtonBase e)
        {
            Midi.Play("Click");

            if (e.Tag == null)
                return;

            Mission.UploadToMap((Identity)e.Tag);
        }

        private void AcceptClick(object sender, ButtonBase e)
        {
            Midi.Play("Click");

            if (e.Tag == null)
                return;

            AcceptMission((Identity)e.Tag);
            MissionViewSetAlpha(0);
        }

        internal void AcceptMission(Identity identity, bool playSound = true)
        {
            if (playSound)
                Midi.Play("Alert");

            Network.Send(new CreateQuestMessage()
            {
                MissionId = identity
            });
        }

        internal void Hide()
        {
            if (_root.FindChild("MissionView", out View view))
                foreach (MissionModel model in _missionViews)
                    _root.RemoveChild(model.Root);

            _root.FitToContents();
        }

        internal void Show()
        {
            if (MainWindow.CurrentTerminal == null)
                return;

            if (_root.FindChild("MissionSlotBg", out BitmapView background))
                return;

            foreach (MissionModel model in _missionViews)
                _root.AddChild(model.Root, false);
            _root.FitToContents();
        }

        internal void Update(MissionInfo[] rollList)
        {
            CombinedItemValue = new int[rollList.Length];

            for (int i = 0; i < rollList.Length; i++)
            {
                MissionItemReward[] itemData = rollList[i].MissionItemData;

                _missionViews[i].Icon.SetBitmap(rollList[i].MissionIcon.ToString());
                _missionViews[i].Title.Text = rollList[i].Title.Length > 25 ? rollList[i].Title.Substring(0, 25).ToUpper() : rollList[i].Title.ToUpper();
                _missionViews[i].Playfield.Text = Extensions.GetZoneName(rollList[i].Playfield.Instance);
                _missionViews[i].Experience.Text = rollList[i].XpReward.ToString();
                _missionViews[i].Accept.Tag = rollList[i].MissionIdentity;
                _missionViews[i].Ping.Tag = rollList[i].MissionIdentity;

                for (int e = 0; e < itemData.Length; e++)
                {
                    if (DummyItem.CreateDummyItemID(itemData[e].LowId, itemData[e].HighId, itemData[e].Ql, out Identity dummyItemId))
                    {
                        if (itemData[e].LowId == 84160 || itemData[e].HighId == 84159) //Cloaking Device ignore
                            CombinedItemValue[i] += 0;
                        else
                            CombinedItemValue[i] += (int)(ShopValue * Extensions.GetItemValue(dummyItemId));

                        if (_missionViews[i].MultiListViewItem[e] != null)
                            _missionViews[i].MultiListView.RemoveItem(_missionViews[i].MultiListViewItem[e]);

                        _missionViews[i].MultiListViewItem[e] = InventoryListViewItem.Create(0, dummyItemId, true);
                        _missionViews[i].MultiListView.AddItem(_missionViews[i].MultiListView.GetFirstFreePos(), _missionViews[i].MultiListViewItem[e], true);
                    }
                }

                CombinedItemValue[i] += rollList[i].Credits;
                _missionViews[i].Credits.Text = $"{rollList[i].Credits} ({CombinedItemValue[i]})";

            }

            MissionViewSetAlpha(1);
        }

        public void UpdateDistance()
        {
            if (MainWindow.CurrentTerminal == null)
                return;

            Vector3 terminalPos = MainWindow.CurrentTerminal.Position;
            Vector3 playerPos = DynelManager.LocalPlayer.Position;

            if (Vector3.Distance(playerPos, terminalPos) > _maxDistanceToTerminal)
            {
                Chat.WriteLine("Current terminal set to None (outside of reach)", ChatColor.Red);
                MainWindow.CurrentTerminal = null;
                Hide();
            }
        }

        private void MissionViewSetAlpha(float num)
        {
            for (int i = 0; i < 5; i++)
            {
                _missionViews[i].Icon.SetAlpha(num);
                _missionViews[i].Title.SetAlpha(num);
                _missionViews[i].Playfield.SetAlpha(num);
                _missionViews[i].Credits.SetAlpha(num);
                _missionViews[i].Experience.SetAlpha(num);
                _missionViews[i].MultiListView.SetAlpha(num);
            }
        }
    }

    internal class MissionModel
    {
        public View Root;
        public BitmapView Bitmap;
        public BitmapView Icon;
        public TextView Title;
        public TextView Playfield;
        public TextView Credits;
        public TextView Experience;
        public Button Ping;
        public Button Accept;
        public MultiListView MultiListView;
        public InventoryListViewItem[] MultiListViewItem = new InventoryListViewItem[6];
    }
}