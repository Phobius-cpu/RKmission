using System;
using System.Linq;
using AOSharp.Common.GameData;
using AOSharp.Core;
using AOSharp.Pathfinding;

namespace RKmission
{
    // Explicit, transaction-free route test. Each playfield crossing must be
    // observed at the recorded arrival point before the next stage begins.
    internal sealed class LogisticsRouteNavigator : IDisposable
    {
        private readonly LogisticsRouteCatalog _catalog;
        private readonly MovementArbiter _movement;
        private readonly Action<string> _say;
        private LogisticsRouteCatalog.Route _route;
        private int _stageIndex, _pointIndex;
        private bool _awaitingZone, _atTarget;
        private DateTime _nextTick, _lastSubmit, _lastProgress, _zoneDeadline;
        private float _bestDistance;

        public bool IsActive => _route != null;
        public string Status => _route == null ? "inactive" :
            $"{_route.Site}/{_route.Purpose}: " + (_atTarget ? "at surveyed target; use /rkm logistics return" :
                $"{_route.Stages[_stageIndex].Name} point {_pointIndex + 1}/{_route.Stages[_stageIndex].Points.Count}" +
                (_awaitingZone ? "; waiting for observed playfield crossing" : ""));

        public LogisticsRouteNavigator(LogisticsRouteCatalog catalog, MovementArbiter movement, Action<string> say)
        {
            _catalog = catalog;
            _movement = movement;
            _say = say;
            Game.OnUpdate += OnUpdate;
        }

        public void Start(string site, string purpose)
        {
            if (IsActive) { _say("A logistics route test is already active; use /rkm logistics stop first."); return; }
            LogisticsRouteCatalog.Route route = _catalog.Find(site, purpose);
            if (route == null) { _say($"No surveyed logistics route for {site}/{purpose}; use /rkm logistics routes."); return; }
            var player = DynelManager.LocalPlayer;
            if (Game.IsZoning || player == null || Playfield.ModelIdentity.Instance != route.OriginPlayfield ||
                player.MovementState == MovementState.Fly ||
                Vector3.Distance(player.Position, V(route.OriginPosition)) > 6f)
            { _say("Start the route test on foot beside its recorded mission terminal in the correct playfield."); return; }
            if (_movement.Owner != MovementOwner.None)
            { _say("Another RKMission movement controller is active; stop it before testing a logistics route."); return; }
            if (!DynelManager.AllDynels.Any(x => x.Identity.Type == IdentityType.MissionTerminal &&
                string.Equals(x.Identity.ToString(), route.OriginTerminalIdentity, StringComparison.OrdinalIgnoreCase) &&
                Vector3.Distance(x.Position, V(route.OriginPosition)) <= 15f))
            { _say($"Route test held: recorded origin terminal {route.OriginTerminalIdentity} is not visible at this site."); return; }
            _route = route;
            SetStage(0);
            _say($"Testing recorded {route.Site}/{route.Purpose} route from PF {route.OriginPlayfield}. " +
                "Transactions remain manual; each zone arrival will be checked before movement continues.");
            if (route.Stages.Count == 1) ArrivedAtTarget();
        }

        public void Return()
        {
            if (_route == null || !_atTarget)
            { _say("Reach a surveyed logistics target with /rkm logistics travel <site> <bank|shop> first."); return; }
            if (Game.IsZoning || DynelManager.LocalPlayer == null ||
                Playfield.ModelIdentity.Instance != _route.DestinationPlayfield ||
                Vector3.Distance(DynelManager.LocalPlayer.Position, V(_route.DestinationPosition)) > 8f)
            { _say("Return held: stand beside the same surveyed bank/shop target in its expected playfield."); return; }
            if (_route.Stages.Count == 1)
            { Finish("Already beside the mission terminal; no return route was needed."); return; }
            _atTarget = false;
            SetStage(2);
            _say($"Testing recorded return route for {_route.Site}/{_route.Purpose}; expected origin PF {_route.OriginPlayfield}.");
        }

        public void Stop(bool silent = false)
        {
            if (_route == null) return;
            _movement.Release(MovementOwner.LogisticsTravel);
            _route = null;
            _atTarget = _awaitingZone = false;
            if (!silent) _say("Logistics route test stopped.");
        }

        private void OnUpdate(object sender, float elapsed)
        {
            if (_route == null || _atTarget || Game.IsZoning || DynelManager.LocalPlayer == null ||
                DateTime.UtcNow < _nextTick) return;
            _nextTick = DateTime.UtcNow.AddMilliseconds(150);
            try { Tick(); }
            catch (Exception ex) { Fail("route test error: " + ex); }
        }

        private void Tick()
        {
            LogisticsRouteCatalog.Stage stage = _route.Stages[_stageIndex];
            int playfield = Playfield.ModelIdentity.Instance;
            Vector3 player = DynelManager.LocalPlayer.Position;
            if (playfield != stage.Playfield)
            {
                if (stage.ExpectedNextPlayfield == playfield && _stageIndex + 1 < _route.Stages.Count &&
                    _route.Stages[_stageIndex + 1].Playfield == playfield &&
                    Vector3.Distance(player, V(_route.Stages[_stageIndex + 1].Points[0])) <= 12f)
                {
                    _movement.Release(MovementOwner.LogisticsTravel);
                    SetStage(_stageIndex + 1);
                    _say($"Verified logistics zone: PF {stage.Playfield} -> PF {playfield}; continuing {_route.Stages[_stageIndex].Name}.");
                    return;
                }
                Fail($"unexpected zone/arrival: PF {stage.Playfield} -> PF {playfield}; no further route movement sent");
                return;
            }
            if (_awaitingZone)
            {
                if (DateTime.UtcNow > _zoneDeadline)
                    Fail($"no observed crossing from PF {stage.Playfield} to PF {stage.ExpectedNextPlayfield} at the recorded doorway");
                return;
            }
            while (_pointIndex < stage.Points.Count - 1 &&
                Vector3.Distance(player, V(stage.Points[_pointIndex])) <= 1.15f)
                NextPoint();
            Vector3 target = V(stage.Points[_pointIndex]);
            float distance = Vector3.Distance(player, target);
            bool last = _pointIndex == stage.Points.Count - 1;
            float tolerance = last && stage.Name == "ToTarget" ? 2.5f :
                last && stage.Name == "ToOrigin" ? 5f : last ? 1.4f : 1.15f;
            if (distance <= tolerance)
            {
                if (last) CompleteStage(stage);
                else NextPoint();
                return;
            }
            if (distance + 0.2f < _bestDistance)
            { _bestDistance = distance; _lastProgress = DateTime.UtcNow; }
            if (DateTime.UtcNow - _lastProgress > TimeSpan.FromSeconds(8))
            { Fail($"no progress toward recorded {_route.Site}/{_route.Purpose} waypoint {_pointIndex + 1}"); return; }
            TimeSpan resubmitAfter = SMovementController.IsNavigating() ?
                TimeSpan.FromSeconds(2) : TimeSpan.FromSeconds(1);
            if (_movement.Owner != MovementOwner.LogisticsTravel ||
                DateTime.UtcNow - _lastSubmit > resubmitAfter)
            {
                if (!_movement.SetDestination(MovementOwner.LogisticsTravel, target))
                { Fail("AO# declined a recorded logistics waypoint"); return; }
                _lastSubmit = DateTime.UtcNow;
            }
        }

        private void CompleteStage(LogisticsRouteCatalog.Stage stage)
        {
            _movement.Release(MovementOwner.LogisticsTravel);
            if (stage.ExpectedNextPlayfield > 0)
            {
                _awaitingZone = true;
                _zoneDeadline = DateTime.UtcNow.AddSeconds(30);
                _say($"At the surveyed doorway in PF {stage.Playfield}; waiting for PF {stage.ExpectedNextPlayfield}. " +
                    "If the doorway does not trigger automatically, cross it manually before the route test times out.");
            }
            else if (stage.Name == "ToTarget") ArrivedAtTarget();
            else if (stage.Name == "ToOrigin") Finish("Returned to the surveyed mission terminal area; route test complete.");
        }

        private void ArrivedAtTarget()
        {
            _atTarget = true;
            _movement.Release(MovementOwner.LogisticsTravel);
            Vector3 target = V(_route.DestinationPosition);
            string liveTarget;
            if (_route.Purpose == "bank")
            {
                SimpleItem terminal = DynelManager.Terminals.FirstOrDefault(x =>
                    x.Name?.IndexOf("Banking Service Terminal", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    Vector3.Distance(x.Position, target) <= 8f);
                liveTarget = terminal == null ? "live bank terminal not visible at the surveyed position" :
                    $"live bank terminal {terminal.Identity} visible at the surveyed position";
            }
            else
            {
                Dynel actor = DynelManager.AllDynels.FirstOrDefault(x =>
                    string.Equals(x.Identity.ToString(), _route.DestinationIdentity,
                        StringComparison.OrdinalIgnoreCase) && Vector3.Distance(x.Position, target) <= 8f);
                liveTarget = actor == null ? "shop actor identity not verified by the live dynel list" :
                    $"surveyed shop actor {actor.Identity} visible";
            }
            _say($"Reached the surveyed {_route.Site}/{_route.Purpose} position in PF {_route.DestinationPlayfield}. " +
                $"{liveTarget}. Use the bank/shop manually, then /rkm logistics return; " +
                "no item was moved by the route test.");
        }

        private void SetStage(int index)
        {
            _stageIndex = index;
            _pointIndex = 0;
            _awaitingZone = false;
            _nextTick = _lastSubmit = DateTime.MinValue;
            _lastProgress = DateTime.UtcNow;
            _bestDistance = float.MaxValue;
        }

        private void NextPoint()
        {
            _pointIndex++;
            _lastProgress = DateTime.UtcNow;
            _lastSubmit = DateTime.MinValue;
            _bestDistance = float.MaxValue;
        }

        private void Finish(string message) { Stop(true); _say(message); }
        private void Fail(string reason) { Stop(true); _say("Logistics route test stopped: " + reason + "."); }
        private static Vector3 V(float[] point) => new Vector3(point[0], point[1], point[2]);
        public void Dispose() { Game.OnUpdate -= OnUpdate; Stop(true); }
    }
}
