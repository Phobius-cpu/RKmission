using System;
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
        private LocalMissionTravel _travel;
        private MovementArbiter _movement;
        private ScottyboiWarpProvider _warp;
        private RubiKaTravelPlanner _longTravel;
        private MissionEntranceResolver _entranceResolver;
        private MissionCheckpoint _checkpoint;
        private bool _pendingCheckpointResume;
        private DateTime _checkpointResumeAfter;
        private AcceptedMission _selected;
        private bool _autoCycle, _autoRolling, _hasRollTerminal, _recoveringDeath;
        private int _autoZone, _autoRollCount, _maxAutoRolls = 100, _rollTerminalPlayfield;
        private Vector3 _rollTerminalPosition;
        private DateTime _nextReturnMove, _returnStarted;
        private bool _running, _dungeonStarted, _clearanceReported, _verifiedRun, _travelInvalidated;
        private Identity _observedDungeon = Identity.None;
        private Identity _activeDungeon = Identity.None;
        private Identity _mapMission = Identity.None, _handoffDoor = Identity.None;
        private int _mapPlayfield;
        private Vector3 _mapAnchor;
        private DateTime _dungeonObservedAt, _nextTick, _nextSelection, _handoffWaitStarted;
        private string _waitingReason;

        public override void Run(string pluginDir)
        {
            _movement = MovementArbiter.Current = new MovementArbiter();
            _autoZone = Playfield.ModelIdentity.Instance;
            _roller = new MaliMissionRoller2.Main();
            _roller.Run(System.IO.Path.Combine(pluginDir, "Plugins", "MaliMissionRoller2"));
            _map = new DungeonMap();
            _map.Run(System.IO.Path.Combine(pluginDir, "Plugins", "MalisDungeonMap2"));
            _loot = new ManagerLoot.ManagerLoot();
            _loot.RunEmbedded(System.IO.Path.Combine(pluginDir, "Plugins", "ManagerLoot"));
            _readiness = new MissionReadiness(Say, MissionReadinessSettings.Load(pluginDir, Say));
            _inventory = InventoryPolicy.Load(pluginDir, Say);
            _dungeon = new MissionDungeon(Say, _loot, _readiness, _inventory);
            _deathRecovery = new DeathRecoveryController(_readiness, _movement, Say);
            _travel = new LocalMissionTravel(Say, pluginDir);
            _warp = new ScottyboiWarpProvider(Say, _movement);
            _longTravel = new RubiKaTravelPlanner(pluginDir, _warp, _movement, Say);
            _entranceResolver = new MissionEntranceResolver(pluginDir, Say);
            _checkpoint = MissionCheckpoint.Load(pluginDir, Say);
            _pendingCheckpointResume = _checkpoint.Armed;
            _checkpointResumeAfter = DateTime.UtcNow.AddSeconds(5);
            SMovementController.Set();
            // The pinned SDK's default stuck action directly changes player
            // position. Replace that action; our Stuck event handles replanning.
            SMovementController.SetStuckLogic(() => { });
            SMovementController.AutoLoadNavmeshes($"{pluginDir}\\NavMeshes", (id, dungeon) => !dungeon);
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
            Stop(true);
            Game.OnUpdate -= Update;
            Game.TeleportStarted -= ZoningStarted;
            Game.TeleportEnded -= ZoningEnded;
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
            _warp.Dispose();
            _roller.Teardown();
            _map.Teardown();
            _loot.Teardown();
        }

        private static void Say(string text) => Chat.WriteLine("RKMission: " + text);

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
                    Say($"Armed={_running}, cycle={(_autoCycle ? "automatic" : "local")}, roll zone={_autoZone}, travel={_travel.Mode}/{_travel.Status}, " +
                        $"accepted RK={_missions.Records.Count(x => x.Present && x.IsRubiKaDestination)}, " +
                        $"mission={_selected?.Name ?? "none"} [{_selected?.State.ToString() ?? "none"}], dungeon={_dungeon.Status}.");
                    if (_longTravel.IsActive)
                        Say($"Cross-playfield provider={_longTravel.CurrentProvider}; last issue={_longTravel.LastFailure ?? _warp.LastFailure ?? "none"}.");
                    if (_waitingReason != null) Say(_waitingReason);
                    break;
                case "start": Start(); break;
                case "auto": _autoCycle = true; Start(); Say("Automatic mission cycle armed."); break;
                case "local":
                    if (_autoRolling) MaliMissionRoller2.Main.Window?.StopZoneRolling();
                    _autoRolling = _autoCycle = false;
                    _longTravel.Reset();
                    Start(); Say("Local mission takeover armed."); break;
                case "stop": Stop(); Say("Stopped. Use /rkm start for local takeover or /rkm auto for the automatic cycle."); break;
                case "zone":
                    if (args.Length < 2 || !int.TryParse(args[1], out int zone) || zone <= 0)
                        Say("Usage: /rkm zone <Rubi-Ka playfield id> (automatic rolling target).");
                    else { _autoZone = zone; Say($"Automatic rolling zone set to {zone}."); }
                    break;
                case "rolls":
                    if (args.Length > 1 && int.TryParse(args[1], out int count) && count > 0)
                        _maxAutoRolls = count;
                    Say($"Automatic roll limit: {_maxAutoRolls}.");
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
                            $"rooms cleared={record.RoomsCleared}, manual return hand-in pending={record.ReturnHandInPending}, evidence={record.CompletionEvidence ?? "none"}.");
                    if (!_missions.Records.Any()) Say("No accepted Rubi-Ka mission destinations detected.");
                    break;
                case "complete": ConfirmCompletion(args); break;
                case "loot": Say("Use /ManagerLoot for the original item list and settings."); break;
                case "map": _map.ToggleWindow(); break;
                default: Say("Commands: start, auto, local, stop, status, missions, zone <id>, rolls <count>, travel auto|ground|flying, complete [mission id], loot, map."); break;
            }
        }

        private void Start()
        {
            if (_running) return;
            _pendingCheckpointResume = false;
            _running = true;
            _recoveringDeath = false;
            _dungeonStarted = false;
            _clearanceReported = false;
            _selected = null;
            _mapMission = _handoffDoor = Identity.None;
            _verifiedRun = false;
            _travelInvalidated = false;
            _handoffWaitStarted = DateTime.MinValue;
            _observedDungeon = Identity.None;
            _waitingReason = null;
            _nextSelection = DateTime.MinValue;
            _travel.Reset();
            _longTravel.Reset();
            _entranceResolver.Reset();
            Say(_autoCycle ? "Armed for automatic rolling, travel and mission chaining." :
                "Armed for local takeover. Rolling and inter-playfield travel remain under your control.");
        }

        private void Stop(bool preserveCheckpoint = false)
        {
            if (preserveCheckpoint && _running && _checkpoint != null)
            { SaveCheckpoint(); _checkpoint.Save(true, Say); }
            if (!preserveCheckpoint) _pendingCheckpointResume = false;
            if (_autoRolling) MaliMissionRoller2.Main.Window?.StopZoneRolling();
            _autoRolling = false;
            _running = false;
            _dungeonStarted = false;
            _travel?.Reset();
            _longTravel?.Reset();
            _entranceResolver?.Reset();
            _dungeon?.Stop();
            _movement?.StopAll();
            _autoCycle = false;
            _recoveringDeath = false;
            _deathRecovery?.Stop();
            if (!preserveCheckpoint && _checkpoint != null)
            { _checkpoint.Armed = false; _checkpoint.Phase = "Idle"; _checkpoint.Save(true, Say); }
            // The embedded roller is independent: stopping travel must not stop user-owned rolling.
        }

        private void ZoningStarted(object sender, EventArgs args)
        {
            _handoffDoor = _travel.ActiveDoor;
            _mapMission = Identity.None; // Re-upload on the next outdoor selection after zoning.
            _travel.SuspendForZoning(); // Keep managed attempt until exact dungeon verification.
            _longTravel.InvalidatePath();
            _observedDungeon = Identity.None;
            _handoffWaitStarted = DateTime.MinValue;
            _travelInvalidated = true;
        }

        private void ZoningEnded(object sender, EventArgs args)
        {
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

        private void Update(object sender, float elapsed)
        {
            if (Game.IsZoning || DynelManager.LocalPlayer == null || DateTime.UtcNow < _nextTick) return;
            _nextTick = DateTime.UtcNow.AddMilliseconds(_travel.UpdateIntervalMilliseconds);
            try
            {
                _missions.Refresh(); // Track acceptance/removal even while local automation is disarmed.
                if (_pendingCheckpointResume) TryResumeCheckpoint();
                if (!_running) return;
                if (!DynelManager.LocalPlayer.IsAlive)
                {
                    if (!_autoCycle) { Stop(); Say("Stopped because the character died."); return; }
                    if (!_recoveringDeath)
                    {
                        _recoveringDeath = true;
                        _dungeon.Stop(); _dungeonStarted = false;
                        _travel.Reset(); _longTravel.Reset(); _movement.StopAll();
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
                    _verifiedRun = false;
                    _selected = _selected != null && _selected.Present ? _selected : null;
                    _entranceResolver.Reset();
                    _nextSelection = DateTime.MinValue;
                    Say("Reclaim complete; replanning from the current playfield.");
                }
                if (Playfield.IsDungeon) { TickDungeon(); return; }
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
                    if (!_selected.Completed)
                    {
                        Wait("The previous mission has no confirmed reward. Check it and use /rkm complete; /rkm stop then start abandons this run binding.");
                        return;
                    }
                    int previousPlayfield = _selected.PlayfieldId;
                    _selected = null;
                    _verifiedRun = false;
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
                if (_selected == null)
                {
                    if (DateTime.UtcNow < _nextSelection) return;
                    _nextSelection = DateTime.UtcNow.AddSeconds(5);
                    var local = _missions.Eligible(Playfield.ModelIdentity.Instance).ToList();
                    if (local.Count == 0)
                    {
                        if (!_autoCycle)
                        { Wait("Waiting for you to reach a playfield containing an accepted Rubi-Ka mission."); return; }
                        _selected = _missions.Records.Where(x => x.Present && x.IsRubiKaDestination && !x.Completed)
                            .OrderBy(x => x.PlayfieldId).ThenBy(x => x.Id.Instance).FirstOrDefault();
                        if (_selected == null)
                        {
                            if (_autoRolling) return;
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
                if (_autoCycle)
                {
                    Mission live = Mission.List?.FirstOrDefault(x => x.Identity == _selected.Id);
                    if (live == null)
                    { Wait("Waiting for the selected accepted mission to appear in AO# before key entry or travel."); return; }
                    _entranceResolver.Select(live);
                    EntranceResult entrance = _entranceResolver.Tick();
                    if (entrance == EntranceResult.Waiting)
                    { _travel.Reset(); _movement.Halt(MovementOwner.MissionEntrance); return; }
                    if (entrance == EntranceResult.Failed)
                    { Stop(); Say("Mission-key entrance was accepted, but exact dungeon entry was not verified."); return; }
                }
                if (_selected.PlayfieldId != Playfield.ModelIdentity.Instance)
                {
                    _travel.Reset();
                    TravelResult longResult = _longTravel.Tick(_selected.PlayfieldId);
                    if (longResult == TravelResult.Blocked)
                    { string reason = _longTravel.LastFailure; Stop(); Say(reason); }
                    return;
                }
                if (_longTravel.IsActive) _longTravel.Reset();
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

        private void TryResumeCheckpoint()
        {
            if (DateTime.UtcNow < _checkpointResumeAfter) return;
            if (Mission.List == null) return;
            _pendingCheckpointResume = false;
            AcceptedMission record = _missions.Records.FirstOrDefault(x =>
                x.Present && x.Id.Instance == _checkpoint.MissionInstance &&
                (int)x.Id.Type == _checkpoint.MissionType);
            if (record == null || !record.IsRubiKaDestination)
            {
                _checkpoint.Armed = false;
                _checkpoint.Save(true, Say);
                Say("Saved run could not be matched to an accepted Rubi-Ka mission; restart remains disarmed.");
                return;
            }
            _autoCycle = _checkpoint.AutoCycle;
            Start();
            _selected = record;
            if (Playfield.IsDungeon && Playfield.ModelIdentity.Instance == _checkpoint.DungeonInstance &&
                MissionEntranceResolver.VerifyCurrentDungeon(Mission.List.FirstOrDefault(x => x.Identity == record.Id), out _))
            {
                // Entry coordinates are useful only for the same exact live instance.
                record.DungeonEntryRoom = _checkpoint.EntranceRoom;
                record.DungeonEntryPosition = _checkpoint.EntrancePosition;
            }
            Say($"Reconciled saved mission {record.Id.Instance}; planning again from the actual playfield and room.");
        }

        private void SaveCheckpoint()
        {
            if (_checkpoint == null || _pendingCheckpointResume) return;
            _checkpoint.Armed = _running;
            _checkpoint.AutoCycle = _autoCycle;
            _checkpoint.Phase = !_running ? "Idle" : _recoveringDeath ? "Recovery" :
                _autoRolling ? "Rolling" : _selected == null ? "SelectingMission" :
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
                _dungeon.Tick();
                if (_dungeon.IsComplete && !_clearanceReported)
                {
                    _clearanceReported = true;
                    _selected.RoomsCleared = true;
                    if (!_selected.Completed)
                    {
                        _selected.State = MissionProgress.CompletedAutomatically;
                        _selected.CompletionEvidence = _selected.ReturnHandInPending
                            ? "Return objective item collected after dungeon clearance; terminal hand-in/reward remains manual"
                            : _dungeon.Objective.Evidence;
                    }
                    Say(_selected.ReturnHandInPending
                        ? "Return item collected, all rooms cleared and loot processed. Run marked completed; hand-in is manual. Automatically exiting."
                        : "Objective acknowledged, all rooms cleared and loot processed. Automatically returning to the mission exit.");
                }
                return;
            }
            if (!Mission.FindMissionForCurrentDungeon(out Mission bound))
            {
                _observedDungeon = Identity.None;
                if (_handoffWaitStarted == DateTime.MinValue) _handoffWaitStarted = DateTime.UtcNow;
                if (DateTime.UtcNow - _handoffWaitStarted > TimeSpan.FromSeconds(20))
                {
                    Say($"Entrance interaction result: mission={_selected?.Id.Instance}, door={_handoffDoor}, dungeon={Playfield.ModelIdentity}, result=unidentified dungeon after zoning.");
                    _travel.CompleteHandoff(false, "AO# did not identify the selected mission dungeon within 20 seconds");
                    Stop(); Say("AO# did not identify an accepted mission for this dungeon within 20 seconds; handoff stopped."); return;
                }
                Wait("Waiting for AO# to associate this dungeon with an accepted mission. Exploration is held until the identity is verified."); return;
            }
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
            _dungeon.Start(record);
            _dungeonStarted = _dungeon.IsRunning;
            _clearanceReported = false;
            if (_dungeonStarted) Say($"Verified mission {_selected.Id.Instance} in dungeon {Playfield.ModelIdentity.Instance}; existing dungeon logic resumed.");
        }

        private void Wait(string reason)
        {
            if (_waitingReason == reason) return;
            _waitingReason = reason;
            Say(reason);
        }

        private void ObserveAutoRoll(object sender, RollListChangedArgs args)
        {
            if (!_running || !_autoCycle || !_autoRolling) return;
            if (++_autoRollCount < _maxAutoRolls) return;
            MaliMissionRoller2.Main.Window?.StopZoneRolling();
            _autoRolling = false;
            Stop();
            Say($"Automatic rolling stopped after {_maxAutoRolls} offers without an accepted target.");
        }

        private void StartAutoRolling()
        {
            if (!_running || !_autoCycle || _autoRolling || MaliMissionRoller2.Main.Window == null) return;
            if (Inventory.NumFreeSlots < 2)
            { Stop(); Say("Two free main-inventory slots are required before automatic rolling."); return; }
            if (!AcceptedMissions.IsRubiKaPlayfield(_autoZone))
            { Stop(); Say($"Rolling zone {_autoZone} is not a configured Rubi-Ka mission destination."); return; }
            Dynel terminal = DynelManager.AllDynels.Where(x =>
                x.Identity.Type == IdentityType.MissionTerminal &&
                x.DistanceFrom(DynelManager.LocalPlayer) < 7.5f)
                .OrderBy(x => x.DistanceFrom(DynelManager.LocalPlayer)).FirstOrDefault();
            if (terminal == null)
            { Stop(); Say("No mission terminal is in range for automatic rolling."); return; }
            MaliMissionRoller2.Main.Window.UpdateTerminal(new MissionTerminal(terminal));
            _hasRollTerminal = true;
            _rollTerminalPlayfield = Playfield.ModelIdentity.Instance;
            _rollTerminalPosition = terminal.Position;
            _autoRollCount = 0;
            _autoRolling = true;
            _returnStarted = DateTime.MinValue;
            MaliMissionRoller2.Main.Window.StartZoneRolling(_autoZone);
            Say($"Mali Mission Roller is rolling for playfield {_autoZone}; limit {_maxAutoRolls}.");
        }

        private bool ReturnToRollTerminal()
        {
            if (!_hasRollTerminal) return true;
            if (Playfield.ModelIdentity.Instance != _rollTerminalPlayfield)
            {
                TravelResult result = _longTravel.Tick(_rollTerminalPlayfield);
                if (result == TravelResult.Blocked)
                { string reason = _longTravel.LastFailure; Stop(); Say("Return to roller stopped: " + reason); }
                return false;
            }
            if (_longTravel.IsActive) _longTravel.Reset();
            if (_returnStarted == DateTime.MinValue) _returnStarted = DateTime.UtcNow;
            if (DateTime.UtcNow - _returnStarted > TimeSpan.FromMinutes(3))
            { Stop(); Say("Return to the mission terminal timed out."); return false; }
            if (Vector3.Distance(DynelManager.LocalPlayer.Position, _rollTerminalPosition) <= 6f)
            { _movement.Release(MovementOwner.OutdoorTravel); _returnStarted = DateTime.MinValue; return true; }
            if (DateTime.UtcNow >= _nextReturnMove)
            {
                _movement.SetDestination(MovementOwner.OutdoorTravel, _rollTerminalPosition);
                _nextReturnMove = DateTime.UtcNow.AddSeconds(3);
            }
            return false;
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
            int tier = _movement?.ObserveDisplacement() ?? 0;
            if (tier == 0) return;
            Say($"Movement {signal}: recovery tier {tier}, owner={_movement.Owner}.");
            if (Playfield.IsDungeon) _dungeon?.RecoverFromStuck(tier);
            else
            {
                _longTravel?.InvalidatePath();
                if (tier >= 3 && _selected != null && !_travelInvalidated)
                    _travel.Reset("repeated movement displacement; rebuilding local route");
            }
        }
    }
}
