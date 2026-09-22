using AOSharp.Core.UI;
using BehaviourTree;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AOBotBase.BTViewer
{
    public class BTNodesView<TContext>
    {
        public BTStatusInfo<TContext> StatusInfo;
        public View Root;
        public List<BTNodeView<TContext>> Nodes;
        private string _path;
        private string _boldViewPath;
        private string _smallViewPath;

        public BTNodesView(string rootDir, string smallFileName, string boldFileName, View root, IBehaviour<TContext> behaviour)
        {
            StatusInfo = new BTStatusInfo<TContext>(behaviour);
            Nodes = new List<BTNodeView<TContext>>();
            Root = root;
            _path = rootDir;
            _smallViewPath = smallFileName;
            _boldViewPath = boldFileName;
        }

        public void AddNode(string nodeName, NodeTextType nodeTextType, IBehaviour<TContext> node)
        {
            string viewPath = nodeTextType == NodeTextType.Bold ? _boldViewPath : _smallViewPath;

            View btNodeView = View.CreateFromXml(_path + $"\\" + viewPath);

            if (btNodeView.FindChild("BtText", out TextView btTextView))
                btTextView.Text = nodeName;

            Nodes.Add(new BTNodeView<TContext> { Node = node, Root = btNodeView, TextView = btTextView });
            Root.AddChild(btNodeView, false);
            Root.FitToContents();
        }

        public void AddNodesToRoot()
        {
            foreach (BTNodeView<TContext> node in Nodes)
                Root.AddChild(node.Root, true);

            Root.FitToContents();
        }

        public void RemoveNodesFromRoot()
        {
            foreach (BTNodeView<TContext> node in Nodes)
                Root.RemoveChild(node.Root);

            Root.FitToContents();
            Nodes.Clear();
        }

        public class BTNodeView<TContext>
        {
            public View Root;
            public TextView TextView;
            public IBehaviour<TContext> Node;
        }
    }

    public enum NodeTextType
    {
        Bold,
        Small
    }
}
