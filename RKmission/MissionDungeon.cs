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
        private readonly Action<string> _navTrace;
        private readonly ManagerLoot.ManagerLoot _loot;
        private readonly MissionReadiness _readiness;
        private readonly InventoryPolicy _inventory;
        private readonly CombatDriver _combat;
        private readonly NavigationRouteRecorder _navRoutes;
        private readonly DungeonNavMeshGeneration _navMeshGeneration;
        private readonly DungeonLiftController _lifts = new DungeonLiftController();
        private MovementOwner _requestedOwner = MovementOwner.DungeonRoom;
        private const float EngagementRange = 20f;
        private const float DoorUseRange = 2f;
        private const float LootUseRange = 3f;
        private const int MaxCombatTopologyAttempts = 3;
        private const float CombatTopologyVerticalSeparation = 1.5f;
        private const int MaxStaircaseRecoveryAttempts = 2;
        private const float StaircaseAirborneLift = 0.15f;
        private const int ReverseEdgeCooldownSeconds = 1;
        private static readonly TimeSpan DeferredLootRouteWaitLimit = TimeSpan.FromSeconds(95);
        private readonly HashSet<int> _clearedRooms = new HashSet<int>();
        private readonly HashSet<int> _combatCheckedRooms = new HashSet<int>();
        private readonly HashSet<int> _visitedRooms = new HashSet<int>();
        private readonly HashSet<int> _surveyedRooms = new HashSet<int>();
        private enum RoomClearanceState { Entered, Surveyed, CombatChecked, CombatDeferred, Cleared, Reopened }
        private readonly Dictionary<int, RoomClearanceState> _roomStates = new Dictionary<int, RoomClearanceState>();
        private readonly HashSet<int> _floorsVisited = new HashSet<int>();
        private readonly HashSet<int> _enemyOwners = new HashSet<int>();
        private readonly Dictionary<string, EdgeFailure> _edgeFailures = new Dictionary<string, EdgeFailure>();
        private readonly Dictionary<string, DateTime> _reverseCooldown = new Dictionary<string, DateTime>();
        private NavMesh[] _meshes;
        private DungeonLayout _layout;
        private Identity _scanTarget = Identity.None;
        private DateTime _scanStarted, _scanProgress;
        private DateTime _deferredLootRouteWaitStarted;
        private readonly List<Vector3> _scanWaypoints = new List<Vector3>();
        private int _scanWaypointIndex;
        private float _scanWaypointBestDistance;
        private Vector3 _scanEnemyPosition;
        private Identity _combatApproachTarget = Identity.None;
        private readonly List<Vector3> _combatApproaches = new List<Vector3>();
        private int _combatApproachIndex;
        private DateTime _combatApproachProgress;
        private DateTime _combatApproachIssuedAt;
        private Vector3 _combatApproachLastPosition;
        private Vector3 _combatApproachEnemyPosition;
        private readonly List<CombatBlockedCorridor> _combatBlockedCorridors = new List<CombatBlockedCorridor>();
        private Vector3? _combatRetreat;
        private int _combatStalls;
        private readonly Dictionary<int, CombatRecovery> _combatRecoveries = new Dictionary<int, CombatRecovery>();
        private enum StaircaseRecoveryPhase { Idle, WaitingForAirborne, ForwardAirborne, Settling }
        private StaircaseRecoveryPhase _staircaseRecoveryPhase;
        private Identity _staircaseRecoveryEnemy = Identity.None;
        private Vector3 _staircaseRecoveryOrigin, _staircaseRecoveryDirection, _staircaseRecoveryLastPosition;
        private DateTime _staircaseRecoveryPhaseStarted, _staircaseRecoveryLastMotion;
        private int _staircaseRecoveryAttempts;
        private int _provenStaircaseRoom = -1;
        private Vector3 _provenStaircaseOrigin, _provenStaircaseDirection;
        private DateTime _provenStaircaseAt;
        private static readonly TimeSpan ProvenStaircaseLifetime = TimeSpan.FromMinutes(5);
        private const float ProvenStaircaseReuseRadius = 6f;
        private sealed class CombatRecovery
        {
            public Identity Enemy;
            public Vector3 Anchor, EnemyPosition;
            public DateTime RetryAt, WaypointStarted, LastProgress;
            public Vector3 LastPosition;
            public readonly List<Vector3> Waypoints = new List<Vector3>();
            public int WaypointIndex, Failures;
            public float TransitCost;
            public string RecordedRoute;
            public bool Active, HoldReported, UsedRoomDetour, Transit, RecordedTransit;
        }
        private sealed class CombatTopologyHandoff
        {
            public int RoomId, DungeonInstance;
            public Identity Enemy;
            public Vector3 EnemyPosition, TargetSide;
            public string RouteName;
            public DateTime ExpiresAt;
            public NavigationRouteRecorder.DungeonTopologyContinuation Continuation;
        }
        private CombatTopologyHandoff _combatTopologyHandoff;
        private static readonly TimeSpan CombatTopologyHandoffLifetime = TimeSpan.FromSeconds(30);
        private sealed class CombatBlockedCorridor
        {
            public Vector3 Origin, Direction;
        }
        private AcceptedMission _record;
        private MissionObjective _objective;
        private bool _exiting;
        private bool _reservedAggroReported;
        private DateTime _findPersonFinaleRouteWait, _findPersonFinaleRouteDiagnostic;
        private static readonly TimeSpan FindPersonFinaleRouteWaitLimit = TimeSpan.FromSeconds(30);
        private DateTime _reservedProtectionStarted;
        private static readonly TimeSpan ReservedProtectionTimeout = TimeSpan.FromSeconds(5);
        private int _entryRoom;
        private Vector3 _entryPosition, _exitThreshold, _exitAcross;
        private Identity _exitDoor = Identity.None;
        private DateTime _exitStarted, _exitCrossingStarted, _exitLastUse;
        private int _currentRoom = -1;
        private Transition _transition;
        private Vector3? _destination;
        private DateTime _roomQuietAt;
        private sealed class PostCombatCorpseGuard
        {
            public Identity Enemy;
            public Identity Corpse;
            public int RoomId;
            public long CorpseOccurrence;
            public Vector3 DeathPosition;
            public DateTime Started, AcquiredAt, LastDiagnostic, SkipInvisibleAt, NextInRoomRetryAt;
            public int InRoomRetryCount;
            public readonly HashSet<Identity> ExistingCorpses = new HashSet<Identity>();
        }
        private readonly List<PostCombatCorpseGuard> _postCombatCorpseGuards = new List<PostCombatCorpseGuard>();
        private Identity _engagedEnemy = Identity.None;
        private int _engagedEnemyRoom = -1;
        private Vector3 _engagedEnemyPosition;
        private readonly HashSet<Identity> _engagementExistingCorpses = new HashSet<Identity>();
        private static readonly TimeSpan PostCombatCorpseMaterializationWait = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan PostCombatCorpseSkipVisibilityGrace = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan PostCombatCorpseSettlementWait = TimeSpan.FromSeconds(90);
        private static readonly TimeSpan GuardedCorpseRetryCooldown = TimeSpan.FromSeconds(5);
        private const int MaxGuardedCorpseSameRoomRetries = 2;

        private int VerifiedDynelRoom(Dynel dynel, int fallbackRoom = -1)
        {
            if (dynel == null || _layout == null) return fallbackRoom;
            Room direct = dynel.Room;
            if (direct != null && _layout.ContainsDynel(direct.Instance, dynel))
                return direct.Instance;
            Room contained = Playfield.Rooms.FirstOrDefault(candidate =>
                _layout.ContainsDynel(candidate.Instance, dynel));
            return contained?.Instance ?? fallbackRoom;
        }

        private void RememberExistingCorpses(int roomId)
        {
            if (roomId < 0) return;
            foreach (Dynel corpse in _layout.VisibleRoomDynels(roomId).Where(x =>
                x.Identity.Type == IdentityType.Corpse && _layout.ContainsDynel(roomId, x)))
                _engagementExistingCorpses.Add(corpse.Identity);
        }
        private DateTime _observedRoomAt;
        private int _observedRoom = -1;
        private DateTime _lootWaitStarted;
        private DateTime _lootLastProgress;
        private Identity _waitingForLoot = Identity.None;
        private readonly List<Vector3> _lootApproachPoints = new List<Vector3>();
        private readonly List<Vector3> _objectiveApproaches = new List<Vector3>();
        private Vector3? _objectiveApproachTarget;
        private int _objectiveApproachIndex;
        private int _objectiveApproachRoom = -1;
        private bool _objectiveTopologyActive;
        private DateTime _objectiveTopologyStarted;
        private readonly List<Tuple<int, Vector3>> _objectiveTopologyAttempts = new List<Tuple<int, Vector3>>();
        private static readonly TimeSpan ObjectiveTopologyTimeout = TimeSpan.FromSeconds(60);
        private float _objectiveBestDistance;
        private DateTime _objectiveProgressAt;
        private int _lootApproachIndex;
        private int _lootCorpseFinalReissues;
        private int _lootContainerFinalReissues;
        private bool _lootTransitStaging;
        private Vector3 _lootLastPosition;
        private DateTime _lootNearStarted;
        private int _lootProgressVersion;
        private bool _lootProbeNextSide;
        private float _lootBestVerticalGap = float.PositiveInfinity;
        private bool _lootRecordedTransit;
        private bool _lootRecordedFallbackAttempted;
        private string _lootRecordedRoute;
        private const float LootStagingRevisitTolerance = 2f;
        private readonly List<Vector3> _lootVisitedStaging = new List<Vector3>();
        private float _lootBestDistance = float.PositiveInfinity;
        private int _lootNonImprovingHops;
        private bool _lootCycleReported;
        private int _floor;
        private DungeonLiftController.Lift _activeLift;
        private DateTime _liftStarted;
        private DateTime _killFinaleOwnershipAt, _killFinaleLastDiagnostic;
        private string _killFinaleState;
        private static readonly TimeSpan KillFinaleOwnershipWatchdog = TimeSpan.FromSeconds(7);

        private sealed class RecordedRoomReturn
        {
            public int RoomId, Floor, Playfield;
            public NavigationRouteRecorder.DungeonTopologyContinuation Ascent;
        }
        private RecordedRoomReturn _recordedRoomReturn;

        private enum TransitionPhase { RecordedReturn, SourceApproach, ProbeDoor, OpenDoor, TargetCenterline, SafeInterior }
        private sealed class Transition
        {
            public DungeonLayout.Connection Edge;
            public TransitionPhase Phase;
            public DateTime Started, PhaseStarted, LastAction, LastProgress, LastApproachCommand;
            public float BestDistance;
            public int DoorAttempts, CrossingRetries;
            public bool PushingDeeper, DoorApproachLogged;
            public Vector3 TargetObservedPosition, ReturnGround, ReturnLastPosition;
        }
        private sealed class EdgeFailure
        {
            public int Count;
            public int Successes;
            public int ConsecutiveFailures;
            public DateTime Until;
            public bool Permanent;
        }

        public bool IsRunning { get; private set; }
        public bool IsComplete { get; private set; }
        public MissionObjective Objective => _objective;
        public int EntryRoom => _entryRoom;
        public int Floor => _floor;
        public bool IsExiting => _exiting;
        public bool CombatRecoveryWaiting { get; private set; }
        public string Status => _exiting ? "returning to exit" : IsComplete ? "complete" : !IsRunning ? "idle" : _readiness.IsWaiting ? _readiness.Status :
            $"floor {_floor}, room {_currentRoom}, transition={_transition?.Phase.ToString() ?? "none"}, visited {_visitedRooms.Count}, cleared {_clearedRooms.Count} rooms, deferred combat {_combatRecoveries.Values.Count(x => x.Active)}";
        public MissionExecutionRecord Snapshot() => _record == null ? null : new MissionExecutionRecord
        {
            MissionId = _record.Id.Instance,
            DungeonInstance = Playfield.ModelIdentity.Instance,
            EntryRoom = _entryRoom,
            EntryPosition = _entryPosition,
            CurrentRoom = _currentRoom,
            Floor = _floor,
            FloorsVisited = _floorsVisited.OrderBy(x => x).ToList(),
            RoomStates = _roomStates.ToDictionary(x => x.Key, x => x.Value.ToString()),
            EdgeFailures = _edgeFailures.ToDictionary(x => x.Key, x => x.Value.Count),
            ObjectiveRooms = _objective?.Rooms.OrderBy(x => x).ToList() ?? new List<int>(),
            ObjectiveSteps = _objective?.StepSummary,
            LootBlockers = _loot.MissionLootBlockers,
            ExitDoor = _exitDoor.ToString(),
            Phase = _exiting ? "Exit" : _transition != null ? "Door:" + _transition.Phase :
                _combatRecoveries.Values.Any(x => x.Active) ? "CombatRecovery" :
                _objective?.Finale == true ? "ObjectiveFinale" : "Exploration",
            UpdatedAtUtc = DateTime.UtcNow
        };

        public MissionDungeon(Action<string> say, Action<string> navTrace,
            ManagerLoot.ManagerLoot loot, MissionReadiness readiness,
            InventoryPolicy inventory, NavigationRouteRecorder navRoutes, string pluginDir)
        {
            _say = say;
            _navTrace = navTrace ?? (_ => { });
            _loot = loot;
            _readiness = readiness;
            _inventory = inventory;
            _navRoutes = navRoutes;
            _navMeshGeneration = new DungeonNavMeshGeneration(pluginDir, navRoutes, _navTrace);
            _combat = new CombatDriver(say);
        }

        public void Start(AcceptedMission record)
        {
            if (IsRunning)
                return;
            if (!Playfield.IsDungeon || DynelManager.LocalPlayer?.Room == null)
                return;
            _inventory.BeginMissionInventorySnapshot();
            MovementArbiter.Current.StopAll();
            _clearedRooms.Clear();
            _combatCheckedRooms.Clear();
            _reservedAggroReported = false;
            ResetFindPersonFinaleRouteWait();
            _reservedProtectionStarted = DateTime.MinValue;
            _visitedRooms.Clear();
            _surveyedRooms.Clear();
            _roomStates.Clear();
            _floorsVisited.Clear();
            _enemyOwners.Clear();
            _edgeFailures.Clear();
            _reverseCooldown.Clear();
            _deferredLootRouteWaitStarted = DateTime.MinValue;
            _combat.Reset();
            ResetPostCombatCorpseTracking();
            ResetCombatApproach();
            _combatRecoveries.Clear();
            ResetStaircaseRecoveryAttempts();
            ResetProvenStaircase();
            _combatTopologyHandoff = null;
            _recordedRoomReturn = null;
            CombatRecoveryWaiting = false;
            _scanTarget = Identity.None;
            _scanWaypoints.Clear();
            _waitingForLoot = Identity.None;
            _lootTransitStaging = false;
            _lootRecordedTransit = false;
            _lootRecordedFallbackAttempted = false;
            _lootRecordedRoute = null;
            _lootVisitedStaging.Clear();
            ResetObjectiveApproach();
            _objectiveTopologyAttempts.Clear();
            ResetKillFinaleOwnership();
            _loot.ResetMissionLootSkips();
            _loot.IgnoreOrdinaryMissionLoot = false;
            _lifts.Reset();
            _activeLift = null;
            _liftStarted = DateTime.MinValue;
            _record = record;
            _objective = new MissionObjective(record, _say);
            _loot.BeginMissionReservations(record.Id);
            _loot.BeginMissionControl();
            _loot.ActiveMissionOwner = record.Id;
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
            _loot.MissionInteractionReady = LootInteractionReady;
            _loot.MissionCombatFree = () => !_readiness.InCombat &&
                !DynelManager.LocalPlayer.IsAttacking &&
                !DynelManager.NPCs.Any(x => x.IsAttacking &&
                    x.FightingTarget?.Identity == DynelManager.LocalPlayer.Identity);
            _loot.MissionCombatBlockReleaseAllowed = () =>
                !((_objective.HasKillTarget || _objective.HasFindPersonTarget) &&
                  !_objective.RewardConfirmed);
            _loot.MissionItemProtected = item => _objective.ProtectsMissionItem(item.UniqueIdentity) ||
                (_objective.ReturnItem != null &&
                 ManagerLoot.ManagerLoot.UsableMissionItemIdentity(_objective.ReturnItem.UniqueIdentity) &&
                 _objective.ReturnItem.UniqueIdentity == item.UniqueIdentity);
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
            _floorsVisited.Add(_floor);
            _navMeshGeneration.GenerateAsync().ContinueWith(task =>
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
            ResetObjectiveApproach();
            _objectiveTopologyAttempts.Clear();
            if (_record != null)
            {
                _loot.RememberMissionCriticalInventory(_record.Id);
                if (_objective?.CollectedReturnItem == true)
                    _loot.ProtectPendingHandIn(_record.Id, _objective.ReturnItem);
            }
            IsRunning = false;
            _readiness.Stop();
            _loot.MissionActionsPaused = false;
            _loot.EndMissionRoom(restoreUserState: false);
            _loot.ReleaseMissionControl();
            _loot.MissionRoomContains = null;
            _loot.MissionRoomDynels = null;
            _loot.MissionLootAllowed = null;
            _loot.MissionInteractionReady = null;
            _loot.MissionCombatFree = null;
            _loot.MissionCombatBlockReleaseAllowed = null;
            _loot.MissionLootReserved = null;
            _loot.MissionItemProtected = null;
            _loot.ActiveMissionOwner = Identity.None;
            _loot.IgnoreOrdinaryMissionLoot = false;
            _loot.MissionObjectiveContainer = Identity.None;
            _destination = null;
            _transition = null;
            _activeLift = null;
            _layout = null;
            _combatRecoveries.Clear();
            _deferredLootRouteWaitStarted = DateTime.MinValue;
            ResetStaircaseRecoveryAttempts();
            ResetProvenStaircase();
            _combatTopologyHandoff = null;
            _recordedRoomReturn = null;
            ResetFindPersonFinaleRouteWait();
            ResetPostCombatCorpseTracking();
            _navRoutes?.StopPlayback();
            CombatRecoveryWaiting = false;
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
            if (_currentRoom >= 0 &&
                _combatRecoveries.TryGetValue(_currentRoom, out CombatRecovery recovery) &&
                recovery.Active && recovery.WaypointIndex < recovery.Waypoints.Count &&
                MovementArbiter.Current.Owner == MovementOwner.CombatPosition)
            {
                Vector3 recoveryTarget = recovery.Waypoints[recovery.WaypointIndex];
                if (TryBeginStaircaseRecovery(recovery, recoveryTarget)) return;
                FailCombatRecoveryRoute(recovery, "movement displacement");
                return;
            }
            if (_transition != null)
            {
                Transition crossing = _transition;
                switch (crossing.Phase)
                {
                    case TransitionPhase.SourceApproach:
                        if (tier >= 3) FailTransition("source-side approach repeatedly stalled");
                        else InvalidatePath();
                        break;
                    case TransitionPhase.ProbeDoor:
                        crossing.Phase = TransitionPhase.OpenDoor;
                        crossing.PhaseStarted = DateTime.UtcNow;
                        _destination = null;
                        MovementArbiter.Current.Halt(MovementOwner.DoorTransition);
                        _say($"Transition {crossing.Edge.Source}->{crossing.Edge.Target}: probe stalled; trying door interaction.");
                        break;
                    case TransitionPhase.OpenDoor:
                        if (tier >= 3) FailTransition("door-side interaction approach repeatedly stalled");
                        else { crossing.LastApproachCommand = DateTime.MinValue; InvalidatePath(); }
                        break;
                    case TransitionPhase.TargetCenterline:
                        crossing.Phase = TransitionPhase.SafeInterior;
                        crossing.PushingDeeper = true;
                        crossing.PhaseStarted = crossing.LastProgress = DateTime.UtcNow;
                        _destination = null;
                        _say($"Transition {crossing.Edge.Source}->{crossing.Edge.Target}: centerline stalled; trying safe interior.");
                        break;
                    case TransitionPhase.SafeInterior:
                        if (++crossing.CrossingRetries > 3 || tier >= 3)
                            FailTransition("safe interior repeatedly stalled without exact room confirmation");
                        else { crossing.LastProgress = DateTime.UtcNow; _destination = null; }
                        break;
                }
                return;
            }
            if ((MovementArbiter.Current.Owner == MovementOwner.LootApproach ||
                 _requestedOwner == MovementOwner.LootApproach) &&
                _destination.HasValue &&
                _lootApproachIndex < _lootApproachPoints.Count)
            {
                int rejectedRoute = _lootApproachIndex + 1;
                _lootApproachIndex++;
                _destination = null;
                _lootLastProgress = DateTime.UtcNow;
                _lootLastPosition = DynelManager.LocalPlayer.Position;
                MovementArbiter.Current.Halt(MovementOwner.LootApproach);
                if (_lootApproachIndex < _lootApproachPoints.Count)
                    _say($"Loot {_waitingForLoot} approach {rejectedRoute} stalled; rejecting route and trying " +
                        $"{_lootApproachIndex + 1}/{_lootApproachPoints.Count}.");
                else
                    _say($"Loot {_waitingForLoot} approach {rejectedRoute} stalled; rejecting final route " +
                        $"{rejectedRoute}/{_lootApproachPoints.Count}.");
                return;
            }
            if ((MovementArbiter.Current.Owner == MovementOwner.CombatPosition ||
                 _requestedOwner == MovementOwner.CombatPosition) &&
                _combatApproachTarget != Identity.None &&
                (_combatRetreat.HasValue || _combatApproachIndex < _combatApproaches.Count))
            {
                Vector3 position = DynelManager.LocalPlayer.Position;
                bool retreatStalled = _combatRetreat.HasValue;
                Vector3 attempted = retreatStalled ? _combatRetreat.Value :
                    _destination ?? _combatApproaches[_combatApproachIndex];
                Vector3 direction = attempted - position;
                direction.Y = 0;
                if (direction.Magnitude > 0.5f)
                    _combatBlockedCorridors.Add(new CombatBlockedCorridor
                    { Origin = position, Direction = direction.Normalize() });
                _combatStalls++;
                if (!retreatStalled) _combatApproachIndex++;
                _combatRetreat = null;
                _destination = null;
                _combatApproachProgress = DateTime.UtcNow;
                _combatApproachLastPosition = position;
                MovementArbiter.Current.Halt(MovementOwner.CombatPosition);
                if (_combatStalls >= 3)
                {
                    _combatApproachIndex = _combatApproaches.Count;
                    _say($"Combat approach halted after {_combatStalls} observed stalls near {position}; " +
                        "no further movement into this geometry will be attempted.");
                    return;
                }
                if (!retreatStalled && _combatStalls == 1)
                    TryCombatRetreat(position, direction);
                _say($"Combat approach stalled at {position}; rejected that heading and " +
                    (_combatRetreat.HasValue ? $"trying mapped retreat {_combatRetreat.Value}." :
                        "trying a different firing side."));
                return;
            }
            if (tier >= 2)
                MovementArbiter.Current.Halt(_requestedOwner);
            InvalidatePath();
        }

        public void CancelMovementRecovery()
        {
            CancelStaircaseRecovery();
            _staircaseRecoveryAttempts = 0;
            _staircaseRecoveryEnemy = Identity.None;
        }

        private bool TryBeginStaircaseRecovery(CombatRecovery recovery, Vector3 target)
        {
            var player = DynelManager.LocalPlayer;
            if (_staircaseRecoveryPhase != StaircaseRecoveryPhase.Idle || player == null ||
                !player.IsAlive || Game.IsZoning ||
                MovementArbiter.Current.Owner != MovementOwner.CombatPosition)
            {
                _say($"Combat staircase recovery rejected: phase={_staircaseRecoveryPhase}, " +
                    $"player={(player == null ? "missing" : player.IsAlive ? "alive" : "dead")}, zoning={Game.IsZoning}, " +
                    $"owner={MovementArbiter.Current.Owner}, attempts={_staircaseRecoveryAttempts}/{MaxStaircaseRecoveryAttempts}.");
                return false;
            }
            float enemyLift = recovery.EnemyPosition.Y - player.Position.Y;
            float routeLift = recovery.Waypoints.Skip(recovery.WaypointIndex)
                .Select(point => point.Y - player.Position.Y)
                .DefaultIfEmpty(target.Y - player.Position.Y)
                .Max();
            bool provenGeometry = TryGetProvenStaircaseDirection(recovery, player.Position,
                out Vector3 provenDirection);
            // The first stair waypoint can be only a small step above us even
            // when the route and enemy establish a genuinely upward topology.
            // A recently successful climb at this exact room geometry is also
            // positive topology evidence when a rebuilt route begins flat.
            if (enemyLift < CombatTopologyVerticalSeparation ||
                routeLift < StaircaseAirborneLift && !provenGeometry)
            {
                _say($"Combat staircase recovery rejected: enemy lift={enemyLift:0.00}m, " +
                    $"route lift={routeLift:0.00}m, proven geometry={provenGeometry}, " +
                    $"room={_currentRoom}, attempts={_staircaseRecoveryAttempts}/{MaxStaircaseRecoveryAttempts}.");
                return false;
            }
            if (_staircaseRecoveryEnemy != recovery.Enemy)
            {
                _staircaseRecoveryEnemy = recovery.Enemy;
                _staircaseRecoveryAttempts = 0;
            }
            if (_staircaseRecoveryAttempts >= MaxStaircaseRecoveryAttempts)
            {
                _say($"Combat staircase recovery rejected: attempt limit " +
                    $"{_staircaseRecoveryAttempts}/{MaxStaircaseRecoveryAttempts} exhausted for {recovery.Enemy}; " +
                    $"enemy lift={enemyLift:0.00}m, route lift={routeLift:0.00}m, proven geometry={provenGeometry}.");
                return false;
            }
            bool reuseProvenHeading = provenGeometry && routeLift < StaircaseAirborneLift;
            Vector3 direction = reuseProvenHeading ? provenDirection : target - player.Position;
            direction.Y = 0;
            if (direction.Magnitude < 0.25f)
            {
                direction = recovery.EnemyPosition - player.Position;
                direction.Y = 0;
            }
            if (direction.Magnitude < 0.25f) return false;
            direction = direction.Normalize();

            _destination = null;
            MovementArbiter.Current.Halt(MovementOwner.CombatPosition);
            MovementArbiter.Current.SetMovement(MovementOwner.CombatPosition, MovementAction.FullStop);
            if (MovementArbiter.Current.Owner != MovementOwner.CombatPosition) return false;
            player.Rotation = Quaternion.LookRotation(direction, Vector3.Up);
            _staircaseRecoveryOrigin = _staircaseRecoveryLastPosition = player.Position;
            _staircaseRecoveryDirection = direction;
            _staircaseRecoveryPhaseStarted = _staircaseRecoveryLastMotion = DateTime.UtcNow;
            _staircaseRecoveryAttempts++;
            _staircaseRecoveryPhase = StaircaseRecoveryPhase.WaitingForAirborne;
            // The confirmed AO sequence requires a stationary jump. Do not
            // send ForwardStart until observed Y lift proves we are airborne.
            MovementArbiter.Current.SetMovement(MovementOwner.CombatPosition, MovementAction.JumpStart);
            _say($"Combat staircase recovery {_staircaseRecoveryAttempts}/{MaxStaircaseRecoveryAttempts}: " +
                $"stationary jump started toward {target}; heading={(reuseProvenHeading ? "proven geometry" : "current route")}, " +
                $"enemy lift={enemyLift:0.00}m, route lift={routeLift:0.00}m; forward input is gated on observed airborne lift.");
            return true;
        }

        private bool TryGetProvenStaircaseDirection(CombatRecovery recovery, Vector3 position,
            out Vector3 direction)
        {
            direction = _provenStaircaseDirection;
            if (_provenStaircaseRoom != _currentRoom ||
                DateTime.UtcNow - _provenStaircaseAt > ProvenStaircaseLifetime ||
                recovery.EnemyPosition.Y - position.Y < CombatTopologyVerticalSeparation)
                return false;
            Vector3 offset = position - _provenStaircaseOrigin;
            offset.Y = 0;
            return offset.Magnitude <= ProvenStaircaseReuseRadius && direction.Magnitude >= 0.9f;
        }

        private bool TickStaircaseRecovery()
        {
            if (_staircaseRecoveryPhase == StaircaseRecoveryPhase.Idle) return false;
            var player = DynelManager.LocalPlayer;
            if (player == null || !player.IsAlive || Game.IsZoning)
            {
                CancelStaircaseRecovery();
                return false;
            }
            if (MovementArbiter.Current.Owner != MovementOwner.CombatPosition)
            {
                // Release only the raw inputs this state machine can hold. Do
                // not reclaim movement from the new owner.
                SMovementController.SetMovement(MovementAction.JumpStop);
                SMovementController.SetMovement(MovementAction.ForwardStop);
                _staircaseRecoveryPhase = StaircaseRecoveryPhase.Idle;
                _say("Combat staircase recovery cancelled after movement ownership changed.");
                return false;
            }

            DateTime now = DateTime.UtcNow;
            Vector3 position = player.Position;
            if (Vector3.Distance(position, _staircaseRecoveryLastPosition) > 0.05f)
            {
                _staircaseRecoveryLastPosition = position;
                _staircaseRecoveryLastMotion = now;
            }
            float verticalLift = position.Y - _staircaseRecoveryOrigin.Y;
            Vector3 horizontal = position - _staircaseRecoveryOrigin;
            horizontal.Y = 0;
            float forwardProgress = Vector3.Dot(horizontal, _staircaseRecoveryDirection);

            if (_staircaseRecoveryPhase == StaircaseRecoveryPhase.WaitingForAirborne)
            {
                if (verticalLift >= StaircaseAirborneLift)
                {
                    MovementArbiter.Current.SetMovement(MovementOwner.CombatPosition, MovementAction.JumpStop);
                    MovementArbiter.Current.SetMovement(MovementOwner.CombatPosition, MovementAction.ForwardStart);
                    MovementArbiter.Current.SetMovement(MovementOwner.CombatPosition, MovementAction.Update);
                    _staircaseRecoveryPhase = StaircaseRecoveryPhase.ForwardAirborne;
                    _staircaseRecoveryPhaseStarted = now;
                    _say($"Combat staircase recovery observed {verticalLift:0.00}m vertical lift; applying bounded forward input while airborne.");
                }
                else if (now - _staircaseRecoveryPhaseStarted > TimeSpan.FromMilliseconds(1250))
                    FinishStaircaseRecovery(false, "no airborne lift observed");
                return true;
            }

            if (_staircaseRecoveryPhase == StaircaseRecoveryPhase.ForwardAirborne)
            {
                if (forwardProgress >= 1.25f ||
                    now - _staircaseRecoveryPhaseStarted > TimeSpan.FromMilliseconds(900))
                {
                    StopStaircaseInputs(true);
                    _staircaseRecoveryPhase = StaircaseRecoveryPhase.Settling;
                    _staircaseRecoveryPhaseStarted = now;
                    _staircaseRecoveryLastMotion = now;
                }
                return true;
            }

            bool settled = now - _staircaseRecoveryLastMotion >= TimeSpan.FromMilliseconds(250);
            bool settleExpired = now - _staircaseRecoveryPhaseStarted >= TimeSpan.FromSeconds(2);
            if (settled || settleExpired)
            {
                bool progressed = verticalLift >= 0.45f && forwardProgress >= 0.35f;
                FinishStaircaseRecovery(progressed, progressed
                    ? $"settled with {verticalLift:0.00}m vertical/{forwardProgress:0.00}m forward progress"
                    : $"settled without enough progress ({verticalLift:0.00}m vertical/{forwardProgress:0.00}m forward)");
            }
            return true;
        }

        private void FinishStaircaseRecovery(bool succeeded, string reason)
        {
            StopStaircaseInputs(true);
            _staircaseRecoveryPhase = StaircaseRecoveryPhase.Idle;
            _destination = null;
            if (_currentRoom >= 0 &&
                _combatRecoveries.TryGetValue(_currentRoom, out CombatRecovery recovery) &&
                recovery.Active && recovery.Enemy == _staircaseRecoveryEnemy)
            {
                recovery.LastPosition = DynelManager.LocalPlayer.Position;
                recovery.LastProgress = recovery.WaypointStarted = DateTime.UtcNow;
                if (!succeeded && _staircaseRecoveryAttempts >= MaxStaircaseRecoveryAttempts)
                {
                    FailCombatRecoveryRoute(recovery,
                        $"staircase recovery exhausted after {_staircaseRecoveryAttempts} attempts: {reason}");
                    return;
                }
            }
            _say($"Combat staircase recovery {(succeeded ? "completed" : "attempt failed")}: {reason}. " +
                (succeeded ? "Rebuilding movement from the observed landing position."
                    : $"Normal route control resumed; {_staircaseRecoveryAttempts}/{MaxStaircaseRecoveryAttempts} attempts used."));
            if (succeeded)
            {
                _provenStaircaseRoom = _currentRoom;
                _provenStaircaseOrigin = _staircaseRecoveryOrigin;
                _provenStaircaseDirection = _staircaseRecoveryDirection;
                _provenStaircaseAt = DateTime.UtcNow;
                _say($"Combat staircase recovery recorded proven geometry in room {_currentRoom} " +
                    $"at {_provenStaircaseOrigin} heading {_provenStaircaseDirection}; reusable within {ProvenStaircaseReuseRadius:0.0}m.");
                ResetStaircaseRecoveryAttempts();
            }
        }

        private void StopStaircaseInputs(bool fullStop)
        {
            if (MovementArbiter.Current.Owner == MovementOwner.CombatPosition)
            {
                MovementArbiter.Current.SetMovement(MovementOwner.CombatPosition, MovementAction.JumpStop);
                MovementArbiter.Current.SetMovement(MovementOwner.CombatPosition, MovementAction.ForwardStop);
                if (fullStop)
                    MovementArbiter.Current.SetMovement(MovementOwner.CombatPosition, MovementAction.FullStop);
            }
            else
            {
                SMovementController.SetMovement(MovementAction.JumpStop);
                SMovementController.SetMovement(MovementAction.ForwardStop);
            }
        }

        private void CancelStaircaseRecovery()
        {
            if (_staircaseRecoveryPhase != StaircaseRecoveryPhase.Idle)
                StopStaircaseInputs(MovementArbiter.Current.Owner == MovementOwner.CombatPosition);
            _staircaseRecoveryPhase = StaircaseRecoveryPhase.Idle;
        }

        private void ResetStaircaseRecoveryAttempts()
        {
            CancelStaircaseRecovery();
            _staircaseRecoveryAttempts = 0;
            _staircaseRecoveryEnemy = Identity.None;
        }

        private void ResetProvenStaircase()
        {
            _provenStaircaseRoom = -1;
            _provenStaircaseOrigin = Vector3.Zero;
            _provenStaircaseDirection = Vector3.Zero;
            _provenStaircaseAt = DateTime.MinValue;
        }

        private void LoadFloor()
        {
            ResetObjectiveApproach();
            if (_meshes != null && _floor >= 0 && _floor < _meshes.Length)
                SMovementController.LoadNavmesh(_meshes[_floor], true);
        }

        public void Tick()
        {
            if (!IsRunning || !Playfield.IsDungeon || DynelManager.LocalPlayer.Room == null)
                return;
            if (TickStaircaseRecovery()) return;
            _requestedOwner = MovementOwner.DungeonRoom;
            CombatRecoveryWaiting = false;

            _readiness.ObserveCombat();
            _inventory.Update(_say);
            _loot.IgnoreOrdinaryMissionLoot = _inventory.SkipOptionalLoot;
            _objective.Refresh(_layout);
            if (_objective.Failure != null) { Stop(); _say(_objective.Failure); return; }
            _objective.ObserveAcknowledgement();
            Room encounterRoom = DynelManager.LocalPlayer.Room;
            if (_transition == null && _layout.IsInside(encounterRoom.Instance,
                DynelManager.LocalPlayer.Position, 0.4f))
                _objective.TryActivatePersonEncounter(encounterRoom, _layout);
            if (_objective.Failure != null) { Stop(); _say(_objective.Failure); return; }
            ReopenOccupiedRooms();
            ObservePostCombatCorpseGuard();
            // Evidence reconciliation and the 90-second deadline must advance
            // even when combat/readiness gates retain action ownership. This
            // pass never moves, halts, opens, or otherwise steals combat.
            if (ReconcilePostCombatCorpseGuards(DynelManager.LocalPlayer.Room)) return;
            foreach (Room occupied in Playfield.Rooms.Where(x =>
                EnemyCandidates(x).Any(enemy => !_objective.IsObjective(enemy.Identity))))
                _combatCheckedRooms.Remove(occupied.Instance);
            bool ordinaryEnemiesRemain = Playfield.Rooms.Any(x =>
                EnemyCandidates(x).Any(enemy => !_objective.IsObjective(enemy.Identity)));
            bool allRoomsChecked = Playfield.Rooms.All(x =>
                _clearedRooms.Contains(x.Instance) || _combatCheckedRooms.Contains(x.Instance));
            _objective.FinalActionsAllowed = _objective.PersonEncounterActive || allRoomsChecked && !ordinaryEnemiesRemain &&
                (_loot.UnfinishedMissionLootCount == 0 ||
                    ((_objective.HasKillTarget || _objective.HasFindPersonTarget) &&
                        _combatCheckedRooms.Count > 0));
            bool reservedAggro = GuardReservedEnemy(ordinaryEnemiesRemain);
            if (!reservedAggro || !_objective.Finale || !_objective.FinalActionsAllowed)
                ResetFindPersonFinaleRouteWait();
            if (!IsRunning) return;
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
                _recordedRoomReturn = null;
                _floorsVisited.Add(floor);
                LoadFloor();
                _activeLift = null;
                _liftStarted = DateTime.MinValue;
                _currentRoom = -1;
                _combatTopologyHandoff = null;
                _destination = null;
                _roomQuietAt = DateTime.MinValue;
            }

            Room room = DynelManager.LocalPlayer.Room;
            if (_objectiveApproachTarget.HasValue &&
                (_objectiveApproachRoom != room.Instance || EnemyCandidates(room).Any()))
                ResetObjectiveApproach();
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
                _combatTopologyHandoff = null;
                _visitedRooms.Add(room.Instance);
                if (!_clearedRooms.Contains(room.Instance)) _roomStates[room.Instance] = RoomClearanceState.Entered;
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
                _loot.ObserveMissionRoomTraversal();
                _visitedRooms.Add(room.Instance);
                if (!_clearedRooms.Contains(room.Instance)) _roomStates[room.Instance] = RoomClearanceState.Entered;
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
                _roomStates[room.Instance] = RoomClearanceState.Surveyed;
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
            if (TickCombatRecovery(room)) return;
            if (_objective.Finale && _objective.FinalActionsAllowed &&
                _objective.HasKillTarget && !_objective.RewardConfirmed &&
                TickKillPersonFinale(room)) return;
            if (_objective.PersonEncounterActive && _objective.HasFindPersonTarget &&
                !_objective.RewardConfirmed && _objective.PendingRoom == room.Instance &&
                !EnemyCandidates(room).Any())
            {
                // Exact FindPerson aggro must not send us back to a global sweep
                // once its room has been reached. Observe/acknowledge it here.
                TickObjective(room);
                if (_objective.Failure != null) { Stop(); _say(_objective.Failure); }
                _roomQuietAt = DateTime.MinValue;
                return;
            }
            if (_readiness.InCombat)
            {
                if (FightInRoom(room))
                {
                    _roomQuietAt = DateTime.MinValue;
                    return;
                }
                if (_combatRecoveries.TryGetValue(room.Instance, out CombatRecovery activeCombat) &&
                    activeCombat.Active)
                {
                    MovementArbiter.Current.Halt(_requestedOwner);
                    CombatRecoveryWaiting = true;
                    return;
                }
                if (reservedAggro && (_objective.HasKillTarget || _objective.HasFindPersonTarget))
                {
                    // The reserved target's aggro prevents chest interaction.
                    // Sweep every room for ordinary enemies, then give the
                    // exact objective its normal finale ownership. Deferred
                    // loot remains pending until combat permits interaction.
                    if (ScanRemainingRoom(room)) return;
                    _combatCheckedRooms.Add(room.Instance);
                    _roomStates[room.Instance] = RoomClearanceState.CombatChecked;
                    if (Playfield.Rooms.All(x => _clearedRooms.Contains(x.Instance) ||
                            _combatCheckedRooms.Contains(x.Instance)) && !ordinaryEnemiesRemain)
                    {
                        _objective.FinalActionsAllowed = true;
                        if (!_objective.Finale && !BeginObjectiveFinale()) return;
                        if (FightInRoom(room)) return;
                        if (ScanRemainingRoom(room)) return;
                        int? objectiveRoom = _objective.PendingRoom;
                        if (objectiveRoom.HasValue && objectiveRoom.Value != room.Instance)
                        {
                            if (RouteToRoom(objectiveRoom.Value))
                            {
                                ResetFindPersonFinaleRouteWait();
                                return;
                            }
                            if (_objective.HasFindPersonTarget &&
                                HoldFindPersonFinaleRoute(room, objectiveRoom.Value)) return;
                        }
                        else ResetFindPersonFinaleRouteWait();
                    }
                    else
                    {
                        Room nextCombatRoom = NextCombatSweepRoom(room);
                        if (nextCombatRoom != null)
                        { BeginTransition(room.Instance, nextCombatRoom.Instance); return; }
                        if (!WaitingForRoute)
                        {
                            if (_combatRecoveries.Count > 0)
                            {
                                CombatRecoveryWaiting = true;
                                return;
                            }
                            Stop();
                            _say("Ordinary-enemy sweep cannot reach every room; reserved objective remains held.");
                            return;
                        }
                    }
                }
                MovementArbiter.Current.Halt(_requestedOwner);
                if (_objective.Finale && _objective.FinalActionsAllowed)
                    TickObjective(room);
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
            if (_combatRecoveries.TryGetValue(room.Instance, out CombatRecovery deferredCombat) &&
                deferredCombat.Active)
            {
                MovementArbiter.Current.Halt(_requestedOwner);
                CombatRecoveryWaiting = true;
                return;
            }
            SimpleChar remainingEnemy = EnemiesInRoom(room).FirstOrDefault();
            if (remainingEnemy != null)
            {
                // A scan that found no usable waypoint is never clearance
                // evidence while a live enemy still occupies this room.
                BeginCombatRecovery(room, remainingEnemy);
                return;
            }

            if (HoldForPostCombatCorpse(room)) return;

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
            _roomStates[room.Instance] = RoomClearanceState.Cleared;
            if (_objective.Failure != null) { Stop(); _say(_objective.Failure); return; }
            Room next = NextRoom(room);
            if (next == null)
            {
                if (_loot.DeferredMissionLootWaitingForTraversal)
                {
                    Room contextRoom = RouteTo(room.Instance, id => id != room.Instance, false);
                    if (contextRoom != null)
                    {
                        _say($"Ordinary loot is deferred in room {room.Instance}; traversing room {contextRoom.Instance} " +
                            "before its independent retry.");
                        BeginTransition(room.Instance, contextRoom.Instance);
                        return;
                    }
                }
                Room deferredLootHop = RouteTo(room.Instance,
                    id => _loot.HasReadyDeferredMissionLootInRoom(id), false);
                if (deferredLootHop != null)
                {
                    _deferredLootRouteWaitStarted = DateTime.MinValue;
                    _say($"Traversal requirement satisfied; routing through room {deferredLootHop.Instance} toward ready deferred loot retry.");
                    BeginTransition(room.Instance, deferredLootHop.Instance);
                    return;
                }
                if (_loot.HasReadyDeferredMissionLoot && WaitingForRoute)
                {
                    MovementArbiter.Current.Halt(_requestedOwner);
                    if (_deferredLootRouteWaitStarted == DateTime.MinValue)
                    {
                        _deferredLootRouteWaitStarted = DateTime.UtcNow;
                        _say("Deferred ordinary loot is ready for its bounded retry; waiting for the temporary room-route cooldown to expire without releasing ManagerLoot control.");
                    }
                    if (DateTime.UtcNow - _deferredLootRouteWaitStarted <= DeferredLootRouteWaitLimit)
                        return;
                    string blockers = _loot.MissionLootBlockers;
                    Stop();
                    _say($"Deferred ordinary-loot return route remained unavailable for {DeferredLootRouteWaitLimit.TotalSeconds:0} seconds; " +
                        $"mission stopped without completing or exiting. {blockers}.");
                    return;
                }
                _deferredLootRouteWaitStarted = DateTime.MinValue;
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
                    if (_combatRecoveries.Count > 0)
                    {
                        CombatRecoveryWaiting = true;
                        return; // Unfinished combat room remains a future route goal.
                    }
                    if (WaitingForRoute) return;
                    Stop();
                    _say("No further reachable rooms; failed doors/routes or missing Mali room geometry remain.");
                    return;
                }
                if (_loot.UnfinishedMissionLootCount > 0)
                {
                    if (_loot.UnfinishedMissionLootCount > _loot.CombatBlockedMissionLootCount)
                    {
                        string blockers = _loot.MissionLootBlockers;
                        Stop();
                        _say($"Ordinary loot remains unfinished; objective completion and automatic exit are held. {blockers}. Check loot logs, rules and free space.");
                        return;
                    }
                }
                if (!_objective.HasSteps)
                {
                    Stop();
                    if (!_objective.PreflightFinale(out string objectiveFailure))
                        _say($"Mission objective metadata could not be reconstructed; automatic completion is held. {objectiveFailure}");
                    else
                        _say("No supported mission objective metadata is available; automatic completion is held.");
                    return;
                }
                if (!_objective.Finale)
                {
                    MovementArbiter.Current.Halt(_requestedOwner);
                    if (_loot.SkippedMissionLootCount > 0)
                        _say($"{_loot.SkippedMissionLootCount} ordinary loot source(s) skipped this mission; continuing to the reserved objective.");
                    if (_loot.ReservedPendingMissionLootCount > 0)
                        _say($"{_loot.ReservedPendingMissionLootCount} reserved objective loot entries are final work, excluded from the ordinary-loot gate.");
                    if (!BeginObjectiveFinale()) return;
                    _roomQuietAt = DateTime.MinValue;
                    // Before any no-rooms-left/exit decision, give an unresolved
                    // objective already in the current room its action turn.
                    // Deferred mission loot and combat have already won above.
                    if (_objective.PendingRoom == room.Instance)
                        TickObjective(room);
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
            bool navigated = false;
            bool handled = _objective.Tick(room, _layout, target =>
            {
                navigated = true;
                return NavigateObjective(room, target);
            }, _loot);
            // Interaction readiness and completed/failed steps bypass navigation.
            if (!navigated || _objective.Failure != null) ResetObjectiveApproach();
            return handled;
        }

        private bool BeginObjectiveFinale()
        {
            if (_objective.PreflightFinale(out string reason))
            {
                _objective.BeginFinale();
                ResetKillFinaleOwnership();
                return true;
            }
            Stop();
            _say("Objective finale preflight failed: " + reason);
            return false;
        }

        // KillPerson is intentionally excluded from MissionObjective.Tick: guarded
        // combat owns it. During the finale, bind that ownership to the exact quest
        // identity instead of relying on the ordinary room candidate scan.
        private bool TickKillPersonFinale(Room room)
        {
            Identity targetId = _objective.KillTarget;
            if (targetId == Identity.None) return false;
            DateTime now = DateTime.UtcNow;
            Dynel live = DynelManager.GetDynel(targetId);
            SimpleChar enemy = live == null ? null : new SimpleChar(live);
            int? mappedRoom = _objective.RoomFor(targetId);

            if (enemy != null && enemy.IsValid && enemy.IsAlive)
            {
                Room liveRoom = Playfield.Rooms.FirstOrDefault(candidate =>
                    _layout.ContainsDynel(candidate.Instance, enemy));
                // Refresh repairs stale remembered room membership from Mali's
                // current live containment before any route decision.
                if (liveRoom != null && mappedRoom != liveRoom.Instance)
                {
                    _objective.Refresh(_layout);
                    mappedRoom = _objective.RoomFor(targetId);
                }
            }

            bool combatOwns = _combat.Target == targetId;
            bool movementOwns = MovementArbiter.Current.Owner == MovementOwner.CombatPosition ||
                MovementArbiter.Current.Owner == MovementOwner.DoorTransition;
            string state;
            if (enemy == null || !enemy.IsValid || !enemy.IsAlive)
            {
                state = mappedRoom.HasValue && mappedRoom.Value != room.Instance
                    ? "routing to remembered room" : "bounded reacquire wait";
                if (mappedRoom.HasValue && mappedRoom.Value != room.Instance)
                {
                    LogKillFinale(targetId, null, mappedRoom, room, state, combatOwns, movementOwns, false);
                    if (RouteToRoom(mappedRoom.Value)) return true;
                }
                if (_combat.Target == targetId) _combat.Reset();
                if (MovementArbiter.Current.Owner == MovementOwner.CombatPosition)
                    MovementArbiter.Current.Release(MovementOwner.CombatPosition);
                movementOwns = MovementArbiter.Current.Owner == MovementOwner.DoorTransition;
                LogKillFinale(targetId, null, mappedRoom, room, state, false, movementOwns, false);
                ReclaimKillFinaleIfUnowned(now, targetId, mappedRoom, room, state, false, movementOwns);
                return true;
            }

            if (mappedRoom.HasValue && mappedRoom.Value != room.Instance)
            {
                state = "routing to live mapped room";
                LogKillFinale(targetId, enemy, mappedRoom, room, state, combatOwns, movementOwns, false);
                if (RouteToRoom(mappedRoom.Value)) return true;
                ReclaimKillFinaleIfUnowned(now, targetId, mappedRoom, room, state, combatOwns, movementOwns);
                return true;
            }

            state = enemy.DistanceFrom(DynelManager.LocalPlayer) > EngagementRange
                ? "same-room exact-target transit/approach" : "exact-target combat handoff";
            _requestedOwner = MovementOwner.CombatPosition;
            _scanTarget = Identity.None;
            _scanWaypoints.Clear();
            _loot.MissionActionsPaused = true;
            RememberCombatEngagement(room, enemy);
            CombatTickResult result = _combat.Tick(enemy, room.Instance,
                target => ApproachCombatEnemy(target, room.Instance),
                () => { _waitingForLoot = Identity.None; ResetCombatApproach(); }, EngagementRange);
            if (_combat.Target == targetId) _objective.ArmKill(enemy);
            if (result == CombatTickResult.ApproachUnavailable)
                BeginCombatRecovery(room, enemy);
            combatOwns = _combat.Target == targetId;
            movementOwns = MovementArbiter.Current.Owner == MovementOwner.CombatPosition;
            LogKillFinale(targetId, enemy, mappedRoom, room, state, combatOwns, movementOwns, false);
            ReclaimKillFinaleIfUnowned(now, targetId, mappedRoom, room, state, combatOwns, movementOwns);
            return true;
        }

        private void ReclaimKillFinaleIfUnowned(DateTime now, Identity target, int? mappedRoom,
            Room current, string state, bool combatOwns, bool movementOwns)
        {
            if (combatOwns || movementOwns || _transition != null ||
                _combatRecoveries.Values.Any(x => x.Active))
            {
                _killFinaleOwnershipAt = now;
                return;
            }
            if (_killFinaleOwnershipAt == DateTime.MinValue) _killFinaleOwnershipAt = now;
            if (now - _killFinaleOwnershipAt < KillFinaleOwnershipWatchdog) return;
            _killFinaleOwnershipAt = now;
            _combat.Reset();
            ResetCombatApproach();
            _destination = null;
            MovementArbiter.Current.Halt(MovementOwner.CombatPosition);
            _say($"KillPerson finale had no combat or movement owner for " +
                $"{KillFinaleOwnershipWatchdog.TotalSeconds:0}s; reclaiming exact target {target}. " +
                $"mapped room={mappedRoom?.ToString() ?? "unknown"}, current room={current.Instance}, state={state}.");
        }

        private void LogKillFinale(Identity target, SimpleChar live, int? mappedRoom,
            Room current, string state, bool combatOwns, bool movementOwns, bool force)
        {
            DateTime now = DateTime.UtcNow;
            if (!force && _killFinaleState == state &&
                now - _killFinaleLastDiagnostic < TimeSpan.FromSeconds(7)) return;
            _killFinaleState = state;
            _killFinaleLastDiagnostic = now;
            string liveDetail = live == null ? "live=false" :
                $"live=true, distance={live.DistanceFrom(DynelManager.LocalPlayer):0.0}m, target Y={live.Position.Y:0.0}";
            _say($"KillPerson finale ownership: target={target}, {liveDetail}, " +
                $"mapped room={mappedRoom?.ToString() ?? "unknown"}, current room={current.Instance}, " +
                $"final actions={_objective.FinalActionsAllowed}, movement owner={MovementArbiter.Current.Owner}, " +
                $"combat target={_combat.Target}, combat owns={combatOwns}, movement owns={movementOwns}, state={state}.");
        }

        private void ResetKillFinaleOwnership()
        {
            _killFinaleOwnershipAt = DateTime.MinValue;
            _killFinaleLastDiagnostic = DateTime.MinValue;
            _killFinaleState = null;
        }

        private void RememberCombatEngagement(Room room, SimpleChar enemy)
        {
            if (room == null || enemy == null || !enemy.IsValid || !enemy.IsAlive) return;
            if (_engagedEnemy != enemy.Identity)
            {
                _engagementExistingCorpses.Clear();
            }
            int verifiedRoom = VerifiedDynelRoom(enemy, room.Instance);
            RememberExistingCorpses(verifiedRoom);
            _engagedEnemy = enemy.Identity;
            _engagedEnemyRoom = verifiedRoom;
            _engagedEnemyPosition = enemy.Position;
        }

        private void ObservePostCombatCorpseGuard()
        {
            DateTime now = DateTime.UtcNow;
            foreach (PostCombatCorpseGuard guard in _postCombatCorpseGuards.Where(x => x.Corpse == Identity.None).ToList())
            {
                var candidates = Playfield.Rooms.SelectMany(candidateRoom =>
                    _layout.VisibleRoomDynels(candidateRoom.Instance).Where(x =>
                        x.Identity.Type == IdentityType.Corpse &&
                        _layout.ContainsDynel(candidateRoom.Instance, x))
                    .Select(x => new { Corpse = x, RoomId = candidateRoom.Instance }))
                    .Where(x =>
                        !_postCombatCorpseGuards.Any(other => other != guard && other.Corpse == x.Corpse.Identity) &&
                        !guard.ExistingCorpses.Contains(x.Corpse.Identity))
                    .ToList();
                var exact = candidates.Where(x =>
                    x.Corpse.Identity.Instance == guard.Enemy.Instance).ToList();
                var nearby = candidates.Where(x => x.RoomId == guard.RoomId &&
                    Vector3.Distance(x.Corpse.Position, guard.DeathPosition) <= 6f).ToList();
                var match = exact.Count == 1 ? exact[0] :
                    exact.Count == 0 && nearby.Count == 1 ? nearby[0] : null;
                Dynel corpse = match?.Corpse;
                if (corpse != null)
                {
                    int verifiedRoom = VerifiedDynelRoom(corpse, match.RoomId);
                    long occurrence = _loot.RegisterOutstandingMissionLoot(corpse.Identity, verifiedRoom);
                    if (occurrence == 0) continue; // Door crossings temporarily release ManagerLoot room ownership.
                    _say($"Post-combat corpse guard matched {corpse.Identity} in verified room {verifiedRoom} " +
                        $"after {(now - guard.Started).TotalSeconds:0.0}s; engagement room was {guard.RoomId}; registered as outstanding ManagerLoot work.");
                    guard.RoomId = verifiedRoom;
                    guard.Corpse = corpse.Identity;
                    guard.AcquiredAt = now;
                    guard.LastDiagnostic = now;
                    guard.CorpseOccurrence = occurrence;
                }
            }

            if (_engagedEnemy == Identity.None) return;
            Dynel observed = DynelManager.GetDynel(_engagedEnemy);
            if (observed != null && observed.Identity.Type == IdentityType.SimpleChar)
            {
                SimpleChar enemy = new SimpleChar(observed);
                if (enemy.IsValid && enemy.IsAlive)
                {
                    int verifiedRoom = VerifiedDynelRoom(enemy, _engagedEnemyRoom);
                    if (verifiedRoom != _engagedEnemyRoom)
                    {
                        RememberExistingCorpses(verifiedRoom);
                        _say($"Combat target {_engagedEnemy} crossed from room {_engagedEnemyRoom} to verified room {verifiedRoom}; corpse ownership will follow its observed location.");
                        _engagedEnemyRoom = verifiedRoom;
                    }
                    _engagedEnemyPosition = enemy.Position;
                    return;
                }
            }
            var player = DynelManager.LocalPlayer;
            if (player != null && (player.IsAttackPending ||
                (player.IsAttacking && player.FightingTarget?.Identity == _engagedEnemy))) return;

            var armed = new PostCombatCorpseGuard
            {
                Enemy = _engagedEnemy,
                RoomId = _engagedEnemyRoom,
                DeathPosition = _engagedEnemyPosition,
                Started = now,
                LastDiagnostic = now
            };
            armed.ExistingCorpses.UnionWith(_engagementExistingCorpses);
            _postCombatCorpseGuards.Add(armed);
            _say($"Post-combat corpse guard armed for RKMission target {_engagedEnemy} in room {_engagedEnemyRoom}; " +
                $"waiting up to {PostCombatCorpseMaterializationWait.TotalSeconds:0}s for a new nearby corpse before clearance/routing.");
            _engagedEnemy = Identity.None;
            _engagedEnemyRoom = -1;
            _engagementExistingCorpses.Clear();
        }

        private bool PostCombatCorpseGuardActive(int roomId) =>
            _postCombatCorpseGuards.Any(guard => guard.RoomId == roomId);

        private bool HoldForPostCombatCorpse(Room room)
        {
            if (_postCombatCorpseGuards.Count == 0) return false;
            if (ReconcilePostCombatCorpseGuards(room)) return true;
            if (_postCombatCorpseGuards.Count == 0) return false;
            bool localOutstanding = room != null &&
                _postCombatCorpseGuards.Any(guard => guard.RoomId == room.Instance);
            PostCombatCorpseGuard wrongRoom = localOutstanding ? null :
                _postCombatCorpseGuards.FirstOrDefault(guard =>
                    room == null || room.Instance != guard.RoomId);
            if (wrongRoom != null)
            {
                MovementArbiter.Current.Halt(_requestedOwner);
                _destination = null;
                if (room != null && wrongRoom.RoomId >= 0 && RouteToRoom(wrongRoom.RoomId))
                {
                    _say($"Post-combat corpse recovery routing from room {room.Instance} toward verified outstanding room {wrongRoom.RoomId} for " +
                        $"{(wrongRoom.Corpse == Identity.None ? wrongRoom.Enemy : wrongRoom.Corpse)}.");
                    return true;
                }
                if (WaitingForRoute) return true;
                Stop();
                _say($"Post-combat corpse guard terminal safe-stop: no mapped route from room {room?.Instance.ToString() ?? "unknown"} to verified outstanding room {wrongRoom.RoomId} for {(wrongRoom.Corpse == Identity.None ? wrongRoom.Enemy : wrongRoom.Corpse)}.");
                return true;
            }
            DateTime now = DateTime.UtcNow;
            MovementArbiter.Current.Halt(_requestedOwner);
            _destination = null;
            _roomQuietAt = DateTime.MinValue;
            foreach (PostCombatCorpseGuard guard in _postCombatCorpseGuards.Where(x =>
                now - x.LastDiagnostic >= (x.Corpse == Identity.None ?
                    TimeSpan.FromSeconds(2) : TimeSpan.FromSeconds(10))))
            {
                guard.LastDiagnostic = now;
                TimeSpan elapsed = now - guard.Started;
                _say(guard.Corpse == Identity.None
                    ? $"Post-combat corpse guard rescanning room {guard.RoomId} for target {guard.Enemy}; elapsed={elapsed.TotalSeconds:0.0}s/{PostCombatCorpseMaterializationWait.TotalSeconds:0}s."
                    : $"Post-combat corpse guard holding room {guard.RoomId} for ManagerLoot settlement of {guard.Corpse}; visible={DynelManager.GetDynel(guard.Corpse) != null}, processing={_loot.ProcessingMissionLootIdentity}.");
            }
            return true;
        }

        private bool ReconcilePostCombatCorpseGuards(Room room)
        {
            if (_postCombatCorpseGuards.Count == 0) return false;
            foreach (PostCombatCorpseGuard settled in _postCombatCorpseGuards.Where(guard =>
                guard.Corpse != Identity.None && _loot.IsMissionLootSuccessfullyProcessed(
                    guard.Corpse, guard.RoomId, guard.CorpseOccurrence)).ToList())
            {
                _say($"Post-combat corpse guard completed {settled.Corpse} in room {settled.RoomId}; exact ContainerOpened and processing-finished evidence confirmed, outstanding corpse ownership released.");
                _postCombatCorpseGuards.Remove(settled);
            }
            if (_postCombatCorpseGuards.Count == 0) return false;
            DateTime now = DateTime.UtcNow;
            // A guarded corpse cannot wait for a room traversal that the same guard forbids.
            // Release only its exact deferred occurrence for a bounded in-room reapproach.
            foreach (PostCombatCorpseGuard guarded in _postCombatCorpseGuards.Where(guard =>
                guard.Corpse != Identity.None && room != null && guard.RoomId == room.Instance &&
                guard.InRoomRetryCount < MaxGuardedCorpseSameRoomRetries &&
                now >= guard.NextInRoomRetryAt).ToList())
            {
                Dynel visible = DynelManager.GetDynel(guarded.Corpse);
                if (visible == null || !_layout.ContainsDynel(guarded.RoomId, visible)) continue;
                if (_loot.TryRetryGuardedDeferredCorpse(guarded.Corpse, guarded.RoomId,
                    guarded.CorpseOccurrence))
                {
                    guarded.InRoomRetryCount++;
                    guarded.NextInRoomRetryAt = now + GuardedCorpseRetryCooldown;
                    guarded.LastDiagnostic = now;
                    _say($"Post-combat corpse guard released exact deferred {guarded.Corpse} in room {guarded.RoomId} " +
                        $"for bounded same-room reapproach {guarded.InRoomRetryCount}/{MaxGuardedCorpseSameRoomRetries}; " +
                        "corpse still requires ContainerOpened and finished processing proof.");
                }
            }
            foreach (PostCombatCorpseGuard skipped in _postCombatCorpseGuards.Where(guard =>
                guard.Corpse != Identity.None && _loot.TryGetMissionLootSkipReason(guard.Corpse, out _)).ToList())
            {
                _loot.TryGetMissionLootSkipReason(skipped.Corpse, out string reason);
                Dynel visible = DynelManager.GetDynel(skipped.Corpse);
                if (visible != null && _layout.ContainsDynel(skipped.RoomId, visible))
                {
                    skipped.SkipInvisibleAt = DateTime.MinValue;
                    // A visible, terminally skipped corpse is never settled, but retries
                    // must remain bounded and cannot repeatedly clear the retry ledger.
                    if (skipped.InRoomRetryCount < MaxGuardedCorpseSameRoomRetries &&
                        now >= skipped.NextInRoomRetryAt)
                    {
                        _say($"Post-combat corpse guard rejected skip for visible {skipped.Corpse} in room {skipped.RoomId}: {reason}; exact processing evidence is absent, retrying this corpse.");
                        _loot.RetryVisibleOutstandingMissionLoot(skipped.Corpse, skipped.RoomId, reason);
                        skipped.InRoomRetryCount++;
                        skipped.NextInRoomRetryAt = now + GuardedCorpseRetryCooldown;
                        skipped.LastDiagnostic = now;
                    }
                }
                else if (skipped.SkipInvisibleAt == DateTime.MinValue)
                {
                    skipped.SkipInvisibleAt = now;
                    skipped.LastDiagnostic = now;
                    _say($"Post-combat corpse guard retaining skipped {skipped.Corpse} in room {skipped.RoomId}: {reason}; " +
                        $"currently not visible, allowing {PostCombatCorpseSkipVisibilityGrace.TotalSeconds:0}s for a transient visibility recovery.");
                }
                else if (now - skipped.SkipInvisibleAt >= PostCombatCorpseSkipVisibilityGrace)
                {
                    _say($"Post-combat corpse guard accepted explicit skip for {skipped.Corpse} in room {skipped.RoomId}: {reason}; corpse is no longer visible in its room.");
                    _postCombatCorpseGuards.Remove(skipped);
                }
            }
            foreach (PostCombatCorpseGuard expired in _postCombatCorpseGuards.Where(guard =>
                guard.Corpse == Identity.None && now - guard.Started >= PostCombatCorpseMaterializationWait).ToList())
            {
                _say($"Post-combat corpse guard timed out after {(now - expired.Started).TotalSeconds:0.0}s for target {expired.Enemy} in room {expired.RoomId}; no new nearby corpse became visible, so that bounded guard is skipped.");
                _postCombatCorpseGuards.Remove(expired);
            }
            PostCombatCorpseGuard failed = _postCombatCorpseGuards.FirstOrDefault(guard =>
                guard.Corpse != Identity.None && now - guard.AcquiredAt >= PostCombatCorpseSettlementWait);
            if (failed != null)
            {
                Dynel visible = DynelManager.GetDynel(failed.Corpse);
                bool visibleInOwnedRoom = visible != null && _layout.ContainsDynel(failed.RoomId, visible);
                string unavailableReason = $"not visible in verified room {failed.RoomId} for the full " +
                    $"{PostCombatCorpseSettlementWait.TotalSeconds:0}-second settlement window; exact ContainerOpened and processing-finished evidence absent";
                string refusal = null;
                if (!visibleInOwnedRoom && _loot.TrySkipUnavailableMissionLoot(failed.Corpse,
                    failed.RoomId, failed.CorpseOccurrence, unavailableReason, out refusal))
                {
                    _say($"Post-combat corpse guard explicitly skipped unavailable ordinary corpse {failed.Corpse} " +
                        $"in room {failed.RoomId} without successful-loot credit: {unavailableReason}.");
                    _postCombatCorpseGuards.Remove(failed);
                    return false;
                }
                Stop();
                _say($"Post-combat corpse guard safe-stop: {failed.Corpse} in room {failed.RoomId} did not reach a settled ManagerLoot state within {PostCombatCorpseSettlementWait.TotalSeconds:0}s; " +
                    $"visible in owned room={visibleInOwnedRoom}; unavailable-skip refusal={refusal ?? "source remained visible"}. {_loot.MissionLootBlockers}");
                return true;
            }
            return !IsRunning;
        }

        private void ResetPostCombatCorpseTracking()
        {
            _postCombatCorpseGuards.Clear();
            _engagedEnemy = Identity.None;
            _engagedEnemyRoom = -1;
            _engagementExistingCorpses.Clear();
        }

        private void ResetObjectiveApproach()
        {
            if (_objectiveTopologyActive) _navRoutes?.StopPlayback();
            if (_objectiveApproachTarget.HasValue)
            {
                MovementArbiter.Current.Halt(MovementOwner.Objective);
                _destination = null;
            }
            _objectiveTopologyActive = false;
            _objectiveApproachTarget = null;
            _objectiveApproachRoom = -1;
            _objectiveApproaches.Clear();
            _objectiveApproachIndex = 0;
            _objectiveBestDistance = float.MaxValue;
            _objectiveProgressAt = DateTime.MinValue;
            _objectiveTopologyStarted = DateTime.MinValue;
        }

        private void BuildObjectiveApproaches(Room room, Vector3 position, Vector3 target)
        {
            _objectiveApproaches.Clear();
            _objectiveApproaches.AddRange(DungeonApproachPlanner.Candidates(_layout,
                room.Instance, position, target, new[] { 1.1f, 1.7f }, 0.25f));
            _objectiveApproachIndex = 0;
            _objectiveProgressAt = DateTime.UtcNow;
            _objectiveBestDistance = float.MaxValue;
            _destination = null;
            _say($"Objective approach: {_objectiveApproaches.Count} complete mapped route(s) to {target}.");
        }

        private bool NavigateObjective(Room room, Vector3 target)
        {
            Vector3 position = DynelManager.LocalPlayer.Position;
            DateTime now = DateTime.UtcNow;
            if (!_objectiveApproachTarget.HasValue || _objectiveApproachRoom != room.Instance ||
                Vector3.Distance(_objectiveApproachTarget.Value, target) > 1f)
            {
                ResetObjectiveApproach();
                _objectiveApproachTarget = target;
                _objectiveApproachRoom = room.Instance;
                BuildObjectiveApproaches(room, position, target);
            }

            if (_objectiveTopologyActive)
            {
                if (now - _objectiveTopologyStarted >= ObjectiveTopologyTimeout ||
                    _navRoutes == null || !_navRoutes.TryNavigatePreparedDungeonTopology(
                        target, MovementArbiter.Current, MovementOwner.Objective, out bool completed))
                {
                    _say("Objective recorded topology failed or timed out; this room/target will not restart it.");
                    ResetObjectiveApproach();
                    return false;
                }
                if (!completed) return true;
                _objectiveTopologyActive = false;
                MovementArbiter.Current.Halt(MovementOwner.Objective);
                // Let MissionObjective recheck its existing range/ray gates on the
                // next tick. A completed route can finish inside interaction range.
                BuildObjectiveApproaches(room, position, target);
                return true;
            }

            // Generated complete approaches remain the first choice.
            while (_objectiveApproachIndex < _objectiveApproaches.Count)
            {
                Vector3 point = _objectiveApproaches[_objectiveApproachIndex];
                float distance = Vector3.Distance(position, point);
                if (distance + 0.3f < _objectiveBestDistance)
                { _objectiveBestDistance = distance; _objectiveProgressAt = now; }
                if (_destination.HasValue && (distance < 0.8f ||
                    now - _objectiveProgressAt > TimeSpan.FromSeconds(7)))
                {
                    _objectiveApproachIndex++;
                    MovementArbiter.Current.Halt(MovementOwner.Objective);
                    _destination = null;
                    _objectiveBestDistance = float.MaxValue;
                    _objectiveProgressAt = now;
                    continue;
                }
                if (_destination.HasValue && SMovementController.IsNavigating()) return true;
                if (MovementArbiter.Current.SetNavDestination(_requestedOwner, point))
                { _destination = point; return true; }
                _objectiveApproachIndex++;
            }

            // One preparation attempt per room/target for the mission, including
            // unavailable recordings. Resets and combat interruptions cannot loop it.
            if (Math.Abs(target.Y - position.Y) >= 1.5f &&
                !_objectiveTopologyAttempts.Any(attempt => attempt.Item1 == room.Instance &&
                    Vector3.Distance(attempt.Item2, target) <= 1f))
            {
                _objectiveTopologyAttempts.Add(Tuple.Create(room.Instance, target));
                if (_navRoutes != null && _navRoutes.TryPrepareDungeonTopology(_layout,
                    room.Instance, position, target, out string routeName))
                {
                    _objectiveTopologyActive = true;
                    _objectiveTopologyStarted = now;
                    _destination = null;
                    MovementArbiter.Current.Halt(MovementOwner.Objective);
                    _say($"Objective recorded topology '{routeName}' prepared in room {room.Instance}.");
                    return true;
                }
            }
            ResetObjectiveApproach();
            return false;
        }

        private bool HoldForReadiness()
        {
            Room liveRoom = DynelManager.LocalPlayer?.Room;
            bool roomReady = !_exiting && _layout != null && liveRoom != null &&
                _currentRoom == liveRoom.Instance;
            bool threat = roomReady && EnemyCandidates(liveRoom).Any();
            bool immediateWork = threat || (roomReady && (_loot.HasPendingMissionCorpse(liveRoom.Instance) ||
                PostCombatCorpseGuardActive(liveRoom.Instance)));
            if (immediateWork && !_readiness.InCombat)
            {
                if (threat || (!_readiness.EmergencyRecoveryNeeded && !_readiness.EmergencyRecoveryActive))
                    return false;
                if (_readiness.HoldEmergency())
                {
                    _combat.Pause();
                    _loot.MissionActionsPaused = true;
                    if (_readiness.Failure != null) { _say(_readiness.Failure); Stop(); }
                    return true;
                }
                return false;
            }
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
                    // Once selected, keep approaching this room's enemy even
                    // if pathing briefly moves us across the 20m scan boundary.
                    (x.Identity == _combat.Target && _layout.ContainsDynel(room.Instance, x)) ||
                    (player.IsAttacking && player.FightingTarget?.Identity == x.Identity))
                .OrderBy(x => _combatRecoveries.TryGetValue(room.Instance, out CombatRecovery deferred) &&
                    deferred.Active && deferred.Enemy == x.Identity)
                .ThenByDescending(x => x.IsAttacking &&
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
            RememberCombatEngagement(room, enemy);

            if (enemy.IsInLineOfSight && enemy.IsInAttackRange(true))
                ResetCombatApproach();

            CombatTickResult result = _combat.Tick(enemy, room.Instance,
                target => ApproachCombatEnemy(target, room.Instance),
                () => { _waitingForLoot = Identity.None; ResetCombatApproach(); }, EngagementRange);
            if (result == CombatTickResult.ApproachUnavailable)
                BeginCombatRecovery(room, enemy);

            return true;
        }

        private void BeginCombatRecovery(Room room, SimpleChar enemy)
        {
            DateTime now = DateTime.UtcNow;
            Vector3 position = DynelManager.LocalPlayer.Position;
            if (!_combatRecoveries.TryGetValue(room.Instance, out CombatRecovery recovery) ||
                recovery.Enemy != enemy.Identity)
                _combatRecoveries[room.Instance] = recovery = new CombatRecovery { Enemy = enemy.Identity };
            bool attemptAvailable = recovery.Failures < MaxCombatTopologyAttempts;
            if (attemptAvailable) recovery.Failures++;
            recovery.Anchor = position;
            recovery.EnemyPosition = enemy.Position;
            recovery.RetryAt = now.AddSeconds(recovery.Failures == 1 ? 5 :
                recovery.Failures == 2 ? 15 : 60);
            recovery.Active = true;
            recovery.HoldReported = false;
            recovery.Waypoints.Clear();
            recovery.WaypointIndex = 0;
            recovery.TransitCost = float.PositiveInfinity;
            recovery.Transit = false;
            recovery.RecordedTransit = false;
            recovery.RecordedRoute = null;
            recovery.WaypointStarted = DateTime.MinValue;
            bool preferRecorded = attemptAvailable && !enemy.IsInLineOfSight &&
                Math.Abs(enemy.Position.Y - position.Y) >= CombatTopologyVerticalSeparation &&
                TryStartRecordedCombatTopology(room, enemy, recovery, position);
            if (preferRecorded)
                _say($"Combat matching recorded topology '{recovery.RecordedRoute}' selected before generated same-room routing; " +
                    $"retry={recovery.Failures}/{MaxCombatTopologyAttempts}.");
            DungeonTransitPlanner.Route route = !preferRecorded && attemptAvailable
                ? FindCombatTransitRoute(room, enemy, position) : null;
            if (route != null)
            {
                recovery.Waypoints.AddRange(route.Waypoints);
                recovery.TransitCost = route.Cost;
                recovery.Transit = true;
            }
            if (!recovery.Transit && !preferRecorded && attemptAvailable)
                TryStartRecordedCombatTopology(room, enemy, recovery, position);
            _combatCheckedRooms.Remove(room.Instance);
            _clearedRooms.Remove(room.Instance);
            _roomStates[room.Instance] = RoomClearanceState.CombatDeferred;
            _roomQuietAt = DateTime.MinValue;
            _destination = null;
            _scanTarget = Identity.None;
            _scanWaypoints.Clear();
            _scanWaypointIndex = 0;
            _combat.Reset();
            MovementArbiter.Current.Halt(_requestedOwner);
            MovementArbiter.Current.Halt(MovementOwner.CombatPosition);
            float distance = enemy.DistanceFrom(DynelManager.LocalPlayer);
            if (recovery.RecordedTransit)
                _say($"Combat local firing approach unavailable for {enemy.Identity} in room {room.Instance}; " +
                    $"same-room recorded topology '{recovery.RecordedRoute}' prepared, distance={distance:0.0}m, " +
                    $"player Y={position.Y:0.0}, target Y={enemy.Position.Y:0.0}, retry={recovery.Failures}/{MaxCombatTopologyAttempts}. " +
                    "Room remains unfinished.");
            else if (recovery.Transit)
                _say($"Combat local firing approach unavailable for {enemy.Identity} in room {room.Instance}; " +
                    $"same-room transit route started with {recovery.Waypoints.Count} waypoint(s), distance={distance:0.0}m, path cost={recovery.TransitCost:0.0}m, " +
                    $"player Y={position.Y:0.0}, target Y={enemy.Position.Y:0.0}, retry={recovery.Failures}/{MaxCombatTopologyAttempts}, " +
                    $"first={recovery.Waypoints[0]}. Room remains unfinished.");
            else
                _say($"Combat local firing approach unavailable for {enemy.Identity} in room {room.Instance}; " +
                    $"same-room transit failed (no complete route), distance={distance:0.0}m, path cost=unavailable, " +
                    $"player Y={position.Y:0.0}, target Y={enemy.Position.Y:0.0}, retry={recovery.Failures}/{MaxCombatTopologyAttempts}; " +
                    $"deferred until {recovery.RetryAt:HH:mm:ss} UTC. Room remains unfinished.");
        }

        private DungeonTransitPlanner.Route FindCombatTransitRoute(Room room, SimpleChar enemy, Vector3 position)
        {
            bool verticallySeparated = Math.Abs(enemy.Position.Y - position.Y) >=
                CombatTopologyVerticalSeparation;
            List<DungeonTransitPlanner.Route> candidates = DungeonTransitPlanner.Candidates(_layout,
                room.Instance, position, enemy.Position, new[] { 4f, 7f, 10f, 14f }, 0.4f)
                .Where(candidate => candidate.Waypoints.Count > 0 &&
                    Vector3.Distance(candidate.Destination, position) >= 3f)
                .OrderBy(candidate => verticallySeparated &&
                    !(candidate.TargetLevel || candidate.VerticalProgress >= 0.75f))
                .ThenByDescending(candidate => verticallySeparated ? candidate.VerticalProgress : 0)
                .ThenByDescending(candidate => candidate.CorridorDerived)
                .ThenBy(candidate => candidate.Cost)
                .ToList();
            DungeonTransitPlanner.Route route = candidates.FirstOrDefault(candidate =>
                DungeonTransitPlanner.IsPhysicallyProgressive(position, candidate.Waypoints));
            if (route == null && candidates.Count > 0)
            {
                DungeonTransitPlanner.Route rejected = candidates[0];
                _say($"Combat generated same-room route rejected: vertical-only leg or no physical walking progression " +
                    $"from {position} through {rejected.Waypoints.Count} waypoint(s).");
            }
            return route;
        }

        private bool TryStartRecordedCombatTopology(Room room, SimpleChar enemy,
            CombatRecovery recovery, Vector3 position)
        {
            if (_navRoutes == null || !_navRoutes.TryPrepareDungeonTopology(_layout,
                room.Instance, position, enemy.Position, out string routeName)) return false;
            MovementArbiter.Current.Halt(MovementOwner.CombatPosition);
            recovery.Waypoints.Clear();
            recovery.WaypointIndex = 0;
            recovery.RecordedTransit = true;
            recovery.RecordedRoute = routeName;
            recovery.Transit = true;
            recovery.TransitCost = float.PositiveInfinity;
            recovery.WaypointStarted = recovery.LastProgress = DateTime.UtcNow;
            recovery.LastPosition = position;
            _destination = null;
            return true;
        }

        private static float CombatRecoveryRouteCost(Vector3 from, Vector3 to)
        {
            try
            {
                return LocalRoutePlanner.TryDungeonCombatCost(from, to, out float cost)
                    ? cost : float.PositiveInfinity;
            }
            catch { return float.PositiveInfinity; }
        }

        private bool TickCombatRecovery(Room room)
        {
            if (!_combatRecoveries.TryGetValue(room.Instance, out CombatRecovery recovery))
                return false;
            SimpleChar enemy = EnemyCandidates(room).FirstOrDefault(x => x.Identity == recovery.Enemy);
            if (enemy == null)
            {
                if (recovery.RecordedTransit) _navRoutes?.StopPlayback();
                _combatRecoveries.Remove(room.Instance);
                _roomStates[room.Instance] = RoomClearanceState.Reopened;
                MovementArbiter.Current.Halt(MovementOwner.CombatPosition);
                _combat.Reset();
                ResetCombatApproach();
                return false;
            }
            if (!recovery.Active) return false;
            _roomStates[room.Instance] = RoomClearanceState.CombatDeferred;
            if (EnemyCandidates(room).Any(x => x.Identity != recovery.Enemy &&
                (x.IsAttacking && x.FightingTarget?.Identity == DynelManager.LocalPlayer.Identity ||
                 x.IsInLineOfSight && x.IsInAttackRange(true))))
                return false; // Handle an immediate different threat first.
            Vector3 position = DynelManager.LocalPlayer.Position;
            DateTime now = DateTime.UtcNow;
            bool targetMoved = Vector3.Distance(enemy.Position, recovery.EnemyPosition) >= 3f;
            bool newVantage = targetMoved ||
                (enemy.IsInLineOfSight && enemy.IsInAttackRange(true));
            if (newVantage && (now >= recovery.RetryAt ||
                enemy.IsInLineOfSight && enemy.IsInAttackRange(true)))
            {
                if (recovery.RecordedTransit)
                    PreserveCombatTopologyHandoff(room, enemy, recovery);
                recovery.Active = false;
                recovery.Waypoints.Clear();
                _roomStates[room.Instance] = RoomClearanceState.Reopened;
                _combat.Reset();
                ResetCombatApproach();
                _say($"Combat topology changed or a valid attack opening appeared in room {room.Instance}; rebuilding local firing approaches to {enemy.Identity}.");
                return false;
            }
            if (recovery.RecordedTransit)
            {
                _requestedOwner = MovementOwner.CombatPosition;
                bool completed = false;
                bool navigating = _navRoutes != null &&
                    _navRoutes.TryNavigatePreparedDungeonTopology(enemy.Position,
                        MovementArbiter.Current, _requestedOwner, out completed);
                if (Vector3.Distance(position, recovery.LastPosition) > 0.5f)
                {
                    recovery.LastPosition = position;
                    recovery.LastProgress = now;
                }
                if (!navigating || now - recovery.LastProgress > TimeSpan.FromSeconds(7) ||
                    now - recovery.WaypointStarted > TimeSpan.FromSeconds(45))
                {
                    FailCombatRecoveryRoute(recovery, "recorded topology failed/stalled");
                    return true;
                }
                float initialGap = Math.Abs(recovery.EnemyPosition.Y - recovery.Anchor.Y);
                float verticalProgress = initialGap - Math.Abs(enemy.Position.Y - position.Y);
                bool betterVerticalVantage = initialGap >= CombatTopologyVerticalSeparation &&
                    verticalProgress >= Math.Min(1.5f, initialGap * 0.5f);
                bool meaningfulHorizontalProgress = Vector3.Distance(position, recovery.Anchor) >= 12f;
                if (!completed && !betterVerticalVantage && !meaningfulHorizontalProgress)
                    return true;
                string routeName = recovery.RecordedRoute;
                if (!completed) PreserveCombatTopologyHandoff(room, enemy, recovery);
                else _navRoutes.StopPlayback();
                recovery.Active = false;
                recovery.Transit = recovery.RecordedTransit = false;
                recovery.RecordedRoute = null;
                _combat.Reset();
                ResetCombatApproach();
                _roomStates[room.Instance] = RoomClearanceState.Reopened;
                _say(completed
                    ? $"Combat recorded topology '{routeName}' completed; rebuilding firing candidates."
                    : $"Combat recorded topology '{routeName}' reached a materially better vantage; rebuilding firing candidates.");
                return true;
            }
            if (recovery.WaypointIndex < recovery.Waypoints.Count)
            {
                _requestedOwner = MovementOwner.CombatPosition;
                Vector3 point = recovery.Waypoints[recovery.WaypointIndex];
                if (Vector3.Distance(position, point) < 1.1f)
                {
                    _destination = null;
                    MovementArbiter.Current.Halt(MovementOwner.CombatPosition);
                    recovery.WaypointIndex++;
                    float initialGap = Math.Abs(recovery.EnemyPosition.Y - recovery.Anchor.Y);
                    float verticalProgress = initialGap - Math.Abs(enemy.Position.Y - position.Y);
                    bool betterVerticalVantage = initialGap >= CombatTopologyVerticalSeparation &&
                        verticalProgress >= Math.Min(1.5f, initialGap * 0.5f);
                    bool meaningfulHorizontalProgress = Vector3.Distance(position, recovery.Anchor) >= 12f;
                    bool routeExhausted = recovery.WaypointIndex >= recovery.Waypoints.Count;
                    _say($"Combat same-room transit waypoint {recovery.WaypointIndex}/{recovery.Waypoints.Count} reached for {enemy.Identity} in room {room.Instance}; " +
                        $"player Y={position.Y:0.0}, target Y={enemy.Position.Y:0.0}, retry={recovery.Failures}/{MaxCombatTopologyAttempts}.");
                    if (!betterVerticalVantage && !meaningfulHorizontalProgress && !routeExhausted)
                        return true;
                    recovery.Active = false;
                    recovery.Transit = false;
                    recovery.Waypoints.Clear();
                    _combat.Reset();
                    ResetCombatApproach();
                    _roomStates[room.Instance] = RoomClearanceState.Reopened;
                    string reason = betterVerticalVantage ? "achieved better vertical vantage" :
                        meaningfulHorizontalProgress ? "achieved substantial topology progress" :
                        "route exhausted without LOS";
                    _say($"Combat same-room transit {reason} for {enemy.Identity} in room {room.Instance}; " +
                        "rebuilding normal firing-side candidates.");
                    return true;
                }
                if (!_destination.HasValue)
                {
                    if (!MovementArbiter.Current.SetNavDestination(_requestedOwner, point))
                    { FailCombatRecoveryRoute(recovery, "navigation ownership unavailable"); return true; }
                    _destination = point;
                    recovery.WaypointStarted = recovery.LastProgress = now;
                    recovery.LastPosition = position;
                    _say($"Combat same-room transit following waypoint {recovery.WaypointIndex + 1}/{recovery.Waypoints.Count} to {point} in room {room.Instance}; " +
                        $"path cost={recovery.TransitCost:0.0}m, player Y={position.Y:0.0}, target Y={enemy.Position.Y:0.0}.");
                }
                if (Vector3.Distance(position, recovery.LastPosition) > 0.5f)
                { recovery.LastPosition = position; recovery.LastProgress = now; }
                if (now - recovery.LastProgress > TimeSpan.FromSeconds(7) ||
                    now - recovery.WaypointStarted > TimeSpan.FromSeconds(15))
                {
                    // This is the real generated same-room transit stall. Run
                    // the bounded stationary-jump state machine here instead
                    // of waiting for the unrelated global stuck watchdog.
                    if (TryBeginStaircaseRecovery(recovery, point))
                        return true;
                    if (TryStartRecordedCombatTopology(room, enemy, recovery, position))
                        _say($"Combat generated same-room route stalled; switching to recorded topology '{recovery.RecordedRoute}' under retry={recovery.Failures}/{MaxCombatTopologyAttempts}.");
                    else
                        FailCombatRecoveryRoute(recovery, "no movement progress");
                }
                return true;
            }
            if (recovery.Failures < MaxCombatTopologyAttempts && now >= recovery.RetryAt)
            {
                recovery.Active = false;
                _roomStates[room.Instance] = RoomClearanceState.Reopened;
                _combat.Reset();
                ResetCombatApproach();
                _say($"Combat same-room transit retry {recovery.Failures + 1}/{MaxCombatTopologyAttempts} released for {enemy.Identity}; rebuilding local firing approaches.");
                return false;
            }
            bool playerThreatened = _readiness.InCombat ||
                enemy.IsAttacking && enemy.FightingTarget?.Identity == DynelManager.LocalPlayer.Identity;
            if (!playerThreatened)
            {
                // Combat recovery owns this earlier Tick slot, but it must not
                // cross a door while a corpse created in this room is still
                // owned by the exact-evidence guard. Give ManagerLoot a safe
                // same-room tick, then hold (or release only after settlement).
                if (PostCombatCorpseGuardActive(room.Instance))
                {
                    MovementArbiter.Current.Halt(MovementOwner.CombatPosition);
                    _destination = null;
                    if (LootInRoom(room)) return true;
                    if (HoldForPostCombatCorpse(room)) return true;
                }
                if (_staircaseRecoveryAttempts < MaxStaircaseRecoveryAttempts &&
                    TryGetProvenStaircaseDirection(recovery, position, out Vector3 retryDirection))
                {
                    _requestedOwner = MovementOwner.CombatPosition;
                    MovementArbiter.Current.Halt(MovementOwner.CombatPosition);
                    Vector3 retryTarget = position + retryDirection * 2f;
                    retryTarget.Y = position.Y + 0.5f;
                    if (TryBeginStaircaseRecovery(recovery, retryTarget)) return true;
                    CombatRecoveryWaiting = true;
                    _say($"Combat staircase recovery retained room {room.Instance}: proven upward geometry still has " +
                        $"{MaxStaircaseRecoveryAttempts - _staircaseRecoveryAttempts} bounded attempt(s); room departure suppressed.");
                    return true;
                }
                Room next = NextRoom(room);
                if (next != null)
                {
                    _say($"Deferring unfinished combat room {room.Instance}; routing through {next.Instance} to other work.");
                    BeginTransition(room.Instance, next.Instance);
                    return true;
                }
                if (!recovery.UsedRoomDetour && recovery.Failures <= 2)
                {
                    int? alternate = _layout.Neighbors(room.Instance)
                        .Where(id => _layout.Edge(room.Instance, id) != null &&
                            !IsUnavailable(room.Instance, id))
                        .OrderByDescending(id => Vector3.Distance(
                            _layout.Edge(room.Instance, id).SourceApproach, recovery.Anchor))
                        .Select(id => (int?)id)
                        .FirstOrDefault();
                    if (alternate.HasValue)
                    {
                        recovery.UsedRoomDetour = true;
                        _say($"Combat recovery using room {alternate.Value} as a bounded alternate entry to unfinished room {room.Instance}.");
                        BeginTransition(room.Instance, alternate.Value);
                        return true;
                    }
                }
            }
            MovementArbiter.Current.Halt(MovementOwner.CombatPosition);
            CombatRecoveryWaiting = true;
            if (!recovery.HoldReported)
            {
                recovery.HoldReported = true;
                _say($"Combat room {room.Instance} remains unfinished after {recovery.Failures}/{MaxCombatTopologyAttempts} topology attempts. " +
                    $"Final defer reason: no complete same-room transit route or valid firing position; distance={enemy.DistanceFrom(DynelManager.LocalPlayer):0.0}m, " +
                    $"player Y={position.Y:0.0}, target Y={enemy.Position.Y:0.0}; mission remains armed.");
            }
            return true;
        }

        private void PreserveCombatTopologyHandoff(Room room, SimpleChar enemy,
            CombatRecovery recovery)
        {
            if (_navRoutes == null || !_navRoutes.PauseDungeonTopology(enemy.Position,
                out NavigationRouteRecorder.DungeonTopologyContinuation continuation))
            {
                _navRoutes?.StopPlayback();
                return;
            }
            _combatTopologyHandoff = new CombatTopologyHandoff
            {
                RoomId = room.Instance,
                DungeonInstance = Playfield.ModelIdentity.Instance,
                Enemy = enemy.Identity,
                EnemyPosition = enemy.Position,
                TargetSide = continuation.TargetSide,
                RouteName = recovery.RecordedRoute,
                ExpiresAt = DateTime.UtcNow + CombatTopologyHandoffLifetime,
                Continuation = continuation
            };
            _say($"Combat recorded topology '{recovery.RecordedRoute}' paused at firing vantage; " +
                "preserving forward continuation for post-combat topology.");
        }

        private void FailCombatRecoveryRoute(CombatRecovery recovery, string reason)
        {
            Vector3 point = recovery.RecordedTransit ? recovery.LastPosition :
                recovery.WaypointIndex < recovery.Waypoints.Count
                    ? recovery.Waypoints[recovery.WaypointIndex] : recovery.LastPosition;
            MovementArbiter.Current.Halt(MovementOwner.CombatPosition);
            if (recovery.RecordedTransit) _navRoutes?.StopPlayback();
            recovery.Waypoints.Clear();
            recovery.WaypointIndex = 0;
            string routeKind = recovery.RecordedTransit
                ? $"recorded topology '{recovery.RecordedRoute}'" : "same-room transit";
            recovery.Transit = recovery.RecordedTransit = false;
            recovery.RecordedRoute = null;
            recovery.RetryAt = DateTime.UtcNow.AddSeconds(15);
            _destination = null;
            _say($"Combat {routeKind} failed at {point} ({reason}); retry={recovery.Failures}/{MaxCombatTopologyAttempts}, " +
                $"path cost={recovery.TransitCost:0.0}m; deferring unfinished room {_currentRoom}.");
        }

        private void ResetCombatApproach()
        {
            MovementOwner activeOwner = MovementArbiter.Current.Owner;
            bool destinationIsCombatOwned = activeOwner == MovementOwner.CombatPosition ||
                (activeOwner == MovementOwner.None && _requestedOwner == MovementOwner.CombatPosition);
            _combatApproachTarget = Identity.None;
            _combatApproaches.Clear();
            _combatApproachIndex = 0;
            _combatApproachProgress = DateTime.MinValue;
            _combatApproachIssuedAt = DateTime.MinValue;
            _combatBlockedCorridors.Clear();
            _combatRetreat = null;
            _combatStalls = 0;
            if (destinationIsCombatOwned) _destination = null;
        }

        private void TryCombatRetreat(Vector3 position, Vector3 blockedDirection)
        {
            if (blockedDirection.Magnitude < 0.5f || _currentRoom < 0) return;
            Vector3 forward = blockedDirection.Normalize();
            Vector3 side = new Vector3(-forward.Z, 0, forward.X);
            foreach (Vector3 offset in new[] { side * 3f, -side * 3f, -forward * 3f,
                side * 5f, -side * 5f, -forward * 5f })
            {
                Vector3 point = position + offset;
                point.Y = position.Y;
                if (!_layout.IsInside(_currentRoom, point, 0.6f) ||
                    float.IsInfinity(CombatRecoveryRouteCost(position, point)) ||
                    !LocalRoutePlanner.ClearInteractionSegment(
                        position + Vector3.Up, point + Vector3.Up)) continue;
                _combatRetreat = point;
                return;
            }
        }

        private bool RepeatsBlockedCombatCorridor(Vector3 position, Vector3 point)
        {
            Vector3 toPoint = point - position;
            toPoint.Y = 0;
            if (toPoint.Magnitude < 0.5f) return false;
            toPoint = toPoint.Normalize();
            return _combatBlockedCorridors.Any(blocked =>
                Vector3.Distance(position, blocked.Origin) < 6f &&
                toPoint.X * blocked.Direction.X + toPoint.Z * blocked.Direction.Z > 0.82f);
        }

        private bool ApproachCombatEnemy(SimpleChar enemy, int roomId)
        {
            if (!_layout.ContainsDynel(roomId, enemy))
            {
                if (float.IsInfinity(CombatRecoveryRouteCost(
                    DynelManager.LocalPlayer.Position, enemy.Position))) return false;
                Navigate(enemy.Position);
                return true;
            }
            DateTime now = DateTime.UtcNow;
            SimpleChar player = DynelManager.LocalPlayer;
            if (_combatApproachTarget != enemy.Identity ||
                Vector3.Distance(_combatApproachEnemyPosition, enemy.Position) > 3f)
            {
                if (_combatApproachTarget != enemy.Identity ||
                    Vector3.Distance(_combatApproachEnemyPosition, enemy.Position) > 6f)
                {
                    _combatBlockedCorridors.Clear();
                    _combatStalls = 0;
                    _combatRetreat = null;
                }
                _combatApproachTarget = enemy.Identity;
                _combatApproachEnemyPosition = enemy.Position;
                _combatApproaches.Clear();
                _combatApproachIndex = 0;
                _combatApproaches.AddRange(DungeonApproachPlanner.Candidates(_layout, roomId,
                    player.Position, enemy.Position, new[] { 2f, 4f, 6f, 8f, 10f }, 0.3f,
                    combatFiringSide: true));
                _destination = null;
                _say($"Combat approach to {enemy.Identity} in room {roomId}: " +
                    $"{_combatApproaches.Count} mapped, scene-checked point(s), " +
                    $"distance={enemy.DistanceFrom(player):0.0}m.");
            }
            if (_combatRetreat.HasValue)
            {
                Vector3 retreat = _combatRetreat.Value;
                if (Vector3.Distance(player.Position, retreat) < 1.1f)
                {
                    _combatRetreat = null;
                    _destination = null;
                    MovementArbiter.Current.Halt(MovementOwner.CombatPosition);
                    _say($"Combat retreat reached {retreat}; selecting a different firing side.");
                }
                else
                {
                    if (!_destination.HasValue)
                    {
                        if (!MovementArbiter.Current.SetNavDestination(_requestedOwner, retreat))
                            return false;
                        _destination = retreat;
                        _combatApproachProgress = _combatApproachIssuedAt = now;
                        _combatApproachLastPosition = player.Position;
                        _say($"Combat retreat toward {retreat} after an observed collision.");
                    }
                    if (Vector3.Distance(player.Position, _combatApproachLastPosition) > 0.5f)
                    {
                        _combatApproachLastPosition = player.Position;
                        _combatApproachProgress = now;
                    }
                    if (now - _combatApproachProgress > TimeSpan.FromSeconds(7) ||
                        now - _combatApproachIssuedAt > TimeSpan.FromSeconds(12) ||
                        (!SMovementController.IsNavigating() &&
                         now - _combatApproachIssuedAt > TimeSpan.FromSeconds(2)))
                        RecoverFromStuck(1);
                    return true;
                }
            }
            if (_combatApproachIndex >= _combatApproaches.Count) return false;
            Vector3 point = _combatApproaches[_combatApproachIndex];
            float distance = Vector3.Distance(player.Position, point);
            // A complete corridor may initially lead away from the target to
            // pass around a wall. Count actual displacement, not straight-line
            // distance to the firing point, as movement progress.
            if (_destination.HasValue &&
                Vector3.Distance(player.Position, _combatApproachLastPosition) > 0.5f)
            {
                _combatApproachLastPosition = player.Position;
                _combatApproachProgress = now;
            }
            bool stalled = _destination.HasValue &&
                (now - _combatApproachProgress > TimeSpan.FromSeconds(7) ||
                 now - _combatApproachIssuedAt > TimeSpan.FromSeconds(30) ||
                 (!SMovementController.IsNavigating() && now - _combatApproachProgress > TimeSpan.FromSeconds(2)));
            if (stalled)
            {
                RecoverFromStuck(1);
                if (_combatRetreat.HasValue || _combatApproachIndex >= _combatApproaches.Count)
                    return _combatRetreat.HasValue;
            }
            else if (_destination.HasValue && distance < 1.1f)
            {
                _combatApproachIndex++;
                _destination = null;
            }
            int rejectedHeadings = 0;
            while (!_destination.HasValue && _combatApproachIndex < _combatApproaches.Count)
            {
                point = _combatApproaches[_combatApproachIndex];
                if (RepeatsBlockedCombatCorridor(player.Position, point))
                {
                    _combatApproachIndex++;
                    rejectedHeadings++;
                    continue;
                }
                _combatApproachLastPosition = player.Position;
                _combatApproachProgress = _combatApproachIssuedAt = now;
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
            if (rejectedHeadings > 0)
                _say($"Combat rejected {rejectedHeadings} mapped point(s) through an observed blocked heading.");
            return _destination.HasValue;
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
                if (newTarget) _scanStarted = now;
                _scanTarget = distant.Identity;
                _scanProgress = now;
                _scanEnemyPosition = distant.Position;
                _scanWaypoints.Clear();
                _scanWaypointIndex = 0;
                Vector3 first = player.Position + (distant.Position - player.Position) *
                    ((distance - 18f) / distance);
                AddScanWaypoint(room.Instance, first);
                // A single AO room can contain stacked corridors joined by
                // ramps. Never assume the enemy and player share one Y plane.
                Vector3 levelPoint = first;
                levelPoint.Y = player.Position.Y;
                AddScanWaypoint(room.Instance, levelPoint);
                levelPoint.Y = distant.Position.Y;
                AddScanWaypoint(room.Instance, levelPoint);
                Vector3 toward = player.Position - distant.Position;
                toward.Y = 0;
                if (toward.Magnitude > 0.1)
                {
                    toward = toward.Normalize();
                    Vector3 side = new Vector3(-toward.Z, 0, toward.X);
                    foreach (float radius in new[] { 14f, 8f })
                        foreach (Vector3 direction in new[] { toward, side, -side, -toward })
                            AddScanWaypoint(room.Instance, distant.Position + direction * radius);
                    if (_scanWaypoints.Count == 0 &&
                        SMovementController.NavAgent?.HasPathfinder == true)
                    {
                        // The first ring can land in a central void or on the
                        // wrong gallery. Sample both observed heights and the
                        // ramp band; only complete scene-clear mesh routes can
                        // become movement targets.
                        Vector3[] directions = { toward, side, -side, -toward,
                            (toward + side).Normalize(), (toward - side).Normalize(),
                            (-toward + side).Normalize(), (-toward - side).Normalize() };
                        float[] levels = { player.Position.Y, distant.Position.Y,
                            (player.Position.Y + distant.Position.Y) * 0.5f };
                        foreach (float radius in new[] { 6f, 12f, 18f, 24f })
                        {
                            foreach (Vector3 direction in directions)
                            {
                                Vector3 candidate = distant.Position + direction * radius;
                                foreach (float height in levels)
                                {
                                    candidate.Y = height;
                                    AddScanWaypoint(room.Instance, candidate);
                                }
                            }
                            if (_scanWaypoints.Count >= 12) break;
                        }
                        _navTrace($"Room {room.Instance} layered scan: player Y={player.Position.Y:0.0}, " +
                            $"enemy Y={distant.Position.Y:0.0}, complete mapped approaches={_scanWaypoints.Count}.");
                    }
                }
                AddScanWaypoint(room.Instance, distant.Position);
                _destination = null;
                _navTrace($"Room {room.Instance} still has an enemy {distance:0.0}m away; " +
                    $"trying {_scanWaypoints.Count} mapped approach point(s) before the 20m engagement check " +
                    $"(player Y={player.Position.Y:0.0}, enemy Y={distant.Position.Y:0.0}, " +
                    $"navmesh available={SMovementController.NavAgent?.HasPathfinder == true}).");
            }
            if (now - _scanStarted > TimeSpan.FromSeconds(60) || _scanWaypointIndex >= _scanWaypoints.Count)
            {
                int tried = Math.Min(_scanWaypoints.Count, _scanWaypointIndex + (_destination.HasValue ? 1 : 0));
                _say($"Room {room.Instance} scan could not reach enemy {distant.Identity} within 20m " +
                    $"(remaining {distance:0.0}m, tried {tried}/{_scanWaypoints.Count} mapped points, " +
                    $"player={player.Position}, enemy={distant.Position}); deferring combat and keeping the room unfinished.");
                BeginCombatRecovery(room, distant);
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
                        _navTrace($"Room {room.Instance} scan approach {_scanWaypointIndex + 1}/{_scanWaypoints.Count}: " +
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
                !_scanWaypoints.Any(existing => Vector3.Distance(existing, point) < 2f) &&
                DungeonTransitPlanner.TryCost(_layout, roomId,
                    DynelManager.LocalPlayer.Position, point, out _))
                _scanWaypoints.Add(point);
        }

        internal static bool ReservedAggroActive(bool finaleReady, bool exactHeldFindPersonAggro,
            int hostileHeldCount) => hostileHeldCount > 0 && (!finaleReady || exactHeldFindPersonAggro);

        private void ResetFindPersonFinaleRouteWait()
        {
            _findPersonFinaleRouteWait = _findPersonFinaleRouteDiagnostic = DateTime.MinValue;
        }

        private bool HoldFindPersonFinaleRoute(Room room, int targetRoom)
        {
            DateTime now = DateTime.UtcNow;
            MovementArbiter.Current.Halt(MovementOwner.Objective);
            _destination = null;
            _roomQuietAt = DateTime.MinValue;
            if (_findPersonFinaleRouteWait == DateTime.MinValue) _findPersonFinaleRouteWait = now;
            if (now - _findPersonFinaleRouteDiagnostic >= TimeSpan.FromSeconds(5))
            {
                _findPersonFinaleRouteDiagnostic = now;
                _say($"FindPerson finale routing held: exact target={_objective.FindPersonTarget}, " +
                    $"current room={room.Instance}, target room={targetRoom}, temporary route cooldown={WaitingForRoute}; " +
                    $"retrying for up to {FindPersonFinaleRouteWaitLimit.TotalSeconds:0}s with target protection active.");
            }
            if (now - _findPersonFinaleRouteWait >= FindPersonFinaleRouteWaitLimit)
            {
                Stop();
                _say($"FindPerson finale could not route from room {room.Instance} to exact target room {targetRoom} " +
                    $"within {FindPersonFinaleRouteWaitLimit.TotalSeconds:0}s; stopped without attacking the protected target or accepting completion.");
            }
            return true;
        }

        private bool GuardReservedEnemy(bool ordinaryEnemiesRemain)
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
            Identity findPersonTarget = _objective.FindPersonTarget;
            bool findPersonHeld = findPersonTarget != Identity.None && _objective.HoldEnemy(findPersonTarget);
            bool attackingFindPerson = findPersonHeld &&
                (((player.IsAttacking || player.IsAttackPending) &&
                    player.FightingTarget?.Identity == findPersonTarget) ||
                player.Pets.Any(x => x.Character?.IsAttacking == true &&
                    x.Character.FightingTarget?.Identity == findPersonTarget));
            var hostileReserved = DynelManager.NPCs.Where(x => x.IsAlive && _objective.HoldEnemy(x.Identity) &&
                x.IsAttacking && owned.Contains(x.FightingTarget?.Identity ?? Identity.None)).ToList();
            // FindPerson stays protected through acknowledgement. Its incoming
            // aggro must also retain routing ownership after the finale starts.
            bool reservedAggro = ReservedAggroActive(
                _objective.Finale && _objective.FinalActionsAllowed,
                findPersonHeld && hostileReserved.Any(x => x.Identity == findPersonTarget),
                hostileReserved.Count);
            if (attackingReserved)
            {
                player.StopAttack(); // Includes pet follow; prevent the reserved kill.
                Targeting.SelectSelf();
                if (_reservedProtectionStarted == DateTime.MinValue)
                    _reservedProtectionStarted = DateTime.UtcNow;
            }
            else
                _reservedProtectionStarted = DateTime.MinValue;
            if (!findPersonHeld)
                _reservedProtectionStarted = DateTime.MinValue;
            bool findPersonReserved = findPersonHeld &&
                (hostileReserved.Any(x => x.Identity == findPersonTarget) || attackingFindPerson);
            if (attackingFindPerson && findPersonReserved &&
                DateTime.UtcNow - _reservedProtectionStarted >= ReservedProtectionTimeout)
            {
                Stop();
                _say($"FindPerson protection could not be maintained for exact target {_objective.FindPersonTarget}: " +
                    $"player/pet attack remained active after repeated cancellation for {ReservedProtectionTimeout.TotalSeconds:0}s. " +
                    "Stopping to prevent an unsafe reserved-target kill.");
                return true;
            }
            if ((attackingReserved || reservedAggro) && !_objective.HasKillTarget && !findPersonReserved)
            {
                Stop();
                _say("Reserved objective enemy engaged before the other enemies were cleared. Attacks stopped; resolve its aggro before resuming. Objective order was not waived.");
                return true;
            }
            if ((attackingReserved || reservedAggro) && !_reservedAggroReported)
            {
                string identity = findPersonReserved
                    ? $"FindPerson exact target={_objective.FindPersonTarget}"
                    : "reserved objective target";
                int ordinaryCount = Playfield.Rooms.Sum(room => EnemyCandidates(room)
                    .Count(enemy => !_objective.IsObjective(enemy.Identity)));
                _say($"Reserved {identity} aggro detected in room {player.Room?.Instance.ToString() ?? "unknown"}; " +
                    $"ordinary enemies={ordinaryCount}, ordinary remaining={ordinaryEnemiesRemain}. " +
                    "Target remains held while ordinary enemies are cleared; combat-blocked loot will be collected before exit.");
                _reservedAggroReported = true;
            }
            if (!attackingReserved && !reservedAggro)
                _reservedAggroReported = false;
            return reservedAggro;
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
                _roomStates[id] = RoomClearanceState.Reopened;
                _say($"Reopening cleared room {id}: " + (enemy != null
                    ? $"live enemy {enemy.Identity} is inside this room."
                    : $"unfinished loot {pendingLoot.Identity} is inside this room."));
            }
        }

        public void BeginExit()
        {
            ResetObjectiveApproach();
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

        public void ResumeExitAfterWarpWait()
        {
            if (!_exiting || !IsRunning) return;
            _exitStarted = DateTime.UtcNow;
            _destination = null;
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
            Room occupied = Playfield.Rooms.FirstOrDefault(x =>
                EnemyCandidates(x).Any(enemy => !_objective.IsObjective(enemy.Identity)));
            Room unlooted = Playfield.Rooms.FirstOrDefault(x => _loot.HasUnprocessedMissionLoot(
                x.Instance, dynel => !_objective.IsNonLootObjective(dynel.Identity)));
            if (_readiness.InCombat || occupied != null || unlooted != null ||
                _loot.UnfinishedMissionLootCount > 0)
            {
                // Check all rooms, including after the objective acknowledgement:
                // combat may have deferred chests in rooms already traversed.
                IsComplete = false;
                _record.RoomsCleared = false;
                _exiting = false;
                if (occupied != null) _clearedRooms.Remove(occupied.Instance);
                if (unlooted != null) _clearedRooms.Remove(unlooted.Instance);
                if (_readiness.InCombat) _clearedRooms.Remove(room.Instance);
                _say("Combat, an ordinary enemy, or unlooted mission container remains; returning to clear it before exit.");
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
            if (_loot.CriticalBlockedMissionLootCount > 0)
            {
                string blockers = _loot.MissionLootBlockers;
                Stop();
                _say("Mission-critical loot is blocked; objective completion and exit are held. " + blockers);
                return true;
            }
            if (_readiness.InCombat && (!_loot.IsProcessingMissionLoot ||
                _loot.ProcessingMissionLootIdentity.Type == IdentityType.Container &&
                _loot.WaitingForOrdinaryMissionContainer(_loot.ProcessingMissionLootIdentity)))
            {
                // Pause approach timers as well as ManagerLoot ownership. Resume
                // from fresh geometry after combat, without deferring the source.
                _waitingForLoot = Identity.None;
                _loot.MissionActionsPaused = true;
                return false;
            }
            Dynel next = _loot.NextMissionLoot(room.Instance);
            DateTime now = DateTime.UtcNow;
            Identity processingIdentity = _loot.ProcessingMissionLootIdentity;
            Identity waitingIdentity = processingIdentity != Identity.None ? processingIdentity : next?.Identity ?? Identity.None;
            if (waitingIdentity != Identity.None && _waitingForLoot != waitingIdentity)
            {
                _waitingForLoot = waitingIdentity;
                _lootWaitStarted = now;
                _lootLastProgress = _lootWaitStarted;
                _lootProgressVersion = _loot.MissionLootProgress;
                _lootLastPosition = DynelManager.LocalPlayer.Position;
                _lootApproachIndex = 0;
                _lootCorpseFinalReissues = 0;
                _lootContainerFinalReissues = 0;
                _lootNearStarted = DateTime.MinValue;
                _lootProbeNextSide = false;
                _lootTransitStaging = false;
                _lootRecordedTransit = false;
                _lootRecordedFallbackAttempted = false;
                _lootRecordedRoute = null;
                _lootVisitedStaging.Clear();
                _lootBestDistance = next != null
                    ? Vector3.Distance(DynelManager.LocalPlayer.Position, next.Position)
                    : float.PositiveInfinity;
                _lootNonImprovingHops = 0;
                _lootCycleReported = false;
                _lootBestVerticalGap = next != null
                    ? Math.Abs(next.Position.Y - DynelManager.LocalPlayer.Position.Y)
                    : float.PositiveInfinity;
                _lootApproachPoints.Clear();
                if (next != null) BuildLootApproaches(room, next.Position);
                _destination = null;
                if (next != null && next.Identity == waitingIdentity)
                {
                    Vector3 player = DynelManager.LocalPlayer.Position;
                    string plan = _lootRecordedTransit
                        ? $"ordered recorded topology '{_lootRecordedRoute}'"
                        : $"{_lootApproachPoints.Count} {(_lootTransitStaging ? "transit staging" : "direct final")} point(s)";
                    _navTrace($"Loot candidate {next.Identity.Type} {next.Identity} in room {room.Instance}; " +
                        $"2D={player.Distance2DFrom(next.Position):0.0}m, 3D={Vector3.Distance(player, next.Position):0.0}m, " +
                        $"player Y={player.Y:0.0}, target Y={next.Position.Y:0.0}, interaction-ready={LootInteractionReady(next)}; " +
                        $"trying {plan}.");
                }
                else
                    _say($"Waiting for Manager.Loot contents/finish response for {waitingIdentity} in room {room.Instance}.");
            }
            if (_loot.IsProcessingMissionLoot)
            {
                if (_loot.CriticalBlockedMissionLootCount > 0)
                {
                    string blockers = _loot.MissionLootBlockers;
                    Stop();
                    _say("Mission-critical loot is blocked; objective completion and exit are held. " + blockers);
                    return true;
                }
                if (_lootProgressVersion != _loot.MissionLootProgress)
                {
                    _lootProgressVersion = _loot.MissionLootProgress;
                    _lootWaitStarted = now;
                }
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
                if (_lootRecordedTransit) _navRoutes?.StopPlayback();
                _combatTopologyHandoff = null;
                _waitingForLoot = Identity.None;
                _lootApproachPoints.Clear();
                _lootProbeNextSide = false;
                _lootTransitStaging = false;
                return false;
            }
            float distance = next.DistanceFrom(DynelManager.LocalPlayer);
            bool interactionReady = LootInteractionReady(next);
            if (!interactionReady || _lootProbeNextSide)
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
                    ResolveLootApproachFailure(next, room.Instance, $"approach search exhausted after 75 seconds; still {distance:0.0}m away");
                    return true;
                }
                if (_lootRecordedTransit)
                {
                    if (_navRoutes == null || !_navRoutes.TryNavigatePreparedDungeonTopology(
                        next.Position, MovementArbiter.Current, _requestedOwner, out bool completed,
                        out NavigationRouteRecorder.DungeonTopologyContinuation completedPath))
                    {
                        _lootRecordedTransit = false;
                        _lootRecordedFallbackAttempted = true;
                        _say($"Loot {next.Identity} recorded topology '{_lootRecordedRoute}' could not continue; route remains unfinished.");
                        BuildLootApproaches(room, next.Position);
                        return true;
                    }
                    if (completed)
                    {
                        if (completedPath != null && completedPath.Points.Count >= 2 &&
                            completedPath.Points[completedPath.Points.Count - 1].Y -
                            completedPath.Points[0].Y >= CombatTopologyVerticalSeparation)
                            _recordedRoomReturn = new RecordedRoomReturn
                            {
                                RoomId = room.Instance, Floor = _floor,
                                Playfield = Playfield.ModelIdentity.Instance, Ascent = completedPath
                            };
                        _lootRecordedTransit = false;
                        _combatTopologyHandoff = null;
                        _lootLastProgress = now;
                        _lootLastPosition = position;
                        _lootBestVerticalGap = Math.Min(_lootBestVerticalGap,
                            Math.Abs(next.Position.Y - position.Y));
                        _say($"Loot {next.Identity} completed recorded topology '{_lootRecordedRoute}' in forward order; rebuilding strict final approaches.");
                        BuildLootApproaches(room, next.Position);
                    }
                    return true;
                }
                if (_destination.HasValue && _lootApproachIndex < _lootApproachPoints.Count &&
                    Vector3.Distance(position, _lootApproachPoints[_lootApproachIndex]) < 1.5f &&
                    LootInteractionReady(next))
                {
                    _lootProbeNextSide = false;
                    _lootNearStarted = now;
                    _destination = null;
                    MovementArbiter.Current.Halt(_requestedOwner);
                    return true;
                }
                // A 1.5m waypoint tolerance can leave loot outside its 3m use range
                // even when the waypoint itself is a legal 2.5–3m side point. Keep
                // the coarse tolerance for transit staging, but settle both corpse
                // and container final approaches precisely before exhausting a side.
                bool preciseContainerFinal = next.Identity.Type == IdentityType.Container &&
                    !_lootTransitStaging;
                float arrivalTolerance = next.Identity.Type == IdentityType.Corpse && !_lootTransitStaging
                    ? 0.35f : 1.5f;
                if (preciseContainerFinal) arrivalTolerance = 0.35f;
                bool reachedApproach = _destination.HasValue && _lootApproachIndex < _lootApproachPoints.Count &&
                    Vector3.Distance(position, _lootApproachPoints[_lootApproachIndex]) < arrivalTolerance;
                if (reachedApproach && _lootTransitStaging)
                {
                    MovementArbiter.Current.Halt(_requestedOwner);
                    _destination = null;
                    _lootLastPosition = position;
                    float verticalGap = Math.Abs(next.Position.Y - position.Y);
                    bool meaningfulProgress = distance <= _lootBestDistance - 1f ||
                        verticalGap <= _lootBestVerticalGap - 0.75f;
                    _lootVisitedStaging.Add(position);
                    if (meaningfulProgress)
                    {
                        _lootBestDistance = Math.Min(_lootBestDistance, distance);
                        _lootBestVerticalGap = Math.Min(_lootBestVerticalGap, verticalGap);
                        _lootNonImprovingHops = 0;
                        _lootLastProgress = now;
                    }
                    else
                    {
                        _lootNonImprovingHops++;
                        if (!_lootCycleReported)
                        {
                            _lootCycleReported = true;
                            _say($"Loot {next.Identity} staging cycle/non-improving hop detected in room {room.Instance}; " +
                                $"excluding {_lootVisitedStaging.Count} recent staging point(s) and trying another candidate.");
                        }
                    }
                    _navTrace($"Loot {next.Identity} transit staging reached; rebuilding direct final approaches from new position {position}.");
                    BuildLootApproaches(room, next.Position);
                }
                else if (_destination.HasValue && _lootApproachIndex < _lootApproachPoints.Count &&
                    (reachedApproach ||
                     now - _lootLastProgress > TimeSpan.FromSeconds(10) ||
                     (!SMovementController.IsNavigating() && now - _lootLastProgress > TimeSpan.FromSeconds(2))))
                {
                    Vector3 finalPoint = _lootApproachPoints[_lootApproachIndex];
                    float waypointDistance = Vector3.Distance(position, finalPoint);
                    bool movementStopped = !SMovementController.IsNavigating();
                    bool rangeReady = distance <= LootUseRange;
                    bool currentRayClear = LocalRoutePlanner.ClearInteractionSegment(
                        position + Vector3.Up, next.Position + Vector3.Up * 0.5f);
                    if (next.Identity.Type == IdentityType.Corpse && !_lootTransitStaging &&
                        !reachedApproach && _lootCorpseFinalReissues < 1)
                    {
                        // A movement controller may stop with the side waypoint
                        // nominally reached while corpse interaction is still 3.1m+.
                        // Force one bounded reissue of this same verified point.
                        MovementArbiter.Current.Halt(_requestedOwner);
                        _destination = null;
                        if (MovementArbiter.Current.SetNavDestination(_requestedOwner, finalPoint))
                        {
                            _lootCorpseFinalReissues++;
                            _destination = finalPoint;
                            _lootLastProgress = now;
                            _lootLastPosition = position;
                            _say($"Loot {next.Identity} reissued precise corpse-side approach " +
                                $"({_lootCorpseFinalReissues}/1) at {distance:0.0}m; " +
                                $"waypoint={finalPoint}, interaction-ready=false.");
                            return true;
                        }
                    }
                    if (preciseContainerFinal && !reachedApproach &&
                        (!rangeReady || !currentRayClear) && _lootContainerFinalReissues < 1)
                    {
                        // Revalidate the originally selected target-side geometry.
                        // This permits one precise recovery from controller early-stop,
                        // but never drives through a blocked interaction segment.
                        float targetSideDistance = Vector3.Distance(finalPoint, next.Position);
                        bool targetSideRayClear = LocalRoutePlanner.ClearInteractionSegment(
                            finalPoint + Vector3.Up, next.Position + Vector3.Up * 0.5f);
                        bool targetSideClear = targetSideDistance >= 2f &&
                            targetSideDistance <= LootUseRange && targetSideRayClear;
                        if (targetSideClear)
                        {
                            MovementArbiter.Current.Halt(_requestedOwner);
                            _destination = null;
                            if (MovementArbiter.Current.SetNavDestination(_requestedOwner, finalPoint))
                            {
                                _lootContainerFinalReissues++;
                                _destination = finalPoint;
                                _lootLastProgress = now;
                                _lootLastPosition = position;
                                _say($"Loot {next.Identity} reissued precise container-side approach " +
                                    $"({_lootContainerFinalReissues}/1) at 3D distance={distance:0.0}m; " +
                                    $"waypoint distance={waypointDistance:0.0}m, waypoint-reached={reachedApproach}, " +
                                    $"movement-stopped={movementStopped}, range-ready={rangeReady}, ray-clear={currentRayClear}.");
                                return true;
                            }
                        }
                        else
                            _say($"Loot {next.Identity} precise container recovery rejected blocked/invalid target-side geometry; " +
                                $"waypoint distance={waypointDistance:0.0}m, target-side distance={targetSideDistance:0.0}m, " +
                                $"target-side ray-clear={targetSideRayClear}.");
                    }
                    _say($"Loot {next.Identity} approach {_lootApproachIndex + 1} ended while 3D distance={distance:0.0}m, " +
                        $"waypoint distance={waypointDistance:0.0}m, waypoint-reached={reachedApproach}, " +
                        $"movement-stopped={movementStopped}, range-ready={rangeReady}, ray-clear={currentRayClear}, " +
                        $"player Y={position.Y:0.0}, target Y={next.Position.Y:0.0}, interaction-ready={interactionReady}; trying another route.");
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
                        _navTrace($"Loot {next.Identity} retained approach ownership; {(_lootTransitStaging ? "transit staging" : "direct final")} " +
                            $"route {_lootApproachIndex + 1}/{_lootApproachPoints.Count}: " +
                            $"destination={point}, 3D distance={distance:0.0}m, player Y={position.Y:0.0}, target Y={next.Position.Y:0.0}, interaction-ready=false.");
                        break;
                    }
                    _say($"Loot {next.Identity} route {_lootApproachIndex + 1} was rejected by navigation.");
                    _lootApproachIndex++;
                }
                if (!_destination.HasValue)
                {
                    string reason = _lootNonImprovingHops > 0
                        ? $"staging cycle/non-improving candidates exhausted; still {distance:0.0}m away"
                        : $"no navigable chest-side point; still {distance:0.0}m away";
                    ResolveLootApproachFailure(next, room.Instance, reason);
                    return true;
                }
                return true;
            }
            if (_lootRecordedTransit)
            {
                NavigationRouteRecorder.DungeonTopologyContinuation activeAscent = null;
                bool preservedReturn = _navRoutes != null &&
                    _navRoutes.PauseDungeonTopology(next.Position, out activeAscent) &&
                    activeAscent != null && activeAscent.PreciseRecordedPlayback &&
                    activeAscent.Points != null && activeAscent.Points.Count >= 2 &&
                    activeAscent.ProgressIndex > 0 &&
                    activeAscent.Points[activeAscent.ProgressIndex].Y -
                        activeAscent.Points[0].Y >= CombatTopologyVerticalSeparation &&
                    DynelManager.LocalPlayer.Position.Y - activeAscent.Points[0].Y >=
                        CombatTopologyVerticalSeparation;
                if (preservedReturn)
                {
                    _recordedRoomReturn = new RecordedRoomReturn
                    {
                        RoomId = room.Instance, Floor = _floor,
                        Playfield = Playfield.ModelIdentity.Instance, Ascent = activeAscent
                    };
                    _say($"Loot {next.Identity} preserved active recorded ascent '{activeAscent.RouteName}' " +
                        $"at waypoint {activeAscent.ProgressIndex + 1}/{activeAscent.Points.Count} before ManagerLoot handoff; return descent required before doorway travel.");
                }
                else
                    _navRoutes?.StopPlayback();
                _lootRecordedTransit = false;
            }
            _combatTopologyHandoff = null;
            MovementArbiter.Current.Halt(_requestedOwner);
            if (_lootNearStarted == DateTime.MinValue)
                _navTrace($"Loot {next.Identity} interaction-ready=true at 3D distance={distance:0.0}m; " +
                    $"player Y={DynelManager.LocalPlayer.Position.Y:0.0}, target Y={next.Position.Y:0.0}; ownership released to ManagerLoot.");
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
                    ResolveLootApproachFailure(next, room.Instance, "Manager.Loot did not open the container from any reachable side");
            }
            return true;
        }

        private void BuildLootApproaches(Room room, Vector3 target)
        {
            Vector3 player = DynelManager.LocalPlayer.Position;
            _lootApproachPoints.Clear();
            _lootApproachIndex = 0;
            _lootCorpseFinalReissues = 0;
            _lootContainerFinalReissues = 0;
            _lootApproachPoints.AddRange(DungeonApproachPlanner.Candidates(_layout,
                room.Instance, player, target, new[] { 2.5f, 3f }, 0.4f)
                .Where(point => Vector3.Distance(point, target) >= 2f &&
                    Vector3.Distance(point, target) <= LootUseRange &&
                    LocalRoutePlanner.ClearInteractionSegment(point + Vector3.Up,
                        target + Vector3.Up * 0.5f)));
            _lootTransitStaging = _lootApproachPoints.Count == 0;
            if (!_lootTransitStaging)
            {
                _navTrace($"Loot direct final approach selected: {_lootApproachPoints.Count} complete, interaction-clear candidate(s) from {player}.");
                return;
            }
            float verticalGap = Math.Abs(target.Y - player.Y);
            if (!_lootRecordedFallbackAttempted && TryUseCombatTopologyHandoff(room, player,
                target, out string inheritedRoute))
            {
                _lootRecordedTransit = true;
                _lootRecordedRoute = inheritedRoute;
                _say($"Loot inheriting recorded topology '{inheritedRoute}' from combat; " +
                    "resuming/reacquiring toward the target before generated staging.");
                return;
            }
            if (!_lootRecordedFallbackAttempted && verticalGap >= CombatTopologyVerticalSeparation &&
                _navRoutes != null && _navRoutes.TryPrepareDungeonTopology(_layout,
                    room.Instance, player, target, out _lootRecordedRoute))
            {
                _lootRecordedTransit = true;
                _say($"Loot direct final unavailable; using ordered recorded topology '{_lootRecordedRoute}'; " +
                    $"prepared recorded loot topology before generated staging at vertical gap {verticalGap:0.0}m.");
                return;
            }
            List<DungeonTransitPlanner.Route> transit = DungeonTransitPlanner.Candidates(_layout,
                room.Instance, player, target, new[] { 3f, 5f, 8f, 12f }, 0.4f)
                .Where(route => Vector3.Distance(player, route.Destination) >= 1.5f)
                .Where(route => !_lootVisitedStaging.Any(visited =>
                    Vector3.Distance(visited, route.Destination) <= LootStagingRevisitTolerance)).ToList();
            int insufficient = 0;
            if (verticalGap > 3f)
            {
                float requiredGap = Math.Min(_lootBestVerticalGap, verticalGap) - 0.75f;
                insufficient = transit.Count(route =>
                    Math.Abs(target.Y - route.Destination.Y) > requiredGap);
                transit = transit.Where(route =>
                    Math.Abs(target.Y - route.Destination.Y) <= requiredGap).ToList();
            }
            _lootApproachPoints.AddRange(transit.Select(route => route.Destination));
            if (transit.Count > 0)
            {
                DungeonTransitPlanner.Route best = transit[0];
                string elevation = best.TargetLevel ? "target-level" :
                    $"vertically progressive (+{best.VerticalProgress:0.0}m)";
                _say($"Loot direct final unavailable; {elevation} transit selected with " +
                    $"{transit.Count} complete candidate(s), best cost={best.Cost:0.0}m, " +
                    $"source={(best.CorridorDerived ? "navmesh corridor" : "surface sample")}; " +
                    $"rejected {insufficient} candidate(s) for insufficient vertical progress.");
            }
            else if (verticalGap > 3f)
                _say($"Loot direct final unavailable; no complete ramp-connected route made meaningful elevation progress; " +
                    $"rejected {insufficient} same-level candidate(s), player Y={player.Y:0.0}, target Y={target.Y:0.0}.");
            else
                _say("Loot direct final unavailable; no complete same-room transit route was found.");
        }

        private bool TryUseCombatTopologyHandoff(Room room, Vector3 player,
            Vector3 target, out string routeName)
        {
            routeName = null;
            CombatTopologyHandoff handoff = _combatTopologyHandoff;
            if (handoff == null) return false;
            bool stale = DateTime.UtcNow > handoff.ExpiresAt ||
                handoff.RoomId != room.Instance ||
                handoff.DungeonInstance != Playfield.ModelIdentity.Instance;
            bool unrelated = Vector3.Distance(target, handoff.TargetSide) > 6f ||
                Math.Abs(target.Y - handoff.TargetSide.Y) > CombatTopologyVerticalSeparation;
            if (stale || unrelated)
            {
                _combatTopologyHandoff = null;
                _say("Loot recorded topology continuation unavailable; falling back to generated staging.");
                return false;
            }
            if (_navRoutes == null || !_navRoutes.TryPrepareDungeonTopologyContinuation(
                handoff.Continuation, player, target, out routeName))
            {
                _combatTopologyHandoff = null;
                _say("Loot recorded topology continuation unavailable; falling back to generated staging.");
                return false;
            }
            return true;
        }

        private bool LootInteractionReady(Dynel loot)
        {
            if (loot == null || DynelManager.LocalPlayer == null) return false;
            Vector3 player = DynelManager.LocalPlayer.Position;
            if (Vector3.Distance(player, loot.Position) > LootUseRange) return false;
            return LocalRoutePlanner.ClearInteractionSegment(player + Vector3.Up,
                loot.Position + Vector3.Up * 0.5f);
        }

        private void ResolveLootApproachFailure(Dynel loot, int roomId, string reason)
        {
            if (_loot.IsMissionCriticalLoot(loot.Identity))
            {
                Stop();
                _say($"Mission-critical loot {loot.Identity} in room {roomId} could not be reached: {reason}. Mission recovery required.");
                return;
            }
            bool deferred = _loot.DeferMissionLoot(loot.Identity, reason, out int attempt, out int maxAttempts);
            if (deferred)
                _say($"Deferring loot {loot.Identity} in room {roomId}, attempt {attempt}/{maxAttempts}: {reason}; " +
                    "will retry after room traversal. Exploration continues.");
            else
                _say($"Skipping unreachable loot {loot.Identity} in room {roomId} after attempt {attempt}/{maxAttempts}: " +
                    $"{reason}. Independent retry policy exhausted; exploration continues.");
            _waitingForLoot = Identity.None;
            _lootCorpseFinalReissues = 0;
            _lootContainerFinalReissues = 0;
            _lootApproachPoints.Clear();
            _lootProbeNextSide = false;
            _lootTransitStaging = false;
            if (_lootRecordedTransit) _navRoutes?.StopPlayback();
            _lootRecordedTransit = false;
            _lootRecordedFallbackAttempted = false;
            _lootRecordedRoute = null;
            _lootNearStarted = DateTime.MinValue;
            _destination = null;
            MovementArbiter.Current.Halt(_requestedOwner);
        }
        private Room NextRoom(Room current)
        {
            var reserved = new HashSet<int>(_objective.Rooms);
            Func<int, bool> ordinary = id => !_clearedRooms.Contains(id) &&
                (!_combatRecoveries.TryGetValue(id, out CombatRecovery recovery) || !recovery.Active);
            // Prefer routes that avoid entering the objective room entirely.
            Room next = RouteTo(current.Instance, id => ordinary(id) && !reserved.Contains(id), true);
            if (next != null) return next;
            // A cut-through person room may be unavoidable; act on its target on entry.
            next = RouteTo(current.Instance, id => ordinary(id) && !reserved.Contains(id), false);
            if (next != null) return next;
            next = RouteTo(current.Instance, ordinary, false);
            if (next != null) return next;
            Func<int, bool> readyDeferred = id => !_clearedRooms.Contains(id) &&
                _combatRecoveries.TryGetValue(id, out CombatRecovery recovery) &&
                recovery.Active && CombatRoomAvailable(id);
            return RouteTo(current.Instance, readyDeferred, false);
        }

        private bool CombatRoomAvailable(int roomId)
        {
            if (!_combatRecoveries.TryGetValue(roomId, out CombatRecovery recovery) ||
                !recovery.Active) return true;
            return DateTime.UtcNow >= recovery.RetryAt &&
                Vector3.Distance(DynelManager.LocalPlayer.Position, recovery.Anchor) >= 12f;
        }

        private Room NextCombatSweepRoom(Room current)
        {
            Func<int, bool> needsCheck = id => !_clearedRooms.Contains(id) &&
                !_combatCheckedRooms.Contains(id);
            Room next = RouteTo(current.Instance, id => needsCheck(id) &&
                (!_combatRecoveries.TryGetValue(id, out CombatRecovery recovery) || !recovery.Active), true);
            if (next != null) return next;
            next = RouteTo(current.Instance, id => needsCheck(id) &&
                (!_combatRecoveries.TryGetValue(id, out CombatRecovery recovery) || !recovery.Active), false);
            return next ?? RouteTo(current.Instance, id => needsCheck(id) && CombatRoomAvailable(id), false);
        }

        private Room RouteTo(int current, Func<int, bool> isGoal, bool avoidObjectiveRooms)
        {
            if (_layout == null) return null;
            var reserved = new HashSet<int>(_objective.Rooms);
            var frontier = new HashSet<int> { current };
            var parent = new Dictionary<int, int>();
            var cost = new Dictionary<int, float> { [current] = 0f };
            parent[current] = -1;

            while (frontier.Count > 0)
            {
                int index = frontier.OrderBy(id => cost[id]).First();
                frontier.Remove(index);
                Room room = _layout.Room(index);
                if (room == null)
                    continue;
                if (index != current && isGoal(index))
                {
                    int goal = index;
                    while (parent[index] != current && parent[index] != -1)
                        index = parent[index];
                    _navTrace($"Routing from room {current} through {index} toward room {goal}" +
                        (reserved.Contains(index) && !_objective.Finale ? "; objective room encountered on this route." : "."));
                    return _layout.Room(index);
                }

                // Door distance, prior failures and successful crossings form
                // a bounded Dijkstra cost. A failed edge remains usable after
                // its cooldown, but a reliable alternate route is preferred.
                Vector3 entryPosition = index == current
                    ? DynelManager.LocalPlayer.Position
                    : _layout.Edge(parent[index], index).Interior;
                foreach (int adjacentCandidate in _layout.Neighbors(index)
                    .Where(id => !IsUnavailable(index, id))
                    .Where(id => !avoidObjectiveRooms || !reserved.Contains(id)))
                {
                    DungeonLayout.Connection edge = _layout.Edge(index, adjacentCandidate);
                    _edgeFailures.TryGetValue(EdgeKey(index, adjacentCandidate), out EdgeFailure history);
                    float failures = history == null ? 0f :
                        Math.Max(0, history.Count - history.Successes) * 12f;
                    float proposed = cost[index] + 10f + failures +
                        Vector3.Distance(edge.SourceApproach, entryPosition) * 0.3f;
                    if (cost.TryGetValue(adjacentCandidate, out float previous) && previous <= proposed)
                        continue;
                    cost[adjacentCandidate] = proposed;
                    parent[adjacentCandidate] = index;
                    frontier.Add(adjacentCandidate);
                }
            }
            return null;
        }

        private void BeginTransition(int source, int target)
        {
            _requestedOwner = MovementOwner.DoorTransition;
            PostCombatCorpseGuard localGuard = _postCombatCorpseGuards.FirstOrDefault(guard =>
                guard.RoomId == source && (guard.Corpse == Identity.None ||
                    !_loot.IsMissionLootSuccessfullyProcessed(
                        guard.Corpse, guard.RoomId, guard.CorpseOccurrence)));
            if (localGuard != null)
            {
                MovementArbiter.Current.Halt(_requestedOwner);
                _destination = null;
                _roomQuietAt = DateTime.MinValue;
                _say($"Transition {source}->{target} held: post-combat corpse ownership for " +
                    $"{(localGuard.Corpse == Identity.None ? localGuard.Enemy : localGuard.Corpse)} must settle in verified room {source} first.");
                return;
            }
            DungeonLayout.Connection edge = _layout.Edge(source, target);
            if (edge == null) return;
            Door door = _layout.DoorAt(edge);
            DateTime now = DateTime.UtcNow;
            _transition = new Transition
            {
                Edge = edge, Phase = TransitionPhase.SourceApproach,
                Started = now, PhaseStarted = now, LastProgress = now,
                BestDistance = float.MaxValue
            };
            if (_recordedRoomReturn != null &&
                _recordedRoomReturn.RoomId == source && _recordedRoomReturn.Floor == _floor &&
                _recordedRoomReturn.Playfield == Playfield.ModelIdentity.Instance &&
                DynelManager.LocalPlayer.Position.Y - edge.SourceApproach.Y >= CombatTopologyVerticalSeparation)
            {
                if (_navRoutes == null || !_navRoutes.TryPrepareRecordedReturn(
                    _recordedRoomReturn.Ascent, DynelManager.LocalPlayer.Position,
                    edge.SourceApproach, out _transition.ReturnGround))
                {
                    Stop();
                    _say($"Transition {source}->{target} held: retained ramp descent cannot attach safely; no direct ground-door attempt was issued.");
                    return;
                }
                MovementArbiter.Current.Halt(_requestedOwner);
                _transition.Phase = TransitionPhase.RecordedReturn;
                _transition.ReturnLastPosition = DynelManager.LocalPlayer.Position;
            }
            _loot.EndMissionRoom(restoreUserState: false);
            _destination = null;
            _observedRoom = -1;
            _navTrace($"Transition {source}->{target}: source approach {edge.SourceApproach}, door center {edge.DoorCenter}, " +
                $"target centerline {edge.TargetCenterline}, aligned safe interior {edge.SafeInterior}, " +
                $"deep fallback {edge.DeepInterior}, door {door?.Identity.ToString() ?? "none"}.");
            if (_transition.Phase == TransitionPhase.RecordedReturn) return;
            if (UseLiveDoorwayHeight(edge, edge.Interior, DynelManager.LocalPlayer.Position))
                _say($"Transition {source}->{target}: Mali interior height {edge.Interior.Y:0.0} differs from the live floor " +
                    $"{DynelManager.LocalPlayer.Position.Y:0.0}; using live floor height for this same-floor crossing.");
            Navigate(DoorwayWaypoint(edge, edge.SourceApproach, DynelManager.LocalPlayer.Position));
        }

        private void TickRecordedReturn(Room room, Transition crossing)
        {
            Vector3 position = DynelManager.LocalPlayer.Position;
            DateTime now = DateTime.UtcNow;
            if (room.Instance != crossing.Edge.Source)
            {
                Stop();
                _say("Recorded return descent left its verified source room; doorway crossing held.");
                return;
            }
            if (_readiness.InCombat)
            {
                MovementArbiter.Current.Halt(MovementOwner.DoorTransition);
                crossing.Started = crossing.LastProgress = now;
                crossing.ReturnLastPosition = position;
                return;
            }
            if (Vector3.Distance(position, crossing.ReturnLastPosition) >= 0.5f)
            {
                crossing.LastProgress = now;
                crossing.ReturnLastPosition = position;
            }
            if (now - crossing.Started > TimeSpan.FromSeconds(75) ||
                now - crossing.LastProgress > TimeSpan.FromSeconds(10))
            {
                Stop();
                _say("Recorded return descent stalled; stopped without charging the doorway edge or attempting a direct ground shortcut.");
                return;
            }
            if (_navRoutes == null || !_navRoutes.TryNavigatePreparedDungeonTopology(
                crossing.ReturnGround, MovementArbiter.Current, MovementOwner.DoorTransition,
                out bool completed))
            {
                Stop();
                _say("Recorded return descent playback failed; doorway crossing held.");
                return;
            }
            if (!completed) return;
            MovementArbiter.Current.Halt(MovementOwner.DoorTransition);
            _recordedRoomReturn = null;
            _destination = null;
            crossing.Phase = TransitionPhase.SourceApproach;
            crossing.Started = crossing.PhaseStarted = crossing.LastProgress = now;
            crossing.BestDistance = float.MaxValue;
            _say($"Transition {crossing.Edge.Source}->{crossing.Edge.Target}: recorded return descent reached ground attachment {crossing.ReturnGround}; normal doorway approach resumed.");
        }

        internal static double DoorProbeSeconds(bool locked) => locked ? 1d : 3d;

        private void TickTransition(Room detectedRoom)
        {
            _requestedOwner = MovementOwner.DoorTransition;
            Transition crossing = _transition;
            DungeonLayout.Connection edge = crossing.Edge;
            if (crossing.Phase == TransitionPhase.RecordedReturn)
            {
                TickRecordedReturn(detectedRoom, crossing);
                return;
            }
            Door door = _layout.DoorAt(edge);
            bool passageOpen = IsPassageOpen(edge, door);
            DateTime now = DateTime.UtcNow;
            Vector3 position = DynelManager.LocalPlayer.Position;
            bool targetDetected = detectedRoom.Instance == edge.Target;
            float inwardProgress = InwardProgress(edge, position);
            bool safelyInsideTarget = targetDetected &&
                LocalRoutePlanner.HorizontalDistance(position, edge.DoorCenter) > 1.5f &&
                _layout.IsInside(edge.Target, position, 0.4f);
            if (targetDetected)
            {
                if (_observedRoom != edge.Target)
                {
                    _observedRoom = edge.Target;
                    _observedRoomAt = now;
                    crossing.TargetObservedPosition = position;
                    _navTrace($"Transition {edge.Source}->{edge.Target}: target room detected at {position}; " +
                        $"inward={inwardProgress:0.00}m, lateral={LateralOffset(edge, position):0.00}m; confirming entry.");
                }
                if (safelyInsideTarget && now - _observedRoomAt >= TimeSpan.FromMilliseconds(500))
                {
                    ConfirmTransition();
                    return;
                }
                if (safelyInsideTarget)
                {
                    // The route can still be heading to a laterally displaced
                    // Mali sample. Stop immediately while room proof settles.
                    MovementArbiter.Current.Halt(MovementOwner.DoorTransition);
                    return;
                }
                // A stable AO room identity plus forward progress is sufficient
                // when the polygon margin disagrees. Do not drift sideways
                // during that observation window.
                if (inwardProgress >= 1.5f)
                {
                    MovementArbiter.Current.Halt(MovementOwner.DoorTransition);
                    if (now - _observedRoomAt >= TimeSpan.FromSeconds(2))
                    {
                        _say($"Transition {edge.Source}->{edge.Target}: live room identity and {inwardProgress:0.0}m of centerline crossing remained stable; confirming entry despite mapped margin disagreement.");
                        ConfirmTransition();
                    }
                    return;
                }
            }
            else
                _observedRoom = -1;

            // Room detection can switch before the approach phase sees the door.
            // Once we are across its Mali boundary, never steer back to the threshold.
            if (crossing.Phase != TransitionPhase.SafeInterior &&
                detectedRoom.Instance == edge.Target &&
                _layout.IsInside(edge.Target, position, 0.2f))
            {
                _navTrace($"Transition {edge.Source}->{edge.Target}: boundary crossed; moving into target interior.");
                crossing.Phase = TransitionPhase.SafeInterior;
                crossing.PhaseStarted = now;
                crossing.LastProgress = now;
                crossing.BestDistance = float.MaxValue;
                _destination = null;
                Navigate(CrossingWaypoint(edge, position));
                return;
            }

            if (now - crossing.Started > TimeSpan.FromSeconds(30))
            {
                FailTransition("entry was not confirmed within 30 seconds");
                return;
            }

            if (crossing.Phase == TransitionPhase.SourceApproach)
            {
                Vector3 approach = DoorwayWaypoint(edge, edge.SourceApproach, position);
                if (Vector3.Distance(position, approach) > 1.1f)
                {
                    if (now - crossing.LastProgress > TimeSpan.FromSeconds(7))
                    { FailTransition("source-side doorway approach stalled"); return; }
                    if (Vector3.Distance(position, approach) + 0.3f < crossing.BestDistance)
                    { crossing.BestDistance = Vector3.Distance(position, approach); crossing.LastProgress = now; }
                    Navigate(approach);
                    return;
                }
                string liveDoorRange = door == null ? "none" :
                    $"{Vector3.Distance(position, door.Position):0.0}m";
                string aoDoorRange = door == null ? "none" :
                    $"{door.DistanceFrom(DynelManager.LocalPlayer):0.0}m";
                _navTrace($"Transition {edge.Source}->{edge.Target}: door reached " +
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
                else if (now - crossing.PhaseStarted < TimeSpan.FromSeconds(DoorProbeSeconds(door.IsLocked)))
                {
                    Navigate(CrossingWaypoint(edge, position));
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
                    _navTrace($"Transition {edge.Source}->{edge.Target}: doorway open; crossing.");
                    crossing.Phase = TransitionPhase.TargetCenterline;
                    crossing.PhaseStarted = now;
                    crossing.LastProgress = now;
                    crossing.BestDistance = float.MaxValue;
                    _destination = null;
                    Navigate(CrossingWaypoint(edge, position));
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
                        MovementArbiter.Current.SetNavDestination(_requestedOwner,
                            DoorwayWaypoint(edge, edge.SourceApproach, position));
                        crossing.LastApproachCommand = now;
                        _destination = DoorwayWaypoint(edge, edge.SourceApproach, position);
                    }
                    return;
                }
                if (now - crossing.LastAction < TimeSpan.FromSeconds(2))
                    return;
                MovementArbiter.Current.Halt(_requestedOwner);
                if (door.IsLocked)
                {
                    CarriedItems.RequestBackpackSnapshots("locked mission door");
                    if (!CarriedItems.BackpackSnapshotsReady) return;
                    if (++crossing.DoorAttempts > 5)
                    {
                        FailTransition("door did not open after five attempts");
                        return;
                    }
                    Item pick = CarriedItems.FindLockpick();
                    if (pick == null)
                    {
                        FailTransition("locked door requires a Lock Pick");
                        return;
                    }
                    pick.UseOn(door);
                    _say($"Transition {edge.Source}->{edge.Target}: lockpick attempt {crossing.DoorAttempts} on {door.Identity} (open={door.IsOpen}, locked={door.IsLocked}, door {doorDistance:0.0}m, AO# {aoDoorDistance:0.0}m).");
                }
                else
                {
                    if (++crossing.DoorAttempts > 5)
                    {
                        FailTransition("door did not open after five attempts");
                        return;
                    }
                    door.Use();
                    _say($"Transition {edge.Source}->{edge.Target}: open attempt {crossing.DoorAttempts} on {door.Identity} (door {doorDistance:0.0}m, AO# {aoDoorDistance:0.0}m, passage open={passageOpen}).");
                }
                crossing.LastAction = now;
                return;
            }

            Vector3 destination = CrossingWaypoint(edge, position, crossing.PushingDeeper);
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
                Vector3 observedProgress = position - crossing.TargetObservedPosition;
                observedProgress.Y = 0;
                // An AO room change can precede a safe physical crossing. If
                // the navmesh turns in place at the doorway, use the short
                // live-floor crossing without waiting for a full route stall.
                if (!crossing.PushingDeeper &&
                    now - _observedRoomAt > TimeSpan.FromSeconds(1.2) &&
                    (stalled || !SMovementController.IsNavigating() || observedProgress.Magnitude < 0.35f))
                {
                    crossing.PushingDeeper = true;
                    crossing.LastProgress = now;
                    crossing.BestDistance = float.MaxValue;
                    _destination = null;
                    _say($"Transition {edge.Source}->{edge.Target}: room changed but safe entry is unconfirmed " +
                        $"({observedProgress.Magnitude:0.0}m horizontal progress, navigating={SMovementController.IsNavigating()}); " +
                        "trying a short direct crossing at the live floor height.");
                }
                if (crossing.PushingDeeper)
                {
                    Vector3 inward = edge.SafeInterior - edge.DoorCenter;
                    inward.Y = 0;
                    if (inward.Magnitude > 0.1f &&
                        now - crossing.LastApproachCommand >= TimeSpan.FromSeconds(1))
                    {
                        Vector3 direct = edge.SafeInterior;
                        direct.Y = position.Y;
                        if (MovementArbiter.Current.Owner != _requestedOwner || !_destination.HasValue ||
                            Vector3.Distance(_destination.Value, direct) > 0.5f || !SMovementController.IsNavigating())
                        {
                            if (MovementArbiter.Current.SetDestination(_requestedOwner, direct))
                                _destination = direct;
                        }
                        crossing.LastApproachCommand = now;
                    }
                }
                else Navigate(CrossingWaypoint(edge, position));
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
                crossing.Phase = TransitionPhase.SafeInterior;
                crossing.PhaseStarted = now;
                crossing.LastProgress = now;
                crossing.BestDistance = float.MaxValue;
                _destination = null;
                _say($"Transition {edge.Source}->{edge.Target}: still in room {detectedRoom.Instance}; pushing deeper (retry {crossing.CrossingRetries}/3).");
            }
            Navigate(CrossingWaypoint(edge, position, crossing.PushingDeeper));
        }

        private void ConfirmTransition()
        {
            DungeonLayout.Connection edge = _transition.Edge;
            MovementArbiter.Current.Release(MovementOwner.DoorTransition);
            _requestedOwner = MovementOwner.DungeonRoom;
            _currentRoom = edge.Target;
            _loot.ObserveMissionRoomTraversal();
            _visitedRooms.Add(edge.Target);
            if (!_roomStates.ContainsKey(edge.Target)) _roomStates[edge.Target] = RoomClearanceState.Entered;
            string key = EdgeKey(edge.Source, edge.Target);
            if (_edgeFailures.TryGetValue(key, out EdgeFailure history))
            {
                history.Successes++;
                history.ConsecutiveFailures = 0;
                history.Until = DateTime.MinValue;
                history.Permanent = false;
            }
            _reverseCooldown[EdgeKey(edge.Source, edge.Target)] = DateTime.UtcNow.AddSeconds(ReverseEdgeCooldownSeconds);
            _navTrace($"Transition {edge.Source}->{edge.Target}: confirmed in " +
                (_clearedRooms.Contains(edge.Target) ? "previously cleared room; no repeat clearance pause" : "target room") +
                $"; inward={InwardProgress(edge, DynelManager.LocalPlayer.Position):0.00}m, " +
                $"lateral={LateralOffset(edge, DynelManager.LocalPlayer.Position):0.00}m; " +
                $"reverse edge on {ReverseEdgeCooldownSeconds}-second cooldown.");
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
            int consecutive = ++failure.ConsecutiveFailures;
            failure.Permanent = consecutive >= 3;
            failure.Until = DateTime.UtcNow.AddSeconds(consecutive == 1 ? 30 : 90);
            _say($"Transition {edge.Source}->{edge.Target}: {reason}; failure {consecutive}/3, " +
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
                if (MovementArbiter.Current.SetNavDestination(_requestedOwner, destination))
                    _destination = destination;
            }
        }

        private Vector3 CrossingWaypoint(DungeonLayout.Connection edge, Vector3 position, bool deep = false)
        {
            Vector3 waypoint = _observedRoom == edge.Target ? edge.SafeInterior :
                deep ? edge.DeepInterior :
                _transition?.Phase == TransitionPhase.SafeInterior ? edge.SafeInterior : edge.TargetCenterline;
            if (UseLiveDoorwayHeight(edge, waypoint, position))
                waypoint.Y = position.Y;
            return waypoint;
        }

        private static float InwardProgress(DungeonLayout.Connection edge, Vector3 position)
        {
            Vector3 inward = edge.TargetCenterline - edge.DoorCenter;
            inward.Y = 0;
            if (inward.Magnitude < 0.1f) return 0f;
            Vector3 crossed = position - edge.DoorCenter;
            crossed.Y = 0;
            inward = inward.Normalize();
            return crossed.X * inward.X + crossed.Z * inward.Z;
        }

        private static float LateralOffset(DungeonLayout.Connection edge, Vector3 position)
        {
            Vector3 inward = edge.TargetCenterline - edge.DoorCenter;
            inward.Y = 0;
            if (inward.Magnitude < 0.1f) return 0f;
            inward = inward.Normalize();
            Vector3 crossed = position - edge.DoorCenter;
            return crossed.X * -inward.Z + crossed.Z * inward.X;
        }

        private Vector3 DoorwayWaypoint(DungeonLayout.Connection edge, Vector3 waypoint, Vector3 position)
        {
            if (UseLiveDoorwayHeight(edge, waypoint, position)) waypoint.Y = position.Y;
            return waypoint;
        }

        private bool UseLiveDoorwayHeight(DungeonLayout.Connection edge, Vector3 waypoint, Vector3 position)
        {
            Room source = _layout.Room(edge.Source), target = _layout.Room(edge.Target);
            // Mali's wall mesh may carry a height several metres above the
            // live walkable floor at a nearby doorway. Keep real height changes
            // farther inside the room for ramps and multi-level geometry.
            float dx = waypoint.X - edge.Threshold.X, dz = waypoint.Z - edge.Threshold.Z;
            return source != null && target != null && source.Floor == target.Floor &&
                Math.Abs(position.Y - edge.Threshold.Y) <= 1.5f &&
                Math.Abs(waypoint.Y - position.Y) > 1f && dx * dx + dz * dz <= 16f;
        }

        private static string EdgeKey(int a, int b) =>
            a < b ? $"{a}:{b}" : $"{b}:{a}";
    }
}
