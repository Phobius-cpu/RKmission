using System;
using System.Collections.Generic;
using System.Linq;
using AOSharp.Common.GameData;
using AOSharp.Common.Unmanaged.Imports;
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
        private readonly InventoryPolicy _inventory;
        private readonly CombatDriver _combat;
        private readonly DungeonLiftController _lifts = new DungeonLiftController();
        private MovementOwner _requestedOwner = MovementOwner.DungeonRoom;
        private const float EngagementRange = 20f;
        private const float DoorUseRange = 2f;
        private const int ReverseEdgeCooldownSeconds = 1;
        private readonly HashSet<int> _clearedRooms = new HashSet<int>();
        private readonly HashSet<int> _visitedRooms = new HashSet<int>();
        private readonly HashSet<int> _surveyedRooms = new HashSet<int>();
        private readonly HashSet<int> _enemyOwners = new HashSet<int>();
        private readonly Dictionary<string, EdgeFailure> _edgeFailures = new Dictionary<string, EdgeFailure>();
        private readonly Dictionary<string, DateTime> _reverseCooldown = new Dictionary<string, DateTime>();
        private NavMesh[] _meshes;
        private DungeonLayout _layout;
        private Identity _scanTarget = Identity.None;
        private DateTime _scanStarted, _scanProgress;
        private readonly List<Vector3> _scanWaypoints = new List<Vector3>();
        private int _scanWaypointIndex;
        private float _scanWaypointBestDistance;
        private Vector3 _scanEnemyPosition;
        private Identity _combatApproachTarget = Identity.None;
        private readonly List<Vector3> _combatApproaches = new List<Vector3>();
        private int _combatApproachIndex;
        private DateTime _combatApproachProgress;
        private float _combatApproachBestDistance;
        private Vector3 _combatApproachEnemyPosition;
        private AcceptedMission _record;
        private MissionObjective _objective;
        private bool _exiting;
        private int _entryRoom;
        private Vector3 _entryPosition, _exitThreshold, _exitAcross;
        private Identity _exitDoor = Identity.None;
        private DateTime _exitStarted, _exitCrossingStarted, _exitLastUse;
        private int _currentRoom = -1;
        private Transition _transition;
        private Vector3? _destination;
        private DateTime _roomQuietAt;
        private DateTime _observedRoomAt;
        private int _observedRoom = -1;
        private DateTime _lootWaitStarted;
        private DateTime _lootLastProgress;
        private Identity _waitingForLoot = Identity.None;
        private readonly List<Vector3> _lootApproachPoints = new List<Vector3>();
        private int _lootApproachIndex;
        private Vector3 _lootLastPosition;
        private DateTime _lootNearStarted;
        private bool _lootProbeNextSide;
        private int _floor;
        private DungeonLiftController.Lift _activeLift;
        private DateTime _liftStarted;

        private enum TransitionPhase { ApproachDoor, ProbeDoor, OpenDoor, CrossDoor }
        private sealed class Transition
        {
            public DungeonLayout.Connection Edge;
            public TransitionPhase Phase;
            public DateTime Started, PhaseStarted, LastAction, LastProgress, LastApproachCommand;
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
        public MissionObjective Objective => _objective;
        public int EntryRoom => _entryRoom;
        public int Floor => _floor;
        public bool IsExiting => _exiting;
        public string Status => _exiting ? "returning to exit" : IsComplete ? "complete" : !IsRunning ? "idle" : _readiness.IsWaiting ? _readiness.Status :
            $"visited {_visitedRooms.Count}, cleared {_clearedRooms.Count} rooms";

        public MissionDungeon(Action<string> say, ManagerLoot.ManagerLoot loot, MissionReadiness readiness, InventoryPolicy inventory)
        {
            _say = say;
            _loot = loot;
            _readiness = readiness;
            _inventory = inventory;
            _combat = new CombatDriver(say);
        }

        public void Start(AcceptedMission record)
        {
            if (IsRunning)
                return;
            if (!Playfield.IsDungeon || DynelManager.LocalPlayer?.Room == null)
                return;
            MovementArbiter.Current.StopAll();
            _clearedRooms.Clear();
            _visitedRooms.Clear();
            _surveyedRooms.Clear();
            _enemyOwners.Clear();
            _edgeFailures.Clear();
            _reverseCooldown.Clear();
            _combat.Reset();
            ResetCombatApproach();
            _scanTarget = Identity.None;
            _scanWaypoints.Clear();
            _waitingForLoot = Identity.None;
            _loot.ResetMissionLootSkips();
            _loot.IgnoreOrdinaryMissionLoot = false;
            _lifts.Reset();
            _activeLift = null;
            _liftStarted = DateTime.MinValue;
            _record = record;
            _objective = new MissionObjective(record, _say);
            _exiting = false;
            _entryRoom = DynelManager.LocalPlayer.Room.Instance;
            _entryPosition = DynelManager.LocalPlayer.Position;
            if (record.DungeonEntryPosition.HasValue)
            { _entryRoom = record.DungeonEntryRoom; _entryPosition = record.DungeonEntryPosition.Value; }
            else
            { record.DungeonEntryRoom = _entryRoom; record.DungeonEntryPosition = _entryPosition; }
            _exitDoor = Identity.None;
            _currentRoom = -1;
            _transition = null;
            _destination = null;
            _meshes = null;
            _layout = new DungeonLayout();
            if (!record.DungeonEntryPosition.HasValue ||
                !_layout.TryExit(_entryRoom, _entryPosition, out _, out _, out _))
            {
                if (_layout.TryEntryFromInside(_entryPosition, out int entryRoom, out Vector3 entryApproach))
                {
                    _entryRoom = entryRoom;
                    _entryPosition = entryApproach;
                    record.DungeonEntryRoom = entryRoom;
                    record.DungeonEntryPosition = entryApproach;
                }
            }
            _loot.MissionRoomContains = (dynel, roomId) =>
                _layout != null && _layout.ContainsDynel(roomId, dynel);
            _loot.MissionRoomDynels = _layout.VisibleRoomDynels;
            _loot.MissionLootReserved = _objective.IsObjective;
            _loot.MissionLootAllowed = dynel => !_exiting && _currentRoom >= 0 &&
                !_objective.HoldLoot(dynel) && !_readiness.InCombat &&
                (!_objective.IsObjective(dynel.Identity) || _loot.MissionObjectiveContainer == dynel.Identity);
            _loot.MissionItemProtected = item => _record.Actions != null && _record.Actions.Any(action =>
                (action is UseItemOnItemAction use && item.UniqueIdentity == use.Source) ||
                (action is FindItemAction find && item.UniqueIdentity == find.Target));
            _objective.Refresh(_layout);
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
            _loot.MissionLootAllowed = null;
            _loot.MissionLootReserved = null;
            _loot.MissionItemProtected = null;
            _loot.IgnoreOrdinaryMissionLoot = false;
            _loot.MissionObjectiveContainer = Identity.None;
            _destination = null;
            _transition = null;
            _activeLift = null;
            _layout = null;
            MovementArbiter.Current.StopAll();
        }

        public void Dispose()
        {
            Stop();
        }

        public void InvalidatePath()
        {
            _destination = null;
            if (_transition != null)
            {
                _transition.LastProgress = DateTime.UtcNow;
                _transition.BestDistance = float.MaxValue;
            }
        }

        public void RecoverFromStuck(int tier)
        {
            if (!IsRunning) return;
            if (tier >= 3 && _transition != null)
            {
                FailTransition("repeated stuck/rubberband events during doorway crossing");
                return;
            }
            if (tier >= 2)
                MovementArbiter.Current.Halt(_requestedOwner);
            InvalidatePath();
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
            _requestedOwner = MovementOwner.DungeonRoom;

            _readiness.ObserveCombat();
            _inventory.Update(_say);
            _loot.IgnoreOrdinaryMissionLoot = _inventory.SkipOptionalLoot;
            _objective.Refresh(_layout);
            if (_objective.Failure != null) { Stop(); _say(_objective.Failure); return; }
            _objective.ObserveAcknowledgement();
            ReopenOccupiedRooms();
            _objective.FinalActionsAllowed = _clearedRooms.Count == Playfield.Rooms.Count &&
                !Playfield.Rooms.Any(x => EnemyCandidates(x).Any(enemy => !_objective.IsObjective(enemy.Identity))) &&
                _loot.UnfinishedMissionLootCount == 0;
            if (GuardReservedEnemy()) return;
            _loot.MissionActionsPaused = _readiness.InCombat;
            // An established doorway crossing retains its existing ownership and
            // deadlines. Defer recovery until safe room arrival, observing aggro
            // throughout; every new room action still passes this gate.
            if (_transition == null && HoldForReadiness()) return;
            if (_meshes == null) return;

            int floor = Math.Abs(DynelManager.LocalPlayer.Room.Floor);
            if (floor != _floor)
            {
                MovementArbiter.Current.StopAll();
                _floor = floor;
                LoadFloor();
                _activeLift = null;
                _liftStarted = DateTime.MinValue;
                _currentRoom = -1;
                _destination = null;
                _roomQuietAt = DateTime.MinValue;
            }

            Room room = DynelManager.LocalPlayer.Room;
            _lifts.Discover();
            if (_transition != null)
            {
                int arrivalRoom = _transition.Edge.Target;
                TickTransition(room);
                // Keep exclusive ownership until entry is confirmed. A cleared
                // arrival can route straight onward in this same update, but
                // recovery still gets its stationary window when needed.
                if (_transition != null || !IsRunning || _currentRoom != arrivalRoom) return;
                if (HoldForReadiness()) return;
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
                if (!_clearedRooms.Contains(_currentRoom)) MovementArbiter.Current.Halt(_requestedOwner);
            }
            else
            {
                _observedRoom = -1;
            }
            if (_activeLift != null) { TickLift(room); return; }
            if (_exiting) { TickExit(room); return; }
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
                if (!FightInRoom(room))
                {
                    MovementArbiter.Current.Halt(_requestedOwner);
                    if (_objective.Finale && _objective.FinalActionsAllowed)
                        TickObjective(room);
                }
                _roomQuietAt = DateTime.MinValue;
                return;
            }
            bool roomAction = FightInRoom(room) || ScanRemainingRoom(room) ||
                (_objective.Finale && TickObjective(room)) || LootInRoom(room);
            if (_objective.Failure != null) { Stop(); _say(_objective.Failure); return; }
            if (roomAction)
            {
                _roomQuietAt = DateTime.MinValue;
                return;
            }

            // Rechecking live contents above is cheap. Only a room that has not
            // been cleared needs another stationary clearance window.
            if (!_clearedRooms.Contains(room.Instance))
            {
                if (_roomQuietAt == DateTime.MinValue)
                {
                    _roomQuietAt = DateTime.UtcNow;
                    return;
                }
                if (DateTime.UtcNow - _roomQuietAt < TimeSpan.FromSeconds(2))
                    return;
            }

            _clearedRooms.Add(room.Instance);
            if (_objective.Failure != null) { Stop(); _say(_objective.Failure); return; }
            Room next = NextRoom(room);
            if (next == null)
            {
                int floorRooms = Playfield.Rooms.Count(x => Math.Abs(x.Floor) == _floor);
                int clearedFloorRooms = _clearedRooms.Count(id =>
                    _layout.Room(id) != null && Math.Abs(_layout.Room(id).Floor) == _floor);
                if (_floor < Playfield.NumFloors - 1 && clearedFloorRooms == floorRooms)
                {
                    if (!_lifts.TryGet(_floor, true, out _activeLift))
                    { Stop(); _say($"Floor {_floor} is clear, but no forward lift was discovered."); return; }
                    _liftStarted = DateTime.UtcNow;
                    _say($"Floor {_floor} is clear; routing through mapped rooms to its forward lift.");
                    return;
                }
                if (_clearedRooms.Count < Playfield.Rooms.Count)
                {
                    MovementArbiter.Current.Halt(_requestedOwner);
                    if (WaitingForRoute) return;
                    Stop();
                    _say("No further reachable rooms; failed doors/routes or missing Mali room geometry remain.");
                    return;
                }
                if (_loot.UnfinishedMissionLootCount > 0)
                {
                    string blockers = _loot.MissionLootBlockers;
                    Stop();
                    _say($"Ordinary loot remains unfinished; objective completion and automatic exit are held. {blockers}. Check loot logs, rules and free space.");
                    return;
                }
                if (!_objective.HasSteps)
                { Stop(); _say("No supported mission objective metadata is available; automatic completion is held."); return; }
                if (!_objective.Finale)
                {
                    MovementArbiter.Current.Halt(_requestedOwner);
                    if (_loot.SkippedMissionLootCount > 0)
                        _say($"{_loot.SkippedMissionLootCount} ordinary loot source(s) skipped this mission; continuing to the reserved objective.");
                    if (_loot.ReservedPendingMissionLootCount > 0)
                        _say($"{_loot.ReservedPendingMissionLootCount} reserved objective loot entries are final work, excluded from the ordinary-loot gate.");
                    _objective.BeginFinale();
                    _roomQuietAt = DateTime.MinValue;
                    return;
                }
                if (!_objective.RewardConfirmed && !_objective.CollectedReturnItem)
                {
                    int? objectiveRoom = _objective.PendingRoom;
                    if (objectiveRoom.HasValue && objectiveRoom.Value != room.Instance)
                    {
                        if (RouteToRoom(objectiveRoom.Value)) return;
                        MovementArbiter.Current.Halt(_requestedOwner);
                        if (WaitingForRoute) return;
                        Stop();
                        _say("No mapped route to the remaining objective room; automatic completion is held.");
                        return;
                    }
                    MovementArbiter.Current.Halt(_requestedOwner);
                    TickObjective(room);
                    if (_objective.Failure != null) { Stop(); _say(_objective.Failure); }
                    return;
                }
                Room occupied = Playfield.Rooms.FirstOrDefault(x => EnemiesInRoom(x).Any());
                if (occupied != null)
                {
                    if (RouteToRoom(occupied.Instance)) return;
                    if (occupied.Instance != room.Instance)
                    {
                        MovementArbiter.Current.Halt(_requestedOwner);
                        if (WaitingForRoute) return;
                        Stop();
                        _say("A remaining enemy has no available room route; automatic exit is held.");
                    }
                    return;
                }
                IsComplete = _objective.RewardConfirmed || _objective.CollectedReturnItem;
                _record.RoomsCleared = true;
                _record.ReturnHandInPending = _objective.CollectedReturnItem && !_objective.RewardConfirmed;
                BeginExit();
                return;
            }

            BeginTransition(room.Instance, next.Instance);
        }

        private bool TickObjective(Room room)
        {
            _requestedOwner = MovementOwner.Objective;
            return _objective.Tick(room, _layout, Navigate, _loot);
        }

        private bool HoldForReadiness()
        {
            if (!_readiness.Hold()) return false;
            _combat.Pause();
            _loot.MissionActionsPaused = true;
            _destination = null;
            _roomQuietAt = DateTime.MinValue;
            if (_readiness.Failure != null)
            {
                _say(_readiness.Failure);
                Stop();
            }
            return true;
        }

        private IEnumerable<SimpleChar> EnemyCandidates(Room room)
        {
            SimpleChar player = DynelManager.LocalPlayer;
            var players = new HashSet<Identity>(DynelManager.Players.Select(x => x.Identity));
            var mappedCharacters = _layout.VisibleRoomDynels(room.Instance)
                .Where(x => x.Identity.Type == IdentityType.SimpleChar)
                .Select(x => new SimpleChar(x)).ToList();
            var corpseIds = new HashSet<int>(DynelManager.Corpses.Select(x => x.Identity.Instance));
            var hostileOwners = new HashSet<int>(mappedCharacters
                .Concat(DynelManager.NPCs)
                .Where(x => x.IsNpc && !x.IsPet && _layout.ContainsDynel(room.Instance, x))
                .Select(x => x.Identity.Instance));
            _enemyOwners.UnionWith(hostileOwners);
            return mappedCharacters
                .Concat(DynelManager.NPCs)
                .GroupBy(x => x.Identity).Select(group => group.First())
                .Where(x =>
                {
                    if (!x.IsAlive || corpseIds.Contains(x.Identity.Instance) ||
                        x.IsPlayer || players.Contains(x.Identity) ||
                        x.Identity == player.Identity ||
                        _objective.HoldEnemy(x.Identity) ||
                        (x.IsPet && players.Any(id => id.Instance == x.PetOwnerId)) ||
                        (x.IsPet && x.PetOwnerId == player.Identity.Instance))
                        return false;
                    bool attackingPlayer = x.IsAttacking &&
                        x.FightingTarget?.Identity == player.Identity;
                    bool alarmSentry = x.Name != null &&
                        x.Name.IndexOf("Alarm Sentry", StringComparison.OrdinalIgnoreCase) >= 0;
                    bool spawnedByEnemy = x.IsPet && _enemyOwners.Contains(x.PetOwnerId);
                    bool inRoom = _layout.ContainsDynel(room.Instance, x);
                    bool near = x.DistanceFrom(player) <= EngagementRange;
                    bool currentFight = player.IsAttacking && player.FightingTarget?.Identity == x.Identity;
                    return currentFight || (!x.IsPet && inRoom) ||
                        (near && attackingPlayer) ||
                        (spawnedByEnemy && (inRoom || near)) ||
                        (alarmSentry && (inRoom || (near && x.Room == null)));
                });
        }

        // Combat candidates also include our current target and nearby
        // attackers outside this room. Clearance and routing need membership.
        private IEnumerable<SimpleChar> EnemiesInRoom(Room room) =>
            EnemyCandidates(room).Where(enemy => _layout.ContainsDynel(room.Instance, enemy));

        private bool FightInRoom(Room room)
        {
            _requestedOwner = MovementOwner.CombatPosition;
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
                _combat.Reset();
                ResetCombatApproach();
                return false;
            }
            _scanTarget = Identity.None;
            _scanWaypoints.Clear();
            _loot.MissionActionsPaused = true;
            _objective.ArmKill(enemy);

            if (enemy.IsInLineOfSight && enemy.IsInAttackRange(true))
                ResetCombatApproach();

            if (!_combat.Tick(enemy, room.Instance, target => ApproachCombatEnemy(target, room.Instance), () =>
                { _waitingForLoot = Identity.None; ResetCombatApproach(); }, EngagementRange))
                Stop();

            return true;
        }

        private void ResetCombatApproach()
        {
            _combatApproachTarget = Identity.None;
            _combatApproaches.Clear();
            _combatApproachIndex = 0;
            _destination = null;
        }

        private bool ApproachCombatEnemy(SimpleChar enemy, int roomId)
        {
            if (!_layout.ContainsDynel(roomId, enemy))
            {
                // Preserve the existing defensive pursuit of an active attacker
                // just outside this room; mapped alternatives apply in-room.
                Navigate(enemy.Position);
                return true;
            }
            DateTime now = DateTime.UtcNow;
            SimpleChar player = DynelManager.LocalPlayer;
            if (_combatApproachTarget != enemy.Identity ||
                Vector3.Distance(_combatApproachEnemyPosition, enemy.Position) > 3f)
            {
                _combatApproachTarget = enemy.Identity;
                _combatApproachEnemyPosition = enemy.Position;
                _combatApproaches.Clear();
                _combatApproachIndex = 0;
                Vector3 toward = player.Position - enemy.Position;
                toward.Y = 0;
                toward = toward.Magnitude > 0.1 ? toward.Normalize() : Vector3.Forward;
                Vector3 side = new Vector3(-toward.Z, 0, toward.X);
                foreach (float radius in new[] { 2f, 4f })
                    foreach (Vector3 direction in new[] { toward, side, -side, -toward })
                        AddCombatApproach(roomId, enemy.Position + direction * radius);
                AddCombatApproach(roomId, enemy.Position);
                if (_combatApproaches.Count == 0 && enemy.Room?.Instance == roomId)
                    _combatApproaches.Add(enemy.Position); // Preserve AO's room assignment when Mali geometry is incomplete.
                _destination = null;
                _say($"Combat approach to {enemy.Identity} in room {roomId}: " +
                    $"{_combatApproaches.Count} mapped point(s), distance={enemy.DistanceFrom(player):0.0}m.");
            }
            if (_combatApproachIndex >= _combatApproaches.Count) return false;
            Vector3 point = _combatApproaches[_combatApproachIndex];
            float distance = Vector3.Distance(player.Position, point);
            if (distance + 0.3f < _combatApproachBestDistance)
            {
                _combatApproachBestDistance = distance;
                _combatApproachProgress = now;
            }
            if (_destination.HasValue &&
                (distance < 1.1f || now - _combatApproachProgress > TimeSpan.FromSeconds(7) ||
                 (!SMovementController.IsNavigating() && now - _combatApproachProgress > TimeSpan.FromSeconds(2))))
            {
                _combatApproachIndex++;
                _destination = null;
            }
            while (!_destination.HasValue && _combatApproachIndex < _combatApproaches.Count)
            {
                point = _combatApproaches[_combatApproachIndex];
                _combatApproachBestDistance = Vector3.Distance(player.Position, point);
                _combatApproachProgress = now;
                if (MovementArbiter.Current.SetNavDestination(_requestedOwner, point))
                {
                    _destination = point;
                    _say($"Combat approach {_combatApproachIndex + 1}/{_combatApproaches.Count} to {enemy.Identity}: {point}.");
                    break;
                }
                _say($"Combat approach {_combatApproachIndex + 1} to {enemy.Identity} could not claim navigation " +
                    $"(owner={MovementArbiter.Current.Owner}, controller loaded={SMovementController.IsLoaded()}).");
                _combatApproachIndex++;
            }
            return _destination.HasValue;
        }

        private void AddCombatApproach(int roomId, Vector3 point)
        {
            if (_layout.IsInside(roomId, point, 0.2f) &&
                !_combatApproaches.Any(existing => Vector3.Distance(existing, point) < 1f))
                _combatApproaches.Add(point);
        }

        private bool ScanRemainingRoom(Room room)
        {
            _requestedOwner = MovementOwner.DungeonRoom;
            // A range limit must not turn a large occupied room into a cleared
            // room. Move within its mapped outline; do not Target/Attack/send
            // pets until the ordinary engagement filter admits an enemy.
            var player = DynelManager.LocalPlayer;
            SimpleChar distant = EnemyCandidates(room).Where(x =>
                _layout.ContainsDynel(room.Instance, x) && x.DistanceFrom(player) > EngagementRange)
                .OrderBy(x => x.DistanceFrom(player)).FirstOrDefault();
            if (distant == null) { _scanTarget = Identity.None; _scanWaypoints.Clear(); return false; }
            DateTime now = DateTime.UtcNow;
            float distance = distant.DistanceFrom(player);
            bool newTarget = _scanTarget != distant.Identity;
            if (newTarget || Vector3.Distance(_scanEnemyPosition, distant.Position) > 5f)
            {
                if (_scanTarget == Identity.None) _scanStarted = now;
                _scanTarget = distant.Identity;
                _scanProgress = now;
                _scanEnemyPosition = distant.Position;
                _scanWaypoints.Clear();
                _scanWaypointIndex = 0;
                AddScanWaypoint(room.Instance, player.Position + (distant.Position - player.Position) *
                    ((distance - 18f) / distance));
                Vector3 toward = player.Position - distant.Position;
                toward.Y = 0;
                if (toward.Magnitude > 0.1)
                {
                    toward = toward.Normalize();
                    Vector3 side = new Vector3(-toward.Z, 0, toward.X);
                    foreach (float radius in new[] { 14f, 8f })
                        foreach (Vector3 direction in new[] { toward, side, -side, -toward })
                            AddScanWaypoint(room.Instance, distant.Position + direction * radius);
                }
                AddScanWaypoint(room.Instance, distant.Position);
                _destination = null;
                _say($"Room {room.Instance} still has an enemy {distance:0.0}m away; " +
                    $"trying {_scanWaypoints.Count} mapped approach point(s) before the 20m engagement check.");
            }
            if (now - _scanStarted > TimeSpan.FromSeconds(60) || _scanWaypointIndex >= _scanWaypoints.Count)
            {
                int tried = Math.Min(_scanWaypoints.Count, _scanWaypointIndex + (_destination.HasValue ? 1 : 0));
                Stop();
                _say($"Room {room.Instance} scan could not reach enemy {distant.Identity} within 20m " +
                    $"(remaining {distance:0.0}m, tried {tried}/{_scanWaypoints.Count} mapped points, " +
                    $"player={player.Position}, enemy={distant.Position}); room is not marked cleared.");
                return true;
            }
            Vector3 point = _scanWaypoints[_scanWaypointIndex];
            float waypointDistance = Vector3.Distance(player.Position, point);
            if (waypointDistance + 0.5f < _scanWaypointBestDistance)
            {
                _scanWaypointBestDistance = waypointDistance;
                _scanProgress = now;
            }
            if (_destination.HasValue &&
                (waypointDistance < 1.5f || now - _scanProgress > TimeSpan.FromSeconds(10) ||
                 (!SMovementController.IsNavigating() && now - _scanProgress > TimeSpan.FromSeconds(2))))
            {
                string reason = waypointDistance < 1.5f ? "reached without engagement" :
                    !SMovementController.IsNavigating() ? "navigation stopped" : "no route progress";
                _say($"Room {room.Instance} scan approach {_scanWaypointIndex + 1} {reason}; trying another mapped point.");
                _scanWaypointIndex++;
                _destination = null;
                if (_scanWaypointIndex >= _scanWaypoints.Count) return true;
                point = _scanWaypoints[_scanWaypointIndex];
            }
            if (!_destination.HasValue)
            {
                while (_scanWaypointIndex < _scanWaypoints.Count)
                {
                    point = _scanWaypoints[_scanWaypointIndex];
                    _scanWaypointBestDistance = Vector3.Distance(player.Position, point);
                    _scanProgress = now;
                    if (MovementArbiter.Current.SetNavDestination(_requestedOwner, point))
                    {
                        _destination = point;
                        _say($"Room {room.Instance} scan approach {_scanWaypointIndex + 1}/{_scanWaypoints.Count}: " +
                            $"{point}, enemy {distance:0.0}m away.");
                        break;
                    }
                    _say($"Room {room.Instance} scan approach {_scanWaypointIndex + 1} could not claim navigation " +
                        $"(owner={MovementArbiter.Current.Owner}, controller loaded={SMovementController.IsLoaded()}).");
                    _scanWaypointIndex++;
                }
            }
            return true;
        }

        private void AddScanWaypoint(int roomId, Vector3 point)
        {
            if (_layout.IsInside(roomId, point, 0.5f) &&
                !_scanWaypoints.Any(existing => Vector3.Distance(existing, point) < 2f))
                _scanWaypoints.Add(point);
        }

        private bool GuardReservedEnemy()
        {
            var player = DynelManager.LocalPlayer;
            if (_record.State != MissionProgress.CompletedByUser && !(_objective.Finale && _objective.FinalActionsAllowed) &&
                Targeting.Target != null && _objective.IsObjective(Targeting.Target.Identity))
                Targeting.SelectSelf();
            var owned = new HashSet<Identity>(player.Pets.Where(x => x.Character != null).Select(x => x.Character.Identity)) { player.Identity };
            bool attackingReserved = ((player.IsAttacking || player.IsAttackPending) && player.FightingTarget?.IsAlive == true &&
                _objective.HoldEnemy(player.FightingTarget.Identity)) ||
                player.Pets.Any(x => x.Character?.IsAttacking == true && x.Character.FightingTarget?.IsAlive == true &&
                    _objective.HoldEnemy(x.Character.FightingTarget.Identity));
            bool reservedAggro = !(_objective.Finale && _objective.FinalActionsAllowed) && DynelManager.NPCs.Any(x => x.IsAlive && _objective.HoldEnemy(x.Identity) &&
                x.IsAttacking && owned.Contains(x.FightingTarget?.Identity ?? Identity.None));
            if (attackingReserved || reservedAggro)
            {
                player.StopAttack(); // Includes pet follow; don't finish a kill/observation early.
                Targeting.SelectSelf();
                Stop();
                _say("Reserved objective enemy engaged before the other enemies were cleared. Attacks stopped; resolve its aggro before resuming. Objective order was not waived.");
                return true;
            }
            return false;
        }

        private void ReopenOccupiedRooms()
        {
            if (_layout == null || _exiting) return;
            foreach (int id in _clearedRooms.ToList())
            {
                Room room = _layout.Room(id);
                if (room == null) continue;
                SimpleChar enemy = EnemiesInRoom(room).FirstOrDefault(x => !_objective.IsObjective(x.Identity));
                Dynel pendingLoot = _loot.NextMissionLoot(id);
                bool unfinishedLoot = pendingLoot != null && !_objective.IsObjective(pendingLoot.Identity);
                if (enemy == null && !unfinishedLoot) continue;
                _clearedRooms.Remove(id);
                _say($"Reopening cleared room {id}: " + (enemy != null
                    ? $"live enemy {enemy.Identity} is inside this room."
                    : $"unfinished loot {pendingLoot.Identity} is inside this room."));
            }
        }

        public void BeginExit()
        {
            _requestedOwner = MovementOwner.DungeonExit;
            if (!IsRunning || _layout == null || _exiting) return;
            if (_transition != null) MovementArbiter.Current.Release(MovementOwner.DoorTransition);
            _exiting = true;
            _exitStarted = DateTime.UtcNow;
            _exitCrossingStarted = _exitLastUse = DateTime.MinValue;
            _transition = null;
            _destination = null;
            _loot.MissionActionsPaused = true;
            _loot.EndMissionRoom();
            _say("Returning through mapped rooms to the entry door; waiting for actual outdoor zoning before mission chaining.");
        }

        // All arbitrary room routes use the same failed-edge-aware graph and
        // enter the existing stateful DoorTransition for each single hop.
        public bool RouteToRoom(int targetRoom)
        {
            if (!IsRunning || _layout == null || _transition != null || _currentRoom < 0)
                return false;
            if (_currentRoom == targetRoom) return true;
            Room hop = RouteTo(_currentRoom, id => id == targetRoom, false);
            if (hop == null) return false;
            BeginTransition(_currentRoom, hop.Instance);
            return true;
        }

        private void TickLift(Room room)
        {
            if (_readiness.InCombat || EnemyCandidates(room).Any())
            {
                _activeLift = null;
                _clearedRooms.Remove(room.Instance);
                if (_exiting)
                { _exiting = false; IsComplete = false; _record.RoomsCleared = false; }
                return;
            }
            if (DateTime.UtcNow - _liftStarted > TimeSpan.FromSeconds(90))
            { Stop(); _say("Lift route or floor transition timed out; run held."); return; }
            if (room.Instance != _activeLift.RoomId)
            {
                if (!RouteToRoom(_activeLift.RoomId) && !WaitingForRoute)
                { Stop(); _say("No mapped route to the discovered lift."); }
                return;
            }
            _requestedOwner = MovementOwner.LiftTransition;
            if (Vector3.Distance(DynelManager.LocalPlayer.Position, _activeLift.Position) > 1.5f)
            { Navigate(_activeLift.Position); return; }
            MovementArbiter.Current.Halt(_requestedOwner);
            _lifts.Use(_activeLift);
        }

        private void TickExit(Room room)
        {
            _requestedOwner = MovementOwner.DungeonExit;
            _loot.MissionActionsPaused = true;
            DateTime now = DateTime.UtcNow;
            if (now - _exitStarted > TimeSpan.FromMinutes(5))
            { Stop(); _say("Automatic exit route exceeded five minutes; exit manually to continue the local chain."); return; }
            if (_floor > 0)
            {
                if (!_lifts.TryGet(_floor, false, out _activeLift))
                { Stop(); _say($"Backward lift on floor {_floor} was not discovered; exit held."); return; }
                _liftStarted = now;
                return;
            }
            if (_readiness.InCombat || (_record.State != MissionProgress.CompletedByUser &&
                (EnemyCandidates(room).Any() || _loot.HasUnprocessedMissionLoot(room.Instance,
                    dynel => !_objective.IsNonLootObjective(dynel.Identity)))))
            {
                // New arrivals invalidate automatic all-enemies-cleared evidence.
                IsComplete = false;
                _record.RoomsCleared = false;
                _exiting = false;
                _clearedRooms.Remove(room.Instance);
                _say("New combat or unfinished room contents found during exit; clear them and recover before leaving.");
                return;
            }
            if (room.Instance != _entryRoom)
            {
                if (!RouteToRoom(_entryRoom) && !_edgeFailures.Values.Any(x => !x.Permanent && x.Until > now) &&
                    !_reverseCooldown.Values.Any(x => x > now))
                {
                    Stop();
                    _say("No mapped route back to the entry room; automatic exit held.");
                }
                return;
            }
            if (_exitCrossingStarted == DateTime.MinValue)
            {
                if (!_layout.TryExit(_entryRoom, _entryPosition, out _exitThreshold, out _exitAcross, out _exitDoor))
                { Stop(); _say("Entry room has no verified external door geometry; exit manually. Interior doors were not substituted."); return; }
                if (Vector3.Distance(DynelManager.LocalPlayer.Position, _entryPosition) > 2f)
                { Navigate(_entryPosition); return; }
                _exitCrossingStarted = now;
                _say($"Exit door {_exitDoor}, threshold={_exitThreshold}, crossing={_exitAcross}; waiting for zoning.");
            }
            if (now - _exitCrossingStarted > TimeSpan.FromSeconds(25))
            { Stop(); _say("Exit crossing did not zone within 25 s; leave manually to continue."); return; }
            if (Vector3.Distance(DynelManager.LocalPlayer.Position, _exitThreshold) <= 4f &&
                now - _exitLastUse > TimeSpan.FromSeconds(3))
            {
                Dynel door = _exitDoor == Identity.None ? null : DynelManager.GetDynel(_exitDoor);
                door?.Use();
                _exitLastUse = now;
            }
            // The destination crosses the external threshold, outside the dungeon mesh.
            MovementArbiter.Current.SetDestination(_requestedOwner, _exitAcross);
        }

        private bool LootInRoom(Room room)
        {
            _requestedOwner = MovementOwner.LootApproach;
            // Manager.Loot owns the list, chest/lockpick handling and item moves.
            // RKMission approaches and can defer an unreachable object.
            Dynel next = _loot.NextMissionLoot(room.Instance);
            DateTime now = DateTime.UtcNow;
            Identity processingIdentity = _loot.ProcessingMissionLootIdentity;
            Identity waitingIdentity = processingIdentity != Identity.None ? processingIdentity : next?.Identity ?? Identity.None;
            if (waitingIdentity != Identity.None && _waitingForLoot != waitingIdentity)
            {
                _waitingForLoot = waitingIdentity;
                _lootWaitStarted = now;
                _lootLastProgress = _lootWaitStarted;
                _lootLastPosition = DynelManager.LocalPlayer.Position;
                _lootApproachIndex = 0;
                _lootNearStarted = DateTime.MinValue;
                _lootProbeNextSide = false;
                _lootApproachPoints.Clear();
                if (next != null) BuildLootApproaches(room, next.Position);
                _destination = null;
                if (next != null && next.Identity == waitingIdentity)
                    _say($"Loot candidate {next.Identity.Type} {next.Identity} in room {room.Instance}; " +
                        $"trying {_lootApproachPoints.Count} mapped approach point(s) to Manager.Loot range.");
                else
                    _say($"Waiting for Manager.Loot contents/finish response for {waitingIdentity} in room {room.Instance}.");
            }
            if (_loot.IsProcessingMissionLoot)
            {
                // A pending ordinary chest use can time out while the chest is
                // still across an interior wall. Keep approaching that same
                // identity; Manager.Loot retains ownership of opening it.
                bool approachingPendingChest = next != null &&
                    _loot.WaitingForOrdinaryMissionContainer(next.Identity);
                int timeoutSeconds = approachingPendingChest ? 90 : 60;
                if (_waitingForLoot != Identity.None && now - _lootWaitStarted > TimeSpan.FromSeconds(timeoutSeconds))
                {
                    string blockers = _loot.MissionLootBlockers;
                    Stop();
                    _say($"Loot processing for {_waitingForLoot} did not finish within {timeoutSeconds} seconds; room clearance/completion held. {blockers}. Check free slots, bags and locks.");
                    return true;
                }
                if (!approachingPendingChest)
                {
                    MovementArbiter.Current.Halt(_requestedOwner);
                    return true;
                }
            }
            if (next == null)
            {
                _waitingForLoot = Identity.None;
                _lootApproachPoints.Clear();
                _lootProbeNextSide = false;
                return false;
            }
            float distance = next.DistanceFrom(DynelManager.LocalPlayer);
            if (distance > 5.5f || _lootProbeNextSide)
            {
                Vector3 position = DynelManager.LocalPlayer.Position;
                if (Vector3.Distance(position, _lootLastPosition) > 0.75f)
                {
                    // A U-shaped route initially increases straight-line distance
                    // to the chest. Actual movement along the nav route is progress.
                    _lootLastPosition = position;
                    _lootLastProgress = now;
                }
                if (now - _lootWaitStarted > TimeSpan.FromSeconds(75))
                {
                    SkipLoot(next, room.Instance, $"no reachable approach within 75 seconds; still {distance:0.0}m away");
                    return true;
                }
                if (_destination.HasValue && _lootApproachIndex < _lootApproachPoints.Count &&
                    Vector3.Distance(position, _lootApproachPoints[_lootApproachIndex]) < 1.5f &&
                    distance <= 5.5f)
                {
                    _lootProbeNextSide = false;
                    _lootNearStarted = now;
                    _destination = null;
                    MovementArbiter.Current.Halt(_requestedOwner);
                    return true;
                }
                if (_destination.HasValue && _lootApproachIndex < _lootApproachPoints.Count &&
                    (Vector3.Distance(position, _lootApproachPoints[_lootApproachIndex]) < 1.5f ||
                     now - _lootLastProgress > TimeSpan.FromSeconds(10) ||
                     (!SMovementController.IsNavigating() && now - _lootLastProgress > TimeSpan.FromSeconds(2))))
                {
                    _say($"Loot {next.Identity} approach {_lootApproachIndex + 1} ended while {distance:0.0}m away; trying another route.");
                    _lootApproachIndex++;
                    _destination = null;
                    _lootLastProgress = now;
                }
                while (!_destination.HasValue && _lootApproachIndex < _lootApproachPoints.Count)
                {
                    Vector3 point = _lootApproachPoints[_lootApproachIndex];
                    if (MovementArbiter.Current.SetNavDestination(_requestedOwner, point))
                    {
                        _destination = point;
                        _lootLastProgress = now;
                        _lootLastPosition = position;
                        _say($"Loot {next.Identity} route {_lootApproachIndex + 1}/{_lootApproachPoints.Count}: destination={point}.");
                        break;
                    }
                    _say($"Loot {next.Identity} route {_lootApproachIndex + 1} was rejected by navigation.");
                    _lootApproachIndex++;
                }
                if (!_destination.HasValue)
                {
                    SkipLoot(next, room.Instance, $"no navigable chest-side point; still {distance:0.0}m away");
                    return true;
                }
                return true;
            }
            MovementArbiter.Current.Halt(_requestedOwner);
            if (_lootNearStarted == DateTime.MinValue) _lootNearStarted = now;
            double sideWait = _lootApproachPoints.Count > 1 ? 8 : 30;
            if (now - _lootNearStarted > TimeSpan.FromSeconds(sideWait))
            {
                if (_lootApproachIndex + 1 < _lootApproachPoints.Count &&
                    now - _lootWaitStarted <= TimeSpan.FromSeconds(75))
                {
                    _lootApproachIndex++;
                    _lootProbeNextSide = true;
                    _destination = null;
                    _lootLastProgress = now;
                    _lootNearStarted = DateTime.MinValue;
                    _say($"Loot {next.Identity} is nearby but did not open; probing chest-side route {_lootApproachIndex + 1}/{_lootApproachPoints.Count}.");
                }
                else
                    SkipLoot(next, room.Instance, "Manager.Loot did not open the container from any reachable side");
            }
            return true;
        }

        private void BuildLootApproaches(Room room, Vector3 target)
        {
            Vector3 player = DynelManager.LocalPlayer.Position;
            Vector3 toward = player - target;
            toward.Y = 0;
            toward = toward.Magnitude > 0.1 ? toward.Normalize() : Vector3.Forward;
            Vector3 side = new Vector3(-toward.Z, 0, toward.X);
            foreach (float radius in new[] { 3.5f, 5f })
                foreach (Vector3 direction in new[] { toward, side, -side, -toward })
                {
                    Vector3 point = target + direction * radius;
                    if (_layout.IsInside(room.Instance, point, 0.4f) &&
                        !_lootApproachPoints.Any(existing => Vector3.Distance(existing, point) < 1f))
                        _lootApproachPoints.Add(point);
                }
            if (_lootApproachPoints.Count == 0)
                _lootApproachPoints.Add(target); // Preserve the original fallback when Mali geometry is incomplete.
            var ordered = _lootApproachPoints.Select((point, index) => new
                { Point = point, Index = index, Cost = LootRouteCost(player, point) })
                .OrderBy(x => x.Cost).ThenBy(x => x.Index).Select(x => x.Point).ToList();
            _lootApproachPoints.Clear();
            _lootApproachPoints.AddRange(ordered);
        }

        private static float LootRouteCost(Vector3 from, Vector3 to)
        {
            try { return LocalRoutePlanner.TryGroundCost(from, to, out float cost) ? cost : float.PositiveInfinity; }
            catch { return float.PositiveInfinity; } // Advisory order; actual navigation is checked separately.
        }

        private void SkipLoot(Dynel loot, int roomId, string reason)
        {
            if (_loot.IsMissionCriticalLoot(loot.Identity))
            {
                Stop();
                _say($"Mission-critical loot {loot.Identity} in room {roomId} could not be reached: {reason}. Mission recovery required.");
                return;
            }
            _loot.SkipUnreachableMissionLoot(loot.Identity);
            _say($"Skipping unreachable loot {loot.Identity} in room {roomId}: {reason}. Exploration continues.");
            _waitingForLoot = Identity.None;
            _lootApproachPoints.Clear();
            _lootProbeNextSide = false;
            _lootNearStarted = DateTime.MinValue;
            _destination = null;
            MovementArbiter.Current.Halt(_requestedOwner);
        }
        private Room NextRoom(Room current)
        {
            var reserved = new HashSet<int>(_objective.Rooms);
            // Prefer routes that avoid entering the objective room entirely.
            Room next = RouteTo(current.Instance, id => !_clearedRooms.Contains(id) && !reserved.Contains(id), true);
            if (next != null) return next;
            // A cut-through objective room may be unavoidable; its actions remain held.
            next = RouteTo(current.Instance, id => !_clearedRooms.Contains(id) && !reserved.Contains(id), false);
            return next ?? RouteTo(current.Instance, id => !_clearedRooms.Contains(id), false);
        }

        private Room RouteTo(int current, Func<int, bool> isGoal, bool avoidObjectiveRooms)
        {
            if (_layout == null) return null;
            var reserved = new HashSet<int>(_objective.Rooms);
            var queue = new Queue<int>();
            var parent = new Dictionary<int, int>();
            queue.Enqueue(current);
            parent[current] = -1;

            while (queue.Count > 0)
            {
                int index = queue.Dequeue();
                Room room = _layout.Room(index);
                if (room == null)
                    continue;
                if (index != current && isGoal(index))
                {
                    int goal = index;
                    while (parent[index] != current && parent[index] != -1)
                        index = parent[index];
                    _say($"Routing from room {current} through {index} toward room {goal}" +
                        (reserved.Contains(index) && !_objective.Finale ? "; objective interaction remains held." : "."));
                    return _layout.Room(index);
                }

                // Prefer the shortest available chain of room connections. The
                // nearest doorway breaks ties between paths of equal depth.
                Vector3 entryPosition = index == current
                    ? DynelManager.LocalPlayer.Position
                    : _layout.Edge(parent[index], index).Interior;
                foreach (int adjacentCandidate in _layout.Neighbors(index)
                    .Where(id => !IsUnavailable(index, id))
                    .Where(id => !avoidObjectiveRooms || !reserved.Contains(id))
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
            _requestedOwner = MovementOwner.DoorTransition;
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
            Navigate(edge.Threshold);
        }

        private void TickTransition(Room detectedRoom)
        {
            _requestedOwner = MovementOwner.DoorTransition;
            Transition crossing = _transition;
            DungeonLayout.Connection edge = crossing.Edge;
            Door door = _layout.DoorAt(edge);
            bool passageOpen = IsPassageOpen(edge, door);
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
                    // Keep the interior route active during the brief stability
                    // check. Cleared-room confirmation can continue onward.
                    if (_clearedRooms.Contains(edge.Target) && !_readiness.InCombat &&
                        !_loot.IsProcessingMissionLoot)
                    {
                        crossing.PushingDeeper = true;
                        Navigate(edge.DeepInterior);
                    }
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
                    $"door {liveDoorRange}, AO# range {aoDoorRange}, " +
                    $"flag open={door?.IsOpen}, locked={door?.IsLocked}, passage open={passageOpen}).");
                crossing.Phase = door != null && !passageOpen
                    ? TransitionPhase.ProbeDoor : TransitionPhase.OpenDoor;
                crossing.PhaseStarted = now;
                crossing.LastProgress = now;
                crossing.BestDistance = float.MaxValue;
                _destination = null;
                if (door != null && !passageOpen) MovementArbiter.Current.Halt(_requestedOwner);
                if (crossing.Phase == TransitionPhase.ProbeDoor)
                    _say($"Transition {edge.Source}->{edge.Target}: passage reported closed; probing crossing before door interaction.");
            }

            if (crossing.Phase == TransitionPhase.ProbeDoor)
            {
                if (door == null || passageOpen)
                {
                    crossing.Phase = TransitionPhase.OpenDoor;
                    crossing.PhaseStarted = now;
                    crossing.LastProgress = now;
                    crossing.BestDistance = float.MaxValue;
                    _destination = null;
                    if (door != null && !passageOpen) MovementArbiter.Current.Halt(_requestedOwner);
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
                    MovementArbiter.Current.Halt(_requestedOwner);
                    _say($"Transition {edge.Source}->{edge.Target}: passage not crossed; approaching door {door.Identity} for {(door.IsLocked ? "Lock Pick" : "open")} interaction.");
                }
            }

            if (crossing.Phase == TransitionPhase.OpenDoor)
            {
                if (door == null || passageOpen)
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
                float aoDoorDistance = door.DistanceFrom(DynelManager.LocalPlayer);
                if (doorDistance > DoorUseRange || aoDoorDistance > DoorUseRange)
                {
                    if (doorDistance + 0.5f < crossing.BestDistance)
                    {
                        crossing.BestDistance = doorDistance;
                        crossing.LastProgress = now;
                    }
                    if (!crossing.DoorApproachLogged)
                    {
                        crossing.DoorApproachLogged = true;
                        _say($"Transition {edge.Source}->{edge.Target}: closing to within {DoorUseRange:0.0}m of door {door.Identity} before interaction (door {doorDistance:0.0}m, AO# {aoDoorDistance:0.0}m).");
                    }
                    if (now - crossing.LastProgress > TimeSpan.FromSeconds(6))
                    {
                        FailTransition($"could not reach door {door.Identity} for interaction (door {doorDistance:0.0}m, AO# {aoDoorDistance:0.0}m)");
                        return;
                    }
                    if (now - crossing.LastApproachCommand >= TimeSpan.FromSeconds(1))
                    {
                        MovementArbiter.Current.SetDestination(_requestedOwner, door.Position);
                        crossing.LastApproachCommand = now;
                        _destination = door.Position;
                    }
                    return;
                }
                if (now - crossing.LastAction < TimeSpan.FromSeconds(2))
                    return;
                MovementArbiter.Current.Halt(_requestedOwner);
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
                    _say($"Transition {edge.Source}->{edge.Target}: lockpick attempt {crossing.DoorAttempts} on {door.Identity} (open={door.IsOpen}, locked={door.IsLocked}, door {doorDistance:0.0}m, AO# {aoDoorDistance:0.0}m).");
                }
                else
                {
                    door.Use();
                    _say($"Transition {edge.Source}->{edge.Target}: open attempt {crossing.DoorAttempts} on {door.Identity} (door {doorDistance:0.0}m, AO# {aoDoorDistance:0.0}m, passage open={passageOpen}).");
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
            MovementArbiter.Current.Release(MovementOwner.DoorTransition);
            _requestedOwner = MovementOwner.DungeonRoom;
            _currentRoom = edge.Target;
            _visitedRooms.Add(edge.Target);
            _edgeFailures.Remove(EdgeKey(edge.Source, edge.Target));
            _reverseCooldown[EdgeKey(edge.Source, edge.Target)] = DateTime.UtcNow.AddSeconds(ReverseEdgeCooldownSeconds);
            _say($"Transition {edge.Source}->{edge.Target}: confirmed in " +
                (_clearedRooms.Contains(edge.Target) ? "previously cleared room; no repeat clearance pause" : "target room") +
                $"; reverse edge on {ReverseEdgeCooldownSeconds}-second cooldown.");
            _transition = null;
            _destination = null;
            _observedRoom = -1;
            _roomQuietAt = DateTime.MinValue;
            if (!_clearedRooms.Contains(_currentRoom) || _readiness.InCombat ||
                _loot.IsProcessingMissionLoot)
                MovementArbiter.Current.Halt(_requestedOwner);
        }

        private void FailTransition(string reason)
        {
            DungeonLayout.Connection edge = _transition.Edge;
            MovementArbiter.Current.Release(MovementOwner.DoorTransition);
            _requestedOwner = MovementOwner.DungeonRoom;
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
            MovementArbiter.Current.Halt(_requestedOwner);
        }

        private bool IsUnavailable(int source, int target)
        {
            string key = EdgeKey(source, target);
            return (_edgeFailures.TryGetValue(key, out EdgeFailure failure) &&
                (failure.Permanent || failure.Until > DateTime.UtcNow)) ||
                (_reverseCooldown.TryGetValue(key, out DateTime until) && until > DateTime.UtcNow);
        }

        private bool WaitingForRoute =>
            _edgeFailures.Values.Any(x => !x.Permanent && x.Until > DateTime.UtcNow) ||
            _reverseCooldown.Values.Any(x => x > DateTime.UtcNow);

        private static bool IsPassageOpen(DungeonLayout.Connection edge, Door door)
        {
            if (door?.IsOpen == true) return true;
            if (edge.Source < 0 || edge.Source > short.MaxValue ||
                edge.Target < 0 || edge.Target > short.MaxValue)
                return false;
            IntPtr playfield = N3EngineClient_t.GetPlayfield();
            return playfield != IntPtr.Zero &&
                N3Playfield_t.IsDoorOpenBetweenRooms(playfield, (short)edge.Source, (short)edge.Target);
        }

        private void Navigate(Vector3 destination)
        {
            if (MovementArbiter.Current.Owner != _requestedOwner || !_destination.HasValue ||
                Vector3.Distance(_destination.Value, destination) > 1f ||
                !SMovementController.IsNavigating())
            {
                MovementArbiter.Current.SetNavDestination(_requestedOwner, destination);
                _destination = destination;
            }
        }

        private static string EdgeKey(int a, int b) =>
            a < b ? $"{a}:{b}" : $"{b}:{a}";
    }
}
