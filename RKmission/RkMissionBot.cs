using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AOSharp.Common.GameData;
using AOSharp.Core;
using AOSharp.Core.Inventory;
using AOSharp.Core.UI;
using AOSharp.Pathfinding;
using MaliMissionRoller2;
using MalisDungeonMap2;
using SmokeLounge.AOtomation.Messaging.Messages;

namespace RKmission
{
    public sealed class RkMissionBot : AOPluginEntry
    {
        private MaliMissionRoller2.Main _roller;
        private DungeonMap _map;
        private ManagerLoot.ManagerLoot _loot;
        private MissionDungeon _dungeon;
        private MissionReadiness _readiness;
        private InventoryPolicy _inventory;
        private DeathRecoveryController _deathRecovery;
        private readonly AcceptedMissions _missions = new AcceptedMissions();
        private readonly ReturnItemHandIn _handIn = new ReturnItemHandIn();
        private LogisticsProbe _logisticsProbe;
        private LogisticsRouteCatalog _logisticsRoutes;
        private LogisticsRouteNavigator _logisticsNavigator;
        private BankTransactions _bankTransactions;
        private ShopSaleTest _shopSale;
        private AutomaticLogisticsCycle _automaticLogistics;
        private LocalMissionTravel _travel;
        private MovementArbiter _movement;
        private ScottyboiWarpProvider _warp;
        private FGridServiceProvider _fgrid;
        private RubiKaTravelPlanner _longTravel;
        private MissionEntranceResolver _entranceResolver;
        private NekoPanel _nekoPanel;
        private NavigationRouteRecorder _navRoutes;
        private NavigationRecorderWindow _navWindow;
        private PostZoneFlightSafety _postZoneSafety;
        private VehicleLifecycle _vehicle;
        private MissionCheckpoint _checkpoint;
        private AcceptedMission _selected;
        private bool _autoCycle, _autoRolling, _hasRollTerminal, _recoveringDeath, _deathReturnToRollTerminal;
        private bool _missionBatchRemountPending;
        private bool _capacityLogisticsBlocked, _capacityLogisticsDeferredWhileAccepted;
        private bool _clearAcceptedBeforeRolling;
        private bool _autoLogisticsPending, _autoLogisticsRunning, _autoLogisticsFinalPending;
        private bool _autoLogisticsFinalRun, _autoFinalLogisticsComplete;
        private int _autoLogisticsPasses;
        private string _autoLogisticsReason;
        private int _autoZone, _autoRollCount, _maxAutoRolls = 100, _rollTerminalPlayfield;
        private int _maxAutoMissions, _autoAcceptedCount;
        private readonly HashSet<int> _autoAcceptedIds = new HashSet<int>();
        private readonly HashSet<int> _awaitingQuestDetails = new HashSet<int>();
        private readonly Dictionary<int, int> _autoAcceptedPlayfields = new Dictionary<int, int>();
        private Vector3 _rollTerminalPosition;
        private string _rollTerminalIdentity, _rollLogisticsSite;
        private Identity _rollingTerminalIdentity = Identity.None;
        private MissionTerminal _rollingTerminalReference;
        private int _rollingTerminalPlayfield;
        private Vector3 _rollingTerminalPosition;
        private DateTime _nextReturnMove, _returnStarted, _returnTerminalVisibilitySince;
        private Vector3 _lastReturnProgressPosition;
        private Vector3 _returnTerminalApproach, _returnLandingStaging;
        private bool _returnTerminalPlanReady;
        private int _returnNoProgressAttempts;
        private bool _returnCityRouteEligible;
        private Vector3 _returnScottyLanding;
        private DateTime _logisticsOriginWaitStarted;
        private const float RollTerminalApproachDistance = 3f;
        private bool _running, _dungeonStarted, _clearanceReported, _verifiedRun, _travelInvalidated;
        private Identity _observedDungeon = Identity.None;
        private Identity _activeDungeon = Identity.None;
        private Identity _mapMission = Identity.None, _handoffDoor = Identity.None;
        private int _mapPlayfield;
        private Vector3 _mapAnchor;
        private DateTime _dungeonObservedAt, _nextTick, _nextSelection, _handoffWaitStarted;
        private DateTime _nextAssociationDiagnostic;
        private int _associationAttempts;
        private string _waitingReason;
        private bool _exitZoningStarted;
        private bool _verifiedExitReported;
        private bool _handInExitVerified;
        private DateTime _handInLocateStarted;
        private int _completedMissionWarpTarget;
        private bool _completedMissionWarpAttempted;
        private string _lifecycleSignature;
        private Vector3 _lifecyclePosition;
        private DateTime _lastLifecycleProgress, _watchdogRecoveryAt;

        public override void Run(string pluginDir)
        {
            _movement = MovementArbiter.Current = new MovementArbiter();
            _autoZone = 0; // Unset means any enabled Rubi-Ka location in the roller.
            _roller = new MaliMissionRoller2.Main();
            _roller.Run(System.IO.Path.Combine(pluginDir, "Plugins", "MaliMissionRoller2"));
            _roller.RollerWindowClosed += OnRollerWindowClosed;
            _map = new DungeonMap();
            _map.RunEmbedded(System.IO.Path.Combine(pluginDir, "Plugins", "MalisDungeonMap2"), MaliMissionRoller2.Main.Window, _roller.ShowRoller);
            _loot = new ManagerLoot.ManagerLoot();
            _loot.RunEmbedded(System.IO.Path.Combine(pluginDir, "Plugins", "ManagerLoot"), MaliMissionRoller2.Main.Window, _roller.ShowRoller);
            _readiness = new MissionReadiness(Say, MissionReadinessSettings.Load(pluginDir, Say));
            _inventory = InventoryPolicy.Load(pluginDir, Say);
            _inventory.Initialize();
            _loot.OperationalItemProtected = _inventory.IsOperationalItem;
            _loot.MissionReservationActive = owner =>
            {
                AcceptedMission mission = _missions.Find(owner);
                return mission != null && !mission.Completed &&
                    (mission.Present || mission.ReturnHandInPending ||
                    mission.State == MissionProgress.InProgress ||
                    mission.State == MissionProgress.AwaitingHandIn);
            };
            _logisticsProbe = new LogisticsProbe(pluginDir, _loot, Say);
            _logisticsRoutes = new LogisticsRouteCatalog(pluginDir, Say);
            _logisticsNavigator = new LogisticsRouteNavigator(_logisticsRoutes, _movement, Say);
            _bankTransactions = new BankTransactions(_logisticsNavigator, _loot, _inventory, Say);
            _shopSale = new ShopSaleTest(_logisticsNavigator, _loot, _inventory, Say);
            _automaticLogistics = new AutomaticLogisticsCycle(_logisticsNavigator,
                _bankTransactions, _shopSale, _loot, _inventory, Say);
            _deathRecovery = new DeathRecoveryController(_readiness, _movement, Say);
            _travel = new LocalMissionTravel(Say, pluginDir);
            _warp = new ScottyboiWarpProvider(pluginDir, Say, _movement);
            _navRoutes = new NavigationRouteRecorder(pluginDir, Say);
            _navWindow = new NavigationRecorderWindow(pluginDir, _navRoutes, Say);
            _dungeon = new MissionDungeon(Say, _loot, _readiness, _inventory, _navRoutes);
            _postZoneSafety = new PostZoneFlightSafety(_movement, Say);
            _vehicle = new VehicleLifecycle(_movement, _inventory, Say);
            _fgrid = new FGridServiceProvider(pluginDir, Say, _movement, _navRoutes);
            _longTravel = new RubiKaTravelPlanner(pluginDir, _warp, _fgrid, _movement, _navRoutes, Say);
            _entranceResolver = new MissionEntranceResolver(pluginDir, Say);
            MaliMissionRoller2.Main.Window.EnsureNekoView = () =>
            {
                if (_nekoPanel == null)
                    _nekoPanel = new NekoPanel(pluginDir, _entranceResolver, ManualNekoWarp,
                        () => { _missions.Refresh(true); return _missions.Records; }, SelectNekoMission);
                MaliMissionRoller2.Main.Window.AttachNekoView(_nekoPanel.Root);
            };
            _checkpoint = MissionCheckpoint.Load(pluginDir, Say);
            CarriedItems.Initialize();
            if (_checkpoint.OriginTerminals == null)
                _checkpoint.OriginTerminals = new List<MissionOriginBinding>();
            if (_checkpoint.AcceptedMissionIds == null)
                _checkpoint.AcceptedMissionIds = new List<int>();
            _missions.AttachCheckpoint(_checkpoint, Say);
            _maxAutoMissions = Math.Max(0, _checkpoint.AutoMissionLimit);
            _autoAcceptedCount = Math.Max(0, _checkpoint.AutoAcceptedCount);
            _hasRollTerminal = _checkpoint.HasRollTerminal && _checkpoint.RollTerminalPlayfield > 0 &&
                AcceptedMissions.Finite(_checkpoint.RollTerminalPosition);
            if (_hasRollTerminal)
            {
                _rollTerminalPlayfield = _checkpoint.RollTerminalPlayfield;
                _rollTerminalPosition = _checkpoint.RollTerminalPosition;
                _rollLogisticsSite = _checkpoint.RollLogisticsSite;
                _rollTerminalIdentity = !string.IsNullOrEmpty(_checkpoint.RollTerminalIdentity)
                    ? _checkpoint.RollTerminalIdentity
                    : _checkpoint.RollLogisticsTerminalIdentity;
            }
            // A module reload/state refresh must never silently re-arm movement,
            // rolling, or accepted-mission mutation. The saved binding remains
            // available for an explicit start/auto command to reconcile.
            if (_checkpoint.Armed)
            {
                _checkpoint.Armed = false;
                _checkpoint.Phase = "SafeStopped";
                _checkpoint.Save(true, Say);
                Say("Saved mission context found after reload; remaining safely stopped until explicit /rkm start or /rkm auto.");
            }
            SMovementController.Set();
            // The pinned SDK's default stuck action directly changes player
            // position. Replace that action; our Stuck event handles replanning.
            SMovementController.SetStuckLogic(() => { });
            // AO# labels Fixer Grid a dungeon even though it is a travel playfield.
            // Allow an explicitly supplied 4107.nav to load there as well.
            SMovementController.AutoLoadNavmeshes($"{pluginDir}\\NavMeshes",
                (id, dungeon) => !dungeon || id == (int)PlayfieldId.FixerGrid);
            SMovementController.OnRubberband += OnRubberband;
            SMovementController.Stuck += OnStuck;
            Chat.RegisterCommand("rkm", Command);
            Game.OnUpdate += Update;
            Game.TeleportStarted += ZoningStarted;
            Game.TeleportEnded += ZoningEnded;
            Network.N3MessageSent += _readiness.ObserveAction;
            Network.N3MessageSent += _missions.ObserveSent;
            Network.N3MessageReceived += _missions.ObserveQuest;
            Network.N3MessageReceived += ObserveMissionMessage;
            Mission.RollListChanged += _missions.ObserveRoll;
            Mission.RollListChanged += ObserveAutoRoll;
            Say("Loaded. /rkm start keeps local takeover; /rkm auto enables automatic rolling and cross-playfield travel. /rkm missions shows accepted work.");
        }

        public override void Teardown()
        {
            _roller.RollerWindowClosed -= OnRollerWindowClosed;
            _postZoneSafety?.Dispose();
            _navWindow?.Dispose();
            _nekoPanel?.Dispose();
            _navRoutes?.Dispose();
            _logisticsProbe?.Dispose();
            _automaticLogistics?.Dispose();
            _bankTransactions?.Dispose();
            _shopSale?.Dispose();
            _logisticsNavigator?.Dispose();
            Stop(true);
            Game.OnUpdate -= Update;
            Game.TeleportStarted -= ZoningStarted;
            Game.TeleportEnded -= ZoningEnded;
            CarriedItems.Teardown();
            _inventory?.Dispose();
            Network.N3MessageSent -= _readiness.ObserveAction;
            Network.N3MessageSent -= _missions.ObserveSent;
            Network.N3MessageReceived -= _missions.ObserveQuest;
            Network.N3MessageReceived -= ObserveMissionMessage;
            Mission.RollListChanged -= _missions.ObserveRoll;
            Mission.RollListChanged -= ObserveAutoRoll;
            SMovementController.OnRubberband -= OnRubberband;
            SMovementController.Stuck -= OnStuck;
            _dungeon.Dispose();
            _entranceResolver.Dispose();
            _fgrid.Dispose();
            _warp.Dispose();
            _roller.Teardown();
            _map.Teardown();
            _loot.Teardown();
        }

        private static void Say(string text) => Chat.WriteLine("RKMission: " + text);

        private void OnRollerWindowClosed(object sender, EventArgs args)
        {
            if (!_autoCycle) return;

            Stop();
            Say("Roller window closed; automatic cycle stopped. Use /rkm auto to restart it.");
        }

        private void ObserveMissionMessage(object sender, N3Message message)
        {
            if (_running && _verifiedRun) _dungeon.Objective?.ObserveMessage(message);
        }

        private void Command(string command, string[] args, ChatWindow window)
        {
            string verb = args == null || args.Length == 0 ? "status" : args[0].ToLowerInvariant();
            switch (verb)
            {
                case "status":
                    Say($"Armed={_running}, cycle={(_autoCycle ? "automatic" : "local")}, roll zone={(_autoZone == 0 ? "enabled locations" : _autoZone.ToString())}, travel={_travel.Mode}/{_travel.Status}, " +
                        $"accepted RK={_missions.Records.Count(x => x.Present && x.IsRubiKaDestination)}, " +
                        $"rolling={_autoRolling}, offers={_autoRollCount}/{_maxAutoRolls}, " +
                        $"auto accepted={_autoAcceptedCount}/{(_maxAutoMissions == 0 ? "unlimited" : _maxAutoMissions.ToString())}, " +
                        $"mission={_selected?.Name ?? "none"} [{_selected?.State.ToString() ?? "none"}], dungeon={_dungeon.Status}, HUD1={_vehicle.Status}.");
                    if (_autoLogisticsPending || _autoLogisticsRunning)
                        Say($"Mission-cycle logistics={(_autoLogisticsRunning ? _automaticLogistics.Status : "queued")}; " +
                            $"reason={_autoLogisticsReason ?? "inventory policy"}.");
                    if (_longTravel.IsActive)
                        Say($"Cross-playfield provider={_longTravel.CurrentProvider}; last issue={_longTravel.LastFailure ?? _warp.LastFailure ?? "none"}.");
                    if (_waitingReason != null) Say(_waitingReason);
                    break;
                case "start":
                    if (_bankTransactions.IsActive || _shopSale.IsActive || _automaticLogistics.IsActive)
                        Say("Finish or stop the logistics transaction before arming mission travel.");
                    else Start();
                    break;
                case "auto":
                    if (_bankTransactions.IsActive || _shopSale.IsActive || _automaticLogistics.IsActive)
                    { Say("Finish or stop the logistics transaction before arming the automatic cycle."); break; }
                    _missions.Refresh(true);
                    AcceptedMission currentDungeon = ExactCurrentDungeonMission();
                    if (currentDungeon != null)
                    {
                        _autoCycle = true;
                        _clearAcceptedBeforeRolling = false;
                        Start();
                        _selected = currentDungeon;
                        Say($"Automatic cycle armed in exact accepted mission {currentDungeon.Id.Instance}; verified-dungeon resume takes precedence over rolling or accepted-mission clearing.");
                        break;
                    }
                    if (!_roller.ShowRoller())
                    { Say("Roller window could not be reopened; automatic cycle was not started."); break; }
                    if (!_running || !_autoCycle)
                    {
                        _autoLogisticsPending = _autoLogisticsRunning = _autoLogisticsFinalPending = false;
                        _autoLogisticsFinalRun = _autoFinalLogisticsComplete = false;
                        _autoLogisticsPasses = 0;
                        _autoLogisticsReason = null;
                        _missions.Refresh(true);
                        var existing = _missions.Records.Where(x => x.Present &&
                            x.IsRubiKaDestination && !x.Completed).ToList();
                        _autoAcceptedCount = existing.Count;
                        _autoAcceptedIds.Clear();
                        _autoAcceptedPlayfields.Clear();
                        _awaitingQuestDetails.Clear();
                        foreach (AcceptedMission mission in existing)
                        {
                            _autoAcceptedIds.Add(mission.Id.Instance);
                            _autoAcceptedPlayfields[mission.Id.Instance] = mission.PlayfieldId;
                        }
                        _clearAcceptedBeforeRolling = existing.Count > 0 || Mission.List == null;
                        if (existing.Count > 0)
                            Say($"Counting {existing.Count} already accepted Rubi-Ka mission(s) toward the automatic limit; " +
                                "clearing all accepted missions before returning to the roller terminal.");
                    }
                    Dynel? visibleTerminal = FindVisibleRollTerminal();
                    if (visibleTerminal != null) RememberRollTerminal(visibleTerminal);
                    _autoCycle = true; Start(); Say("Automatic mission cycle armed."); break;
                case "local":
                    if (_bankTransactions.IsActive || _shopSale.IsActive || _automaticLogistics.IsActive)
                    { Say("Finish or stop the logistics transaction before arming local takeover."); break; }
                    if (_autoRolling) MaliMissionRoller2.Main.Window?.StopZoneRolling();
                    _autoRolling = _autoCycle = false;
                    _clearAcceptedBeforeRolling = false;
                    _autoLogisticsPending = _autoLogisticsRunning = _autoLogisticsFinalPending = false;
                    _autoLogisticsFinalRun = _autoFinalLogisticsComplete = false;
                    _autoLogisticsPasses = 0;
                    _autoLogisticsReason = null;
                    _longTravel.Reset();
                    Start(); Say("Local mission takeover armed."); break;
                case "stop": Stop(); Say("Stopped. Use /rkm start for local takeover or /rkm auto for the automatic cycle."); break;
                case "zone":
                    if (args.Length > 1 && args[1].Equals("all", StringComparison.OrdinalIgnoreCase))
                    { _autoZone = 0; Say("Automatic rolling uses all enabled Rubi-Ka locations."); }
                    else if (args.Length > 1 && int.TryParse(args[1], out int zone) &&
                        AcceptedMissions.IsRubiKaPlayfield(zone))
                    { _autoZone = zone; Say($"Automatic rolling zone set to {zone}."); }
                    else Say("Usage: /rkm zone <Rubi-Ka playfield id|all>.");
                    break;
                case "rolls":
                    if (args.Length > 1 && int.TryParse(args[1], out int count) && count > 0)
                        _maxAutoRolls = count;
                    Say($"Automatic roll limit: {_maxAutoRolls}.");
                    break;
                case "limit":
                case "missionslimit":
                    if (args.Length > 1)
                    {
                        if (args[1].Equals("off", StringComparison.OrdinalIgnoreCase))
                            _maxAutoMissions = 0;
                        else if (int.TryParse(args[1], out int missionLimit) &&
                            missionLimit > 0 && missionLimit <= 1000)
                            _maxAutoMissions = missionLimit;
                        else
                        { Say("Usage: /rkm limit <1-1000|off> (automatic acceptances per cycle)."); break; }
                        _checkpoint.AutoMissionLimit = _maxAutoMissions;
                        _checkpoint.Save(true, Say);
                    }
                    Say($"Automatic mission limit: {(_maxAutoMissions == 0 ? "unlimited" : _maxAutoMissions.ToString())}; " +
                        $"accepted this cycle: {_autoAcceptedCount}.");
                    break;
                case "travel":
                    if (args.Length < 2 || !Enum.TryParse(args[1], true, out TravelMode mode) ||
                        !Enum.IsDefined(typeof(TravelMode), mode))
                        Say("Usage: /rkm travel auto|ground|flying (default auto).");
                    else
                    {
                        _travel.Mode = mode;
                        if (!Playfield.IsDungeon && !_verifiedRun)
                            _selected = null; // Explicit mode change starts a nearest-entrance choice from a new origin.
                        _travel.Reset();
                        _nextSelection = DateTime.MinValue;
                        Say($"Travel mode: {mode}. Flying requires an active flying vehicle and continues entrance approach/entry in that vehicle; ground requires ground movement.");
                    }
                    break;
                case "missions":
                    if (!Game.IsZoning) _missions.Refresh(true);
                    foreach (AcceptedMission record in _missions.Records.OrderBy(x => x.Id.Instance))
                        Say($"{record.Id.Instance}: {record.Name}; playfield={record.PlayfieldId}, entrance={record.Entrance}; " +
                            $"type={record.Kind}, objective={record.Objectives}; state={record.State}, accepted={record.Present}, RK destination={record.IsRubiKaDestination}, " +
                            $"rooms cleared={record.RoomsCleared}, return hand-in pending={record.ReturnHandInPending}, evidence={record.CompletionEvidence ?? "none"}.");
                    if (!_missions.Records.Any()) Say("No accepted Rubi-Ka mission destinations detected.");
                    break;
                case "complete": ConfirmCompletion(args); break;
                case "nav":
                    if (args.Length == 1)
                    {
                        _navWindow.Show();
                        break;
                    }
                    if (args.Length > 1 && args[1].Equals("capture", StringComparison.OrdinalIgnoreCase))
                    { _navRoutes.CaptureCurrentRoom(); break; }
                    if (args.Length > 1 && args[1].Equals("record", StringComparison.OrdinalIgnoreCase))
                    {
                        string navMode = args.Length > 2 && (args[2].Equals("foot", StringComparison.OrdinalIgnoreCase) ||
                            args[2].Equals("vehicle", StringComparison.OrdinalIgnoreCase) ||
                            args[2].Equals("auto", StringComparison.OrdinalIgnoreCase)) ? args[2] : "Auto";
                        int nameIndex = navMode == "Auto" && (args.Length <= 2 || !args[2].Equals("auto", StringComparison.OrdinalIgnoreCase)) ? 2 : 3;
                        string name = args.Length > nameIndex ? string.Join("-", args.Skip(nameIndex)) : null;
                        _navRoutes.Start(name, navMode, "Bidirectional"); break;
                    }
                    if (args.Length > 1 && args[1].Equals("stop", StringComparison.OrdinalIgnoreCase))
                    { _navRoutes.Stop(); break; }
                    if (args.Length > 1 && args[1].Equals("list", StringComparison.OrdinalIgnoreCase))
                    { _navRoutes.List(); break; }
                    if (args.Length > 1 && args[1].Equals("window", StringComparison.OrdinalIgnoreCase))
                    { _navWindow.Show(); break; }
                    Say("Usage: /rkm nav [window] | capture | record [foot|vehicle|auto] [name] | stop | list. Existing files remain Foot/Bidirectional; vehicle profiles preserve recorded altitude and support forward reacquisition.");
                    break;
                case "fgrid":
                    if (args.Length > 1 && args[1].Equals("nav", StringComparison.OrdinalIgnoreCase))
                    {
                        if (args.Length == 2) Say(_fgrid.NavMeshStatus());
                        else if (args.Length == 5 &&
                            float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
                            float.TryParse(args[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) &&
                            float.TryParse(args[4], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
                            Say(_fgrid.NavMeshStatus(new Vector3(x, y, z)) + " Probe only; no movement requested.");
                        else Say("Usage: /rkm fgrid nav [X Y Z] (optional current-position path probe; no movement).");
                        break;
                    }
                    if (args.Length > 1 && args[1].Equals("scan", StringComparison.OrdinalIgnoreCase))
                    {
                        Say(_fgrid.SurveySummary);
                        Say(_fgrid.SurveyFloorCounts);
                        Say("Survey file: " + _fgrid.SurveyFilePath);
                        break;
                    }
                    if (args.Length > 1)
                    {
                        Say("Usage: /rkm fgrid [scan|nav]");
                        break;
                    }
                    Say($"FGrid service configured={_fgrid.IsConfigured}, mapped destinations={_fgrid.MappedDestinations}, " +
                        $"active={_fgrid.IsActive}, last issue={_fgrid.LastFailure ?? "none"}.");
                    Say($"Surveyed FGrid destinations={_fgrid.SurveyedDestinationCount}; " +
                        _fgrid.TargetStatus(_selected?.PlayfieldId ?? 0) + ".");
                    Say(_fgrid.SurveySummary + " Use /rkm fgrid scan for floor counts, or /rkm fgrid nav for mesh status.");
                    break;
                case "loot": _loot.ShowSettingsTab(); break;
                case "logistics":
                    if (args.Length == 2 && args[1].Equals("routes", StringComparison.OrdinalIgnoreCase))
                        _logisticsRoutes.Report(Say);
                    else if ((args.Length == 2 || args.Length == 3) &&
                        args[1].Equals("cycle", StringComparison.OrdinalIgnoreCase))
                    {
                        if (_running || _autoRolling)
                            Say("Use /rkm stop to disarm the mission cycle before validating automatic logistics.");
                        else if (args.Length == 3 && (!int.TryParse(args[2], out int cycleCount) ||
                            cycleCount < 1 || cycleCount > 20))
                            Say("Usage: /rkm logistics cycle [1-20] (default: 20).");
                        else _automaticLogistics.Start(args.Length == 3 ? int.Parse(args[2]) : 20);
                    }
                    else if (args.Length == 4 && args[1].Equals("travel", StringComparison.OrdinalIgnoreCase))
                    {
                        if (_bankTransactions.IsActive || _shopSale.IsActive || _automaticLogistics.IsActive)
                            Say("Finish or stop the logistics transaction before starting a new route.");
                        else if (_running || _autoRolling)
                            Say("Use /rkm stop to disarm the mission cycle before testing a logistics route.");
                        else _logisticsNavigator.Start(args[2], args[3]);
                    }
                    else if (args.Length == 2 && args[1].Equals("return", StringComparison.OrdinalIgnoreCase))
                    {
                        if (_bankTransactions.IsActive || _shopSale.IsActive || _automaticLogistics.IsActive)
                            Say("Wait for the logistics transaction to finish or stop it before returning.");
                        else _logisticsNavigator.Return();
                    }
                    else if (args.Length == 3 && args[1].Equals("bank", StringComparison.OrdinalIgnoreCase) &&
                        args[2].Equals("test", StringComparison.OrdinalIgnoreCase))
                    {
                        if (_running || _autoRolling)
                            Say("Use /rkm stop to disarm the mission cycle before testing a bank transfer.");
                        else if (_shopSale.IsActive || _automaticLogistics.IsActive)
                            Say("Finish or stop the shop sale test before starting a bank transaction.");
                        else _bankTransactions.StartRoundTrip();
                    }
                    else if ((args.Length == 3 || args.Length == 4) &&
                        args[1].Equals("bank", StringComparison.OrdinalIgnoreCase) &&
                        args[2].Equals("store", StringComparison.OrdinalIgnoreCase))
                    {
                        if (_running || _autoRolling)
                            Say("Use /rkm stop to disarm the mission cycle before storing bank items.");
                        else if (args.Length == 4 && (!int.TryParse(args[3], out int storeCount) || storeCount < 1 || storeCount > 20))
                            Say("Usage: /rkm logistics bank store [1-20] (default: 1).");
                        else if (_shopSale.IsActive || _automaticLogistics.IsActive)
                            Say("Finish or stop the shop sale test before starting a bank transaction.");
                        else _bankTransactions.StartStore(args.Length == 4 ? int.Parse(args[3]) : 1);
                    }
                    else if (args.Length == 3 && args[1].Equals("bank", StringComparison.OrdinalIgnoreCase) &&
                        args[2].Equals("stop", StringComparison.OrdinalIgnoreCase))
                        _bankTransactions.Stop();
                    else if (args.Length == 3 && args[1].Equals("shop", StringComparison.OrdinalIgnoreCase) &&
                        args[2].Equals("preview", StringComparison.OrdinalIgnoreCase))
                        _shopSale.Preview();
                    else if (args.Length == 4 && args[1].Equals("shop", StringComparison.OrdinalIgnoreCase) &&
                        args[2].Equals("stage", StringComparison.OrdinalIgnoreCase))
                    {
                        if (_running || _autoRolling)
                            Say("Use /rkm stop to disarm the mission cycle before staging shop items.");
                        else if (!int.TryParse(args[3], out int stageId) || stageId <= 0)
                            Say("Usage: /rkm logistics shop stage <item id> (select an expendable main Reject from preview).");
                        else if (_bankTransactions.IsActive || _automaticLogistics.IsActive)
                            Say("Finish or stop the bank transaction before staging shop items.");
                        else _shopSale.StartStage(stageId);
                    }
                    else if (args.Length == 3 && args[1].Equals("shop", StringComparison.OrdinalIgnoreCase) &&
                        args[2].Equals("test", StringComparison.OrdinalIgnoreCase))
                    {
                        if (_running || _autoRolling)
                            Say("Use /rkm stop to disarm the mission cycle before testing a shop sale.");
                        else if (_bankTransactions.IsActive || _automaticLogistics.IsActive)
                            Say("Finish or stop the bank transaction before testing a shop sale.");
                        else _shopSale.Start();
                    }
                    else if ((args.Length == 3 || args.Length == 4) &&
                        args[1].Equals("shop", StringComparison.OrdinalIgnoreCase) &&
                        args[2].Equals("sell", StringComparison.OrdinalIgnoreCase))
                    {
                        if (_running || _autoRolling)
                            Say("Use /rkm stop to disarm the mission cycle before selling shop items.");
                        else if (args.Length == 4 && (!int.TryParse(args[3], out int saleCount) ||
                            saleCount < 1 || saleCount > 20))
                            Say("Usage: /rkm logistics shop sell [1-20] (default: 1).");
                        else if (_bankTransactions.IsActive || _automaticLogistics.IsActive)
                            Say("Finish or stop the bank transaction before selling shop items.");
                        else _shopSale.Start(args.Length == 4 ? int.Parse(args[3]) : 1);
                    }
                    else if (args.Length == 3 && args[1].Equals("shop", StringComparison.OrdinalIgnoreCase) &&
                        args[2].Equals("stop", StringComparison.OrdinalIgnoreCase))
                        _shopSale.Stop();
                    else if (args.Length == 2 && args[1].Equals("stop", StringComparison.OrdinalIgnoreCase))
                    { _automaticLogistics.Stop(); _bankTransactions.Stop(); _shopSale.Stop(); _logisticsNavigator.Stop(); }
                    else if (args.Length == 2 && args[1].Equals("status", StringComparison.OrdinalIgnoreCase))
                        Say("Logistics route test " + _logisticsNavigator.Status + "; bank " +
                            _bankTransactions.Status + "; shop " + _shopSale.Status +
                            "; automatic cycle " + _automaticLogistics.Status +
                            "; probe " + _logisticsProbe.Status + ".");
                    else if (args.Length >= 3 && args[1].Equals("probe", StringComparison.OrdinalIgnoreCase))
                    {
                        if (args[2].Equals("start", StringComparison.OrdinalIgnoreCase))
                        {
                            if (args.Length == 3) _logisticsProbe.Start();
                            else if (args.Length == 4 &&
                                (args[3].Equals("bank", StringComparison.OrdinalIgnoreCase) ||
                                 args[3].Equals("shop", StringComparison.OrdinalIgnoreCase)))
                                _logisticsProbe.Start(null, args[3]);
                            else if (args.Length == 4) _logisticsProbe.Start(args[3]);
                            else if (args.Length == 5 &&
                                (args[4].Equals("bank", StringComparison.OrdinalIgnoreCase) ||
                                 args[4].Equals("shop", StringComparison.OrdinalIgnoreCase)))
                                _logisticsProbe.Start(args[3], args[4]);
                            else Say("Usage: /rkm logistics probe start [site] [bank|shop].");
                        }
                        else if (args.Length == 3 && args[2].Equals("stop", StringComparison.OrdinalIgnoreCase))
                            _logisticsProbe.Stop();
                        else if (args.Length == 3 && args[2].Equals("status", StringComparison.OrdinalIgnoreCase))
                            Say("Logistics probe " + _logisticsProbe.Status + ".");
                        else Say("Usage: /rkm logistics probe start [site] [bank|shop] | stop | status.");
                    }
                    else Say("Usage: /rkm logistics routes | cycle [1-20] | travel <site> <bank|shop> | bank <test|store [1-20]|stop> | shop <preview|stage <item id>|test|sell [1-20]|stop> | return | stop | status | probe start [site] [bank|shop] | probe stop.");
                    break;
                case "map": _map.ToggleWindow(); break;
                case "neko":
                    if (args.Length > 1 && args[1].Equals("toggle", StringComparison.OrdinalIgnoreCase))
                    { _entranceResolver.ToggleEnabled(); Say($"Neko key use {(_entranceResolver.Enabled ? "enabled" : "disabled")}."); }
                    else if (args.Length > 1 && args[1].Equals("warp", StringComparison.OrdinalIgnoreCase)) ManualNekoWarp();
                    else
                    {
                        if (MaliMissionRoller2.Main.Window?.Window?.IsValid != true) _roller.ShowRoller();
                        MaliMissionRoller2.Main.Window?.ShowNekoTab();
                    }
                    break;
                case "settings":
                    if (MaliMissionRoller2.Main.Window?.Window?.IsValid != true)
                        _roller.ShowRoller();
                    MaliMissionRoller2.Main.Window?.ShowSettingsTab();
                    break;
                default: Say("Commands: start, auto, local, stop, status, missions, zone <id|all>, rolls <count>, limit <count|off>, travel auto|ground|flying, neko [warp|toggle], fgrid [scan|nav], nav [window]|record [name]|stop|list, logistics routes|cycle [1-20]|travel <site> <bank|shop>|return|stop|status|probe, complete [mission id], loot, map, settings."); break;
            }
        }

        private void ManualNekoWarp()
        {
            if (!_entranceResolver.Enabled) { Say("Neko key use is disabled."); return; }
            if (Game.IsZoning || Playfield.IsDungeon || _selected == null ||
                _selected.PlayfieldId != Playfield.ModelIdentity.Instance)
            { Say("Key Warp requires a selected mission after its destination playfield is verified."); return; }
            Mission live = Mission.List?.FirstOrDefault(x => x.Identity == _selected.Id);
            if (live == null) { Say("The selected mission is not currently available from AO#."); return; }
            _travel.Reset("manual Neko key-warp handoff");
            _movement.StopAll();
            _entranceResolver.Select(live);
            EntranceResult result = _entranceResolver.Tick();
            Say($"Manual Neko key-warp engine: {result}; {_entranceResolver.Status}, candidates={_entranceResolver.CandidateCount}.");
        }

        private void SelectNekoMission(int missionId)
        {
            _missions.Refresh(true);
            AcceptedMission selected = _missions.Records.FirstOrDefault(x => x.Id.Instance == missionId &&
                x.Present && x.IsRubiKaDestination && !x.Completed);
            if (selected == null) { Say($"Neko mission {missionId} is no longer an eligible accepted Rubi-Ka mission."); return; }
            _selected = selected;
            _travel.Reset("Neko UI mission selection changed");
            _entranceResolver.Reset();
            bool mapPublished = PublishMissionDestination(selected);
            if (selected.PlayfieldId == Playfield.ModelIdentity.Instance)
            {
                Mission live = Mission.List?.FirstOrDefault(x => x.Identity == selected.Id);
                if (live != null) _entranceResolver.Select(live);
            }
            Say($"Neko selected mission {selected.Id.Instance}: {selected.Name}; destination PF {selected.PlayfieldId}, " +
                $"location=({LocalRoutePlanner.Coordinates(selected.Entrance)}), map/minimap upload={(mapPublished ? "sent" : "deferred")}. " +
                "Use Key Warp to run its bounded key candidates.");
        }

        private void Start()
        {
            if (_running) return;
            _automaticLogistics?.Stop(true);
            _bankTransactions?.Stop();
            _shopSale?.Stop();
            _logisticsNavigator?.Stop(true);
            _logisticsNavigator?.ResetOriginConnector();
            _running = true;
            _recoveringDeath = false;
            _deathReturnToRollTerminal = false;
            _missionBatchRemountPending = _autoCycle && _autoAcceptedCount > 0;
            _dungeonStarted = false;
            _clearanceReported = false;
            _selected = null;
            _mapMission = _handoffDoor = Identity.None;
            _verifiedRun = false;
            _exitZoningStarted = false;
            _verifiedExitReported = false;
            _handInExitVerified = false;
            _handInLocateStarted = DateTime.MinValue;
            _handIn.Reset();
            _completedMissionWarpTarget = 0;
            _completedMissionWarpAttempted = false;
            _lifecycleSignature = null;
            _lastLifecycleProgress = DateTime.UtcNow;
            _watchdogRecoveryAt = DateTime.MinValue;
            _travelInvalidated = false;
            _handoffWaitStarted = DateTime.MinValue;
            _observedDungeon = Identity.None;
            _associationAttempts = 0;
            _nextAssociationDiagnostic = DateTime.MinValue;
            _waitingReason = null;
            _autoLogisticsPending = _autoLogisticsRunning = _autoLogisticsFinalPending = false;
            _autoLogisticsFinalRun = _autoFinalLogisticsComplete = false;
            _autoLogisticsPasses = 0;
            _autoLogisticsReason = null;
            _capacityLogisticsBlocked = _capacityLogisticsDeferredWhileAccepted = false;
            _nextSelection = DateTime.MinValue;
            _travel.Reset();
            _vehicle?.ResetJourney();
            _longTravel.Reset();
            _entranceResolver.Reset();
            Say(_autoCycle ? "Armed for automatic rolling, travel and mission chaining." :
                "Armed for local takeover. Rolling and inter-playfield travel remain under your control.");
        }

        private void Stop(bool preserveCheckpoint = false)
        {
            if (preserveCheckpoint && _running && _checkpoint != null)
            { SaveCheckpoint(); _checkpoint.Save(true, Say); }
            if (_autoRolling) MaliMissionRoller2.Main.Window?.StopZoneRolling();
            _autoRolling = false;
            _rollingTerminalIdentity = Identity.None;
            _rollingTerminalReference = null;
            _clearAcceptedBeforeRolling = false;
            _running = false;
            _dungeonStarted = false;
            _travel?.Reset();
            _vehicle?.RestoreOwnedEquipmentOnStop();
            _vehicle?.ResetJourney();
            _longTravel?.Reset();
            _entranceResolver?.Reset();
            _navRoutes?.StopPlayback();
            _automaticLogistics?.Stop(true);
            _bankTransactions?.Stop();
            _shopSale?.Stop();
            _logisticsNavigator?.Stop(true);
            _logisticsNavigator?.ResetOriginConnector();
            _dungeon?.Stop();
            _movement?.StopAll();
            _autoCycle = false;
            _autoLogisticsPending = _autoLogisticsRunning = _autoLogisticsFinalPending = false;
            _autoLogisticsFinalRun = _autoFinalLogisticsComplete = false;
            _autoLogisticsPasses = 0;
            _autoLogisticsReason = null;
            _capacityLogisticsBlocked = _capacityLogisticsDeferredWhileAccepted = false;
            _recoveringDeath = false;
            _deathReturnToRollTerminal = false;
            _missionBatchRemountPending = false;
            _exitZoningStarted = false;
            _handInExitVerified = false;
            _handInLocateStarted = DateTime.MinValue;
            _handIn.Reset();
            _completedMissionWarpTarget = 0;
            _completedMissionWarpAttempted = false;
            _deathRecovery?.Stop();
            if (!preserveCheckpoint && _checkpoint != null)
            { _checkpoint.Armed = false; _checkpoint.Phase = "Idle"; _checkpoint.Save(true, Say); }
            // The embedded roller is independent: stopping travel must not stop user-owned rolling.
        }

        private void ZoningStarted(object sender, EventArgs args)
        {
            _dungeon?.CancelMovementRecovery();
            if (_dungeonStarted && _dungeon.IsExiting && _verifiedRun &&
                Playfield.ModelIdentity == _activeDungeon)
                _exitZoningStarted = true;
            _handoffDoor = _travel.ActiveDoor;
            _mapMission = Identity.None; // Re-upload on the next outdoor selection after zoning.
            _travel.SuspendForZoning(); // Keep managed attempt until exact dungeon verification.
            _longTravel.InvalidatePath();
            _observedDungeon = Identity.None;
            _handoffWaitStarted = DateTime.MinValue;
            _associationAttempts = 0;
            _nextAssociationDiagnostic = DateTime.MinValue;
            _travelInvalidated = true;
        }

        private void ZoningEnded(object sender, EventArgs args)
        {
            CarriedItems.ResetBackpackSnapshots();
            _nextTick = DateTime.UtcNow.AddSeconds(2); // Let quest/door state settle after a zone.
        }

        private void ConfirmCompletion(string[] args)
        {
            if (_selected == null || !_verifiedRun)
            {
                Say("No verified mission run to confirm. Enter the mission through local travel or /rkm start inside its dungeon first.");
                return;
            }
            if (args.Length > 1 && (!int.TryParse(args[1], out int id) || id != _selected.Id.Instance))
            {
                Say($"The bound mission is {_selected.Id.Instance}. Use /rkm complete after checking its objective/reward in game.");
                return;
            }
            _selected.State = MissionProgress.CompletedByUser;
            _selected.ReturnHandInPending = false;
            _loot.ReleaseMissionReservations(_selected.Id);
            _checkpoint.OriginTerminals.RemoveAll(x => x.QuestInstance == _selected.Id.Instance);
            _missions.RemoveObjectiveMetadata(_selected);
            _checkpoint.Save(true, Say);
            _handIn.Reset();
            _selected.CompletionEvidence = "User confirmed the objective/reward with /rkm complete";
            _travel.Reset();
            _waitingReason = null;
            if (_running && Playfield.IsDungeon)
            {
                if (!_dungeon.IsRunning) _dungeon.Start(_selected);
                _dungeonStarted = _dungeon.IsRunning;
                _dungeon.BeginExit();
            }
            Say($"Completion recorded for {_selected.Id.Instance}: {_selected.Name}. " +
                (_autoCycle ? "While armed, exit is automatic and the automatic cycle continues." :
                    "While armed, exit is automatic and the next closest accepted mission in this playfield will take over."));
        }

        private AcceptedMission ExactCurrentDungeonMission()
        {
            if (!Playfield.IsDungeon || Mission.List == null ||
                !Mission.FindMissionForCurrentDungeon(out Mission bound)) return null;
            AcceptedMission record = _missions.Find(bound.Identity);
            if (record == null || !record.Present || !record.IsRubiKaDestination || record.Completed)
                return null;
            Mission expected = Mission.List.FirstOrDefault(x => x.Identity == record.Id);
            return expected != null && MissionEntranceResolver.VerifyCurrentDungeon(expected, out Mission exact) &&
                exact.Identity == record.Id ? record : null;
        }

        private void Update(object sender, float elapsed)
        {
            if (Game.IsZoning || DynelManager.LocalPlayer == null || DateTime.UtcNow < _nextTick) return;
            _nextTick = DateTime.UtcNow.AddMilliseconds(_travel.UpdateIntervalMilliseconds);
            try
            {
                CarriedItems.UpdateBackpackSnapshots();
                ManagedBagOpenOwnership.Tick();
                _missions.Refresh(); // Track acceptance/removal even while local automation is disarmed.
                // AO can replace the offer anchor with a later authoritative
                // mission entrance after the initial quest details appeared.
                // Reconcile only an existing exact quest/character binding;
                // terminal identity, terminal PF and terminal position remain
                // immutable acceptance-time provenance.
                foreach (AcceptedMission accepted in _missions.Records.Where(x =>
                    x.Present && x.IsRubiKaDestination))
                    ReconcileAcceptedMissionOrigin(accepted);
                foreach (int id in _awaitingQuestDetails.ToList())
                {
                    AcceptedMission resolved = _missions.Records.FirstOrDefault(x =>
                        x.Id.Instance == id && x.Present && x.IsRubiKaDestination);
                    if (resolved == null) continue;
                    ReconcileAcceptedMissionOrigin(resolved);
                    _awaitingQuestDetails.Remove(id);
                }
                if (!_running) return;
                if (_autoLogisticsRunning || _automaticLogistics.IsActive)
                {
                    if (!DynelManager.LocalPlayer.IsAlive)
                    {
                        string phase = _automaticLogistics.Status;
                        Stop();
                        Say("Automatic mission cycle stopped because the character died during logistics. " +
                            $"Inspect the last route or transaction before restarting; phase={phase}.");
                        return;
                    }
                    if (_automaticLogistics.IsActive) return;
                    FinishScheduledAutomaticLogistics();
                    return;
                }
                // Give a just-zoned flying character a short diagonal altitude
                // escape before mission selection, terminal return or other travel.
                if (_postZoneSafety.Tick()) return;
                if (!DynelManager.LocalPlayer.IsAlive)
                {
                    if (!_autoCycle) { Stop(); Say("Stopped because the character died."); return; }
                    if (!_recoveringDeath)
                    {
                        _recoveringDeath = true;
                        _dungeon.Stop(); _dungeonStarted = false;
                        _travel.Reset(); _longTravel.Reset(); _movement.StopAll();
                        _completedMissionWarpTarget = 0;
                        _completedMissionWarpAttempted = false;
                        _deathRecovery.Begin();
                        Say("Character died; waiting for reclaim, then replanning the accepted mission.");
                    }
                    return;
                }
                if (_recoveringDeath)
                {
                    DeathRecoveryResult recovery = _deathRecovery.Tick();
                    if (recovery == DeathRecoveryResult.Waiting) return;
                    if (recovery == DeathRecoveryResult.Failed)
                    { Stop(); Say("Reclaim/readiness recovery timed out or could not complete."); return; }
                    _recoveringDeath = false;
                    _deathReturnToRollTerminal = true;
                    _verifiedRun = false;
                    _exitZoningStarted = false;
                    _completedMissionWarpTarget = 0;
                    _completedMissionWarpAttempted = false;
                    _selected = _selected != null && _selected.Present ? _selected : null;
                    _entranceResolver.Reset();
                    _nextSelection = DateTime.MinValue;
                    Say($"Death recovery readiness complete; returning by Scotty/travel graph to the exact saved roller terminal in PF {_rollTerminalPlayfield} before mission retry.");
                }
                if (_deathReturnToRollTerminal)
                {
                    if (!_hasRollTerminal)
                    { Stop(); Say("Death recovery stopped: no exact saved roller terminal exists for this automatic cycle."); return; }
                    if (!ReturnToRollTerminal()) return;
                    _deathReturnToRollTerminal = false;
                    _nextSelection = DateTime.MinValue;
                    Say($"Death recovery reacquired exact saved roller terminal in PF {_rollTerminalPlayfield}; resuming the normal automatic mission selection and travel path.");
                }
                if (_autoCycle && _autoRolling && !ObserveAutoAcceptance()) return;
                // AO# classifies Fixer Grid as a dungeon, but it is a travel
                // playfield. Actual mission dungeons still need exact binding.
                bool inFixerGrid = Playfield.ModelIdentity.Instance == (int)PlayfieldId.FixerGrid;
                if (Playfield.IsDungeon && !inFixerGrid) { TickDungeon(); return; }
                if (_travelInvalidated)
                {
                    _travelInvalidated = false;
                    _travel.CompleteHandoff(false, "zoning ended outdoors; no selected mission dungeon entered");
                    _travel.Reset("outdoor zoning invalidated local route");
                    if (!_verifiedRun && (!_autoCycle || _selected == null || !_selected.Present))
                        _selected = null;
                    _nextSelection = DateTime.MinValue;
                }
                if (_dungeonStarted)
                {
                    _dungeon.Stop();
                    _dungeonStarted = false;
                }
                _observedDungeon = Identity.None;
                _handoffWaitStarted = DateTime.MinValue;
                if (_selected != null && _verifiedRun)
                {
                    bool warpExitVerified = false;
                    if (_completedMissionWarpTarget != 0)
                    {
                        if (_exitZoningStarted && Playfield.ModelIdentity.Instance == _selected.PlayfieldId)
                        {
                            // A manual mission exit while queued is still a
                            // verified ordinary exit, not a Scotty warp.
                            _warp.Reset();
                            _completedMissionWarpTarget = 0;
                        }
                        else
                        {
                            WarpResult warpResult = _warp.Tick(_completedMissionWarpTarget);
                            if (warpResult == WarpResult.InProgress) return;
                            warpExitVerified = warpResult == WarpResult.Succeeded &&
                                _warp.VerifiedDestination(_completedMissionWarpTarget);
                            if (warpResult == WarpResult.Failed) _completedMissionWarpTarget = 0;
                        }
                    }
                    if (!warpExitVerified && !_handInExitVerified && (!_exitZoningStarted ||
                        Playfield.ModelIdentity.Instance != _selected.PlayfieldId))
                    {
                        Wait($"Mission exit proof is incomplete: " +
                            $"exit zoning={_exitZoningStarted}, outdoor playfield={Playfield.ModelIdentity.Instance}, " +
                            $"expected={_selected.PlayfieldId}. Chaining is held.");
                        return;
                    }
                    if (!_handInExitVerified && !_verifiedExitReported)
                    {
                        if (warpExitVerified && _hasRollTerminal &&
                            Playfield.ModelIdentity.Instance == _rollTerminalPlayfield &&
                            PostZoneFlightSafety.IsCityReturnPlayfield(_rollTerminalPlayfield))
                        {
                            _returnCityRouteEligible = true;
                            _returnScottyLanding = DynelManager.LocalPlayer.Position;
                            Say("Verified completed-mission city warp: recorded terminal-return route enabled from the observed landing.");
                        }
                        Say(warpExitVerified
                            ? $"Verified Scottyboi warp from completed mission {_selected.Id.Instance} to playfield {_completedMissionWarpTarget}."
                            : $"Verified exit from mission {_selected.Id.Instance} to its outdoor playfield {_selected.PlayfieldId}.");
                        _verifiedExitReported = true;
                    }
                    if (_selected.ReturnHandInPending)
                    {
                        _checkpoint.HandInPending = true;
                        _checkpoint.RewardSettlementPending = true;
                        _checkpoint.LastVerifiedPhase = "DungeonExitVerified";
                        _handInExitVerified = true;
                        if (!TickReturnHandIn()) return;
                    }
                    if (!_selected.Completed)
                    {
                        Wait("The previous mission has no confirmed reward. Check it and use /rkm complete; /rkm stop then start abandons this run binding.");
                        return;
                    }
                    if (_checkpoint?.Execution != null) _checkpoint.Execution.Phase = "InventoryClassification";
                    _checkpoint.RewardSettlementPending = true;
                    _checkpoint.InventorySettlementPending = true;
                    InventorySettlement settlement = _inventory.TickAfterVerifiedExit(_loot, Say);
                    if (settlement == InventorySettlement.Moving) return;
                    if (settlement == InventorySettlement.Blocked)
                    {
                        string reason = _inventory.SettlementFailure;
                        Stop(); Say("Post-mission item staging stopped: " + reason); return;
                    }
                    bool capacityReady = settlement == InventorySettlement.Ready;
                    _checkpoint.HandInPending = false;
                    _checkpoint.RewardSettlementPending = false;
                    _checkpoint.InventorySettlementPending = false;
                    _checkpoint.LastVerifiedPhase = "RewardAndInventorySettled";
                    Say($"Lifecycle: mission complete -> reward verified -> post-mission Keep/Junk settlement verified; free main slots={Inventory.NumFreeSlots}.");
                    bool logisticsQueued = false;
                    if (_autoCycle && _inventory.NeedsAutomaticLogistics(out string logisticsReason))
                    {
                        QueueAutomaticLogistics(logisticsReason);
                        logisticsQueued = true;
                    }
                    if (_checkpoint?.Execution != null)
                        _checkpoint.Execution.Phase = capacityReady && !logisticsQueued ?
                            "ExitVerified" : "LogisticsRequired";
                    int previousPlayfield = _selected.PlayfieldId;
                    _selected = null;
                    _verifiedRun = false;
                    _exitZoningStarted = false;
                    _verifiedExitReported = false;
                    _completedMissionWarpTarget = 0;
                    _completedMissionWarpAttempted = false;
                    _handInExitVerified = false;
                    _handIn.Reset();
                    _travel.Reset();
                    _nextSelection = DateTime.MinValue;
                    if (Playfield.ModelIdentity.Instance != previousPlayfield ||
                        !_missions.Eligible(previousPlayfield).Any())
                    {
                        if (!_autoCycle)
                        { Stop(); Say("Local mission chain finished. Travel to another mission playfield yourself, then /rkm start."); return; }
                    }
                }
                if (_selected != null && (!_selected.Present ||
                    (!_autoCycle && _selected.PlayfieldId != Playfield.ModelIdentity.Instance) ||
                    (!_autoCycle && _selected.PlayfieldId == Playfield.ModelIdentity.Instance && !_travel.MatchesAnchor(_selected))))
                {
                    Say($"Mission destination invalidated: mission={_selected.Id.Instance}; acceptance/playfield/anchor changed; choose again from current origin.");
                    _selected = null;
                    _travel.Reset();
                    _nextSelection = DateTime.MinValue;
                }
                if (_selected == null && inFixerGrid && _checkpoint != null &&
                    _checkpoint.MissionInstance != 0)
                {
                    _selected = _missions.Records.FirstOrDefault(x => x.Present &&
                        x.IsRubiKaDestination && !x.Completed &&
                        x.Id.Instance == _checkpoint.MissionInstance &&
                        x.PlayfieldId == _checkpoint.DestinationPlayfield);
                    if (_selected != null)
                        Say($"Resuming accepted mission {_selected.Id.Instance} toward playfield {_selected.PlayfieldId} from Fixer Grid.");
                }
                if (_selected == null)
                {
                    if (_autoCycle && TickScheduledAutomaticLogistics()) return;
                    if (DateTime.UtcNow < _nextSelection) return;
                    _nextSelection = DateTime.UtcNow.AddSeconds(5);
                    // After a stop/restart, finish every already accepted mission
                    // before refilling the batch. An unavailable quest list is
                    // not proof that the accepted work has disappeared.
                    if (_autoCycle && Mission.List == null)
                    { Wait("Waiting for AO# to load the accepted mission list before deciding whether to roll."); return; }
                    var accepted = RunnableAcceptedMissions().ToList();
                    bool rollingSuspendedForCapacity = _autoCycle &&
                        Inventory.NumFreeSlots < _inventory.MinimumFreeSlots;
                    if (rollingSuspendedForCapacity && accepted.Count == 0)
                    {
                        Wait($"Inventory needs {_inventory.MinimumFreeSlots} free main slots before rolling; " +
                            "no accepted runnable Rubi-Ka missions remain.");
                        return;
                    }
                    if (rollingSuspendedForCapacity)
                        Say($"Inventory below rolling threshold; continuing {accepted.Count} accepted mission(s), new rolling suspended.");
                    if (_clearAcceptedBeforeRolling && !_missions.Records.Any(x =>
                        x.Present && x.IsRubiKaDestination && !x.Completed) &&
                        _awaitingQuestDetails.Count == 0)
                    {
                        _clearAcceptedBeforeRolling = false;
                        Say("All previously accepted Rubi-Ka missions are cleared; the roller may refill the remaining limit.");
                    }
                    // Fresh cycles still fill the requested batch before travel.
                    // A resumed cycle selects accepted work before this branch.
                    if (_autoCycle && !_clearAcceptedBeforeRolling && !inFixerGrid &&
                        _awaitingQuestDetails.Count == 0 && Inventory.NumFreeSlots >= 2 &&
                        !rollingSuspendedForCapacity &&
                        (_maxAutoMissions == 0 || _autoAcceptedCount < _maxAutoMissions))
                    {
                        if (!_autoRolling && ReturnToRollTerminal()) StartAutoRolling();
                        return;
                    }
                    var local = _missions.Eligible(Playfield.ModelIdentity.Instance)
                        .Where(CanSafelyStartAcceptedMission).ToList();
                    if (local.Count == 0)
                    {
                        if (!_autoCycle)
                        { Wait("Waiting for you to reach a playfield containing an accepted Rubi-Ka mission."); return; }
                        _selected = accepted.Where(CanSafelyStartAcceptedMission)
                            .OrderBy(x => x.PlayfieldId).ThenBy(x => x.Id.Instance).FirstOrDefault();
                        if (_selected == null)
                        {
                            if (accepted.Count > 0 && Inventory.NumFreeSlots <= 1 &&
                                accepted.All(RequiresObjectiveFreeSlot))
                            {
                                Wait($"{accepted.Count} accepted item-objective mission(s) remain, but one free main-inventory slot is required; new rolling remains suspended.");
                                return;
                            }
                            if (_autoRolling) return;
                            if (_awaitingQuestDetails.Count > 0)
                            {
                                Wait($"Waiting for AO# to resolve {_awaitingQuestDetails.Count} accepted mission destination(s) before returning to the roller terminal.");
                                return;
                            }
                            if (_maxAutoMissions > 0 && _autoAcceptedCount >= _maxAutoMissions)
                            {
                                if (!_autoFinalLogisticsComplete)
                                {
                                    QueueAutomaticLogistics("the finite mission batch is complete; running final bank/shop recycling", true);
                                    _nextSelection = DateTime.MinValue;
                                    return;
                                }
                                if (!ReadyToRoll(out string finalInvariant))
                                {
                                    RecordUnresolvedInvariant(finalInvariant);
                                    return;
                                }
                                int completed = _autoAcceptedCount;
                                _checkpoint.LastVerifiedPhase = "READY_TO_ROLL";
                                _checkpoint.StopInvariant = null;
                                _checkpoint.Save(true, Say);
                                Stop();
                                Say($"Lifecycle: terminal return verified -> logistics verified -> READY_TO_ROLL. " +
                                    $"Finite automatic cycle finished after {completed} accepted mission(s) and disarmed cleanly.");
                                return;
                            }
                            if (Inventory.NumFreeSlots < 2)
                            { Wait("Accepted missions are cleared; waiting for two free main-inventory slots before rolling."); return; }
                            if (ReturnToRollTerminal()) StartAutoRolling();
                            return;
                        }
                        _waitingReason = null;
                    }
                    else
                    {
                        _selected = _travel.SelectNearest(local);
                        if (_selected == null)
                        {
                            Wait("No local estimate for the active movement state. Selection will retry.");
                            return;
                        }
                        _waitingReason = null;
                    }
                }
                if (_autoRolling)
                { MaliMissionRoller2.Main.Window?.StopZoneRolling(); _autoRolling = false; }
                if (_autoCycle && _missionBatchRemountPending)
                {
                    bool footDeparture = _selected != null &&
                        _longTravel.PrefersFootDeparture(_selected.PlayfieldId, _selected.Entrance);
                    if (footDeparture)
                    {
                        VehicleTransitionResult footReady = _vehicle.RequireFlightAfterMissionBatch(false);
                        if (footReady != VehicleTransitionResult.Ready)
                        { Wait("Mission batch Foot-route departure is held until grounded and detached: " + _vehicle.Status + "."); return; }
                        _missionBatchRemountPending = false;
                        _vehicle.CompleteMissionBatchRemount();
                        Say("Mission batch departure verified on foot: the selected FGrid provider has an endpoint-compatible Foot recording; remount is deferred so the recorded terminal approach remains usable.");
                    }
                    else
                    {
                        VehicleTransitionResult remount = _vehicle.RequireFlightAfterMissionBatch();
                        if (remount == VehicleTransitionResult.Unavailable)
                        {
                            string remountFailure = _vehicle.Status;
                            Stop();
                            Say("Mission batch remount stopped: " + remountFailure + ".");
                            return;
                        }
                        if (remount != VehicleTransitionResult.Ready)
                        { Wait("Mission batch remount required before mission selection/travel: " + _vehicle.Status + "."); return; }
                        _missionBatchRemountPending = false;
                        _vehicle.CompleteMissionBatchRemount();
                        Say("Mission batch remount verified: equipped vehicle attachment and authoritative live Fly state confirmed; mission travel may begin.");
                    }
                }
                if (!PreflightSelectedMission()) return;
                if (_selected.PlayfieldId != Playfield.ModelIdentity.Instance || _longTravel.IsActive)
                {
                    // Static ACG identities have no destination evidence while
                    // the character is in another playfield. Travel first and
                    // rebuild entrance candidates only after exact zone arrival.
                    _entranceResolver.Reset();
                    _travel.Reset();
                    TravelResult longResult = _longTravel.Tick(_selected.PlayfieldId, _selected.Entrance);
                    if (longResult == TravelResult.Blocked)
                    { string reason = _longTravel.LastFailure; Stop(); Say(reason); return; }
                    if (longResult == TravelResult.InProgress) return;
                }
                if (_longTravel.IsActive) _longTravel.Reset();
                if (_autoCycle && !inFixerGrid &&
                    _selected.PlayfieldId == Playfield.ModelIdentity.Instance)
                {
                    Mission live = Mission.List?.FirstOrDefault(x => x.Identity == _selected.Id);
                    if (live == null)
                    { Wait("Waiting for the selected accepted mission to appear in AO# before local key entry."); return; }
                    _entranceResolver.Select(live);
                    EntranceResult entrance = _entranceResolver.Tick();
                    if (entrance == EntranceResult.Waiting)
                    { _travel.Reset(); _movement.Halt(MovementOwner.MissionEntrance); return; }
                    if (entrance == EntranceResult.Failed)
                    { Stop(); Say("Mission-key entrance was accepted, but exact dungeon entry was not verified."); return; }
                }
                if (_autoCycle && !_travel.MatchesAnchor(_selected) &&
                    DynelManager.LocalPlayer != null)
                {
                    float localDistance = LocalRoutePlanner.HorizontalDistance(
                        DynelManager.LocalPlayer.Position, _selected.Entrance);
                    VehicleTransitionResult vehicle = _vehicle.RequireFlight(localDistance >= 120f);
                    if (vehicle == VehicleTransitionResult.Waiting) return;
                    Say($"Local mission route mode choice: distance={localDistance:0.0}m, vehicle={_vehicle.Status}; " +
                        $"selected={(DynelManager.LocalPlayer.MovementState == MovementState.Fly ? "Fly" : "Foot")}.");
                }
                if (_autoCycle && !_travel.MatchesAnchor(_selected) &&
                    _travel.SelectNearest(new[] { _selected }) == null)
                { Wait("Local route estimate is unavailable after cross-playfield travel; retrying."); return; }
                if (!PublishMissionDestination(_selected))
                {
                    _travel.Reset(); _selected = null; _mapMission = Identity.None;
                    _nextSelection = DateTime.UtcNow.AddSeconds(5);
                    return;
                }
                if (!_travel.Tick(_selected, _missions.Eligible(Playfield.ModelIdentity.Instance)))
                {
                    Stop(); Say("Local travel stopped. Resolve the reported entrance/route problem, then /rkm start.");
                }
            }
            catch (Exception ex)
            {
                Stop(); Say("Stopped after an AO# error: " + ex);
            }
            finally { SaveCheckpoint(); }
        }

        private void SaveCheckpoint()
        {
            if (_checkpoint == null) return;
            _checkpoint.Armed = _running;
            _checkpoint.AutoCycle = _autoCycle;
            _checkpoint.AutoMissionLimit = _maxAutoMissions;
            _checkpoint.AutoAcceptedCount = _autoAcceptedCount;
            _checkpoint.HasRollTerminal = _hasRollTerminal;
            _checkpoint.RollTerminalPlayfield = _rollTerminalPlayfield;
            _checkpoint.RollTerminalPosition = _rollTerminalPosition;
            _checkpoint.RollTerminalIdentity = _rollTerminalIdentity;
            _checkpoint.RollLogisticsSite = _rollLogisticsSite;
            _checkpoint.Phase = !_running ? "Idle" : _recoveringDeath ? "Recovery" :
                _autoRolling ? "Rolling" : _selected == null ? "SelectingMission" :
                Playfield.ModelIdentity.Instance == (int)PlayfieldId.FixerGrid ? "Travel" :
                Playfield.IsDungeon ? (_dungeon.IsExiting ? "Exit" : _selected.Completed ? "MissionComplete" : "Dungeon") :
                _selected.PlayfieldId != Playfield.ModelIdentity.Instance ? "Travel" : "MissionEntry";
            _checkpoint.MissionType = _selected == null ? 0 : (int)_selected.Id.Type;
            _checkpoint.MissionInstance = _selected?.Id.Instance ?? 0;
            _checkpoint.DestinationPlayfield = _selected?.PlayfieldId ?? 0;
            _checkpoint.Destination = _selected?.Entrance ?? default(Vector3);
            _checkpoint.EntranceIdentity = _entranceResolver?.ActiveEntranceIdentity ?? 0;
            _checkpoint.DungeonInstance = _verifiedRun ? _activeDungeon.Instance : 0;
            _checkpoint.EntranceRoom = _selected?.DungeonEntryRoom ?? 0;
            _checkpoint.EntrancePosition = _selected?.DungeonEntryPosition;
            _checkpoint.Floor = _dungeon?.Floor ?? 0;
            _checkpoint.TravelProvider = _longTravel?.CurrentProvider ?? "Local";
            _checkpoint.ObjectiveState = _dungeon?.Objective?.Evidence ?? _selected?.CompletionEvidence;
            _checkpoint.PendingHandInExitVerified = _handInExitVerified &&
                _selected?.ReturnHandInPending == true;
            _checkpoint.HandInPending = _selected?.ReturnHandInPending == true;
            _checkpoint.LogisticsPending = _autoLogisticsPending || _autoLogisticsRunning ||
                _automaticLogistics?.IsActive == true;
            _checkpoint.AcceptedMissionIds = _missions.Records.Where(x => x.Present &&
                    x.IsRubiKaDestination && !x.Completed)
                .Select(x => x.Id.Instance).Distinct().OrderBy(x => x).ToList();
            if (_dungeon?.IsRunning == true) _checkpoint.Execution = _dungeon.Snapshot();
            _checkpoint.Save(false, Say);
        }

        private bool PublishMissionDestination(AcceptedMission selected)
        {
            if (_mapMission == selected.Id && _mapPlayfield == selected.PlayfieldId &&
                Vector3.Distance(_mapAnchor, selected.Entrance) <= 0.5f) return true;
            // Reuse the embedded Mali MissionView's API, refreshing the accepted list
            // immediately before upload. Offered missions/generic waypoints are excluded.
            Mission live = Mission.List?.FirstOrDefault(x => x.Identity == selected.Id);
            MissionLocation location = live?.Location;
            if (location == null || location.Playfield.Instance != selected.PlayfieldId ||
                !AcceptedMissions.Finite(location.Pos) || Vector3.Distance(location.Pos, selected.Entrance) > 0.5f)
            {
                Say($"Map/minimap marker update: mission={selected.Id.Instance}, result=deferred; accepted mission/location unavailable or changed.");
                return false;
            }
            live.UploadToMap();
            _mapMission = selected.Id; _mapPlayfield = selected.PlayfieldId; _mapAnchor = selected.Entrance;
            Say($"Map/minimap marker update: mission={selected.Id.Instance}, playfield={selected.PlayfieldId}, " +
                $"mission anchor=({LocalRoutePlanner.Coordinates(selected.Entrance)}), API=Mission.UploadToMap, " +
                "result=selected mission upload command sent; native GUI has no marker acknowledgement.");
            return true;
        }

        private void QueueAutomaticLogistics(string reason, bool finalPass = false,
            bool continuation = false)
        {
            if (_autoLogisticsRunning) return;
            if (!continuation && !_autoLogisticsPending) _autoLogisticsPasses = 0;
            _autoLogisticsPending = true;
            _autoLogisticsFinalPending |= finalPass;
            _autoLogisticsReason = reason;
            if (_checkpoint != null) _checkpoint.LogisticsPending = true;
            _waitingReason = null;
            Say($"Automatic logistics queued: {reason}. Navigating to the saved city's surveyed logistics hub before any bank or shop action.");
        }

        private bool TickScheduledAutomaticLogistics()
        {
            if (!_autoCycle || _autoLogisticsRunning) return false;
            if (_capacityLogisticsBlocked)
            {
                if (Inventory.NumFreeSlots >= _inventory.MinimumFreeSlots)
                {
                    _capacityLogisticsBlocked = false;
                    _capacityLogisticsDeferredWhileAccepted = false;
                }
                else if (_capacityLogisticsDeferredWhileAccepted && !RunnableAcceptedMissions().Any())
                {
                    // Accepted work has now been cleared. Permit one new bounded
                    // logistics cycle before considering any additional rolling.
                    _capacityLogisticsBlocked = false;
                    _capacityLogisticsDeferredWhileAccepted = false;
                }
                else return false;
            }
            if (!_autoLogisticsPending && _inventory.NeedsAutomaticLogistics(out string reason))
                QueueAutomaticLogistics(reason);
            if (!_autoLogisticsPending) return false;
            if (string.IsNullOrEmpty(_rollLogisticsSite))
            {
                _rollLogisticsSite = _logisticsRoutes.FindSharedLogisticsSite(_rollTerminalPlayfield);
                if (!string.IsNullOrEmpty(_rollLogisticsSite) && _checkpoint != null)
                {
                    _checkpoint.RollLogisticsSite = _rollLogisticsSite;
                    _checkpoint.Save(true, Say);
                }
            }
            if (string.IsNullOrEmpty(_rollLogisticsSite))
            {
                Stop();
                Say($"Automatic mission cycle stopped before logistics: PF {_rollTerminalPlayfield} has no unique validated bank+shop logistics hub.");
                return true;
            }
            if (Playfield.ModelIdentity.Instance != _rollTerminalPlayfield || _longTravel.IsActive)
            {
                TravelResult result = _longTravel.Tick(_rollTerminalPlayfield);
                if (result == TravelResult.Blocked)
                { string travelFailure = _longTravel.LastFailure; Stop(); Say("Travel to the shared logistics city stopped: " + travelFailure); }
                return true;
            }
            if (!_logisticsNavigator.TryNavigateToOrigin(_rollLogisticsSite, "bank",
                out bool atHub, out string connectorFailure))
            {
                Stop();
                Say("Automatic mission cycle stopped before logistics: " + connectorFailure + ".");
                return true;
            }
            if (!atHub)
            {
                Wait($"Navigating by AO# outdoor routing to the surveyed {_rollLogisticsSite} logistics hub; " +
                    "the target is a recorded reachable standoff, not a terminal centre.");
                return true;
            }
            string site = string.IsNullOrEmpty(_rollLogisticsSite) ? null :
                _logisticsNavigator.FindVerifiedOriginSite(_rollLogisticsSite);
            if (string.IsNullOrEmpty(site))
            {
                if (_logisticsOriginWaitStarted == DateTime.MinValue)
                    _logisticsOriginWaitStarted = DateTime.UtcNow;
                if (DateTime.UtcNow - _logisticsOriginWaitStarted <= TimeSpan.FromSeconds(30))
                {
                    Wait(string.IsNullOrEmpty(_rollLogisticsSite)
                        ? "Saved roller terminal has no unique surveyed logistics-origin binding yet; waiting for its exact live terminal identity."
                        : $"Waiting for surveyed logistics hub '{_rollLogisticsSite}' to become live-verifiable; " +
                            LogisticsOriginDiagnostic(_rollLogisticsSite) + ".");
                    return true;
                }
                string failure = string.IsNullOrEmpty(_rollLogisticsSite)
                    ? "the saved roller terminal could not be bound to one unique surveyed OA, Borealis, or ICC logistics origin"
                    : $"surveyed logistics hub '{_rollLogisticsSite}' was not live-verified within 30 seconds; " +
                        LogisticsOriginDiagnostic(_rollLogisticsSite);
                Stop();
                Say("Automatic mission cycle stopped before logistics: " + failure + ".");
                return true;
            }
            _logisticsOriginWaitStarted = DateTime.MinValue;
            bool finalRun = _autoLogisticsFinalPending;
            string startReason = _autoLogisticsReason;
            _autoLogisticsPending = false;
            _autoLogisticsFinalPending = false;
            _autoLogisticsFinalRun = finalRun;
            _autoLogisticsPasses++;
            if (!_automaticLogistics.Start(_inventory.AutomaticLogisticsItemLimit, true))
            {
                string failure = _automaticLogistics.LastFailure ?? "the verified logistics cycle did not start";
                Stop();
                Say("Automatic mission cycle stopped before logistics: " + failure + ".");
                return true;
            }
            _autoLogisticsRunning = true;
            if (_checkpoint?.Execution != null) _checkpoint.Execution.Phase = "AutomaticLogistics";
            Say($"Automatic mission-cycle logistics started at {site}: {startReason}; " +
                $"limit={_inventory.AutomaticLogisticsItemLimit} item(s) per destination.");
            return true;
        }

        private void FinishScheduledAutomaticLogistics()
        {
            VerifiedOperationResult result = _automaticLogistics.Result;
            string failure = _automaticLogistics.LastFailure;
            int stored = _automaticLogistics.StoredCount;
            int sold = _automaticLogistics.SoldCount;
            bool finalRun = _autoLogisticsFinalRun;
            int passes = _autoLogisticsPasses;
            _autoLogisticsRunning = false;
            _autoLogisticsFinalRun = false;
            if (result != VerifiedOperationResult.Succeeded)
            {
                Stop();
                Say("Automatic mission cycle stopped because logistics did not finish with verified success: " +
                    (failure ?? "unknown result") + ". No failed action will be retried.");
                return;
            }
            _inventory.MarkAutomaticLogisticsCompleted();
            _checkpoint.LogisticsPending = false;
            _checkpoint.LastVerifiedPhase = "LogisticsVerified";
            if (finalRun) _autoFinalLogisticsComplete = true;
            int progress = stored + sold;
            if (Inventory.NumFreeSlots < _inventory.MinimumFreeSlots)
            {
                if (progress > 0 && passes < 3)
                {
                    QueueAutomaticLogistics(
                        $"{Inventory.NumFreeSlots} free main slot(s) remain after verified logistics; another bounded pass is required",
                        finalRun, true);
                    _nextSelection = DateTime.MinValue;
                    return;
                }
                int accepted = RunnableAcceptedMissions().Count();
                _capacityLogisticsBlocked = true;
                _capacityLogisticsDeferredWhileAccepted = accepted > 0;
                _autoLogisticsPasses = 0;
                _autoLogisticsReason = null;
                _nextSelection = DateTime.MinValue;
                if (accepted > 0)
                    Say($"Inventory below rolling threshold after verified logistics; continuing {accepted} accepted mission(s), new rolling suspended.");
                else
                    Wait($"Verified logistics left {Inventory.NumFreeSlots} free main slot(s); " +
                        $"{_inventory.MinimumFreeSlots} are required before rolling. Inspect protected, unknown, or unstaged items.");
                return;
            }
            _capacityLogisticsBlocked = false;
            _capacityLogisticsDeferredWhileAccepted = false;
            _autoLogisticsPasses = 0;
            _autoLogisticsReason = null;
            _nextSelection = DateTime.MinValue;
            Say($"Automatic mission cycle resuming after verified logistics: {stored} Keep item(s) stored, " +
                $"{sold} Reject item(s) sold, main free slots={Inventory.NumFreeSlots}.");
        }

        private bool PreflightSelectedMission()
        {
            if (_selected == null) return false;
            if (!_selected.Present || !_selected.IsRubiKaDestination ||
                !AcceptedMissions.Finite(_selected.Entrance))
            { Wait("Selected mission metadata or entrance anchor is incomplete; travel is held."); return false; }
            if ((_selected.Kind == RkMissionKind.FindItem || _selected.Kind == RkMissionKind.ReturnItem) &&
                Inventory.NumFreeSlots <= 1)
            { Wait("One free main-inventory slot is needed before traveling to this item objective."); return false; }
            return true;
        }

        private IEnumerable<AcceptedMission> RunnableAcceptedMissions() =>
            _missions.Records.Where(x => x.Present && x.IsRubiKaDestination &&
                !x.Completed && AcceptedMissions.Finite(x.Entrance));

        private static bool RequiresObjectiveFreeSlot(AcceptedMission mission) =>
            mission.Kind == RkMissionKind.FindItem || mission.Kind == RkMissionKind.ReturnItem;

        private static bool CanSafelyStartAcceptedMission(AcceptedMission mission) =>
            Inventory.NumFreeSlots > 1 || !RequiresObjectiveFreeSlot(mission);

        private bool ObserveDungeonProgress()
        {
            DateTime now = DateTime.UtcNow;
            Vector3 position = DynelManager.LocalPlayer.Position;
            string signature = _dungeon.Status + ":loot=" + _loot.MissionLootProgress +
                ":objective=" + (_dungeon.Objective?.StepSummary ?? "none");
            if (signature != _lifecycleSignature ||
                Vector3.Distance(position, _lifecyclePosition) > 1f ||
                _readiness.IsWaiting || _dungeon.CombatRecoveryWaiting)
            {
                _lifecycleSignature = signature;
                _lifecyclePosition = position;
                _lastLifecycleProgress = now;
                _watchdogRecoveryAt = DateTime.MinValue;
                return true;
            }
            TimeSpan idle = now - _lastLifecycleProgress;
            if (idle > TimeSpan.FromMinutes(4))
            {
                Say($"Mission progress watchdog stopped after {idle.TotalSeconds:0}s without room, movement, loot or objective progress. " +
                    $"Phase={_dungeon.Status}; {_loot.MissionLootBlockers}.");
                Stop();
                return false;
            }
            if (idle > TimeSpan.FromMinutes(2) && _watchdogRecoveryAt == DateTime.MinValue)
            {
                _watchdogRecoveryAt = now;
                _dungeon.RecoverFromStuck(1);
                Say($"Mission progress watchdog replanned after {idle.TotalSeconds:0}s: {_dungeon.Status}.");
            }
            return true;
        }

        private void TickDungeon()
        {
            if (_dungeonStarted)
            {
                if (Playfield.ModelIdentity != _activeDungeon)
                {
                    Stop(); Say("Dungeon instance changed outside the verified handoff; run stopped."); return;
                }
                if (!_dungeon.IsRunning)
                {
                    if (_selected.Completed && _dungeon.IsExiting)
                    { Wait("Automatic exit is held; leave manually to resume the armed local mission chain. See the exit diagnostic."); return; }
                    Stop(); return;
                }
                if (_selected.Completed && _dungeon.IsComplete && _dungeon.IsExiting &&
                    TickCompletedMissionWarp()) return;
                _dungeon.Tick();
                if (_dungeon.IsRunning && !ObserveDungeonProgress()) return;
                if (_dungeon.IsComplete && !_clearanceReported)
                {
                    _clearanceReported = true;
                    _selected.RoomsCleared = true;
                    if (_selected.ReturnHandInPending)
                    {
                        _selected.State = MissionProgress.AwaitingHandIn;
                        _selected.CompletionEvidence = "Return item collected; bound quest hand-in/reward is pending";
                    }
                    else if (!_selected.Completed)
                    {
                        _selected.State = MissionProgress.CompletedAutomatically;
                        _selected.CompletionEvidence = _dungeon.Objective.Evidence;
                        _loot.ReleaseMissionReservations(_selected.Id);
                        _missions.RemoveObjectiveMetadata(_selected);
                    }
                    Say(_selected.ReturnHandInPending
                        ? "Return item collected, all rooms cleared and loot processed. Exiting for exact-source hand-in; reward remains pending."
                        : "Objective acknowledged, all rooms cleared and loot processed. Automatically returning to the mission exit.");
                }
                return;
            }
            if (!Mission.FindMissionForCurrentDungeon(out Mission bound))
            {
                _observedDungeon = Identity.None;
                DateTime now = DateTime.UtcNow;
                if (_handoffWaitStarted == DateTime.MinValue) _handoffWaitStarted = now;
                _associationAttempts++;
                if (_nextAssociationDiagnostic == DateTime.MinValue || now >= _nextAssociationDiagnostic)
                {
                    _nextAssociationDiagnostic = now.AddSeconds(10);
                    Say($"Dungeon association retry: attempt={_associationAttempts}, selected mission={_selected?.Id.ToString() ?? "none"}, " +
                        $"door={_handoffDoor}, dungeon={Playfield.ModelIdentity}, room={DynelManager.LocalPlayer.Room?.Instance.ToString() ?? "none"}; " +
                        "movement and exploration remain held pending exact AO# identity evidence.");
                }
                if (now - _handoffWaitStarted > TimeSpan.FromSeconds(60))
                {
                    Say($"Entrance interaction result: mission={_selected?.Id.Instance}, door={_handoffDoor}, dungeon={Playfield.ModelIdentity}, result=unidentified dungeon after zoning.");
                    _travel.CompleteHandoff(false, "AO# did not identify the selected mission dungeon within 60 seconds of bounded exact retries");
                    Stop(); Say("AO# did not identify an accepted mission for this dungeon within 60 seconds; exact association retries are exhausted and the bot is safely stopped."); return;
                }
                Wait("Retrying exact AO# dungeon association for up to 60 seconds. Exploration is held; no mission identity will be guessed."); return;
            }
            _associationAttempts = 0;
            _nextAssociationDiagnostic = DateTime.MinValue;
            AcceptedMission record = _missions.Find(bound.Identity);
            if (record == null || !record.IsRubiKaDestination || (_selected != null && _selected.Id != bound.Identity))
            {
                Say($"Entrance interaction result: mission={_selected?.Id.Instance}, door={_handoffDoor}, actual mission={bound.Identity.Instance}, " +
                    $"dungeon={Playfield.ModelIdentity}, result=mission mismatch; handoff refused.");
                _travel.CompleteHandoff(false, "zoned dungeon does not match selected accepted mission");
                Stop(); Say("Dungeon does not match the selected accepted Rubi-Ka mission; handoff refused."); return;
            }
            if (_observedDungeon != Playfield.ModelIdentity)
            {
                _observedDungeon = Playfield.ModelIdentity;
                _dungeonObservedAt = DateTime.UtcNow;
                return;
            }
            if (DateTime.UtcNow - _dungeonObservedAt < TimeSpan.FromSeconds(1) || DynelManager.LocalPlayer.Room == null) return;
            _selected = record;
            _verifiedRun = true;
            _verifiedExitReported = false;
            _activeDungeon = Playfield.ModelIdentity;
            _entranceResolver.RecordVerified(bound);
            if (record.Completed)
            {
                Wait("This mission was already confirmed complete in this session. Exit to continue local travel."); return;
            }
            _selected.State = MissionProgress.InProgress;
            _selected.HandoffVerified = true;
            Say($"Entrance interaction result: mission={_selected.Id.Instance}, door={_handoffDoor}, " +
                $"dungeon={Playfield.ModelIdentity}, result=exact selected mission verified after zoning.");
            _travel.CompleteHandoff(true, "exact selected mission dungeon stable for one second with a live room");
            _travel.Reset();
            _waitingReason = null;
            VehicleTransitionResult indoorHud1 = _vehicle.RequireIndoorHud1();
            if (indoorHud1 == VehicleTransitionResult.Waiting || indoorHud1 == VehicleTransitionResult.Blocked)
            {
                Wait("Exact mission entered; HUD1 indoor role is settling: " + _vehicle.Status + ".");
                return;
            }
            _dungeon.Start(record);
            _dungeonStarted = _dungeon.IsRunning;
            _lifecycleSignature = null;
            _lastLifecycleProgress = DateTime.UtcNow;
            _watchdogRecoveryAt = DateTime.MinValue;
            _clearanceReported = false;
            if (_dungeonStarted) Say($"Verified mission {_selected.Id.Instance} in dungeon {Playfield.ModelIdentity.Instance}; existing dungeon logic resumed.");
        }

        private bool TickCompletedMissionWarp()
        {
            if (!_autoCycle || _completedMissionWarpAttempted && _completedMissionWarpTarget == 0)
                return false;
            if (!_completedMissionWarpAttempted)
            {
                _completedMissionWarpAttempted = true;
                // Mirror outdoor selection: refill the batch at the saved
                // terminal first, then travel to the next accepted mission.
                bool refill = !_clearAcceptedBeforeRolling && _hasRollTerminal &&
                    _awaitingQuestDetails.Count == 0 && Inventory.NumFreeSlots >= 2 &&
                    (_maxAutoMissions == 0 || _autoAcceptedCount < _maxAutoMissions);
                if (refill) _completedMissionWarpTarget = _rollTerminalPlayfield;
                else if (!_missions.Eligible(_selected.PlayfieldId).Any())
                    _completedMissionWarpTarget = _missions.Records
                        .Where(x => x.Present && x.IsRubiKaDestination && !x.Completed)
                        .OrderBy(x => x.PlayfieldId).ThenBy(x => x.Id.Instance)
                        .Select(x => x.PlayfieldId).FirstOrDefault();
                if (_completedMissionWarpTarget == _selected.PlayfieldId)
                    _completedMissionWarpTarget = 0;
                if (_completedMissionWarpTarget != 0)
                    Say($"Mission complete; requesting Scottyboi warp to playfield {_completedMissionWarpTarget} before leaving the dungeon.");
            }
            if (_completedMissionWarpTarget == 0) return false;
            _readiness.ObserveCombat();
            if (_readiness.InCombat)
            {
                _warp.Reset();
                _completedMissionWarpTarget = 0;
                _dungeon.ResumeExitAfterWarpWait();
                Say("Combat resumed during the completed-mission warp wait; returning to dungeon exit handling.");
                return false;
            }
            WarpResult result = _warp.Tick(_completedMissionWarpTarget);
            if (result == WarpResult.InProgress) return true;
            if (result == WarpResult.Failed)
            {
                Say($"Scottyboi did not warp from inside the completed mission: {_warp.LastFailure}. Using the verified dungeon exit.");
                _warp.Reset();
                _completedMissionWarpTarget = 0;
                _dungeon.ResumeExitAfterWarpWait();
                return false;
            }
            return true; // Outdoor zoning must verify the exact warp destination.
        }

        private void Wait(string reason)
        {
            if (_waitingReason == reason) return;
            _waitingReason = reason;
            Say(reason);
        }

        private bool ObserveAutoAcceptance()
        {
            MaliMissionRoller2.MainWindow window = MaliMissionRoller2.Main.Window;
            if (window == null)
            { Stop(); Say("Mission roller is unavailable; automatic cycle stopped."); return false; }
            int pending = window.PendingAutoMissionId;
            if (pending > 0)
            {
                _missions.Refresh(true);
                int destination = window.PendingAutoPlayfieldId;
                AcceptedMission accepted = _missions.Records.FirstOrDefault(x =>
                    x.Id.Instance == pending && x.Present);
                if (accepted == null)
                {
                    // The accepted quest receives a new identity; the offer ID
                    // is only the argument to CreateQuestMessage. Match a new
                    // native quest against the offered destination and the
                    // pre-accept mission-list snapshot.
                    var added = _missions.Records.Where(x => x.Present &&
                        !window.PendingAutoPreviousQuestIds.Contains(x.Id.Instance)).ToList();
                    var inDestination = added.Where(x => x.PlayfieldId == destination).ToList();
                    if (inDestination.Count == 1) accepted = inDestination[0];
                    else if (added.Count > 0)
                    {
                        var matching = added.Where(x =>
                            string.Equals(x.Name, window.PendingAutoTitle, StringComparison.OrdinalIgnoreCase) ||
                            (x.PlayfieldId == destination &&
                                Vector3.Distance(x.Entrance, window.PendingAutoLocation) <= 12f)).ToList();
                        if (matching.Count == 1) accepted = matching[0];
                        else if (added.Count == 1 && added[0].PlayfieldId == 0)
                            accepted = added[0]; // Live quest exists; destination metadata is still loading.
                    }
                }
                var updatedIds = _missions.NewQuestUpdateIdsSince(window.AutoAcceptRequestedAtUtc,
                    window.PendingAutoPreviousQuestIds).ToList();
                int updateId = updatedIds.Count == 1 ? updatedIds[0] : 0;
                bool exactQuestAcknowledged = _missions.ObservedQuestUpdate(pending, window.AutoAcceptRequestedAtUtc);
                if (accepted != null || updateId > 0 || exactQuestAcknowledged)
                {
                    int acceptedId = accepted?.Id.Instance ?? (updateId > 0 ? updateId : pending);
                    BindAcceptedMissionOrigin(acceptedId, destination, window.PendingAutoLocation);
                    if (accepted?.IsRubiKaDestination == true)
                        ReconcileAcceptedMissionOrigin(accepted);
                    if (_autoAcceptedIds.Add(acceptedId)) _autoAcceptedCount++;
                    _autoAcceptedPlayfields[acceptedId] = destination;
                    if (accepted == null || !accepted.IsRubiKaDestination)
                        _awaitingQuestDetails.Add(acceptedId);
                    window.StopZoneRolling();
                    _autoRolling = false;
                    if (Inventory.NumFreeSlots < _inventory.MinimumFreeSlots ||
                        (_maxAutoMissions > 0 && _autoAcceptedCount >= _maxAutoMissions))
                        _missionBatchRemountPending = true;
                    Say($"Automatic acceptance confirmed: offer {pending}, quest {acceptedId}; " +
                        $"destination playfield {destination}, " +
                        $"{_autoAcceptedCount}/{(_maxAutoMissions == 0 ? "unlimited" : _maxAutoMissions.ToString())} this cycle" +
                        (accepted == null || !accepted.IsRubiKaDestination
                            ? "; waiting for AO# quest-list destination." : "."));
                    return false;
                }
                if (DateTime.UtcNow - window.AutoAcceptRequestedAtUtc < TimeSpan.FromSeconds(20))
                {
                    Wait($"Waiting for accepted mission {pending} to appear in the quest list.");
                    return false;
                }
                var observed = _missions.Records.Where(x => x.Present &&
                    !window.PendingAutoPreviousQuestIds.Contains(x.Id.Instance))
                    .Take(5).Select(x => $"{x.Id.Instance}/PF{x.PlayfieldId}").ToList();
                Stop();
                Say($"Offer {pending} was sent for acceptance, but no matching new quest appeared in playfield {destination} within 20 seconds. Check /rkm missions before restarting /rkm auto.");
                Say("New quest-list identities seen during acceptance: " +
                    (observed.Count > 0 ? string.Join(", ", observed) : "none") + ".");
                return false;
            }
            if (Inventory.NumFreeSlots < _inventory.MinimumFreeSlots ||
                (_maxAutoMissions > 0 && _autoAcceptedCount >= _maxAutoMissions))
            {
                window.StopZoneRolling();
                _autoRolling = false;
                _missionBatchRemountPending = true;
                Say($"Mission batch ready: {_autoAcceptedCount} accepted, " +
                    $"{Inventory.NumFreeSlots} free inventory slots; selecting an accepted mission.");
                return false;
            }
            if (!window.IsAutoRolling)
            {
                string reason = window.LastAutoError ?? "The mission roller stopped before finding a mission.";
                Stop();
                Say(reason);
                return false;
            }
            if (DateTime.UtcNow - window.LastAutoOfferAtUtc > TimeSpan.FromSeconds(20))
            {
                Stop();
                Say("The mission terminal did not return an offer within 20 seconds. Check terminal range and restart /rkm auto.");
                return false;
            }
            return false;
        }

        private void ObserveAutoRoll(object sender, RollListChangedArgs args)
        {
            if (!_running || !_autoCycle || !_autoRolling) return;
            if (MaliMissionRoller2.Main.Window?.PendingAutoMissionId > 0) return;
            if (++_autoRollCount < _maxAutoRolls) return;
            MaliMissionRoller2.Main.Window?.StopZoneRolling();
            _autoRolling = false;
            Stop();
            Say($"Automatic rolling stopped after {_maxAutoRolls} offers without an accepted target.");
        }

        private void StartAutoRolling()
        {
            if (!_running || !_autoCycle || _autoRolling || MaliMissionRoller2.Main.Window == null) return;
            if (!ReadyToRefill(out string unresolved))
            {
                Wait("Batch refill held: " + unresolved + ".");
                return;
            }
            if (MaliMissionRoller2.Main.Window.Window?.IsValid != true)
                _roller.ShowRoller();
            if (MaliMissionRoller2.Main.Window.Window?.IsValid != true)
            { Stop(); Say("Roller window could not be opened for automatic rolling."); return; }
            if (Inventory.NumFreeSlots < 2)
            { Stop(); Say("Two free main-inventory slots are required before automatic rolling."); return; }
            if (_autoZone > 0 && !AcceptedMissions.IsRubiKaPlayfield(_autoZone))
            { Stop(); Say($"Rolling zone {_autoZone} is not a configured Rubi-Ka mission destination."); return; }
            if (_autoZone == 0 && !MaliMissionRoller2.Main.Window.HasEnabledAutoDestination)
            { Stop(); Say("No Rubi-Ka destination is enabled in the roller. Enable a location or use /rkm zone <id>."); return; }
            MaliMissionRoller2.Main.Window.AutoAcceptedCountForPlayfield = playfield =>
                _autoAcceptedPlayfields.Values.Count(x => x == playfield) +
                _missions.Records.Count(x => x.Present && x.IsRubiKaDestination &&
                    !x.Completed && x.PlayfieldId == playfield &&
                    !_autoAcceptedPlayfields.ContainsKey(x.Id.Instance));
            Dynel? terminal = FindVisibleRollTerminal(7.5f);
            if (terminal == null)
            { Wait("Mission terminal is not yet in range; automatic cycle remains armed and will retry."); return; }
            _rollingTerminalReference = new MissionTerminal(terminal);
            MaliMissionRoller2.Main.Window.UpdateTerminal(_rollingTerminalReference);
            RememberRollTerminal(terminal);
            _rollingTerminalIdentity = terminal.Identity;
            _rollingTerminalPlayfield = Playfield.ModelIdentity.Instance;
            _rollingTerminalPosition = terminal.Position;
            _autoRollCount = 0;
            _autoRolling = true;
            ResetRollTerminalReturn();
            if (!MaliMissionRoller2.Main.Window.StartZoneRolling(_autoZone))
            {
                string reason = MaliMissionRoller2.Main.Window.LastAutoError ?? "The mission terminal did not start rolling.";
                Stop(); Say(reason); return;
            }
            Say($"Mission roller is rolling for {(_autoZone == 0 ? $"{MaliMissionRoller2.Main.Window.EnabledAutoDestinationCount} enabled Rubi-Ka playfield(s)" : $"playfield {_autoZone}")}; " +
                $"offer limit {_maxAutoRolls}, mission limit {(_maxAutoMissions == 0 ? "unlimited" : _maxAutoMissions.ToString())}.");
        }

        private bool ReadyToRoll(out string unresolved)
        {
            return CycleReadiness.IsReadyToRoll(BuildRollReadinessSnapshot(), out unresolved);
        }

        private bool ReadyToRefill(out string unresolved)
        {
            return CycleReadiness.IsReadyToRefill(BuildRollReadinessSnapshot(), out unresolved);
        }

        private CycleReadinessSnapshot BuildRollReadinessSnapshot()
        {
            SimpleChar player = DynelManager.LocalPlayer;
            bool exactTerminal = false;
            if (player != null && _hasRollTerminal &&
                !string.IsNullOrEmpty(_rollTerminalIdentity) &&
                Playfield.ModelIdentity.Instance == _rollTerminalPlayfield)
            {
                Dynel terminal = DynelManager.AllDynels.FirstOrDefault(x =>
                    x.Identity.Type == IdentityType.MissionTerminal &&
                    Vector3.Distance(x.Position, _rollTerminalPosition) <= 1f &&
                    string.Equals(x.Identity.ToString(), _rollTerminalIdentity,
                        StringComparison.OrdinalIgnoreCase));
                exactTerminal = terminal != null &&
                    Vector3.Distance(player.Position, terminal.Position) <= RollTerminalApproachDistance;
            }
            var state = new CycleReadinessSnapshot
            {
                Outdoors = !Playfield.IsDungeon ||
                    Playfield.ModelIdentity.Instance == (int)PlayfieldId.FixerGrid,
                Alive = player?.IsAlive == true,
                NotZoning = !Game.IsZoning,
                MissionListKnown = Mission.List != null,
                NoAcceptedWork = !RunnableAcceptedMissions().Any() && _awaitingQuestDetails.Count == 0,
                NoHandInPending = !_missions.Records.Any(x => x.Present && x.ReturnHandInPending),
                NoRewardPending = !_checkpoint.RewardSettlementPending,
                NoInventorySettlementPending = !_checkpoint.InventorySettlementPending,
                NoLogisticsPending = !_autoLogisticsPending && !_autoLogisticsRunning &&
                    !_autoLogisticsFinalPending && !_automaticLogistics.IsActive,
                InventoryReady = CarriedItems.BackpackSnapshotsReady,
                NoPendingItemAction = !Item.HasPendingUse && !Spell.HasPendingCast,
                NoOwnedMovement = _movement.Owner == MovementOwner.None,
                ExactRollTerminalVerified = exactTerminal,
                PriorAcceptanceSettled = MaliMissionRoller2.Main.Window != null &&
                    MaliMissionRoller2.Main.Window.PendingAutoMissionId <= 0,
                QuestDetailsSettled = _awaitingQuestDetails.Count == 0,
                HasBatchCapacity = Inventory.NumFreeSlots >= 2,
                UnderMissionLimit = _maxAutoMissions == 0 || _autoAcceptedCount < _maxAutoMissions
            };
            return state;
        }

        private void RecordUnresolvedInvariant(string unresolved)
        {
            string reason = "READY_TO_ROLL held: " + (unresolved ?? "unknown invariant");
            if (_checkpoint != null)
            {
                _checkpoint.StopInvariant = unresolved;
                _checkpoint.Save(true, Say);
            }
            Wait(reason + ".");
        }

        private void BindAcceptedMissionOrigin(int questId, int destination, Vector3 entrance)
        {
            Dynel terminal = DynelManager.GetDynel(_rollingTerminalIdentity);
            if (_rollingTerminalIdentity.Type != IdentityType.MissionTerminal ||
                terminal == null || Playfield.ModelIdentity.Instance != _rollingTerminalPlayfield ||
                Vector3.Distance(terminal.Position, _rollingTerminalPosition) > 1f ||
                !ReferenceEquals(MaliMissionRoller2.MainWindow.CurrentTerminal,
                    _rollingTerminalReference) ||
                !AcceptedMissions.Finite(entrance) || destination <= 0)
            {
                Say($"Quest {questId} has no verified issuing terminal; automatic return-item hand-in will be held.");
                return;
            }
            _checkpoint.OriginTerminals.RemoveAll(x => x.QuestInstance == questId);
            _checkpoint.OriginTerminals.Add(new MissionOriginBinding
            {
                QuestInstance = questId,
                CharacterInstance = DynelManager.LocalPlayer.Identity.Instance,
                DestinationPlayfield = destination,
                Destination = entrance,
                TerminalInstance = _rollingTerminalIdentity.Instance,
                TerminalPlayfield = _rollingTerminalPlayfield,
                TerminalPosition = _rollingTerminalPosition,
                AcceptedAtUtc = DateTime.UtcNow
            });
            _checkpoint.Save(true, Say);
            Say($"Bound accepted quest {questId} to issuing terminal {_rollingTerminalIdentity} " +
                $"at PF {_rollingTerminalPlayfield} ({LocalRoutePlanner.Coordinates(_rollingTerminalPosition)}).");
        }

        private bool ReconcileAcceptedMissionOrigin(AcceptedMission accepted)
        {
            if (accepted == null || !accepted.Present || !accepted.IsRubiKaDestination ||
                !AcceptedMissions.Finite(accepted.Entrance)) return false;
            MissionOriginBinding origin = _checkpoint.OriginTerminals.FirstOrDefault(x =>
                x.QuestInstance == accepted.Id.Instance &&
                x.CharacterInstance > 0 &&
                x.CharacterInstance == DynelManager.LocalPlayer.Identity.Instance &&
                x.TerminalInstance != 0);
            if (origin == null || origin.DestinationPlayfield != accepted.PlayfieldId) return false;

            float adjustment = Vector3.Distance(origin.Destination, accepted.Entrance);
            if (adjustment <= 0.01f) return true;
            origin.Destination = accepted.Entrance;
            _checkpoint.Save(true, Say);
            Say($"Reconciled quest {accepted.Id.Instance} origin to its authoritative accepted entrance " +
                $"in PF {accepted.PlayfieldId}; live entrance adjustment={adjustment:0.00}m; " +
                $"issuing terminal {origin.TerminalIdentity} at PF {origin.TerminalPlayfield} preserved.");
            return true;
        }

        private bool ReturnToRollTerminal()
        {
            if (!_hasRollTerminal)
            {
                Dynel? visible = FindVisibleRollTerminal();
                if (visible == null)
                {
                    Wait("No saved roller terminal is available. Automatic cycle remains armed; move within sight of a mission terminal to resume rolling.");
                    return false;
                }
                RememberRollTerminal(visible);
            }
            if (Playfield.ModelIdentity.Instance != _rollTerminalPlayfield || _longTravel.IsActive)
            {
                TravelResult result = _longTravel.Tick(_rollTerminalPlayfield);
                if (result == TravelResult.Blocked)
                { string reason = _longTravel.LastFailure; Stop(); Say("Return to roller stopped: " + reason); return false; }
                if (result == TravelResult.InProgress) return false;
                if (result == TravelResult.Arrived &&
                    string.Equals(_longTravel.CurrentProvider, "Scottyboi",
                        StringComparison.OrdinalIgnoreCase) &&
                    DynelManager.LocalPlayer != null)
                {
                    _returnCityRouteEligible = true;
                    _returnScottyLanding = DynelManager.LocalPlayer.Position;
                }
            }
            if (_longTravel.IsActive) _longTravel.Reset();
            if (_returnCityRouteEligible)
            {
                CityReturnRouteResult cityRoute = _navRoutes.TryNavigateCityReturn(
                    _returnScottyLanding, _rollTerminalPosition, _movement,
                    MovementOwner.OutdoorTravel);
                if (cityRoute == CityReturnRouteResult.InProgress) return false;
                _returnCityRouteEligible = false;
                if (cityRoute == CityReturnRouteResult.NoMatch)
                    Say($"No safe recorded Scotty-to-terminal route matched PF {_rollTerminalPlayfield} " +
                        $"from observed landing {_returnScottyLanding}; using existing local terminal-return behavior.");
                else
                    Say("Recorded city return route reached its terminal-side endpoint; " +
                        "continuing to grounded exact-terminal identity/position verification.");
            }
            SimpleChar player = DynelManager.LocalPlayer;
            if (string.IsNullOrEmpty(_rollTerminalIdentity))
            {
                Dynel[] legacyMatches = DynelManager.AllDynels.Where(x =>
                    x.Identity.Type == IdentityType.MissionTerminal &&
                    Vector3.Distance(x.Position, _rollTerminalPosition) <= 1f).ToArray();
                if (legacyMatches.Length == 1)
                {
                    _rollTerminalIdentity = legacyMatches[0].Identity.ToString();
                    _checkpoint.RollTerminalIdentity = _rollTerminalIdentity;
                    _checkpoint.Save(true, Say);
                    Say($"Migrated legacy saved roller position to unambiguous live terminal {_rollTerminalIdentity}.");
                }
            }
            Dynel terminal = DynelManager.AllDynels.FirstOrDefault(x =>
                x.Identity.Type == IdentityType.MissionTerminal &&
                string.Equals(x.Identity.ToString(), _rollTerminalIdentity,
                    StringComparison.OrdinalIgnoreCase) &&
                Vector3.Distance(x.Position, _rollTerminalPosition) <= 1f);
            if (player == null) return false;
            // Saved position is navigation evidence; live identity remains the
            // required rolling proof. Do not require streaming at a distant warp.
            Vector3 terminalPosition = terminal?.Position ?? _rollTerminalPosition;
            if (terminal == null && Vector3.Distance(player.Position, terminalPosition) <= 10f)
            {
                _movement.Release(MovementOwner.OutdoorTravel);
                if (_returnTerminalVisibilitySince == DateTime.MinValue)
                    _returnTerminalVisibilitySince = DateTime.UtcNow;
                if (DateTime.UtcNow - _returnTerminalVisibilitySince > TimeSpan.FromSeconds(8))
                { Stop(); Say("Return to roller stopped: the exact saved mission terminal is not visible at its recorded position."); return false; }
                Wait("Near the saved terminal position; waiting for exact terminal visibility before landing verification and rolling.");
                return false;
            }
            _returnTerminalVisibilitySince = DateTime.MinValue;
            if (!_returnTerminalPlanReady)
            {
                if (!_logisticsRoutes.TryFindTerminalApproach(_rollLogisticsSite, _rollTerminalPlayfield,
                    _rollTerminalIdentity, terminalPosition, player.Position, out _returnTerminalApproach))
                { Stop(); Say("Return to roller stopped: no collision-safe interaction standoff could be derived for the exact saved mission terminal."); return false; }
                if (!_logisticsRoutes.TryFindTerminalLandingStaging(_rollLogisticsSite, _rollTerminalPlayfield,
                    _rollTerminalIdentity, terminalPosition, player.Position, out _returnLandingStaging))
                { Stop(); Say("Return to roller stopped: no safe lateral ground-level landing staging point could be derived for the exact saved mission terminal."); return false; }
                _returnTerminalPlanReady = true;
            }
            Vector3 approach = _returnTerminalApproach;
            Vector3 landingStaging = _returnLandingStaging;
            if (_returnStarted == DateTime.MinValue)
            {
                _returnStarted = DateTime.UtcNow;
                _lastReturnProgressPosition = player.Position;
                _returnNoProgressAttempts = 0;
            }
            if (DateTime.UtcNow - _returnStarted > TimeSpan.FromMinutes(3))
            { Stop(); Say("Return to the mission terminal timed out before reaching its surveyed interaction standoff."); return false; }
            VehicleTransitionResult foot = _vehicle.RequireFootForTerminal(landingStaging);
            if (foot == VehicleTransitionResult.Unavailable)
            {
                _movement.Release(MovementOwner.OutdoorTravel);
                string vehicleFailure = _vehicle.Status;
                Stop(); Say($"Return to roller stopped: safe terminal landing/dismount could not be verified; {vehicleFailure}.");
                return false;
            }
            if (foot != VehicleTransitionResult.Ready)
            {
                Wait($"Vehicle lifecycle: {_vehicle.Status}; exact terminal={terminal?.Identity.ToString() ?? _rollTerminalIdentity}, " +
                    $"player-to-landing={Vector3.Distance(player.Position, landingStaging):0.0}m, " +
                    $"player-to-terminal={Vector3.Distance(player.Position, terminalPosition):0.0}m.");
                return false;
            }
            if (terminal != null && Vector3.Distance(player.Position, terminalPosition) <= RollTerminalApproachDistance)
            {
                _movement.Release(MovementOwner.OutdoorTravel);
                Say($"Exact saved roller terminal verification passed: {terminal?.Identity.ToString() ?? _rollTerminalIdentity} in PF {_rollTerminalPlayfield}, " +
                    $"position delta={Vector3.Distance(terminalPosition, _rollTerminalPosition):0.0}m, " +
                    $"player range={Vector3.Distance(player.Position, terminalPosition):0.0}m.");
                ResetRollTerminalReturn();
                return true;
            }
            if (DateTime.UtcNow >= _nextReturnMove)
            {
                float progress = Vector3.Distance(player.Position, _lastReturnProgressPosition);
                if (progress >= 0.5f)
                { _lastReturnProgressPosition = player.Position; _returnNoProgressAttempts = 0; }
                else if (++_returnNoProgressAttempts >= 4)
                {
                    _movement.Release(MovementOwner.OutdoorTravel);
                    Stop();
                    Say($"Return to roller stopped after {_returnNoProgressAttempts} non-progress retries; " +
                        $"player-to-standoff={Vector3.Distance(player.Position, approach):0.0}m, " +
                        $"player-to-terminal={Vector3.Distance(player.Position, terminalPosition):0.0}m.");
                    return false;
                }
                _movement.SetDestination(MovementOwner.OutdoorTravel, approach);
                _nextReturnMove = DateTime.UtcNow.AddSeconds(3);
            }
            return false;
        }

        private void ResetRollTerminalReturn()
        {
            _returnStarted = DateTime.MinValue;
            _returnTerminalVisibilitySince = DateTime.MinValue;
            _nextReturnMove = DateTime.MinValue;
            _lastReturnProgressPosition = Vector3.Zero;
            _returnNoProgressAttempts = 0;
            _returnCityRouteEligible = false;
            _returnScottyLanding = Vector3.Zero;
            _returnTerminalApproach = _returnLandingStaging = Vector3.Zero;
            _returnTerminalPlanReady = false;
            _vehicle.ResetTerminalRequirement();
        }

        private string LogisticsOriginDiagnostic(string site)
        {
            SimpleChar player = DynelManager.LocalPlayer;
            if (player == null) return "local player unavailable";
            LogisticsRouteCatalog.Route route = _logisticsRoutes?.Routes
                .Where(x => string.Equals(x.Site, site, StringComparison.OrdinalIgnoreCase) &&
                    x.OriginPlayfield == Playfield.ModelIdentity.Instance)
                .OrderBy(x => Vector3.Distance(player.Position,
                    new Vector3(x.OriginPosition[0], x.OriginPosition[1], x.OriginPosition[2])))
                .FirstOrDefault();
            float savedDistance = Vector3.Distance(player.Position, _rollTerminalPosition);
            if (route == null)
                return $"player-to-saved-terminal={savedDistance:0.0}m, matching surveyed route unavailable";
            Vector3 origin = new Vector3(route.OriginPosition[0], route.OriginPosition[1], route.OriginPosition[2]);
            Dynel terminal = DynelManager.AllDynels.FirstOrDefault(x =>
                x.Identity.Type == IdentityType.MissionTerminal &&
                string.Equals(x.Identity.ToString(), route.OriginTerminalIdentity,
                    StringComparison.OrdinalIgnoreCase));
            string live = terminal == null ? "not visible" :
                $"visible, live-to-origin={Vector3.Distance(terminal.Position, origin):0.0}m";
            return $"player-to-saved-terminal={savedDistance:0.0}m, " +
                $"player-to-surveyed-origin={Vector3.Distance(player.Position, origin):0.0}m, exact terminal={live}";
        }

        private bool TickReturnHandIn()
        {
            MissionOriginBinding origin = _checkpoint.OriginTerminals
                .FirstOrDefault(x => x.Matches(_selected, DynelManager.LocalPlayer.Identity));
            if (origin == null)
            {
                MissionOriginBinding candidate = _checkpoint.OriginTerminals
                    .FirstOrDefault(x => x.QuestInstance == _selected.Id.Instance);
                string provenance = candidate == null
                    ? $"no stored binding has quest instance {_selected.Id.Instance}; stored binding count={_checkpoint.OriginTerminals.Count}"
                    : candidate.TryMatch(_selected, DynelManager.LocalPlayer.Identity, out string mismatch)
                        ? "candidate unexpectedly matched during diagnostic replay"
                        : mismatch;
                Stop();
                Say("Automatic return-item hand-in stopped: this quest has no verified issuing terminal binding. " +
                    $"Provenance check: {provenance}. Return it manually to the terminal where it was pulled.");
                return false;
            }
            Identity issuingTerminal = origin.TerminalIdentity;
            UseItemOnItemAction action = _selected.Actions?.OfType<UseItemOnItemAction>()
                .FirstOrDefault(x => x.Destination == _selected.Source);
            if (_selected.Source != issuingTerminal || action == null ||
                action.Destination != issuingTerminal)
            {
                Stop();
                Say($"Automatic return-item hand-in stopped: quest {_selected.Id.Instance} names {_selected.Source} " +
                    $"but its verified issuing terminal is {issuingTerminal}. No item was used.");
                return false;
            }
            if (Playfield.ModelIdentity.Instance != origin.TerminalPlayfield || _longTravel.IsActive)
            {
                TravelResult travel = _longTravel.Tick(origin.TerminalPlayfield, origin.TerminalPosition);
                if (travel == TravelResult.Blocked)
                {
                    string reason = _longTravel.LastFailure;
                    Stop(); Say("Return to the issuing terminal stopped: " + reason);
                }
                if (travel == TravelResult.InProgress || travel == TravelResult.Blocked) return false;
            }
            if (_longTravel.IsActive) _longTravel.Reset();
            Dynel target = DynelManager.GetDynel(issuingTerminal);
            if (target != null && Vector3.Distance(target.Position, origin.TerminalPosition) > 3f)
            {
                Stop();
                Say("Automatic return-item hand-in stopped: the issuing terminal identity appeared at an unexpected position.");
                return false;
            }
            if (target != null &&
                Vector3.Distance(DynelManager.LocalPlayer.Position, target.Position) <= 2.5f)
                _movement.Halt(MovementOwner.OutdoorTravel);
            HandInResult result = _handIn.Tick(_selected, issuingTerminal);
            if (result == HandInResult.Confirmed)
            {
                _movement.Release(MovementOwner.OutdoorTravel);
                _selected.ReturnHandInPending = false;
                _selected.State = MissionProgress.CompletedAutomatically;
                _loot.ReleaseMissionReservations(_selected.Id);
                _selected.CompletionEvidence =
                    "Exact return item used on its verified issuing terminal; item consumed and bound quest absent for 2 s";
                _checkpoint.OriginTerminals.Remove(origin);
                _missions.RemoveObjectiveMetadata(_selected);
                _checkpoint.Save(true, Say);
                _handInLocateStarted = DateTime.MinValue;
                Say($"Return-item hand-in confirmed for mission {_selected.Id.Instance}; the bound quest cleared and chaining may proceed.");
                return true;
            }
            if (result == HandInResult.Blocked)
            {
                string reason = _handIn.Failure;
                Stop();
                Say("Automatic return-item hand-in stopped: " + reason);
                return false;
            }
            if (target != null)
            {
                _handInLocateStarted = DateTime.MinValue;
                if (Vector3.Distance(DynelManager.LocalPlayer.Position, target.Position) > 2.5f)
                    _movement.SetDestination(MovementOwner.OutdoorTravel, target.Position);
                else _movement.Halt(MovementOwner.OutdoorTravel);
                return false;
            }
            if (_handInLocateStarted == DateTime.MinValue)
                _handInLocateStarted = DateTime.UtcNow;
            if (DateTime.UtcNow - _handInLocateStarted > TimeSpan.FromMinutes(4))
            {
                Stop();
                Say("Automatic hand-in stopped: the verified issuing terminal did not become visible within four minutes.");
                return false;
            }
            if (Vector3.Distance(DynelManager.LocalPlayer.Position, origin.TerminalPosition) > 6f)
            {
                if (DateTime.UtcNow >= _nextReturnMove)
                {
                    _movement.SetDestination(MovementOwner.OutdoorTravel, origin.TerminalPosition);
                    _nextReturnMove = DateTime.UtcNow.AddSeconds(3);
                }
            }
            else Wait("At the issuing terminal location; waiting for its exact identity to become visible.");
            return false;
        }

        private static Dynel? FindVisibleRollTerminal(float maxDistance = float.MaxValue)
        {
            var player = DynelManager.LocalPlayer;
            if (player == null) return null;
            return DynelManager.AllDynels.Where(x => x.Identity.Type == IdentityType.MissionTerminal &&
                    x.DistanceFrom(player) < maxDistance)
                .OrderBy(x => x.DistanceFrom(player)).FirstOrDefault();
        }

        private void RememberRollTerminal(Dynel? terminal)
        {
            if (terminal == null) return;
            int playfield = Playfield.ModelIdentity.Instance;
            string site = _logisticsRoutes?.FindSharedLogisticsSite(playfield);
            bool unchanged = _hasRollTerminal && _rollTerminalPlayfield == playfield &&
                Vector3.Distance(_rollTerminalPosition, terminal.Position) < 0.5f &&
                string.Equals(_rollLogisticsSite, site, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(_rollTerminalIdentity, terminal.Identity.ToString(),
                    StringComparison.OrdinalIgnoreCase);
            if (unchanged) return;
            _hasRollTerminal = true;
            _rollTerminalPlayfield = playfield;
            _rollTerminalPosition = terminal.Position;
            _rollTerminalIdentity = terminal.Identity.ToString();
            _rollLogisticsSite = site;
            if (_checkpoint == null) return;
            _checkpoint.HasRollTerminal = true;
            _checkpoint.RollTerminalPlayfield = playfield;
            _checkpoint.RollTerminalPosition = terminal.Position;
            _checkpoint.RollTerminalIdentity = _rollTerminalIdentity;
            _checkpoint.RollLogisticsSite = _rollLogisticsSite;
            _checkpoint.Save(true, Say);
            Say($"Saved roller terminal {terminal.Identity} in playfield {playfield} for the automatic return after missions; " +
                (site == null ? "no complete city logistics hub is available."
                    : $"shared surveyed logistics hub='{site}'."));
        }

        private void OnRubberband(Vector3 position)
        {
            RecoverMovement("rubberband");
        }

        private void OnStuck(Vector3 position, Vector3 destination)
        {
            RecoverMovement("stuck");
        }

        private void RecoverMovement(string signal)
        {
            if (_logisticsNavigator?.IsActive == true && _movement?.Owner == MovementOwner.LogisticsTravel)
            {
                _logisticsNavigator.Stop(true);
                Say($"Logistics route test stopped after movement {signal}; no further waypoint was sent.");
                return;
            }
            int tier = _movement?.ObserveDisplacement() ?? 0;
            if (tier == 0) return;
            Say($"Movement {signal}: recovery tier {tier}, owner={_movement.Owner}.");
            if (Playfield.ModelIdentity.Instance == (int)PlayfieldId.FixerGrid)
                _longTravel?.InvalidatePath();
            else if (Playfield.IsDungeon) _dungeon?.RecoverFromStuck(tier);
            else
            {
                _longTravel?.InvalidatePath();
                if (tier >= 3 && _selected != null && !_travelInvalidated)
                    _travel.Reset("repeated movement displacement; rebuilding local route");
            }
        }
    }
}
