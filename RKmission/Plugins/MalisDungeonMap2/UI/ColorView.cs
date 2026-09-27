using AOSharp.Core.UI;

namespace MalisDungeonMap2
{
    public class ColorView
    {
        public View Root;
        public TextInputView ColorInputView;

        public ColorView(string viewPath, string headerText)
        {
            Root = View.CreateFromXml(viewPath);

            if (Root.FindChild("ColorInputText", out TextInputView colorInputView))
                ColorInputView = colorInputView;

            if (Root.FindChild("ColorHeaderText", out TextView headerTextView))
                headerTextView.Text = headerText;
        }
    }
}