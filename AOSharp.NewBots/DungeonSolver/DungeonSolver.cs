using AOSharp.Common.GameData;
using AOSharp.Core;
using AOSharp.Core.Inventory;
using AOSharp.Core.Misc;
using AOSharp.Core.UI;
using AOSharp.Pathfinding;
using Serilog;
using Serilog.Core;
using SharpNav;
using SmokeLounge.AOtomation.Messaging.Messages;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Dungeon.Solver
{
    public enum LiftDirection
    {
        Forward,
        Backward
    }

    public enum SolverMode
    {
        Clear,
        Blitz
    }

    public class Lift
    {
        public Identity Identity;
        public Vector3 Position;
        public Room Room;
    }

    public class TargetRoom
    {
        public Room Room;
        public int Door;
    }

    public class DungeonSolver
    {
        public static string[] MainHallNames = new string[]
        {
            "SL_mh",
            "clan_mh",
            "AI_mh"
        };

        public Stack<Room> RoomStack = new Stack<Room>();
        public List<int> VisitedRooms = new List<int>();
        public SMovementController MovementController;
        public SolverMode Mode;
        public bool IsCurrentRoomStale { private set; get; } = true;

        public TargetRoom TargetRoom;

        public Lift[] UpLifts;
        public Lift[] DownLifts;
        private bool[] _clearedFloors;

        public bool IsNavMeshLoaded => _navMeshes != null;
        public bool IsLiftFound => TryGetLift(LiftDirection.Forward, out _);
        public bool IsFloorClear => _clearedFloors[DynelManager.LocalPlayer.Room.AbsFloor] || Mode == SolverMode.Blitz && IsLiftFound;

        public bool IsOnBossFloor => DynelManager.LocalPlayer.Room.AbsFloor == Playfield.NumFloors - 1;

        public Action FloorChanged;
        public Action NavMeshLoadFailed;
        public Action NavMeshLoadFinished;

        private DungeonNavMeshFactory navMeshFactory;
        private AutoResetInterval _pingInterval = new AutoResetInterval(1000);
        private int _currentFloor = DynelManager.LocalPlayer.Room.Floor;
        private int _currentAbsFloor => Math.Abs(_currentFloor);
        private NavMesh[] _navMeshes;
        private Logger _logger;

        public DungeonSolver(SolverMode mode, Logger logger)
        {
            Mode = mode;
            _logger = logger;
            navMeshFactory = new DungeonNavMeshFactory();

            UpLifts = new Lift[Playfield.NumFloors];
            DownLifts = new Lift[Playfield.NumFloors];
            _clearedFloors = new bool[Playfield.NumFloors];

            SMovementController.Set();

            FindPreExistingLift();

            DynelManager.DynelSpawned += OnDynelSpawned;
            Network.N3MessageReceived += OnN3Message;
        }

        public void LoadNavmesh()
        {
            _logger.Information($"Loading Dungeon Navmesh..");

            navMeshFactory.GenerateNavMeshAsync().ContinueWith(navMesh =>
            {
                if (navMesh.IsFaulted || navMesh.Result == null)
                {
                    NavMeshLoadFailed?.Invoke();
                    return;
                }

                _navMeshes = navMesh.Result;
                SMovementController.LoadNavmesh(_navMeshes[_currentAbsFloor], true);
                NavMeshLoadFinished?.Invoke();
                _logger.Information($"Dungeon NavMesh Ready.");
            });
        }

        public void SetInitialRoom(Room room)
        {
            RoomStack.Clear();
            SetTargetRoom(room, 0);
            //IsCurrentRoomStale = true;
        }

        public void SetTargetRoom(Room room, int doorIdx)
        {
            RoomStack.Push(room);

            TargetRoom = new TargetRoom
            {
                Room = room,
                Door = doorIdx
            };
        }

        public void Regress()
        {
            Room regressedRoom = RoomStack.Pop();
            VisitedRooms.Remove(regressedRoom.Instance);
            IsCurrentRoomStale = true;

            _logger.Debug($"Regressing back to {RoomStack.Peek().Name}");
        }

        public bool Progress()
        {
            if (!RoomStack.Any())
            {
                _clearedFloors[DynelManager.LocalPlayer.Room.AbsFloor] = true;
                TargetRoom = null;
                IsCurrentRoomStale = false;
                return false;
            }

            Room currentRoom = RoomStack.Peek();
            VisitedRooms.Add(currentRoom.Instance);

            var unvisitedDoors = currentRoom.GetUnvisitedDoors(VisitedRooms);

            if (!unvisitedDoors.Any())
            {
                RoomStack.Pop();
                return Progress();
            }

            var sortedUnvisitedDoors = unvisitedDoors.OrderBy(x => TryGetLift(LiftDirection.Forward, out Lift lift) && currentRoom.DoorwayLeadsToLift(x, lift))
                                                     .ThenByDescending(x => Playfield.Rooms[currentRoom.GetDoorConnectZone(x)].IsEndblocker())
                                                     .ThenBy(x => GetNavDistance(currentRoom, x)).ToList();

            _logger.Debug("Sorted Choices:");
            foreach (int door in sortedUnvisitedDoors)
                _logger.Debug($"\t{Playfield.Rooms[currentRoom.GetDoorConnectZone(door)]}");

            int bestDoor = sortedUnvisitedDoors.First();

            //Temp
            Room nextRoom = Playfield.Rooms[currentRoom.GetDoorConnectZone(bestDoor)];

            currentRoom.GetDoorPosRot(bestDoor, out Vector3 bestDoorPos, out _);
            int doorIdx = GetDoorIdxForPos(nextRoom, bestDoorPos);
            SetTargetRoom(nextRoom, doorIdx);

            IsCurrentRoomStale = false;
            return true;        
        }

        public void Update()
        {
            if (!Playfield.IsDungeon)
                return;

            OpenNearbyLockedDoors();

            if (_currentFloor != DynelManager.LocalPlayer.Room.Floor)
            {
                _currentFloor = DynelManager.LocalPlayer.Room.Floor;
                SMovementController.LoadNavmesh(_navMeshes[_currentAbsFloor], true);
                _logger.Debug($"Switching navmesh to {_currentAbsFloor}");
                SetInitialRoom(DynelManager.LocalPlayer.Room);
                FloorChanged?.Invoke();
            }

            if (!RoomStack.Any())
                return;

            if (_pingInterval.Elapsed)
                CharacterAction.InfoRequest(DynelManager.LocalPlayer.Identity);

            var adjacentRooms = GetAdjacentRooms(DynelManager.LocalPlayer.Room, 1);

            if (adjacentRooms.Contains(RoomStack.Peek()) && RoomStack.Peek().IsEmpty())
                IsCurrentRoomStale = true;

            InvalidateEmptyRooms(adjacentRooms);
        }

        private void OpenNearbyLockedDoors()
        {
            Door lockedDoor = Playfield.Doors.Where(x => x.IsLocked && x.DistanceFrom(DynelManager.LocalPlayer) < 5f).FirstOrDefault();

            if (lockedDoor == null)
                return;

            if (Inventory.Find("Lock Pick", out Item lockPick))
                lockPick.UseOn(lockedDoor);
        }

        private void InvalidateEmptyRooms(List<Room> rooms)
        {
            List<Room> emptyRooms = rooms.Where(x => x != null && x.IsClear(VisitedRooms, Mode)).ToList();

            foreach(Room room in emptyRooms)
            {
                if (VisitedRooms.Contains(room.Instance))
                    continue;

                if (room.Instance == RoomStack.Peek().Instance)
                    IsCurrentRoomStale = true;

                VisitedRooms.Add(room.Instance);

                _logger.Debug($"Invalidated empty room {room.Name} ({room.Instance}) from {DynelManager.LocalPlayer.Room.Name} ({DynelManager.LocalPlayer.Room.Instance})");
            }
        }

        public List<Room> GetAdjacentRooms(Room origin, int depth)
        {
            List<int> rooms = new List<int>();
            GetAdjacentRoomsInternal(origin, rooms, depth);
            rooms.Add(origin.Instance);

            return rooms.Select(x => Playfield.Rooms[x]).ToList();
        }

        private void GetAdjacentRoomsInternal(Room origin, List<int> rooms, int depth)
        {
            if (depth < 1)
                return;

            for (int i = 0; i < origin.NumDoors; i++)
            {
                int roomIdx = origin.GetDoorConnectZone(i);

                if (roomIdx == -1)
                    continue;

                if (rooms.Contains(roomIdx))
                    continue;

                Room room = Playfield.Rooms[roomIdx];
                rooms.Add(room.Instance);

                if (depth > 1)
                    GetAdjacentRoomsInternal(room, rooms, depth - 1);
            }
        }

        public float GetNavDistance(Room room, int doorIdx)
        {
            room.GetDoorPosRot(doorIdx, out Vector3 doorPos, out _);

            if (SMovementController.GenerateNavPath(doorPos, out _, out float totalDistance))
                return totalDistance;

            return 0;
        }

        public int GetDoorIdxForPos(Room room, Vector3 pos)
        {
            for(int i = 0; i < room.NumDoors; i++)
            {
                room.GetDoorPosRot(i, out Vector3 doorPos, out _);
                if (doorPos.DistanceFrom(pos) < 1f)
                    return i;
            }

            throw new Exception($"Unable to determine door idx for position {pos} in room {room.Name}");
        }

        public bool TryGetLift(LiftDirection direction, out Lift lift)
        {
            int currentFloor = DynelManager.LocalPlayer.Room.AbsFloor;

            if (Playfield.DungeonDirection == DungeonDirection.Up)
                lift = direction == LiftDirection.Forward ? UpLifts[currentFloor] : DownLifts[currentFloor];
            else
                lift = direction == LiftDirection.Backward ? UpLifts[currentFloor] : DownLifts[currentFloor];

            return lift != null;
        }

        private void FindPreExistingLift()
        {
            foreach (SimpleItem lift in DynelManager.Terminals.Where(x => x.Name.StartsWith("Button (")))
                OnDynelSpawned(null, lift);
        }

        private void OnDynelSpawned(object sender, Dynel dynel)
        {
            if (!Playfield.IsDungeon)
                return;

            int floor = dynel.Room.AbsFloor;

            Lift[] liftArray;
            bool isForwardLift;

            if (dynel.Name == "Button (boss)")
            {
                liftArray = Playfield.DungeonDirection == DungeonDirection.Up ? UpLifts : DownLifts;
                isForwardLift = true;
            }
            else if (dynel.Name == "Button (up)")
            {
                liftArray = UpLifts;
                isForwardLift = Playfield.DungeonDirection == DungeonDirection.Up;
            }
            else if (dynel.Name == "Button (down)")
            {
                liftArray = DownLifts;
                isForwardLift = Playfield.DungeonDirection == DungeonDirection.Down;
            }
            else return;

            if (liftArray[floor] != null)
                return;

            liftArray[floor] = new Lift
            {
                Identity = dynel.Identity,
                Position = dynel.Position,
                Room = dynel.Room
            };

            if (isForwardLift)
                OnForwardLiftFound();
        }

        private void OnForwardLiftFound()
        {
            TryGetLift(LiftDirection.Forward, out Lift lift);

            List<int> pathToLift = DynelManager.LocalPlayer.Room.PathToLift(lift);

            if (RoomStack.Any(x => !pathToLift.Contains(x.Instance)))
                while (pathToLift.Contains(RoomStack.Peek().Instance))
                    Regress();
        }

        private void OnN3Message(object sender, N3Message msg)
        {
            if (msg.Identity != DynelManager.LocalPlayer.Identity)
                return;

            if (msg is InfoPacketMessage)
                InvalidateEmptyRooms(GetAdjacentRooms(DynelManager.LocalPlayer.Room, 1));
        }

        public void Test()
        {
            _logger.Debug("Adjacent Rooms:");
            foreach (Room room in GetAdjacentRooms(DynelManager.LocalPlayer.Room, 2))
            {
                _logger.Debug($"\t{room}");
            }
        }

        public void Dispose()
        {
            DynelManager.DynelSpawned -= OnDynelSpawned;
            Network.N3MessageReceived -= OnN3Message;
        }
    }
}
