using AOSharp.Common.GameData;
using AOSharp.Core;
using AOSharp.Core.Inventory;
using AOSharp.Core.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MalisDungeonMap2
{
    public class DungeonMapRenderer
    {
        public static MapConfig MapConfig;
        public static float StraightLineWidth => 0.0015f * MapConfig.OutlineOffset;
        public static float SlantedLineWidth => 0.00115f * MapConfig.OutlineOffset;
        private DungeonData _dungeonData;
        private List<int> _visitedRooms;
        private List<int> _openedContainers;
        private const int _maxViewDistance = 1000;
        private bool _isLoading = false;
        private bool _isActive = false;

        private Vector3 _centerPos;
        private Vector3 _playerPos;
        private float _squaredDistance;

        public static DungeonMapRenderer Start()
        {
            DungeonMapRenderer mapRenderer = new DungeonMapRenderer();
            mapRenderer.InternalStart();
            return mapRenderer;
        }

        public void Stop()
        {
            if (_isActive) Game.OnUpdate -= OnUpdate;
            Game.PlayfieldInit -= OnPlayfieldInit;
            _isActive = false;
        }

        private void InternalStart()
        {
            MapConfig = new MapConfig();
            _visitedRooms = new List<int>();
            _openedContainers = new List<int>();

            if (Playfield.IsDungeon && !MapConfig.BlacklistIds.Contains((int)Playfield.ModelId))
            {
                Game.OnUpdate += OnUpdate;
                _isActive = true;
            }

            Game.PlayfieldInit += OnPlayfieldInit;
        }

        private void OnPlayfieldInit(object sender, uint e)
        {
            _dungeonData = null;
            _visitedRooms = new List<int>();
            _openedContainers = new List<int>();

            if (!Playfield.IsDungeon)
                return;

            if (MapConfig.BlacklistIds.Contains((int)Playfield.ModelId))
            {
                if (_isActive)
                    Game.OnUpdate -= OnUpdate;

                _isActive = false;
            }
            else
            {
                if (!_isActive)
                    Game.OnUpdate += OnUpdate;

                _isActive = true;
            }
        }

        private void OnUpdate(object sender, float e)
        {
            try
            {
                _squaredDistance = MapConfig.ViewDistance * MapConfig.ViewDistance;

                if (!DrawMap())
                    return;

                RenderDynels();
            }
            catch (Exception ex)
            {
                Chat.WriteLine(ex.Message);
            }
        }

        private void RenderDynels()
        {
            DrawMissionObjective(out Dynel objective);

            foreach (var dynel in DynelManager.AllDynels)
            {
                var dynelPos = dynel.Position - _centerPos;

                switch (dynel.Identity.Type)
                {
                    case IdentityType.SimpleChar:
                        DrawSimpleChar(dynel, dynelPos);
                        break;
                    case IdentityType.Terminal:
                        DrawTerminal(dynel, dynelPos, objective != null && dynel.Identity == objective.Identity);
                        break;
                    case IdentityType.Container:
                        DrawContainer(dynel, dynelPos);
                        break;
                    default:
                        break;
                }
            }
        }

        private void DrawContainer(Dynel dynel, Vector3 dynelPos)
        {
            if (dynel.Room?.Floor != DynelManager.LocalPlayer.Room?.Floor) return;

            Vector3 color;
            EntrySettings shapeEntry, lineEntry;
            bool isOpened = _openedContainers.Contains(dynel.Identity.Instance) || Inventory.GetContainerItems(dynel.Identity).Any();

            if (isOpened)
            {
                color = MapConfig.Colors[Entry.OpenedChest];
                shapeEntry = MapConfig.ShapeEntries[Entry.OpenedChest];
                lineEntry = MapConfig.LineEntries[Entry.OpenedChest];

                if (!_openedContainers.Contains(dynel.Identity.Instance))
                    _openedContainers.Add(dynel.Identity.Instance);
            }
            else
            {
                color = MapConfig.Colors[Entry.UnopenedChest];
                shapeEntry = MapConfig.ShapeEntries[Entry.UnopenedChest];
                lineEntry = MapConfig.LineEntries[Entry.UnopenedChest];
            }

            if (shapeEntry.Show)
            {
                var squareMethod = GetDrawSquareAction(shapeEntry.Outline, 1f, 1f, 0, color, StraightLineWidth);
                DrawSquare(squareMethod, dynelPos);
            }

            if (lineEntry.Show)
            {
                var lineMethod = GetDrawLineAction(lineEntry.Outline, color, StraightLineWidth);
                DrawEdge(lineMethod, _playerPos, dynelPos);
            }
        }

        private void DrawSimpleChar(Dynel dynel, Vector3 dynelPos)
        {
            var shapeEntry = MapConfig.ShapeEntries[Entry.Character];

            if (!shapeEntry.Show)
                return;

            if (dynel.Room?.Floor != DynelManager.LocalPlayer.Room?.Floor)
                return;

            var simpleChar = new SimpleChar(dynel);

            Vector3 color = simpleChar.IsNpc ? MapConfig.Colors[Entry.Npc] :
                            simpleChar.IsPlayer && simpleChar.Identity == DynelManager.LocalPlayer.Identity ? MapConfig.Colors[Entry.LocalPlayer] :
                            MapConfig.Colors[Entry.Player];

            var squareMethod = GetDrawArrowheadAction(shapeEntry.Outline, color, SlantedLineWidth);
            DrawArrowhead(squareMethod, dynelPos, (float)simpleChar.Rotation.Yaw);
        }

        private void DrawTerminal(Dynel dynel, Vector3 dynelPos, bool isMissionObjective)
        {
            if (dynel.Room?.Floor != DynelManager.LocalPlayer.Room?.Floor)
                return;

            Vector3 color = MapConfig.Colors[Entry.Terminal];

            var shapeEntry = MapConfig.ShapeEntries[Entry.Terminal];

            if (shapeEntry.Show)
            {
                var squareMethod = GetDrawSquareAction(shapeEntry.Outline, 1, 1, 45, color, SlantedLineWidth);
                DrawSquare(squareMethod, dynelPos);
            }

            if (isMissionObjective)
                return;

            var lineEntry = MapConfig.LineEntries[Entry.Terminal];

            if (lineEntry.Show)
            {
                var lineMethod = GetDrawLineAction(lineEntry.Outline, color, SlantedLineWidth);
                DrawEdge(lineMethod, _playerPos, dynelPos);
            }
        }

        private void DrawMissionObjective(out Dynel objective)
        {
            if (!(Utils.GetMissionDynel(out objective) && objective.Room?.Floor == DynelManager.LocalPlayer.Room?.Floor))
                return;

            var color = MapConfig.Colors[Entry.MissionObjective];
            var shapeEntry = MapConfig.ShapeEntries[Entry.MissionObjective];
            var lineEntry = MapConfig.LineEntries[Entry.MissionObjective];
            var targetPos = objective.Position - _centerPos;

            if (shapeEntry.Show)
            {
                var squareMethod = GetDrawSquareAction(shapeEntry.Outline, 0.75f, 0.75f, 45f, color, SlantedLineWidth);
                DrawSquare(squareMethod, targetPos);
            }

            if (lineEntry.Show)
            {
                var lineMethod = GetDrawLineAction(lineEntry.Outline, color, SlantedLineWidth);
                DrawEdge(lineMethod, _playerPos, targetPos);
            }
        }

        private bool DrawMap()
        {
            if (_isLoading)
                return false;

            if (_dungeonData == null)
            {
                DungeonMapFactory newFactory = new DungeonMapFactory();
                _isLoading = true;
                Task.Run(() =>
                {
                    _dungeonData = newFactory.GetDungeonData();
                    _isLoading = false;
                });

                return false;
            }

            if (!_dungeonData.MeshData.TryGetValue(DynelManager.LocalPlayer.Room.Floor, out MeshData meshData))
                return false;

            _centerPos = meshData.Center;
            _playerPos = DynelManager.LocalPlayer.Position - _centerPos;

            var playerRoom = DynelManager.LocalPlayer.Room.Instance;

            if (!_visitedRooms.Contains(playerRoom))
                _visitedRooms.Add(playerRoom);

            DrawWalls(meshData.Walls);
            DrawDoors(meshData.Doors);

            return true;
        }

        private void DrawWalls(Dictionary<int, List<Edge>> wallEdges)
        {
            var wallEntry = MapConfig.ShapeEntries[Entry.Wall];

            if (!wallEntry.Show)
                return;


            foreach (var edgesPerRoom in wallEdges)
            {
                var color = !_visitedRooms.Contains(edgesPerRoom.Key) ? MapConfig.Colors[Entry.Wall] : MapConfig.Colors[Entry.ActiveRoom];
                var lineMethod = GetDrawLineAction(wallEntry.Outline, color, StraightLineWidth);

                DrawEdges(lineMethod, edgesPerRoom.Value);
            }
        }

        private void DrawDoors(Dictionary<int, DoorTransform> doors)
        {
            var doorShapeEntry = MapConfig.ShapeEntries[Entry.Door];
            var entranceDoorShape = MapConfig.ShapeEntries[Entry.EntranceDoor];
            var entranceDoorLine = MapConfig.LineEntries[Entry.EntranceDoor];

            foreach (var door in doors.Values)
            {
                Vector3 color = door.IsEntrance ? MapConfig.Colors[Entry.EntranceDoor] : MapConfig.Colors[Entry.Door];

                if (!door.IsEntrance)
                {
                    if (doorShapeEntry.Show)
                    {
                        var lineMethod = GetDrawLineAction(doorShapeEntry.Outline, color, StraightLineWidth);
                        DrawEdges(lineMethod, door.Edges);
                    }
                }
                else
                {
                    if (entranceDoorShape.Show)
                    {
                        var lineMethod = GetDrawLineAction(doorShapeEntry.Outline, color, StraightLineWidth);
                        DrawEdges(lineMethod, door.Edges);
                    }

                    if (entranceDoorLine.Show)
                    {
                        var lineMethod = GetDrawLineAction(entranceDoorLine.Outline, color, SlantedLineWidth);
                        DrawEdge(lineMethod, _playerPos, door.Pos);
                    }
                }
            }
        }

        private void DrawEdges(Action<Vector3, Vector3> drawMethod, List<Edge> edges)
        {
            foreach (var edge in edges)
            {
                DrawEdge(drawMethod, edge);
            }
        }

        private void DrawEdge(Action<Vector3, Vector3> drawMethod, Edge edge)
        {
            if (!IsInRange(edge.Center, _playerPos, _squaredDistance))
                return;

            drawMethod(edge.V1, edge.V2);
        }

        private void DrawEdge(Action<Vector3, Vector3> drawMethod, Vector3 pos1, Vector3 pos2)
        {
            if (!IsInRange(pos1, _playerPos, _squaredDistance))
                return;

            drawMethod(pos1, pos2);
        }

        private void DrawSquare(Action<Vector3> drawMethod, Vector3 pos)
        {
            if (!IsInRange(pos, _playerPos, _squaredDistance))
                return;

            drawMethod(pos);
        }

        private void DrawArrowhead(Action<Vector3, float> drawMethod, Vector3 pos, float yaw)
        {
            if (!IsInRange(pos, _playerPos, _squaredDistance))
                return;

            drawMethod(pos, yaw);
        }

        private bool IsInRange(Vector3 pos1, Vector3 pos2, float squaredDistance)
        {
            if (MapConfig.ViewDistance == _maxViewDistance)
                return true;

            Vector2 distanceOffset = new Vector2(pos1.X - pos2.X, pos1.Z - pos2.Z);

            if (distanceOffset.X * distanceOffset.X + distanceOffset.Y * distanceOffset.Y > squaredDistance)
                return false;

            return true;
        }

        private Action<Vector3, Vector3> GetDrawLineAction(bool outLine, Vector3 color, float width)
        {
            Action<Vector3, Vector3> drawLine = outLine ?
                drawLine = (v1, v2) => MDebug.DrawLineWithOutline(v1, v2, color, color, width) :
                drawLine = (v1, v2) => MDebug.DrawLine(v1, v2, color);

            return drawLine;
        }

        private Action<Vector3> GetDrawSquareAction(bool outLine, float width, float height, float angle, Vector3 color, float lineWidth)
        {
            Action<Vector3> drawMethod = outLine ?
              drawMethod = (v1) => MDebug.DrawSquareWithOutline(v1, width, height, angle, color, lineWidth) :
              drawMethod = (v1) => MDebug.DrawSquare(v1, width, height, angle, color);

            return drawMethod;
        }

        private Action<Vector3, float> GetDrawArrowheadAction(bool outLine, Vector3 color, float lineWidth)
        {
            Action<Vector3, float> drawMethod = outLine ?
               drawMethod = (v1, yaw) => MDebug.DrawArrowheadWithOutline(v1, yaw, 2, color, color, lineWidth) :
               drawMethod = (v1, yaw) => MDebug.DrawArrowhead(v1, yaw, 2, color);

            return drawMethod;
        }
    }
}
