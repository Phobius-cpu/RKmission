using System;
using System.Collections.Generic;
using System.Linq;
using AOSharp.Common.GameData;
using AOSharp.Core;
using AOSharp.Core.Inventory;
using AOSharp.Pathfinding;
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
        private readonly HashSet<int> _clearedRooms = new HashSet<int>();
        private readonly HashSet<Identity> _processedLoot = new HashSet<Identity>();
        private readonly HashSet<Identity> _failedLoot = new HashSet<Identity>();
        private NavMesh[] _meshes;
        private Container _openedContainer;
        private Identity _lootTarget = Identity.None;
        private Identity _combatTarget = Identity.None;
        private Mission _mission;
        private bool _objectiveAttempted;
        private int _currentRoom = -1;
        private Vector3? _destination;
        private DateTime _actionAt;
        private DateTime _roomQuietAt;
        private DateTime _lastProgress;
        private Vector3 _lastPosition;
        private int _floor;
        private int _lootAttempts;
        private int _doorAttempts;

        public bool IsRunning { get; private set; }
        public bool IsComplete { get; private set; }
        public string Status => IsComplete ? "complete" : !IsRunning ? "idle" :
            $"cleared {_clearedRooms.Count} rooms";

        public MissionDungeon(Action<string> say)
        {
            _say = say;
            Inventory.ContainerOpened += ContainerOpened;
        }

        public void Start(Mission mission)
        {
            if (IsRunning)
                return;
            if (!Playfield.IsDungeon || DynelManager.LocalPlayer?.Room == null)
                return;
            _clearedRooms.Clear();
            _processedLoot.Clear();
            _failedLoot.Clear();
            _lootTarget = Identity.None;
            _combatTarget = Identity.None;
            _mission = (Mission.List ?? new List<Mission>())
                .FirstOrDefault(x => mission != null && x.Identity == mission.Identity) ??
                (mission == null && Mission.List?.Count == 1 ? Mission.List[0] : null);
            _objectiveAttempted = false;
            _currentRoom = -1;
            _destination = null;
            _openedContainer = null;
            _meshes = null;
            IsComplete = false;
            IsRunning = true;
            _roomQuietAt = DateTime.MinValue;
            _lastProgress = DateTime.UtcNow;
            _lastPosition = DynelManager.LocalPlayer.Position;
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
            _lootTarget = Identity.None;
            _openedContainer = null;
            _destination = null;
            SMovementController.Halt();
        }

        public void Dispose()
        {
            Stop();
            Inventory.ContainerOpened -= ContainerOpened;
        }

        private void ContainerOpened(object sender, Container container)
        {
            if (IsRunning && container.Identity == _lootTarget)
                _openedContainer = container;
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

            if (Vector3.Distance(_lastPosition, DynelManager.LocalPlayer.Position) > 1f)
            {
                _lastPosition = DynelManager.LocalPlayer.Position;
                _lastProgress = DateTime.UtcNow;
            }
            else if (SMovementController.IsNavigating() &&
                DateTime.UtcNow - _lastProgress > TimeSpan.FromSeconds(12))
            {
                Stop();
                _say("Navigation stalled; stopped to avoid skipping a room.");
                return;
            }

            Room room = DynelManager.LocalPlayer.Room;
            if (room.Instance != _currentRoom)
            {
                _currentRoom = room.Instance;
                _roomQuietAt = DateTime.MinValue;
                _doorAttempts = 0;
            }
            if (HandleObjective(room, false) || FightInRoom(room) ||
                HandleObjective(room, true) || LootInRoom(room))
            {
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
            if (_lootTarget != Identity.None)
                return ContinueLoot();

            Dynel loot = DynelManager.AllDynels
                .Where(x => (x.Identity.Type == IdentityType.Corpse ||
                             x.Identity.Type == IdentityType.Container) &&
                            x.Room != null && x.Room.Instance == room.Instance &&
                            !_processedLoot.Contains(x.Identity) &&
                            !_failedLoot.Contains(x.Identity))
                .OrderBy(x => x.DistanceFrom(DynelManager.LocalPlayer))
                .FirstOrDefault();
            if (loot == null)
                return false;

            _lootTarget = loot.Identity;
            _lootAttempts = 0;
            _openedContainer = null;
            _actionAt = DateTime.MinValue;
            return ContinueLoot();
        }

        private bool ContinueLoot()
        {
            Dynel loot = DynelManager.GetDynel(_lootTarget);
            if (loot == null)
            {
                _processedLoot.Add(_lootTarget);
                _lootTarget = Identity.None;
                return true;
            }

            if (loot.DistanceFrom(DynelManager.LocalPlayer) > 4f)
            {
                Navigate(loot.Position);
                return true;
            }
            SMovementController.Halt();

            if (_openedContainer != null)
            {
                Item item = _openedContainer.Items.FirstOrDefault();
                if (item != null)
                {
                    if (Inventory.NumFreeSlots == 0)
                    {
                        Stop();
                        _say("Inventory full; loot remains in this room.");
                        return true;
                    }
                    if (DateTime.UtcNow - _actionAt > TimeSpan.FromMilliseconds(650))
                    {
                        item.MoveToInventory();
                        _actionAt = DateTime.UtcNow;
                    }
                    return true;
                }
                _processedLoot.Add(_lootTarget);
                _lootTarget = Identity.None;
                _openedContainer = null;
                return true;
            }

            if (DateTime.UtcNow - _actionAt < TimeSpan.FromSeconds(2))
                return true;
            if (++_lootAttempts > 4)
            {
                _failedLoot.Add(_lootTarget);
                Stop();
                _say($"Unable to open or loot {_lootTarget}; stopped in this room.");
                return true;
            }

            if (loot.Identity.Type == IdentityType.Container &&
                new LockableItem(loot).IsLocked)
            {
                if (!Inventory.Find("Lock Pick", out Item pick))
                {
                    Stop();
                    _say("Locked chest found without a Lock Pick.");
                    return true;
                }
                pick.UseOn(loot.Identity);
            }
            else
                loot.Use();

            _actionAt = DateTime.UtcNow;
            return true;
        }

        private Room NextRoom(Room current)
        {
            var rooms = Playfield.Rooms;
            var queue = new Queue<int>();
            var parent = new Dictionary<int, int>();
            queue.Enqueue(current.Instance);
            parent[current.Instance] = -1;

            while (queue.Count > 0)
            {
                int index = queue.Dequeue();
                Room room = rooms[index];
                if (!_clearedRooms.Contains(index))
                {
                    while (parent[index] != current.Instance && parent[index] != -1)
                        index = parent[index];
                    return rooms[index];
                }

                for (int door = 0; door < room.NumDoors; door++)
                {
                    int adjacent = room.GetDoorConnectZone(door);
                    if (adjacent < 0 || adjacent >= rooms.Count || parent.ContainsKey(adjacent))
                        continue;
                    parent[adjacent] = index;
                    queue.Enqueue(adjacent);
                }
            }
            return null;
        }

        private void MoveToAdjacentRoom(Room current, Room next)
        {
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

        private void Navigate(Vector3 destination)
        {
            if (!_destination.HasValue || Vector3.Distance(_destination.Value, destination) > 1f ||
                !SMovementController.IsNavigating())
            {
                SMovementController.SetNavDestination(destination);
                _destination = destination;
            }
        }
    }
}
