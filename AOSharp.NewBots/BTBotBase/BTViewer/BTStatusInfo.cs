using AOSharp.Core.Misc;
using AOSharp.Core.UI;
using BehaviourTree;
using BehaviourTree.Composites;
using BehaviourTree.Decorators;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AOBotBase.BTViewer
{
    public sealed class BTStatusInfo<TContext>
    {
        private readonly IBehaviour<TContext> _behaviour;
        private AutoResetInterval _internalTick;
        public BTStatusInfo(IBehaviour<TContext> behaviour)
        {
            _behaviour = behaviour;
            _internalTick = new AutoResetInterval(200);
        }

        private void RenderBehaviourTree(BTWindow<TContext> _btWindow, int depth, IBehaviour<TContext> behaviour)
        {
            if (behaviour is CompositeBehaviour<TContext>)
                RenderBehaviourTree(_btWindow, depth, (CompositeBehaviour<TContext>)behaviour);
            else if (behaviour is DecoratorBehaviour<TContext>)
                RenderBehaviourTree(_btWindow, depth, (DecoratorBehaviour<TContext>)behaviour);
            else if (behaviour is BaseBehaviour<TContext>)
                RenderBehaviourTree(_btWindow, depth, (BaseBehaviour<TContext>)behaviour);
            //else
            //    context.Logger.Information(behaviour.GetType());
            //RenderBehaviourTree(_btWindow, depth, (dynamic)behaviour);
        }

        private void RenderBehaviourTree(BTWindow<TContext> _btWindow, int depth, CompositeBehaviour<TContext> obj)
        {
            RenderInternal(_btWindow, depth, obj);

            var childDepth = depth + 1;

            foreach (var child in obj.Children)
                RenderBehaviourTree(_btWindow, childDepth, child);
        }

        private void RenderBehaviourTree(BTWindow<TContext> _btWindow, int depth, DecoratorBehaviour<TContext> obj)
        {
            RenderInternal(_btWindow, depth, obj);
            RenderBehaviourTree(_btWindow, ++depth, obj.Child);
        }

        private void RenderBehaviourTree(BTWindow<TContext> _btWindow, int depth, BaseBehaviour<TContext> obj)
        {
            RenderInternal(_btWindow, depth, obj);
        }

        private void RenderInternal(BTWindow<TContext> _btWindow, int depth, IBehaviour<TContext> node)
        {
            var nodeNameIndented = $"{GetIndentation(depth)}{GetName(node)}";

            // NodeTextType nodeTexType = depth == 0 || depth == 1 ? NodeTextType.Bold : NodeTextType.Small;
            _btWindow.BtNodes.AddNode(nodeNameIndented, NodeTextType.Small, node);
        }

        public void Update(BTWindow<TContext> _btWindow)
        {
            if (!_internalTick.Elapsed)
                return;

            string activeNodeText = "";

            foreach (var node in _btWindow.BtNodes.Nodes)
            {
                if (node.Node.Status != BehaviourStatus.Ready)
                    activeNodeText = node.TextView.Text;

                node.TextView.SetColor(GetColor(node.Node.Status));
            }

            _btWindow.ActiveNode.Text = activeNodeText.Trim();
        }

        private static uint GetColor(BehaviourStatus status)
        {
            switch (status)
            {
                case BehaviourStatus.Ready: return Color.Ready;
                case BehaviourStatus.Running: return Color.Running;
                case BehaviourStatus.Succeeded: return Color.Success;
                case BehaviourStatus.Failed: return Color.Failure;
                default: throw new ArgumentOutOfRangeException(nameof(status), status, null);
            }
        }

        private static string GetName(IBehaviour<TContext> obj)
        {
            if (!string.IsNullOrWhiteSpace(obj.Name))
                return obj.Name;

            var type = obj.GetType();

            return type.Name;
        }

        public void Render(BTWindow<TContext> _btWindow) => RenderBehaviourTree(_btWindow, 0, _behaviour);

        private static string GetIndentation(int depth) => string.Join(string.Empty, Enumerable.Repeat("   ", depth));

        private static class Color
        {
            public static readonly uint Ready = 0x005ca7ae; // blueish
            public static readonly uint Running = 0x00FFF1AC; // yellow
            public static readonly uint Success = 0x0000ff6c; // green
            public static readonly uint Failure = 0x00ff0048; // red
        }
    }
}
