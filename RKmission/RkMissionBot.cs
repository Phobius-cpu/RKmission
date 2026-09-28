using System;
using System.Linq;
using AOSharp.Common.GameData;
using AOSharp.Core;
using AOSharp.Core.UI;
using AOSharp.Pathfinding;
using MalisDungeonMap2;

namespace RKmission
{
    public sealed class RkMissionBot : AOPluginEntry
    {
        private MaliMissionRoller2.Main _roller;
        private DungeonMap _map;
        private ManagerLoot.ManagerLoot _loot;
        private MissionDungeon _dungeon;
        private readonly AcceptedMissions _missions = new AcceptedMissions();
        private LocalMissionTravel _travel;
        private AcceptedMission _selected;
        private bool _running, _dungeonStarted, _clearanceReported, _verifiedRun, _travelInvalidated;
        private Identity _observedDungeon = Identity.None;
        private Identity _activeDungeon = Identity.None;
        private DateTime _dungeonObservedAt, _nextTick, _nextSelection, _handoffWaitStarted;
        private string _waitingReason;

        public override void Run(string pluginDir)
        {
            _roller = new MaliMissionRoller2.Main();
            _roller.Run(System.IO.Path.Combine(pluginDir, "Plugins", "MaliMissionRoller2"));
            _map = new DungeonMap();
            _map.Run(System.IO.Path.Combine(pluginDir, "Plugins", "MalisDungeonMap2"));
            _loot = new ManagerLoot.ManagerLoot();
            _loot.RunEmbedded(System.IO.Path.Combine(pluginDir, "Plugins", "ManagerLoot"));
            _dungeon = new MissionDungeon(Say, _loot);
            _travel = new LocalMissionTravel(Say);
            SMovementController.Set();
            SMovementController.AutoLoadNavmeshes($"{pluginDir}\\NavMeshes", (id, dungeon) => !dungeon);
            Chat.RegisterCommand("rkm", Command);
            Game.OnUpdate += Update;
            Game.TeleportStarted += ZoningStarted;
            Game.TeleportEnded += ZoningEnded;
            Say("Loaded. Accept missions yourself, /rkm start, then travel to any mission playfield. /rkm travel auto|ground|flying; /rkm missions.");
        }

        public override void Teardown()
        {
            Stop();
            Game.OnUpdate -= Update;
            Game.TeleportStarted -= ZoningStarted;
            Game.TeleportEnded -= ZoningEnded;
            _dungeon.Dispose();
            _roller.Teardown();
            _map.Teardown();
            _loot.Teardown();
        }

        private static void Say(string text) => Chat.WriteLine("RKMission: " + text);

        private void Command(string command, string[] args, ChatWindow window)
        {
            string verb = args == null || args.Length == 0 ? "status" : args[0].ToLowerInvariant();
            switch (verb)
            {
                case "status":
                    Say($"Armed={_running}, travel={_travel.Mode}/{_travel.Status}, " +
                        $"accepted RK={_missions.Records.Count(x => x.Present && x.IsRubiKaDestination)}, " +
                        $"mission={_selected?.Name ?? "none"} [{_selected?.State.ToString() ?? "none"}], dungeon={_dungeon.Status}.");
                    if (_waitingReason != null) Say(_waitingReason);
                    break;
                case "start": Start(); break;
                case "stop": Stop(); Say("Stopped. Use /rkm start to arm local takeover again."); break;
                case "travel":
                    if (args.Length < 2 || !Enum.TryParse(args[1], true, out TravelMode mode) ||
                        !Enum.IsDefined(typeof(TravelMode), mode))
                        Say("Usage: /rkm travel auto|ground|flying (default auto).");
                    else
                    {
                        _travel.Mode = mode;
                        if (!Playfield.IsDungeon && !_verifiedRun)
                            _selected = null; // Re-cost every candidate after a mode change.
                        _travel.Reset();
                        _nextSelection = DateTime.MinValue;
                        Say($"Travel mode: {mode}. Flying requires an active flying vehicle; ground requires dismount.");
                    }
                    break;
                case "missions":
                    if (!Game.IsZoning) _missions.Refresh(true);
                    foreach (AcceptedMission record in _missions.Records.OrderBy(x => x.Id.Instance))
                        Say($"{record.Id.Instance}: {record.Name}; playfield={record.PlayfieldId}, entrance={record.Entrance}; " +
                            $"objective={record.Objectives}; state={record.State}, accepted={record.Present}, RK destination={record.IsRubiKaDestination}, " +
                            $"rooms cleared={record.RoomsCleared}, evidence={record.CompletionEvidence ?? "none"}.");
                    if (!_missions.Records.Any()) Say("No accepted Rubi-Ka mission destinations detected.");
                    break;
                case "complete": ConfirmCompletion(args); break;
                case "zone":
                case "rolls":
                    Say("RKMission now monitors all accepted Rubi-Ka missions. Roll/select in Mali's window and travel between playfields yourself.");
                    break;
                case "loot": Say("Use /ManagerLoot for the original item list and settings."); break;
                case "map": _map.ToggleWindow(); break;
                default: Say("Commands: start, stop, status, missions, travel auto|ground|flying, complete [mission id], loot, map."); break;
            }
        }

        private void Start()
        {
            if (_running) return;
            _running = true;
            _dungeonStarted = false;
            _clearanceReported = false;
            _selected = null;
            _verifiedRun = false;
            _travelInvalidated = false;
            _handoffWaitStarted = DateTime.MinValue;
            _observedDungeon = Identity.None;
            _waitingReason = null;
            _nextSelection = DateTime.MinValue;
            _travel.Reset();
            Say("Armed. Rolling, acceptance, and inter-playfield travel remain under your control.");
        }

        private void Stop()
        {
            _running = false;
            _dungeonStarted = false;
            _travel?.Reset();
            _dungeon?.Stop();
            // The embedded roller is independent: stopping travel must not stop user-owned rolling.
        }

        private void ZoningStarted(object sender, EventArgs args)
        {
            _travel.Reset();
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
            _dungeon.Stop();
            _travel.Reset();
            _waitingReason = null;
            Say($"Completion recorded for {_selected.Id.Instance}: {_selected.Name}. " +
                "Exit the mission yourself; while armed, the next accepted mission in the same playfield will take over.");
        }

        private void Update(object sender, float elapsed)
        {
            if (Game.IsZoning || DynelManager.LocalPlayer == null || DateTime.UtcNow < _nextTick) return;
            _nextTick = DateTime.UtcNow.AddMilliseconds(_travel.IsFlightActive ? 100 : 250);
            try
            {
                _missions.Refresh(); // Track acceptance/removal even while local automation is disarmed.
                if (!_running) return;
                if (!DynelManager.LocalPlayer.IsAlive)
                {
                    Stop(); Say("Stopped because the character died."); return;
                }
                if (Playfield.IsDungeon) { TickDungeon(); return; }
                if (_travelInvalidated)
                {
                    _travelInvalidated = false;
                    if (!_verifiedRun) _selected = null;
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
                    if (_selected.State != MissionProgress.CompletedByUser)
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
                        Stop(); Say("Local mission chain finished. Travel to another mission playfield yourself, then /rkm start."); return;
                    }
                }
                if (_selected != null && (!_selected.Present ||
                    _selected.PlayfieldId != Playfield.ModelIdentity.Instance || _travel.NeedsReselection))
                {
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
                        Wait("Waiting for you to reach a playfield containing an accepted Rubi-Ka mission."); return;
                    }
                    _selected = _travel.SelectBest(local);
                    if (_selected == null)
                    {
                        Wait("No usable local route. Supply this playfield's outdoor navmesh, check travel mode, or move to a reachable approach; route evaluation will retry.");
                        return;
                    }
                    _waitingReason = null;
                }
                if (!_travel.Tick(_selected))
                {
                    Stop(); Say("Local travel stopped. Resolve the reported entrance/route problem, then /rkm start.");
                }
            }
            catch (Exception ex)
            {
                Stop(); Say("Stopped after an AO# error: " + ex);
            }
        }

        private void TickDungeon()
        {
            if (_selected?.State == MissionProgress.CompletedByUser)
            {
                Wait("Mission completion confirmed. Exit yourself to resume same-playfield travel."); return;
            }
            if (_dungeonStarted)
            {
                if (Playfield.ModelIdentity != _activeDungeon)
                {
                    Stop(); Say("Dungeon instance changed outside the verified handoff; run stopped."); return;
                }
                if (!_dungeon.IsRunning && !_dungeon.IsComplete) { Stop(); return; }
                _dungeon.UpdateMissionBinding(Mission.List?.FirstOrDefault(x => x.Identity == _selected.Id));
                _dungeon.Tick();
                if (_dungeon.IsComplete && !_clearanceReported)
                {
                    _clearanceReported = true;
                    _selected.RoomsCleared = true;
                    _dungeon.Stop();
                    Wait("All reachable rooms cleared; quest completion is unconfirmed. Check the objective/reward, /rkm complete, then exit.");
                }
                return;
            }
            if (!Mission.FindMissionForCurrentDungeon(out Mission bound))
            {
                _observedDungeon = Identity.None;
                if (_handoffWaitStarted == DateTime.MinValue) _handoffWaitStarted = DateTime.UtcNow;
                if (DateTime.UtcNow - _handoffWaitStarted > TimeSpan.FromSeconds(20))
                {
                    Stop(); Say("AO# did not identify an accepted mission for this dungeon within 20 seconds; handoff stopped."); return;
                }
                Wait("Waiting for AO# to associate this dungeon with an accepted mission. Exploration is held until the identity is verified."); return;
            }
            AcceptedMission record = _missions.Find(bound.Identity);
            if (record == null || !record.IsRubiKaDestination || (_selected != null && _selected.Id != bound.Identity))
            {
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
            if (record.State == MissionProgress.CompletedByUser)
            {
                Wait("This mission was already confirmed complete in this session. Exit to continue local travel."); return;
            }
            _selected.State = MissionProgress.InProgress;
            _selected.HandoffVerified = true;
            _travel.Reset();
            _waitingReason = null;
            _dungeon.Start(bound); // The working dungeon implementation receives this exact accepted mission.
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
    }
}
