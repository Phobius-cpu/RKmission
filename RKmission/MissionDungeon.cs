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
    /// Clears each confirmed room, then crosses one Mali-mapped room connection.
    /// </summary>
    internal sealed class MissionDungeon : IDisposable
    {
        private readonly Action<string> _say;
        private readonly ManagerLoot.ManagerLoot _loot;
        private readonly MissionReadiness _readiness;
        private const float EngagementRange = 20f;
        private readonly HashSet<int> _clearedRooms = new HashSet<int>();
        private readonly HashSet<int> _visitedRooms = new HashSet<int>();
        private readonly HashSet<int> _surveyedRooms = new HashSet<int>();
        private readonly Dictionary<string, EdgeFailure> _edgeFailures = new Dictionary<string, EdgeFailure>();
        private readonly Dictionary<string, DateTime> _reverseCooldown = new Dictionary<string, DateTime>();
        private NavMesh[] _meshes;
        private DungeonLayout _layout;
        private Identity _combatTarget = Identity.None;
        private Identity _scanTarget = Identity.None;
        private DateTime _scanStarted, _scanProgress;
        private float _scanDistance;
        private Mission _mission;
        private bool _objectiveAttempted;
        private int _currentRoom = -1;
        private Transition _transition;
        private Vector3? _destination;
        private DateTime _actionAt;
        private DateTime _roomQuietAt;
        private DateTime _observedRoomAt;
        private int _observedRoom = -1;
        private DateTime _lootWaitStarted;
        private DateTime _lootLastProgress;
        private Identity _waitingForLoot = Identity.None;
        private Vector3 _lootApproachPoint;
        private float _lootBestDistance;
        private int _lootApproachRetries;
        private int _floor;

        private enum TransitionPhase { ApproachDoor, ProbeDoor, OpenDoor, CrossDoor }
        private sealed class Transition
        {
            public DungeonLayout.Connection Edge;
            public TransitionPhase Phase;
            public DateTime Started, PhaseStarted, LastAction, LastProgress;
            public float BestDistance;
            public int DoorAttempts, CrossingRetries;
            public bool PushingDeeper, DoorApproachLogged;
        }
        private sealed class EdgeFailure
        {
            public int Count;
            public DateTime Until;
            public bool Permanent;
        }

        public bool IsRunning { get; private set; }
        public bool IsComplete { get; private set; }
        public string Status => IsComplete ? "complete" : !IsRunning ? "idle" : _readiness.IsWaiting ? _readiness.Status :
            $"visited {_visitedRooms.Count}, cleared {_clearedRooms.Count} rooms";

        public MissionDungeon(Action<string> say, ManagerLoot.ManagerLoot loot, MissionReadiness readiness)
        {
            _say = say;
            _loot = loot;
            _readiness = readiness;
        }

        public void Start(Mission mission)
        {
            if (IsRunning)
                return;
            if (!Playfield.IsDungeon || DynelManager.LocalPlayer?.Room == null)
                return;
            _clearedRooms.Clear();
            _visitedRooms.Clear();
            _surveyedRooms.Clear();
            _edgeFailures.Clear();
            _reverseCooldown.Clear();
            _combatTarget = Identity.None;
            _scanTarget = Identity.None;
            _waitingForLoot = Identity.None;
            _loot.ResetMissionLootSkips();
            _mission = (Mission.List ?? new List<Mission>())
                .FirstOrDefault(x => mission != null && x.Identity == mission.Identity) ??
                (mission == null && Mission.List?.Count == 1 ? Mission.List[0] : null);
            _objectiveAttempted = false;
            _currentRoom = -1;
            _transition = null;
            _destination = null;
            _meshes = null;
            _layout = new DungeonLayout();
            _loot.MissionRoomContains = (dynel, roomId) =>
                _layout != null && _layout.ContainsDynel(roomId, dynel);
            _loot.MissionRoomDynels = _layout.VisibleRoomDynels;
            if (_layout.MissingConnections > 0)
                _say($"Mali map has no safe interior point for {_layout.MissingConnections} room connections; those routes are unavailable.");
            IsComplete = false;
            IsRunning = true;
            _readiness.Start();
            _loot.MissionActionsPaused = true;
            _roomQuietAt = DateTime.MinValue;
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
            _readiness.Stop();
            _loot.MissionActionsPaused = false;
            _loot.EndMissionRoom();
            _loot.MissionRoomContains = null;
            _loot.MissionRoomDynels = null;
            _destination = null;
            _transition = null;
            _layout = null;
            SMovementController.Halt();
        }

        // Front-half integration only: refresh AO#'s live quest pointer, or detach a removed quest.
        // The coordinator keeps its managed identity/objective history after removal.
        public void UpdateMissionBinding(Mission mission)
        {
            _mission = mission;
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
            if (!IsRunning || !Playfield.IsDungeon || DynelManager.LocalPlayer.Room == null)
                return;

            _readiness.ObserveCombat();
            _loot.MissionActionsPaused = _readiness.InCombat;
            // An established doorway crossing retains its existing ownership and
            // deadlines. Defer recovery until safe room arrival, observing aggro
            // throughout; every new room action still passes this gate.
            if (_transition == null && _readiness.Hold())
            {
                _loot.MissionActionsPaused = true;
                _destination = null;
                _roomQuietAt = DateTime.MinValue;
                if (_readiness.Failure != null)
                {
                    _say(_readiness.Failure);
                    Stop();
                }
                return;
            }
            if (_meshes == null) return;

            int floor = Math.Abs(DynelManager.LocalPlayer.Room.Floor);
            if (floor != _floor)
            {
                _floor = floor;
                LoadFloor();
                _roomQuietAt = DateTime.MinValue;
            }

            Room room = DynelManager.LocalPlayer.Room;
            if (_transition != null)
            {
                TickTransition(room);
                return; // Room selection, combat and loot cannot retarget a doorway crossing.
            }
            if (_currentRoom < 0)
            {
                _currentRoom = room.Instance;
                _visitedRooms.Add(room.Instance);
                _roomQuietAt = DateTime.MinValue;
            }
            else if (room.Instance != _currentRoom)
            {
                if (_observedRoom != room.Instance)
                {
                    _observedRoom = room.Instance;
                    _observedRoomAt = DateTime.UtcNow;
                }
                if (DateTime.UtcNow - _observedRoomAt < TimeSpan.FromSeconds(1))
                    return;
                if (!_layout.IsInside(room.Instance, DynelManager.LocalPlayer.Position, 0.5f))
                    return;
                _currentRoom = room.Instance;
                _visitedRooms.Add(room.Instance);
                _destination = null;
                _observedRoom = -1;
                _roomQuietAt = DateTime.MinValue;
                _say($"Confirmed room {_currentRoom} outside an active doorway crossing.");
                SMovementController.Halt();
            }
            else
            {
                _observedRoom = -1;
            }
            _loot.BeginMissionRoom(room.Instance);
            if (_surveyedRooms.Add(room.Instance))
            {
                var visible = _layout.VisibleRoomDynels(room.Instance).ToList();
                int characters = visible.Where(x => x.Identity.Type == IdentityType.SimpleChar)
                    .Select(x => new SimpleChar(x))
                    .Count(x => x.IsAlive && !x.IsPlayer &&
                        !(x.IsPet && x.PetOwnerId == DynelManager.LocalPlayer.Identity.Instance));
                int containers = visible.Count(x => x.Identity.Type == IdentityType.Container);
                int corpses = visible.Count(x => x.Identity.Type == IdentityType.Corpse);
                _say($"Mali room {room.Instance}: {characters} live enemy candidates, " +
                    $"{containers} containers, {corpses} corpses currently visible.");
            }
            if (_readiness.InCombat)
            {
                // Aggro outside the new-target range still prevents sitting,
                // looting, room clearance and initiating another fight.
                if (!FightInRoom(room)) SMovementController.Halt();
                _roomQuietAt = DateTime.MinValue;
                return;
            }
            if (HandleObjective(room, false) || FightInRoom(room) ||
                HandleObjective(room, true) || LootInRoom(room) || ScanRemainingRoom(room))
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
                if (_edgeFailures.Values.Any(x => !x.Permanent && x.Until > DateTime.UtcNow) ||
                    _reverseCooldown.Values.Any(x => x > DateTime.UtcNow))
                    return;
                if (_clearedRooms.Count < Playfield.Rooms.Count)
                {
                    Stop();
                    _say("No further reachable rooms; failed doors/routes or missing Mali room geometry remain.");
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
                if (_loot.SkippedMissionLootCount > 0)
                    _say($"{_loot.SkippedMissionLootCount} unreachable loot objects were skipped; see the loot logs.");
                return;
            }

            BeginTransition(room.Instance, next.Instance);
        }

        private IEnumerable<SimpleChar> EnemyCandidates(Room room)
        {
            SimpleChar player = DynelManager.LocalPlayer;
            var players = new HashSet<Identity>(DynelManager.Players.Select(x => x.Identity));
            var mappedCharacters = _layout.VisibleRoomDynels(room.Instance)
                .Where(x => x.Identity.Type == IdentityType.SimpleChar)
                .Select(x => new SimpleChar(x)).ToList();
            var hostileOwners = new HashSet<int>(mappedCharacters
                .Concat(DynelManager.NPCs)
                .Where(x => x.IsNpc && !x.IsPet && _layout.ContainsDynel(room.Instance, x))
                .Select(x => x.Identity.Instance));
            return mappedCharacters
                .Concat(DynelManager.NPCs)
                .GroupBy(x => x.Identity).Select(group => group.First())
                .Where(x =>
                {
                    if (!x.IsAlive || x.IsPlayer || players.Contains(x.Identity) ||
                        x.Identity == player.Identity ||
                        (x.IsPet && x.PetOwnerId == player.Identity.Instance))
                        return false;
                    bool attackingPlayer = x.IsAttacking &&
                        x.FightingTarget?.Identity == player.Identity;
                    bool alarmSentry = x.Name != null &&
                        x.Name.IndexOf("Alarm Sentry", StringComparison.OrdinalIgnoreCase) >= 0;
                    bool spawnedByEnemy = x.IsPet && hostileOwners.Contains(x.PetOwnerId);
                    bool inRoom = _layout.ContainsDynel(room.Instance, x);
                    bool near = x.DistanceFrom(player) <= EngagementRange;
                    bool currentFight = player.IsAttacking && player.FightingTarget?.Identity == x.Identity;
                    return currentFight || (!x.IsPet && inRoom) ||
                        (near && attackingPlayer) ||
                        (near && spawnedByEnemy) ||
                        (near && alarmSentry && (inRoom || x.Room == null));
                });
        }

        private bool FightInRoom(Room room)
        {
            SimpleChar player = DynelManager.LocalPlayer;
            SimpleChar enemy = EnemyCandidates(room)
                .Where(x => x.DistanceFrom(player) <= EngagementRange ||
                    (player.IsAttacking && player.FightingTarget?.Identity == x.Identity))
                .OrderByDescending(x => x.IsAttacking &&
                    x.FightingTarget?.Identity == player.Identity)
                .ThenBy(x => x.DistanceFrom(player))
                .FirstOrDefault();
            if (enemy == null)
            {
                _combatTarget = Identity.None;
                return false;
            }
            _scanTarget = Identity.None;
            _loot.MissionActionsPaused = true;

            if (_combatTarget != enemy.Identity)
            {
                _combatTarget = enemy.Identity;
                _actionAt = DateTime.UtcNow;
                _waitingForLoot = Identity.None;
                _destination = null;
                SMovementController.Halt();
                _say($"Targeting {enemy.Name} ({enemy.Identity}) in room {room.Instance}" +
                    $" at {enemy.DistanceFrom(player):0.0}m (new engagement range {EngagementRange:0}m)" +
                    (enemy.IsPet ? " (spawned entity)." : "."));
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
            else
                Navigate(enemy.Position);

            return true;
        }

        private bool ScanRemainingRoom(Room room)
        {
            // A range limit must not turn a large occupied room into a cleared
            // room. Move within its mapped outline; do not Target/Attack/send
            // pets until the ordinary engagement filter admits an enemy.
            var player = DynelManager.LocalPlayer;
            SimpleChar distant = EnemyCandidates(room).Where(x => !x.IsPet &&
                _layout.ContainsDynel(room.Instance, x) && x.DistanceFrom(player) > EngagementRange)
                .OrderBy(x => x.DistanceFrom(player)).FirstOrDefault();
            if (distant == null) { _scanTarget = Identity.None; return false; }
            DateTime now = DateTime.UtcNow;
            float distance = distant.DistanceFrom(player);
            if (_scanTarget != distant.Identity)
            {
                _scanTarget = distant.Identity;
                _scanStarted = _scanProgress = now;
                _scanDistance = distance;
                _destination = null;
                _say($"Room {room.Instance} still has an enemy {distance:0.0}m away; " +
                    "moving within the room before the 20m engagement check.");
            }
            if (distance + 0.5f < _scanDistance)
            {
                _scanDistance = distance;
                _scanProgress = now;
            }
            if (now - _scanProgress > TimeSpan.FromSeconds(10) || now - _scanStarted > TimeSpan.FromSeconds(30))
            {
                Stop();
                _say($"Room {room.Instance} scan could not reach engagement range; room is not marked cleared.");
                return true;
            }
            Vector3 point = player.Position + (distant.Position - player.Position) *
                ((distance - 18f) / distance);
            if (!_layout.IsInside(room.Instance, point, 0.5f)) point = distant.Position;
            Navigate(point);
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
            if (target == null || !_layout.ContainsDynel(room.Instance, target))
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
            // RKMission approaches and can defer an unreachable object.
            Dynel next = _loot.NextMissionLoot(room.Instance);
            DateTime now = DateTime.UtcNow;
            if (next != null && _waitingForLoot != next.Identity)
            {
                _waitingForLoot = next.Identity;
                _lootWaitStarted = now;
                _lootLastProgress = _lootWaitStarted;
                _lootBestDistance = next.DistanceFrom(DynelManager.LocalPlayer);
                _lootApproachRetries = 0;
                _lootApproachPoint = LootApproach(room, next.Position, false);
                _say($"Loot candidate {next.Identity.Type} {next.Identity} in room {room.Instance}; approaching Manager.Loot range.");
            }
            if (_loot.IsProcessingMissionLoot)
            {
                SMovementController.Halt();
                if (next != null && _loot.WaitingMissionLootIdentity == next.Identity &&
                    now - _lootWaitStarted > TimeSpan.FromSeconds(30))
                    SkipLoot(next, room.Instance, "Manager.Loot could not open it within 30 seconds");
                return true;
            }
            if (next == null)
            {
                _waitingForLoot = Identity.None;
                return false;
            }
            float distance = next.DistanceFrom(DynelManager.LocalPlayer);
            if (distance > 5.5f)
            {
                if (distance + 0.5f < _lootBestDistance)
                {
                    _lootBestDistance = distance;
                    _lootLastProgress = now;
                }
                bool stalled = now - _lootLastProgress > TimeSpan.FromSeconds(10) ||
                    (now - _lootWaitStarted > TimeSpan.FromSeconds(3) &&
                        !SMovementController.IsNavigating());
                if (stalled && _lootApproachRetries == 0)
                {
                    _lootApproachRetries = 1;
                    _lootApproachPoint = LootApproach(room, next.Position, true);
                    _lootLastProgress = now;
                    _destination = null;
                    _say($"Loot {next.Identity} is still {distance:0.0}m away; trying another approach.");
                }
                else if ((stalled && _lootApproachRetries > 0) ||
                    now - _lootWaitStarted > TimeSpan.FromSeconds(30))
                {
                    SkipLoot(next, room.Instance, $"path stayed blocked at {distance:0.0}m");
                    return true;
                }
                Navigate(_lootApproachPoint);
                return true;
            }
            SMovementController.Halt();
            if (now - _lootWaitStarted > TimeSpan.FromSeconds(30))
                SkipLoot(next, room.Instance, "Manager.Loot did not finish within 30 seconds");
            return true;
        }

        private Vector3 LootApproach(Room room, Vector3 target, bool alternate)
        {
            Vector3 player = DynelManager.LocalPlayer.Position;
            float dx = player.X - target.X, dz = player.Z - target.Z;
            float length = (float)Math.Sqrt(dx * dx + dz * dz);
            if (length < 0.1f) return target;
            dx = dx / length * 3.5f;
            dz = dz / length * 3.5f;
            var points = new[]
            {
                new Vector3(target.X + dx, target.Y, target.Z + dz),
                new Vector3(target.X - dz, target.Y, target.Z + dx),
                new Vector3(target.X + dz, target.Y, target.Z - dx),
                new Vector3(target.X - dx, target.Y, target.Z - dz)
            }.Where(point => _layout.IsInside(room.Instance, point, 0.4f)).ToList();
            return points.Count == 0 ? target : points[Math.Min(alternate ? 1 : 0, points.Count - 1)];
        }

        private void SkipLoot(Dynel loot, int roomId, string reason)
        {
            _loot.SkipUnreachableMissionLoot(loot.Identity);
            _say($"Skipping unreachable loot {loot.Identity} in room {roomId}: {reason}. Exploration continues.");
            _waitingForLoot = Identity.None;
            _destination = null;
            SMovementController.Halt();
        }
        private Room NextRoom(Room current)
        {
            if (_layout == null)
                return null;
            int? adjacentTarget = _layout.Neighbors(current.Instance)
                .Where(id => !_visitedRooms.Contains(id) && !IsUnavailable(current.Instance, id))
                .OrderBy(id => Vector3.Distance(_layout.Edge(current.Instance, id).Threshold,
                    DynelManager.LocalPlayer.Position))
                .Select(id => (int?)id)
                .FirstOrDefault();
            if (adjacentTarget.HasValue)
            {
                _say($"Choosing adjacent unvisited room {adjacentTarget} from {current.Instance}.");
                return _layout.Room(adjacentTarget.Value);
            }

            // Backtrack only when no usable adjacent room remains unexplored.
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
                if (index != current.Instance && !_visitedRooms.Contains(index))
                {
                    int goal = index;
                    while (parent[index] != current.Instance && parent[index] != -1)
                        index = parent[index];
                    _say($"No adjacent unvisited room from {current.Instance}; " +
                        $"routing through room {index} toward unvisited room {goal}.");
                    return _layout.Room(index);
                }

                // Prefer the shortest available chain of room connections. The
                // nearest doorway breaks ties between paths of equal depth.
                Vector3 entryPosition = index == current.Instance
                    ? DynelManager.LocalPlayer.Position
                    : _layout.Edge(parent[index], index).Interior;
                foreach (int adjacentCandidate in _layout.Neighbors(index)
                    .Where(id => !IsUnavailable(index, id))
                    .OrderBy(id => Vector3.Distance(_layout.Edge(index, id).Threshold,
                        entryPosition)))
                {
                    if (parent.ContainsKey(adjacentCandidate))
                        continue;
                    parent[adjacentCandidate] = index;
                    queue.Enqueue(adjacentCandidate);
                }
            }
            return null;
        }

        private void BeginTransition(int source, int target)
        {
            DungeonLayout.Connection edge = _layout.Edge(source, target);
            if (edge == null) return;
            Door door = _layout.DoorAt(edge);
            DateTime now = DateTime.UtcNow;
            _transition = new Transition
            {
                Edge = edge, Phase = TransitionPhase.ApproachDoor,
                Started = now, PhaseStarted = now, LastProgress = now,
                BestDistance = float.MaxValue
            };
            _loot.EndMissionRoom();
            _destination = null;
            _observedRoom = -1;
            _say($"Transition {source}->{target}: approach doorway at {edge.Threshold}, door {door?.Identity.ToString() ?? "none"}; interior {edge.Interior}.");
        }

        private void TickTransition(Room detectedRoom)
        {
            Transition crossing = _transition;
            DungeonLayout.Connection edge = crossing.Edge;
            Door door = _layout.DoorAt(edge);
            DateTime now = DateTime.UtcNow;
            Vector3 position = DynelManager.LocalPlayer.Position;
            bool targetDetected = detectedRoom.Instance == edge.Target;
            bool safelyInsideTarget = targetDetected &&
                Vector3.Distance(position, edge.Threshold) > 1.5f &&
                _layout.IsInside(edge.Target, position, 0.4f);
            if (targetDetected)
            {
                if (_observedRoom != edge.Target)
                {
                    _observedRoom = edge.Target;
                    _observedRoomAt = now;
                    _say($"Transition {edge.Source}->{edge.Target}: target room detected; confirming entry.");
                }
                if (safelyInsideTarget && now - _observedRoomAt >= TimeSpan.FromMilliseconds(500))
                {
                    ConfirmTransition();
                    return;
                }
                if (safelyInsideTarget)
                {
                    // Keep the current interior route active during the brief
                    // stability check; confirmation will stop it once complete.
                    return;
                }
            }
            else
                _observedRoom = -1;

            // Room detection can switch before the approach phase sees the door.
            // Once we are across its Mali boundary, never steer back to the threshold.
            if (crossing.Phase != TransitionPhase.CrossDoor &&
                detectedRoom.Instance == edge.Target &&
                _layout.IsInside(edge.Target, position, 0.2f))
            {
                _say($"Transition {edge.Source}->{edge.Target}: boundary crossed; moving into target interior.");
                crossing.Phase = TransitionPhase.CrossDoor;
                crossing.PhaseStarted = now;
                crossing.LastProgress = now;
                crossing.BestDistance = float.MaxValue;
                _destination = null;
                Navigate(edge.Interior);
                return;
            }

            if (now - crossing.Started > TimeSpan.FromSeconds(30))
            {
                FailTransition("entry was not confirmed within 30 seconds");
                return;
            }

            if (crossing.Phase == TransitionPhase.ApproachDoor)
            {
                if (Vector3.Distance(position, edge.Threshold) > 3.5f)
                {
                    Navigate(edge.Threshold);
                    return;
                }
                string liveDoorRange = door == null ? "none" :
                    $"{Vector3.Distance(position, door.Position):0.0}m";
                string aoDoorRange = door == null ? "none" :
                    $"{door.DistanceFrom(DynelManager.LocalPlayer):0.0}m";
                _say($"Transition {edge.Source}->{edge.Target}: door reached " +
                    $"(threshold {Vector3.Distance(position, edge.Threshold):0.0}m, " +
                    $"door {liveDoorRange}, AO# range {aoDoorRange}).");
                crossing.Phase = door != null && door.IsLocked && !door.IsOpen
                    ? TransitionPhase.ProbeDoor : TransitionPhase.OpenDoor;
                crossing.PhaseStarted = now;
                crossing.LastProgress = now;
                crossing.BestDistance = float.MaxValue;
                _destination = null;
                SMovementController.Halt();
                if (crossing.Phase == TransitionPhase.ProbeDoor)
                    _say($"Transition {edge.Source}->{edge.Target}: door flags say locked and closed; probing passage before lockpicking.");
            }

            if (crossing.Phase == TransitionPhase.ProbeDoor)
            {
                if (door == null || door.IsOpen || !door.IsLocked)
                {
                    crossing.Phase = TransitionPhase.OpenDoor;
                    crossing.PhaseStarted = now;
                    crossing.LastProgress = now;
                    crossing.BestDistance = float.MaxValue;
                    _destination = null;
                    SMovementController.Halt();
                }
                else if (now - crossing.PhaseStarted < TimeSpan.FromSeconds(3))
                {
                    Navigate(edge.Interior);
                    return;
                }
                else
                {
                    crossing.Phase = TransitionPhase.OpenDoor;
                    crossing.PhaseStarted = now;
                    crossing.LastProgress = now;
                    crossing.BestDistance = float.MaxValue;
                    _destination = null;
                    SMovementController.Halt();
                    _say($"Transition {edge.Source}->{edge.Target}: passage blocked; trying Lock Pick on door {door.Identity}.");
                }
            }

            if (crossing.Phase == TransitionPhase.OpenDoor)
            {
                if (door == null || door.IsOpen)
                {
                    _say($"Transition {edge.Source}->{edge.Target}: doorway open; crossing.");
                    crossing.Phase = TransitionPhase.CrossDoor;
                    crossing.PhaseStarted = now;
                    crossing.LastProgress = now;
                    crossing.BestDistance = float.MaxValue;
                    _destination = null;
                    Navigate(edge.Interior);
                    return;
                }
                float doorDistance = Vector3.Distance(position, door.Position);
                if (doorDistance > 4.5f)
                {
                    if (doorDistance + 0.5f < crossing.BestDistance)
                    {
                        crossing.BestDistance = doorDistance;
                        crossing.LastProgress = now;
                    }
                    if (!crossing.DoorApproachLogged)
                    {
                        crossing.DoorApproachLogged = true;
                        _say($"Transition {edge.Source}->{edge.Target}: closing {doorDistance:0.0}m to door {door.Identity} before interaction.");
                    }
                    if (now - crossing.LastProgress > TimeSpan.FromSeconds(6))
                    {
                        FailTransition($"could not reach door {door.Identity} for interaction ({doorDistance:0.0}m away)");
                        return;
                    }
                    Navigate(door.Position);
                    return;
                }
                if (now - crossing.LastAction < TimeSpan.FromSeconds(2))
                    return;
                if (++crossing.DoorAttempts > 5)
                {
                    FailTransition("door did not open after five attempts");
                    return;
                }
                if (door.IsLocked)
                {
                    if (!Inventory.Find("Lock Pick", out Item pick))
                    {
                        FailTransition("locked door requires a Lock Pick");
                        return;
                    }
                    pick.UseOn(door);
                    _say($"Transition {edge.Source}->{edge.Target}: lockpick attempt {crossing.DoorAttempts} on {door.Identity} (open={door.IsOpen}, locked={door.IsLocked}).");
                }
                else
                {
                    door.Use();
                    _say($"Transition {edge.Source}->{edge.Target}: open attempt {crossing.DoorAttempts}.");
                }
                crossing.LastAction = now;
                return;
            }

            Vector3 destination = crossing.PushingDeeper ? edge.DeepInterior : edge.Interior;
            float distance = Vector3.Distance(position, destination);
            if (distance + 0.5f < crossing.BestDistance)
            {
                crossing.BestDistance = distance;
                crossing.LastProgress = now;
            }
            bool arrivedWithoutEntry = detectedRoom.Instance == edge.Source &&
                (distance < 2.5f || (!SMovementController.IsNavigating() &&
                    now - crossing.PhaseStarted > TimeSpan.FromSeconds(2)));
            bool stalled = now - crossing.LastProgress > TimeSpan.FromSeconds(6);
            if (targetDetected)
            {
                // A target-room reading is progress, even if the player is still
                // near the threshold. Only push deeper if the interior route
                // actually ends or stops making progress before safe entry.
                if (!crossing.PushingDeeper &&
                    now - _observedRoomAt > TimeSpan.FromSeconds(3) &&
                    (stalled || !SMovementController.IsNavigating()))
                {
                    crossing.PushingDeeper = true;
                    crossing.LastProgress = now;
                    crossing.BestDistance = float.MaxValue;
                    _destination = null;
                    _say($"Transition {edge.Source}->{edge.Target}: interior route stalled before safe entry; moving farther inside.");
                }
                Navigate(crossing.PushingDeeper ? edge.DeepInterior : edge.Interior);
                return;
            }
            if (detectedRoom.Instance == edge.Source && (arrivedWithoutEntry || stalled) &&
                now - crossing.PhaseStarted > TimeSpan.FromSeconds(2))
            {
                if (++crossing.CrossingRetries > 3)
                {
                    FailTransition("crossing arrived or stalled without a confirmed room change");
                    return;
                }
                crossing.PushingDeeper = true;
                crossing.PhaseStarted = now;
                crossing.LastProgress = now;
                crossing.BestDistance = float.MaxValue;
                _destination = null;
                _say($"Transition {edge.Source}->{edge.Target}: still in room {detectedRoom.Instance}; pushing deeper (retry {crossing.CrossingRetries}/3).");
            }
            Navigate(crossing.PushingDeeper ? edge.DeepInterior : edge.Interior);
        }

        private void ConfirmTransition()
        {
            DungeonLayout.Connection edge = _transition.Edge;
            _currentRoom = edge.Target;
            _visitedRooms.Add(edge.Target);
            _edgeFailures.Remove(EdgeKey(edge.Source, edge.Target));
            _reverseCooldown[EdgeKey(edge.Source, edge.Target)] = DateTime.UtcNow.AddSeconds(8);
            _say($"Transition {edge.Source}->{edge.Target}: confirmed in target room; reverse edge on 8-second cooldown.");
            _transition = null;
            _destination = null;
            _observedRoom = -1;
            _roomQuietAt = DateTime.MinValue;
            SMovementController.Halt();
        }

        private void FailTransition(string reason)
        {
            DungeonLayout.Connection edge = _transition.Edge;
            string key = EdgeKey(edge.Source, edge.Target);
            if (!_edgeFailures.TryGetValue(key, out EdgeFailure failure))
                _edgeFailures[key] = failure = new EdgeFailure();
            failure.Count++;
            failure.Permanent = failure.Count >= 3;
            failure.Until = DateTime.UtcNow.AddSeconds(failure.Count == 1 ? 30 : 90);
            _say($"Transition {edge.Source}->{edge.Target}: {reason}; failure {failure.Count}/3, " +
                (failure.Permanent ? "edge blocked for this run." :
                    $"edge blacklisted until {failure.Until:HH:mm:ss} UTC; selecting the next closest reachable room."));
            _transition = null;
            _destination = null;
            _observedRoom = -1;
            _roomQuietAt = DateTime.MinValue;
            SMovementController.Halt();
        }

        private bool IsUnavailable(int source, int target)
        {
            string key = EdgeKey(source, target);
            return (_edgeFailures.TryGetValue(key, out EdgeFailure failure) &&
                (failure.Permanent || failure.Until > DateTime.UtcNow)) ||
                (_reverseCooldown.TryGetValue(key, out DateTime until) && until > DateTime.UtcNow);
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
