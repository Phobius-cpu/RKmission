using System;
using AOSharp.Common.Unmanaged.DataTypes;
using AOSharp.Common.Unmanaged.Imports;
using AOSharp.Core.UI;

namespace ManagerLoot
{
    public static class Extensions
    {
        public static void SetText(this ComboBox comboBox, string text)
        {
            IntPtr pTextView = TextInputView_c.GetTextView(comboBox.Pointer);

            if (pTextView == IntPtr.Zero)
                return;

            TextView_c.SetText(pTextView, StdString.Create(text).Pointer);
        }

        public static string GetText(this ComboBox comboBox)
        {
            IntPtr pTextView = TextInputView_c.GetTextView(comboBox.Pointer);

            if (pTextView == IntPtr.Zero)
                return string.Empty;

            Variant var = Variant.Create();
            IntPtr pStr = TextView_c.GetValue(pTextView, var.Pointer);

            if (pStr == IntPtr.Zero)
                return string.Empty;

            return var.AsString();
        }
    }
}
