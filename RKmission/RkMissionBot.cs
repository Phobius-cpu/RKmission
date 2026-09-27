using System;
using System.Linq;
using AOSharp.Common.GameData;
using AOSharp.Core;
using AOSharp.Core.UI;
using AOSharp.Pathfinding;
using SmokeLounge.AOtomation.Messaging.Messages;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;

namespace RKmission
{
    /// <summary>AO# entry point. /rkm zone &lt;playfield id&gt; and /rkm start begin a run.</summary>
    public sealed class RkMissionBot : AOPluginEntry
    {
        private MissionRoller _roller;
        private MissionDungeon _dungeon;
        private bool _running;
        private DateTime _nextTick;
        private DateTime _nextTravel;
        private int _zoneId;
        private bool _dungeonStarted;

        public override void Run(string pluginDir)
        {
            _zoneId = Playfield.ModelIdentity.Instance;
            _roller = new MissionRoller(Say) { ZoneId = _zoneId };
            _dungeon = new MissionDungeon(Say);
            SMovementController.Set();
            SMovementController.AutoLoadNavmeshes($"{pluginDir}\\NavMeshes");
            Chat.RegisterCommand("rkm", Command);
            Game.OnUpdate += Update;
            Mission.RollListChanged += OffersChanged;
            Network.N3MessageReceived += MessageReceived;
            Say("Loaded. Use a mission terminal, then /rkm zone <id> and /rkm start.");
        }

        public override void Teardown()
        {
            Stop();
            Game.OnUpdate -= Update;
            Mission.RollListChanged -= OffersChanged;
            Network.N3MessageReceived -= MessageReceived;
            _dungeon.Dispose();
        }

        private static void Say(string text) => Chat.WriteLine("RKMission: " + text);

        private void Command(string command, string[] args, ChatWindow window)
        {
            if (args == null || args.Length == 0)
            {
                Status();
                return;
            }

            switch (args[0].ToLowerInvariant())
            {
                case "zone":
                    if (args.Length < 2 || !int.TryParse(args[1], out int zone) || zone <= 0)
                    {
                        Say("Usage: /rkm zone <Rubi-Ka playfield id>");
                        return;
                    }
                    _zoneId = zone;
                    _roller.ZoneId = zone;
                    Say($"Target zone set to {zone}.");
                    break;
                case "start":
                    Start();
                    break;
                case "stop":
                    Stop();
                    Say("Stopped.");
                    break;
                case "status":
                    Status();
                    break;
                case "rolls":
                    if (args.Length > 1 && int.TryParse(args[1], out int count) && count > 0)
                    {
                        _roller.MaxRolls = count;
                        Say($"Roll limit set to {count}.");
                    }
                    break;
                case "difficulty":
                    if (args.Length > 1 && byte.TryParse(args[1], out byte difficulty))
                    {
                        _roller.Difficulty = difficulty;
                        Say($"Difficulty slider set to {difficulty}.");
                    }
                    break;
                default:
                    Say("Commands: zone <id>, start, stop, status, rolls <count>, difficulty <0-255>.");
                    break;
            }
        }

        private void Start()
        {
            if (_running)
                return;

            _running = true;
            _dungeonStarted = false;
            _nextTravel = DateTime.MinValue;
            _roller.ZoneId = _zoneId;
            _roller.SelectAcceptedMission();
            if (_roller.Selected == null && !Playfield.IsDungeon)
                _roller.Start();
            if (Playfield.IsDungeon)
            {
                _dungeon.Start(_roller.Selected);
                _dungeonStarted = _dungeon.IsRunning;
            }
            Say("Started.");
        }

        private void Stop()
        {
            _running = false;
            _dungeonStarted = false;
            _roller?.Stop();
            _dungeon?.Stop();
            SMovementController.Halt();
        }

        private void Status()
        {
            Say($"Running={_running}, zone={_zoneId}, rolling={_roller.IsRolling}, " +
                $"mission={_roller.Selected?.DisplayName ?? "none"}, dungeon={_dungeon.Status}.");
        }

        private void OffersChanged(object sender, RollListChangedArgs offers)
        {
            if (_running)
                _roller.OnOffers(offers.MissionDetails);
        }

        private void MessageReceived(object sender, N3Message message)
        {
            if (message is GenericCmdMessage use &&
                use.Action == GenericCmdAction.Use &&
                use.Target.Type == IdentityType.MissionTerminal &&
                DynelManager.LocalPlayer != null &&
                message.Identity == DynelManager.LocalPlayer.Identity)
            {
                _roller.RememberTerminal(DynelManager.GetDynel(use.Target));
            }
        }

        private void Update(object sender, float elapsed)
        {
            if (!_running || Game.IsZoning || DynelManager.LocalPlayer == null ||
                DateTime.UtcNow < _nextTick)
                return;

            _nextTick = DateTime.UtcNow.AddMilliseconds(250);
            try
            {
                if (!DynelManager.LocalPlayer.IsAlive)
                {
                    Stop();
                    Say("Stopped because the character died.");
                    return;
                }

                if (Playfield.IsDungeon)
                {
                    _roller.Stop();
                    if (!_dungeonStarted)
                    {
                        _dungeon.Start(_roller.Selected);
                        _dungeonStarted = _dungeon.IsRunning;
                        if (!_dungeonStarted)
                            return;
                    }
                    if (!_dungeon.IsRunning && !_dungeon.IsComplete)
                    {
                        Stop();
                        return;
                    }
                    _dungeon.Tick();
                    if (_dungeon.IsComplete)
                    {
                        Stop();
                        Say("All reachable rooms have been cleared. Check the mission objective and exit.");
                    }
                    return;
                }

                if (_dungeon.IsRunning)
                    _dungeon.Stop();
                _dungeonStarted = false;

                if (_roller.Selected == null && !_roller.IsRolling &&
                    !_roller.HasPendingAcceptance)
                    _roller.SelectAcceptedMission();

                if (_roller.Selected == null)
                {
                    _roller.Tick();
                    return;
                }

                TravelToMission(_roller.Selected);
            }
            catch (Exception ex)
            {
                Stop();
                Say("Stopped after an AO# error: " + ex);
            }
        }

        private void TravelToMission(Mission mission)
        {
            MissionLocation location = mission.Location;
            if (location == null)
                return;

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

            float distance = Vector3.Distance(DynelManager.LocalPlayer.Position, location.Pos);
            if (distance > 4f)
            {
                if (DateTime.UtcNow >= _nextTravel || !SMovementController.IsNavigating())
                {
                    SMovementController.SetNavDestination(location.Pos);
                    _nextTravel = DateTime.UtcNow.AddSeconds(3);
                }
                return;
            }

            SMovementController.Halt();
            Door entrance = Playfield.Doors
                .Where(x => Vector3.Distance(x.Position, location.Pos) < 8f)
                .OrderBy(x => Vector3.Distance(x.Position, location.Pos))
                .FirstOrDefault();
            if (entrance != null && DateTime.UtcNow >= _nextTravel)
            {
                entrance.Use();
                _nextTravel = DateTime.UtcNow.AddSeconds(3);
            }
            else if (entrance == null && DateTime.UtcNow >= _nextTravel)
            {
                Say("At the mission coordinates, but no entrance door is visible.");
                _nextTravel = DateTime.UtcNow.AddSeconds(10);
            }
        }
    }
}
