using System;
using System.Linq;
using AOSharp.Common.GameData;
using AOSharp.Core;
using AOSharp.Core.UI;
using AOSharp.Pathfinding;
using MaliMissionRoller2;
using MalisDungeonMap2;
using ManagerLoot;

namespace RKmission
{
    public sealed class RkMissionBot : AOPluginEntry
    {
        private Main _roller;
        private DungeonMap _map;
        private ManagerLoot.ManagerLoot _loot;
        private MissionDungeon _dungeon;
        private Mission _selected;
        private bool _running;
        private bool _dungeonStarted;
        private bool _rolling;
        private int _zoneId;
        private int _rolls;
        private int _maxRolls = 100;
        private DateTime _nextTick;
        private DateTime _nextTravel;

        public override void Run(string pluginDir)
        {
            _zoneId = Playfield.ModelIdentity.Instance;
            _roller = new Main();
            _roller.Run(System.IO.Path.Combine(pluginDir, "Plugins", "MaliMissionRoller2"));
            _map = new DungeonMap();
            _map.Run(System.IO.Path.Combine(pluginDir, "Plugins", "MalisDungeonMap2"));
            _loot = new ManagerLoot.ManagerLoot();
            _loot.RunEmbedded(System.IO.Path.Combine(pluginDir, "Plugins", "ManagerLoot"));
            _dungeon = new MissionDungeon(Say, _loot);
            SMovementController.Set();
            SMovementController.AutoLoadNavmeshes($"{pluginDir}\\NavMeshes");
            Chat.RegisterCommand("rkm", Command);
            Game.OnUpdate += Update;
            Mission.RollListChanged += OffersChanged;
            Say("Loaded. Use a mission terminal, configure /mmr and /ManagerLoot, then /rkm zone <id> and /rkm start.");
        }

        public override void Teardown()
        {
            Stop();
            Game.OnUpdate -= Update;
            Mission.RollListChanged -= OffersChanged;
            _dungeon.Dispose();
            _roller.Teardown();
            _map.Teardown();
            _loot.Teardown();
        }

        private static void Say(string text) => Chat.WriteLine("RKMission: " + text);

        private void Command(string command, string[] args, ChatWindow window)
        {
            if (args == null || args.Length == 0 || args[0] == "status")
            {
                Say($"Running={_running}, zone={_zoneId}, rolling={_rolling}, mission={_selected?.DisplayName ?? "none"}, dungeon={_dungeon.Status}.");
                return;
            }
            switch (args[0].ToLowerInvariant())
            {
                case "zone":
                    if (args.Length < 2 || !int.TryParse(args[1], out int zone) || zone <= 0)
                        Say("Usage: /rkm zone <Rubi-Ka playfield id>");
                    else { _zoneId = zone; Say($"Target zone set to {zone}."); }
                    break;
                case "start": Start(); break;
                case "stop": Stop(); Say("Stopped."); break;
                case "rolls":
                    if (args.Length > 1 && int.TryParse(args[1], out int count) && count > 0)
                        _maxRolls = count;
                    Say($"Roll limit: {_maxRolls}.");
                    break;
                case "loot": Say("Use /ManagerLoot for the original item list and settings."); break;
                case "map": _map.ToggleWindow(); break;
                default: Say("Commands: zone <id>, start, stop, status, rolls <count>, loot, map."); break;
            }
        }

        private void Start()
        {
            if (_running) return;
            _running = true;
            _dungeonStarted = false;
            _nextTravel = DateTime.MinValue;
            _selected = ClosestAcceptedMission();
            if (Playfield.IsDungeon)
            {
                _dungeon.Start(_selected);
                _dungeonStarted = _dungeon.IsRunning;
            }
            else if (_selected == null)
                StartRoller();
            Say("Started.");
        }

        private void StartRoller()
        {
            if (_rolling || Main.Window == null) return;
            if (MainWindow.CurrentTerminal == null)
            {
                Dynel terminal = DynelManager.AllDynels
                    .Where(x => x.Identity.Type == IdentityType.MissionTerminal &&
                        x.DistanceFrom(DynelManager.LocalPlayer) < 7.5f)
                    .OrderBy(x => x.DistanceFrom(DynelManager.LocalPlayer)).FirstOrDefault();
                if (terminal != null)
                    Main.Window.UpdateTerminal(new MissionTerminal(terminal));
            }
            if (MainWindow.CurrentTerminal == null)
            {
                Stop();
                Say("Stand by a mission terminal and use it before starting.");
                return;
            }
            _rolls = 0;
            _rolling = true;
            Main.Window.StartZoneRolling(_zoneId);
            Say($"Mali's Mission Roller is rolling for zone {_zoneId}.");
        }

        private void OffersChanged(object sender, RollListChangedArgs offers)
        {
            if (!_running || !_rolling || Main.Window?.AutoZoneId == 0) return;
            if (++_rolls < _maxRolls) return;
            Main.Window.StopZoneRolling();
            _rolling = false;
            Stop();
            Say($"No mission in zone {_zoneId} after {_maxRolls} rolls.");
        }

        private void Stop()
        {
            _running = false;
            _rolling = false;
            _dungeonStarted = false;
            Main.Window?.StopZoneRolling();
            _dungeon?.Stop();
            SMovementController.Halt();
        }

        private Mission ClosestAcceptedMission()
        {
            if (DynelManager.LocalPlayer == null) return null;
            Vector3 origin = DynelManager.LocalPlayer.Position;
            return Mission.List?.Where(x => x.Location != null && x.Location.Playfield.Instance == _zoneId)
                .OrderBy(x => Vector3.Distance(x.Location.Pos, origin)).FirstOrDefault();
        }

        private void Update(object sender, float elapsed)
        {
            if (!_running || Game.IsZoning || DynelManager.LocalPlayer == null || DateTime.UtcNow < _nextTick)
                return;
            _nextTick = DateTime.UtcNow.AddMilliseconds(250);
            try
            {
                if (!DynelManager.LocalPlayer.IsAlive)
                {
                    Stop(); Say("Stopped because the character died."); return;
                }
                if (Playfield.IsDungeon)
                {
                    Main.Window?.StopZoneRolling();
                    _rolling = false;
                    if (!_dungeonStarted)
                    {
                        _dungeon.Start(_selected);
                        _dungeonStarted = _dungeon.IsRunning;
                    }
                    if (!_dungeonStarted) return;
                    if (!_dungeon.IsRunning && !_dungeon.IsComplete) { Stop(); return; }
                    _dungeon.Tick();
                    if (_dungeon.IsComplete)
                    {
                        Stop();
                        Say("All reachable rooms have been cleared. Check the mission objective and exit.");
                    }
                    return;
                }
                if (_dungeon.IsRunning) _dungeon.Stop();
                _dungeonStarted = false;
                if (_selected == null) _selected = ClosestAcceptedMission();
                if (_selected == null) { if (!_rolling) StartRoller(); return; }
                Main.Window?.StopZoneRolling();
                _rolling = false;
                TravelToMission(_selected);
            }
            catch (Exception ex)
            {
                Stop(); Say("Stopped after an AO# error: " + ex);
            }
        }

        private void TravelToMission(Mission mission)
        {
            MissionLocation location = mission.Location;
            if (location == null) return;
            if (Playfield.ModelIdentity.Instance != location.Playfield.Instance)
            {
                SMovementController.Halt();
                if (DateTime.UtcNow >= _nextTravel)
                {
                    Say($"Travel to playfield {location.Playfield.Instance}; mission is at {location.Pos}. Navigation resumes there.");
                    _nextTravel = DateTime.UtcNow.AddSeconds(30);
                }
                return;
            }
            if (Vector3.Distance(DynelManager.LocalPlayer.Position, location.Pos) > 4f)
            {
                if (DateTime.UtcNow >= _nextTravel || !SMovementController.IsNavigating())
                {
                    SMovementController.SetNavDestination(location.Pos);
                    _nextTravel = DateTime.UtcNow.AddSeconds(3);
                }
                return;
            }
            SMovementController.Halt();
            Door entrance = Playfield.Doors.Where(x => Vector3.Distance(x.Position, location.Pos) < 8f)
                .OrderBy(x => Vector3.Distance(x.Position, location.Pos)).FirstOrDefault();
            if (entrance != null && DateTime.UtcNow >= _nextTravel)
            {
                entrance.Use(); _nextTravel = DateTime.UtcNow.AddSeconds(3);
            }
            else if (entrance == null && DateTime.UtcNow >= _nextTravel)
            {
                Say("At the mission coordinates, but no entrance door is visible.");
                _nextTravel = DateTime.UtcNow.AddSeconds(10);
            }
        }
    }
}
