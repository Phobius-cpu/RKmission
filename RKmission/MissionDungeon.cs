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
        private readonly LootRules _lootRules;
        private readonly HashSet<int> _clearedRooms = new HashSet<int>();
        private readonly HashSet<string> _blockedEdges = new HashSet<string>();
        private readonly HashSet<Identity> _processedLoot = new HashSet<Identity>();
        private readonly HashSet<Identity> _failedLoot = new HashSet<Identity>();
        private NavMesh[] _meshes;
        private DungeonLayout _layout;
        private Container _openedContainer;
        private Identity _lootTarget = Identity.None;
        private Identity _combatTarget = Identity.None;
        private Identity _pendingItem = Identity.None;
        private LootRule _pendingRule;
        private Mission _mission;
        private bool _objectiveAttempted;
        private int _currentRoom = -1;
        private int _targetRoom = -1;
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

        public MissionDungeon(Action<string> say, LootRules lootRules)
        {
            _say = say;
            _lootRules = lootRules;
            Inventory.ContainerOpened += ContainerOpened;
        }

        public void Start(Mission mission)
        {
            if (IsRunning)
                return;
            if (!Playfield.IsDungeon || DynelManager.LocalPlayer?.Room == null)
                return;
            _clearedRooms.Clear();
            _blockedEdges.Clear();
            _processedLoot.Clear();
            _failedLoot.Clear();
            _lootTarget = Identity.None;
            _combatTarget = Identity.None;
            _pendingItem = Identity.None;
            _pendingRule = null;
            _mission = (Mission.List ?? new List<Mission>())
                .FirstOrDefault(x => mission != null && x.Identity == mission.Identity) ??
                (mission == null && Mission.List?.Count == 1 ? Mission.List[0] : null);
            _objectiveAttempted = false;
            _currentRoom = -1;
            _targetRoom = -1;
            _destination = null;
            _openedContainer = null;
            _meshes = null;
            _layout?.Dispose();
            _layout = new DungeonLayout(_clearedRooms);
            _layout.Show(() => _targetRoom);
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
            _targetRoom = -1;
            _layout?.Dispose();
            _layout = null;
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

            Room room = DynelManager.LocalPlayer.Room;
            if (room.Instance != _currentRoom)
            {
                _currentRoom = room.Instance;
                _targetRoom = -1;
                _destination = null;
                _roomQuietAt = DateTime.MinValue;
                _doorAttempts = 0;
                _lastProgress = DateTime.UtcNow;
                _lastPosition = DynelManager.LocalPlayer.Position;
            }

            if (Vector3.Distance(_lastPosition, DynelManager.LocalPlayer.Position) > 1f)
            {
                _lastPosition = DynelManager.LocalPlayer.Position;
                _lastProgress = DateTime.UtcNow;
            }
            else if (_targetRoom >= 0 &&
                DateTime.UtcNow - _lastProgress > TimeSpan.FromSeconds(12))
            {
                _blockedEdges.Add(EdgeKey(room.Instance, _targetRoom));
                _say($"Route to room {_targetRoom} stalled; choosing the closest reachable room.");
                _targetRoom = -1;
                _destination = null;
                _lastProgress = DateTime.UtcNow;
                SMovementController.Halt();
            }
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
            _pendingItem = Identity.None;
            _pendingRule = null;
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
                if (_pendingItem != Identity.None)
                {
                    if (_openedContainer.Items.All(x => x.UniqueIdentity != _pendingItem) ||
                        Inventory.Items.Any(x => x.UniqueIdentity == _pendingItem &&
                            x.Slot.Type == IdentityType.Inventory))
                    {
                        _lootRules.RecordLoot(_pendingRule);
                        _pendingItem = Identity.None;
                        _pendingRule = null;
                        _lootAttempts = 0;
                    }
                    else if (DateTime.UtcNow - _actionAt < TimeSpan.FromSeconds(2))
                        return true;
                    else if (++_lootAttempts > 4)
                    {
                        Stop();
                        _say("Selected loot did not move to inventory; stopped in this room.");
                        return true;
                    }
                }

                Item item = _openedContainer.Items.FirstOrDefault(x => _lootRules.Match(x) != null);
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
                        _pendingItem = item.UniqueIdentity;
                        _pendingRule = _lootRules.Match(item);
                        item.MoveToInventory();
                        _actionAt = DateTime.UtcNow;
                    }
                    return true;
                }
                // Unselected items stay in the corpse/chest, as in Manager.Loot.
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
            _targetRoom = next.Instance;
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

        private static string EdgeKey(int a, int b) =>
            a < b ? $"{a}:{b}" : $"{b}:{a}";
    }
}
