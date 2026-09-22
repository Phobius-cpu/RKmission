using AOSharp.Common.GameData.UI;
using AOSharp.Core.UI;
using BehaviourTree;
using Serilog.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AOBotBase.BTViewer
{
    public class BTWindow<TContext> : AOSharpWindow
    {
        private View _root;
        public TextView ActiveNode;
        public BTNodesView<TContext> BtNodes;

        private string _rootDir;
        private Logger _logger;
        IBehaviour<TContext> _behaviour;
        //private PowerBarView _progressBar;
        //private Button _startButton;
        //private bool _btActive = false;

        private const string _defaultNodeText = "- BT Viewer -";

        public BTWindow(string name, IBehaviour<TContext> behaviourTree, string windowPath, Logger logger, WindowStyle windowStyle = WindowStyle.Popup, WindowFlags flags = WindowFlags.AutoScale | WindowFlags.NoFade) : base(name, windowPath, windowStyle, flags)
        {
            _logger = logger;
            _rootDir = System.IO.Path.GetDirectoryName(windowPath);
            _behaviour = behaviourTree;
        }

        protected override void OnWindowCreating()
        {
            if (Window == null)
            {
                _logger.Error("BTWindow: Window is null, cannot create BT Viewer.");
                return;
            }

            try
            {
                Window.FindView("NodeRoot", out _root);
                BtNodes = new BTNodesView<TContext>(_rootDir, "BTNodeViewSmall.xml", "BTNodeViewBold.xml", _root, _behaviour);

                Window.FindView("ActiveNode", out ActiveNode);
                ActiveNode.Text = _defaultNodeText;

                Window.FindView("Icon", out BitmapView icon);
                icon.SetBitmap("HeaderIcon");

                Window.MoveTo(990, 300);
            }
            catch (Exception e)
            {
                _logger.Information(e.ToString());
            }
        }

        public void StatusUpdate()
        {
            int gfxId = 0;

            if (BtNodes.Nodes.Count() == 0)
                BtNodes.StatusInfo.Render(this);

            BtNodes.AddNodesToRoot();
            _root.FitToContents();

            ActiveNode.Text = _defaultNodeText;
        }

        public void Update()
        {
            BtNodes?.StatusInfo.Update(this);
        }
    }
}
