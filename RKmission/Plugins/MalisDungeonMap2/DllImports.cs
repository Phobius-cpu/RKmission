using AOSharp.Common.GameData;
using AOSharp.Common.Unmanaged.Imports;
using System.Runtime.InteropServices;
using System;
using AOSharp.Core;
using AOSharp.Core.UI;
using System.Security.AccessControl;

namespace MalisDungeonMap2
{
    public class MDebug
    {
        [DllImport("Randy31.dll", CallingConvention = CallingConvention.ThisCall, EntryPoint = "?Add2DLine@Debugger_t@@QAEXVVector3_t@@0MMM_N@Z")]
        public static extern int DrawLine(IntPtr pThis, float pos1X, float pos1Y, float pos1Z, float pos2X, float pos2Y, float pos2Z, float unk1, float unk2, float unk3, bool unk4);

        public static void DrawLine(Vector3 pos1, Vector3 pos2, Vector3 color)
        {
            DrawLine(Debugger_t.GetInstance(),
                DungeonMapRenderer.MapConfig.Offset.X + pos1.X * DungeonMapRenderer.MapConfig.Scale,
                DungeonMapRenderer.MapConfig.Offset.Y + pos1.Z * DungeonMapRenderer.MapConfig.Scale,
                0,
                DungeonMapRenderer.MapConfig.Offset.X + pos2.X * DungeonMapRenderer.MapConfig.Scale,
                DungeonMapRenderer.MapConfig.Offset.Y + pos2.Z * DungeonMapRenderer.MapConfig.Scale,
                0, color.X, color.Y, color.Z, false);
        }

        public static void DrawLineNoScale(Vector3 pos1, Vector3 pos2, Vector3 color)
        {
            DrawLine(Debugger_t.GetInstance(),
                DungeonMapRenderer.MapConfig.Offset.X + pos1.X,
                DungeonMapRenderer.MapConfig.Offset.Y + pos1.Z,
                0,
                DungeonMapRenderer.MapConfig.Offset.X + pos2.X,
                DungeonMapRenderer.MapConfig.Offset.Y + pos2.Z,
                0,
                color.X, color.Y, color.Z, false);
        }

        public static void DrawLineWithOutline(Vector3 pos1, Vector3 pos2, Vector3 color, Vector3 outlineColor, float outlineWidth)
        {
            var offset = pos2 - pos1;

            if (offset != Vector3.Zero)
                DrawLineWithOutline(pos1, pos2, offset.Normalize(), color, color, outlineWidth);
        }

        private static void DrawLineWithOutline(Vector3 pos1, Vector3 pos2, Vector3 forwardVector, Vector3 color, Vector3 outlineColor, float outlineWidth)
        {
            DrawLine(pos1, pos2, color);

            Vector3 leftVector = new Vector3(-forwardVector.Z, 0, forwardVector.X) * outlineWidth;
            Vector3 rightVector = new Vector3(forwardVector.Z, 0, -forwardVector.X) * outlineWidth;

            var pos1Scaled = pos1 * DungeonMapRenderer.MapConfig.Scale;
            var pos2Scaled = pos2 * DungeonMapRenderer.MapConfig.Scale;

            DrawLineNoScale(pos1Scaled + leftVector, pos2Scaled + leftVector, outlineColor);
            DrawLineNoScale(pos1Scaled + rightVector, pos2Scaled + rightVector, outlineColor);
        }

        public static void DrawArrowhead(Vector3 wsShapePos, float shapeDirInRad, float shapeSize, Vector3 color)
        {
            Vector3[] pos = CalculateArrowheadPos(wsShapePos, shapeDirInRad, shapeSize);
            DrawArrowhead(pos, color);
        }

        public static void DrawArrowheadWithOutline(Vector3 wsShapePos, float shapeDirInRad, float shapeSize, Vector3 color,Vector3 outlineColor, float lineWidth)
        {
            Vector3[] pos = CalculateArrowheadPos(wsShapePos, shapeDirInRad, shapeSize);
            DrawArrowheadWithOutline(pos, color, color,lineWidth);;
        }

        public static void DrawSquare(Vector3 pos, float width, float height, float rotDegree, Vector3 color)
        {
            var squarePos = CalculateSquarePos(pos, width, height, rotDegree);
            DrawSquare(squarePos, color);
        }

        public static void DrawSquareWithOutline(Vector3 wsShapePos, float width, float height, float rotDegree, Vector3 color, float lineWidth)
        {
            Vector3[] pos = CalculateSquarePos(wsShapePos, width, height, rotDegree);
            DrawSquareWithOutline(pos, color, color,lineWidth);
        }

        private static void DrawSquare(Vector3[] positions,Vector3 color)
        {
            DrawLine(positions[0], positions[1], color);
            DrawLine(positions[1], positions[2], color);
            DrawLine(positions[2], positions[3], color);
            DrawLine(positions[3], positions[0], color);
        }

        private static void DrawSquareWithOutline(Vector3[] positions, Vector3 color, Vector3 outlineColor, float lineWidth)
        {
            DrawLineWithOutline(positions[0], positions[1], (positions[1] - positions[0]).Normalize(), color, outlineColor, lineWidth);
            DrawLineWithOutline(positions[1], positions[2], (positions[2] - positions[1]).Normalize(), color, outlineColor, lineWidth);
            DrawLineWithOutline(positions[2], positions[3], (positions[3] - positions[2]).Normalize(), color, outlineColor, lineWidth);
            DrawLineWithOutline(positions[3], positions[0], (positions[3] - positions[0]).Normalize(), color, outlineColor, lineWidth);
        }

        private static void DrawArrowhead(Vector3[] positions, Vector3 color)
        {
            DrawLine(positions[0], positions[1], color);
            DrawLine(positions[1], positions[2], color);
            DrawLine(positions[2], positions[3], color);
            DrawLine(positions[3], positions[0], color);
        }

        private static void DrawArrowheadWithOutline(Vector3[] positions, Vector3 color, Vector3 outlineColor, float lineWidth)
        {
            DrawLineWithOutline(positions[0], positions[1], (positions[1] - positions[0]).Normalize(), color, outlineColor, lineWidth);
            DrawLineWithOutline(positions[1], positions[2], (positions[2] - positions[1]).Normalize(), color, outlineColor, lineWidth);
            DrawLineWithOutline(positions[2], positions[3], (positions[3] - positions[2]).Normalize(), color, outlineColor, lineWidth);
            DrawLineWithOutline(positions[3], positions[0], (positions[3] - positions[0]).Normalize(), color, outlineColor, lineWidth);
        }

        private static Vector3[] CalculateArrowheadPos(Vector3 center, float yawRad, float size)
        {
            var yaw = Quaternion.AngleAxis(yawRad * MathExtras.Rad2Deg, Vector3.Up);
            var sizeMultiplier = Vector3.Right * size;

            return new Vector3[4]
            {
                center + yaw * MathExtras.RotateAroundAxis(sizeMultiplier, Vector3.Up, 150f),
                center + yaw * MathExtras.RotateAroundAxis(sizeMultiplier, Vector3.Up, -90f),
                center + yaw * MathExtras.RotateAroundAxis(sizeMultiplier, Vector3.Up, 30f),
                center + yaw * MathExtras.RotateAroundAxis(sizeMultiplier, Vector3.Up, 90f)
            };
        }

        private static Vector3[] CalculateSquarePos(Vector3 pos, float width, float height, float rotDegree)
        {
            Vector3 pivot = new Vector3(pos.X, pos.Y, pos.Z);
            return new Vector3[4]
            {
                Vector3.Rotate(pivot, new Vector3(pos.X - width, pos.Y, pos.Z - height), rotDegree),
                Vector3.Rotate(pivot, new Vector3(pos.X + width, pos.Y, pos.Z - height), rotDegree),
                Vector3.Rotate(pivot, new Vector3(pos.X + width, pos.Y, pos.Z + height), rotDegree),
                Vector3.Rotate(pivot, new Vector3(pos.X - width, pos.Y, pos.Z + height), rotDegree)
            };
        }
    }

    public class MathExtras
    {
        public const float Rad2Deg = 360f / ((float)Math.PI * 2f);
        public const float Deg2Rad = (float)Math.PI / 180f;

        public static Vector3 RotateAroundAxis(Vector3 point, Vector3 axis, float angle)
        {
            Quaternion rotation = Quaternion.AngleAxis(angle, axis.Normalize());
            return rotation * point;
        }
    }
}