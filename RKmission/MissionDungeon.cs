using System;
using System.Collections.Generic;
using System.Linq;
using AOSharp.Common.GameData;
using AOSharp.Core;
using AOSharp.Core.Inventory;
using AOSharp.Pathfinding;
using ManagerLoot;
using SharpNav;

namespace RKmission
{
    /// <summary>
    /// Clears the current room before traversing the shortest route to an uncleared
    /// adjacent room. Room connections come from AO#'s dungeon map, not screen pixels.
    /// </summary>
    internal sealed class MissionDungeon : IDisposable
    {
        private readonly Action<string> _say;
        private readonly ManagerLoot.ManagerLoot _loot;
        private readonly HashSet<int> _clearedRooms = new HashSet<int>();
        private readonly HashSet<string> _blockedEdges = new HashSet<string>();
        private NavMesh[] _meshes;
        private DungeonLayout _layout;
        private Identity _combatTarget = Identity.None;
        private Mission _mission;
        private bool _objectiveAttempted;
        private int _currentRoom = -1;
        private int _targetRoom = -1;
        private Vector3? _destination;
        private DateTime _actionAt;
        private DateTime _roomQuietAt;
        private DateTime _transitionStarted;
        private DateTime _observedRoomAt;
        private int _observedRoom = -1;
        private DateTime _lootWaitStarted;
        private Identity _waitingForLoot = Identity.None;
        private int _floor;
        private int _doorAttempts;

        public bool IsRunning { get; private set; }
        public bool IsComplete { get; private set; }
        public string Status => IsComplete ? "complete" : !IsRunning ? "idle" :
            $"cleared {_clearedRooms.Count} rooms";

        public MissionDungeon(Action<string> say, ManagerLoot.ManagerLoot loot)
        {
            _say = say;
            _loot = loot;
        }

        public void Start(Mission mission)
        {
            if (IsRunning)
                return;
            if (!Playfield.IsDungeon || DynelManager.LocalPlayer?.Room == null)
                return;
            _clearedRooms.Clear();
            _blockedEdges.Clear();
            _combatTarget = Identity.None;
            _waitingForLoot = Identity.None;
            _mission = (Mission.List ?? new List<Mission>())
                .FirstOrDefault(x => mission != null && x.Identity == mission.Identity) ??
                (mission == null && Mission.List?.Count == 1 ? Mission.List[0] : null);
            _objectiveAttempted = false;
            _currentRoom = -1;
            _targetRoom = -1;
            _destination = null;
            _meshes = null;
            _layout = new DungeonLayout();
            IsComplete = false;
            IsRunning = true;
            _roomQuietAt = DateTime.MinValue;
            _transitionStarted = DateTime.MinValue;
            _observedRoom = -1;
            _floor = Math.Abs(DynelManager.LocalPlayer.Room.Floor);
            new DungeonNavMeshFactory().GenerateNavMeshAsync().ContinueWith(task =>
            {
                if (!IsRunning)
                    return;
                if (task.IsFaulted || task.IsCanceled || task.Result == null || task.Result.Length == 0)
                {
                    _say("Dungeon navmesh generation failed; run stopped.");
                    Stop();
                    return;
                }
                _meshes = task.Result;
                LoadFloor();
            });
            _say("Entered mission; mapping rooms.");
        }

        public void Stop()
        {
            IsRunning = false;
            _loot.EndMissionRoom();
            _destination = null;
            _targetRoom = -1;
            _layout = null;
            SMovementController.Halt();
        }

        public void Dispose()
        {
            Stop();
        }

        private void LoadFloor()
        {
            if (_meshes != null && _floor >= 0 && _floor < _meshes.Length)
                SMovementController.LoadNavmesh(_meshes[_floor], true);
        }

        public void Tick()
        {
            if (!IsRunning || !Playfield.IsDungeon || DynelManager.LocalPlayer.Room == null ||
                _meshes == null)
                return;

            int floor = Math.Abs(DynelManager.LocalPlayer.Room.Floor);
            if (floor != _floor)
            {
                _floor = floor;
                LoadFloor();
                _roomQuietAt = DateTime.MinValue;
            }

            Room room = DynelManager.LocalPlayer.Room;
            if (_targetRoom >= 0 && _transitionStarted != DateTime.MinValue &&
                DateTime.UtcNow - _transitionStarted > TimeSpan.FromSeconds(18))
            {
                FailTransition(_currentRoom, _targetRoom);
                if (room.Instance != _currentRoom) return;
            }
            if (_currentRoom < 0)
            {
                _currentRoom = room.Instance;
                _roomQuietAt = DateTime.MinValue;
            }
            else if (room.Instance != _currentRoom)
            {
                if (_observedRoom != room.Instance)
                {
                    _observedRoom = room.Instance;
                    _observedRoomAt = DateTime.UtcNow;
                }
                // A doorway can briefly report either room. Do not treat a boundary
                // flicker as a completed transition or plan a route back through it.
                if (DateTime.UtcNow - _observedRoomAt < TimeSpan.FromSeconds(1))
                    return;
                if (_targetRoom >= 0 && room.Instance != _targetRoom)
                {
                    FailTransition(_currentRoom, _targetRoom);
                    return;
                }
                _currentRoom = room.Instance;
                _targetRoom = -1;
                _destination = null;
                _transitionStarted = DateTime.MinValue;
                _observedRoom = -1;
                _roomQuietAt = DateTime.MinValue;
                _doorAttempts = 0;
                SMovementController.Halt();
            }
            else
            {
                _observedRoom = -1;
            }
            _loot.BeginMissionRoom(room.Instance);
            if (HandleObjective(room, false) || FightInRoom(room) ||
                HandleObjective(room, true) || LootInRoom(room))
            {
                _targetRoom = -1;
                _roomQuietAt = DateTime.MinValue;
                return;
            }

            if (_roomQuietAt == DateTime.MinValue)
            {
                _roomQuietAt = DateTime.UtcNow;
                return;
            }
            if (DateTime.UtcNow - _roomQuietAt < TimeSpan.FromSeconds(2))
                return;

            _clearedRooms.Add(room.Instance);
            Room next = NextRoom(room);
            if (next == null)
            {
                SMovementController.Halt();
                if (_blockedEdges.Count > 0 && _clearedRooms.Count < Playfield.Rooms.Count)
                {
                    Stop();
                    _say("No further reachable rooms; blocked doors/routes remain. The current room was cleared.");
                    return;
                }
                if (_mission != null && !_objectiveAttempted && _mission.Actions.Any(x =>
                    x is FindItemAction || x is FindPersonAction || x is UseItemOnItemAction))
                {
                    Stop();
                    _say("Every reachable room was visited, but the mission objective was not found.");
                    return;
                }
                IsComplete = true;
                IsRunning = false;
                return;
            }

            MoveToAdjacentRoom(room, next);
        }

        private bool FightInRoom(Room room)
        {
            SimpleChar enemy = DynelManager.NPCs
                .Where(x => x.IsAlive && !x.IsPet && x.Room != null &&
                    x.Room.Instance == room.Instance)
                .OrderBy(x => x.DistanceFrom(DynelManager.LocalPlayer))
                .FirstOrDefault();
            if (enemy == null)
            {
                _combatTarget = Identity.None;
                return false;
            }

            if (_combatTarget != enemy.Identity)
            {
                _combatTarget = enemy.Identity;
                _actionAt = DateTime.UtcNow;
            }
            if (DateTime.UtcNow - _actionAt > TimeSpan.FromSeconds(20))
            {
                Stop();
                _say($"Enemy {enemy.Name} could not be reached; stopped in room {room.Instance}.");
                return true;
            }

            if (enemy.IsInLineOfSight && enemy.IsInAttackRange(true))
            {
                _actionAt = DateTime.UtcNow;
                SMovementController.Halt();
                if (!DynelManager.LocalPlayer.IsAttackPending &&
                    (!DynelManager.LocalPlayer.IsAttacking ||
                     DynelManager.LocalPlayer.FightingTarget?.Identity != enemy.Identity))
                    DynelManager.LocalPlayer.Attack(enemy);
            }
            else if (!SMovementController.IsNavigating())
                Navigate(enemy.Position);

            return true;
        }

        private bool HandleObjective(Room room, bool allowApproach)
        {
            if (_objectiveAttempted || _mission == null)
                return false;

            MissionAction action = _mission.Actions.FirstOrDefault();
            Identity targetId = Identity.None;
            if (action is FindItemAction findItem)
                targetId = findItem.Target;
            else if (action is FindPersonAction findPerson)
                targetId = findPerson.Target;
            else if (action is UseItemOnItemAction useItem)
                targetId = useItem.Destination;
            else
                return false; // KillPersonAction is handled by combat.

            Dynel target = DynelManager.GetDynel(targetId);
            if (target == null || target.Room?.Instance != room.Instance)
                return false;
            if (target.DistanceFrom(DynelManager.LocalPlayer) > 4f)
            {
                if (!allowApproach)
                    return false;
                Navigate(target.Position);
                return true;
            }

            SMovementController.Halt();
            if (action is UseItemOnItemAction useAction)
            {
                Item source = Inventory.Items.FirstOrDefault(x => x.UniqueIdentity == useAction.Source);
                if (source == null)
                {
                    Stop();
                    _say("Mission objective item is missing from inventory.");
                    return true;
                }
                source.UseOn(target.Identity);
            }
            else
                target.Target();

            _objectiveAttempted = true;
            _say("Mission objective interaction sent.");
            return true;
        }

        private bool LootInRoom(Room room)
        {
            // Manager.Loot owns the list, chest/lockpick handling and item moves.
            // RKMission only brings it within range of the next object in this room.
            if (_loot.IsProcessingMissionLoot)
            {
                SMovementController.Halt();
                return true;
            }
            Dynel next = _loot.NextMissionLoot(room.Instance);
            if (next == null)
            {
                _waitingForLoot = Identity.None;
                return _loot.IsProcessingMissionLoot;
            }
            if (_waitingForLoot != next.Identity)
            {
                _waitingForLoot = next.Identity;
                _lootWaitStarted = DateTime.UtcNow;
            }
            if (next.DistanceFrom(DynelManager.LocalPlayer) > 4.5f)
            {
                Navigate(next.Position);
                return true;
            }
            SMovementController.Halt();
            if (DateTime.UtcNow - _lootWaitStarted > TimeSpan.FromSeconds(20))
            {
                Stop();
                _say($"Manager.Loot could not finish {next.Identity} in room {room.Instance}.");
            }
            return true;
        }
        private Room NextRoom(Room current)
        {
            if (_layout == null)
                return null;
            if (_targetRoom >= 0 && _layout.Neighbors(current.Instance).Contains(_targetRoom) &&
                !_blockedEdges.Contains(EdgeKey(current.Instance, _targetRoom)))
                return _layout.Room(_targetRoom);
            var queue = new Queue<int>();
            var parent = new Dictionary<int, int>();
            queue.Enqueue(current.Instance);
            parent[current.Instance] = -1;

            while (queue.Count > 0)
            {
                int index = queue.Dequeue();
                Room room = _layout.Room(index);
                if (room == null)
                    continue;
                if (!_clearedRooms.Contains(index))
                {
                    while (parent[index] != current.Instance && parent[index] != -1)
                        index = parent[index];
                    return _layout.Room(index);
                }

                // BFS preserves room-by-room travel. Sort each frontier by distance
                // so a stalled route falls back to the nearest reachable neighbor.
                foreach (int adjacent in _layout.Neighbors(index)
                    .Where(id => !_blockedEdges.Contains(EdgeKey(index, id)))
                    .OrderBy(id => Vector3.Distance(_layout.Room(id).Center,
                        DynelManager.LocalPlayer.Position)))
                {
                    if (parent.ContainsKey(adjacent))
                        continue;
                    parent[adjacent] = index;
                    queue.Enqueue(adjacent);
                }
            }
            return null;
        }

        private void MoveToAdjacentRoom(Room current, Room next)
        {
            if (_targetRoom != next.Instance)
            {
                _targetRoom = next.Instance;
                _transitionStarted = DateTime.UtcNow;
                _destination = null;
                _doorAttempts = 0;
            }
            Door door = Playfield.Doors.FirstOrDefault(x =>
                (x.RoomLink1?.Instance == current.Instance && x.RoomLink2?.Instance == next.Instance) ||
                (x.RoomLink2?.Instance == current.Instance && x.RoomLink1?.Instance == next.Instance));

            if (door != null && door.DistanceFrom(DynelManager.LocalPlayer) > 4f)
            {
                Navigate(door.Position);
                return;
            }

            if (door != null && door.IsLocked)
            {
                if (!Inventory.Find("Lock Pick", out Item pick))
                {
                    Stop();
                    _say($"Locked door to room {next.Instance} requires a Lock Pick.");
                    return;
                }
                if (DateTime.UtcNow - _actionAt > TimeSpan.FromSeconds(2))
                {
                    if (++_doorAttempts > 5)
                    {
                        Stop();
                        _say($"Could not unlock door to room {next.Instance}.");
                        return;
                    }
                    pick.UseOn(door.Identity);
                    _actionAt = DateTime.UtcNow;
                }
                return;
            }

            _doorAttempts = 0;
            if (door != null && !door.IsOpen && DateTime.UtcNow - _actionAt > TimeSpan.FromSeconds(2))
            {
                door.Use();
                _actionAt = DateTime.UtcNow;
                return;
            }

            Navigate(next.Center);
        }

        private void FailTransition(int fromRoom, int toRoom)
        {
            _blockedEdges.Add(EdgeKey(fromRoom, toRoom));
            _say($"Entry from room {fromRoom} to {toRoom} was not confirmed; trying another closest reachable room.");
            _targetRoom = -1;
            _destination = null;
            _transitionStarted = DateTime.MinValue;
            _observedRoom = -1;
            SMovementController.Halt();
        }

        private void Navigate(Vector3 destination)
        {
            if (!_destination.HasValue || Vector3.Distance(_destination.Value, destination) > 1f ||
                !SMovementController.IsNavigating())
            {
                SMovementController.SetNavDestination(destination);
                _destination = destination;
            }
        }

        private static string EdgeKey(int a, int b) =>
            a < b ? $"{a}:{b}" : $"{b}:{a}";
    }
}
