using System;
using System.Runtime.InteropServices;
using AOSharp.Common.GameData;
using AOSharp.Common.Unmanaged.Imports;

namespace MalisDungeonMap2
{
    // Small portion of Mali's map drawing helper used by DungeonLayout.
    internal static class MDebug
    {
        private const float Scale = 0.005f;

        [DllImport("Randy31.dll", CallingConvention = CallingConvention.ThisCall,
            EntryPoint = "?Add2DLine@Debugger_t@@QAEXVVector3_t@@0MMM_N@Z")]
        private static extern int DrawLineNative(IntPtr debugger,
            float x1, float y1, float z1, float x2, float y2, float z2,
            float red, float green, float blue, bool unknown);

        public static void DrawLine(Vector3 first, Vector3 second, Vector3 color)
        {
            DrawLineNative(Debugger_t.GetInstance(),
                first.X * Scale, first.Z * Scale, 0f,
                second.X * Scale, second.Z * Scale, 0f,
                color.X, color.Y, color.Z, false);
        }
    }

    internal static class MathExtras
    {
        public const float Rad2Deg = 180f / (float)Math.PI;
    }
}
