using AOSharp.Core.UI;

namespace MalisDungeonMap2
{
    public class EntryView
    {
        public View Root;
        public Checkbox Show;
        public Checkbox Outline;

        public EntryView(string viewPath, string headerText)
        {
            Root = View.CreateFromXml(viewPath);

            if (Root.FindChild("HeaderText", out TextView headerTextView))
                headerTextView.Text = headerText;

            if (Root.FindChild("Show", out Checkbox show))
                Show = show;

            if (Root.FindChild("Outline", out Checkbox outline))
                Outline = outline;
        }
    }
}