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
        private const float MaximumSmoothedLeg = 18f;
        private const float RecordedCorridorTolerance = 1f;
        private const float RecordedHeightTolerance = 1.25f;
        private const float BankArrivalTolerance = 1.25f;
        private const float ShopArrivalTolerance = 2.5f;
        private const float OriginArrivalTolerance = 2f;
        private readonly LogisticsRouteCatalog _catalog;
        private readonly MovementArbiter _movement;
        private readonly Action<string> _say;
        private LogisticsRouteCatalog.Route _route;
        private int _stageIndex, _pointIndex;
        private bool _awaitingZone, _atTarget, _crossingSubmitted, _crossingStopped;
        private Vector3 _crossingTarget;
        private DateTime _nextTick, _lastSubmit, _lastProgress, _zoneDeadline, _crossingStarted;
        private float _bestDistance;

        public bool IsActive => _route != null;
        public SimpleItem FindVerifiedBankTerminal(string requiredIdentity = null)
        {
            if (Game.IsZoning || DynelManager.LocalPlayer == null) return null;
            // An active route owns its destination. A direct bank visit can use
            // the same survey only when no route test is running.
            var routes = _route == null ? _catalog.Routes.Where(x => x.Purpose == "bank") :
                _atTarget && _route.Purpose == "bank" ? new[] { _route } :
                Enumerable.Empty<LogisticsRouteCatalog.Route>();
            Vector3 player = DynelManager.LocalPlayer.Position;
            var matches = routes.Where(route =>
                    Playfield.ModelIdentity.Instance == route.DestinationPlayfield &&
                    Vector3.Distance(player, V(route.DestinationPosition)) <= 8f)
                .Select(route => DynelManager.Terminals.FirstOrDefault(terminal =>
                    string.Equals(terminal.Identity.ToString(), route.DestinationIdentity,
                        StringComparison.OrdinalIgnoreCase) &&
                    (requiredIdentity == null || string.Equals(terminal.Identity.ToString(), requiredIdentity,
                        StringComparison.OrdinalIgnoreCase)) &&
                    terminal.Name?.IndexOf("Banking Service Terminal", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    Vector3.Distance(terminal.Position, V(route.DestinationPosition)) <= 8f &&
                    Vector3.Distance(terminal.Position, player) <= 8f))
                .Where(terminal => terminal != null).ToList();
            return matches.Count == 1 ? matches[0] : null;
        }
        public Dynel FindVerifiedShopActor(string requiredIdentity = null)
        {
            if (Game.IsZoning || DynelManager.LocalPlayer == null) return null;
            var routes = _route == null ? _catalog.Routes.Where(x => x.Purpose == "shop") :
                _atTarget && _route.Purpose == "shop" ? new[] { _route } :
                Enumerable.Empty<LogisticsRouteCatalog.Route>();
            Vector3 player = DynelManager.LocalPlayer.Position;
            var matches = routes.Where(route =>
                    Playfield.ModelIdentity.Instance == route.DestinationPlayfield &&
                    Vector3.Distance(player, V(route.DestinationPosition)) <= 8f)
                .Select(route => DynelManager.AllDynels.FirstOrDefault(actor =>
                    actor.Identity.Type == IdentityType.VendingMachine &&
                    string.Equals(actor.Identity.ToString(), route.DestinationIdentity,
                        StringComparison.OrdinalIgnoreCase) &&
                    (requiredIdentity == null || string.Equals(actor.Identity.ToString(), requiredIdentity,
                        StringComparison.OrdinalIgnoreCase)) &&
                    Vector3.Distance(actor.Position, V(route.DestinationPosition)) <= 8f &&
                    Vector3.Distance(actor.Position, player) <= 8f))
                .Where(actor => actor != null).ToList();
            return matches.Count == 1 ? matches[0] : null;
        }
        public string BankTargetDiagnostic()
        {
            if (Game.IsZoning || DynelManager.LocalPlayer == null) return "player is zoning or unavailable";
            int playfield = Playfield.ModelIdentity.Instance;
            Vector3 player = DynelManager.LocalPlayer.Position;
            var sites = _catalog.Routes.Where(x => x.Purpose == "bank" &&
                    x.DestinationPlayfield == playfield)
                .OrderBy(x => Vector3.Distance(player, V(x.DestinationPosition)))
                .Take(3).Select(route =>
                    $"{route.Site} {Vector3.Distance(player, V(route.DestinationPosition)):F1} m " +
                    $"terminal {route.DestinationIdentity} visible=" +
                    DynelManager.Terminals.Any(x => string.Equals(x.Identity.ToString(),
                        route.DestinationIdentity, StringComparison.OrdinalIgnoreCase))).ToArray();
            return sites.Length == 0 ? $"no surveyed bank destination for PF {playfield}" :
                $"PF {playfield}; " + string.Join("; ", sites) + $"; route={Status}";
        }
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
                else TickDoorwayCrossing(stage, player);
                return;
            }
            while (_pointIndex < stage.Points.Count - 1 &&
                Vector3.Distance(player, V(stage.Points[_pointIndex])) <= 1.15f)
                NextPoint();
            if (_lastSubmit == DateTime.MinValue)
                SelectFarthestSafePoint(stage, player);
            Vector3 target = V(stage.Points[_pointIndex]);
            float distance = Vector3.Distance(player, target);
            bool last = _pointIndex == stage.Points.Count - 1;
            float tolerance = last && stage.Name == "ToTarget" ?
                    (_route.Purpose == "bank" ? BankArrivalTolerance : ShopArrivalTolerance) :
                last && stage.Name == "ToOrigin" ? OriginArrivalTolerance :
                last ? 1.4f : 1.15f;
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
            if (_movement.Owner != MovementOwner.LogisticsTravel ||
                _lastSubmit == DateTime.MinValue ||
                (!SMovementController.IsNavigating() &&
                    DateTime.UtcNow - _lastSubmit > TimeSpan.FromSeconds(1)))
            {
                if (!_movement.SetDestination(MovementOwner.LogisticsTravel, target))
                { Fail("AO# declined a recorded logistics waypoint"); return; }
                _lastSubmit = DateTime.UtcNow;
            }
        }

        private void SelectFarthestSafePoint(LogisticsRouteCatalog.Stage stage, Vector3 player)
        {
            if (_pointIndex >= stage.Points.Count - 1) return;
            int anchor = Math.Max(0, _pointIndex - 1);
            for (int candidate = stage.Points.Count - 1; candidate > _pointIndex; candidate--)
            {
                if (!RecordedShortcutSafe(stage, anchor, candidate) ||
                    !LiveShortcutClear(player, V(stage.Points[candidate]))) continue;
                _pointIndex = candidate;
                _lastProgress = DateTime.UtcNow;
                _bestDistance = float.MaxValue;
                return;
            }
        }

        // Recorded samples remain the route authority. A shortcut is allowed
        // only along a nearly straight section that stays inside their narrow
        // horizontal/vertical corridor and has a bounded length.
        private static bool RecordedShortcutSafe(LogisticsRouteCatalog.Stage stage,
            int anchor, int candidate)
        {
            Vector3 start = V(stage.Points[anchor]);
            Vector3 end = V(stage.Points[candidate]);
            if (Vector3.Distance(start, end) > MaximumSmoothedLeg) return false;
            Vector3 horizontal = end - start;
            horizontal.Y = 0;
            float squared = horizontal.X * horizontal.X + horizontal.Z * horizontal.Z;
            if (squared < 0.01f) return false;
            for (int i = anchor + 1; i < candidate; i++)
            {
                Vector3 point = V(stage.Points[i]);
                float projection = ((point.X - start.X) * horizontal.X +
                    (point.Z - start.Z) * horizontal.Z) / squared;
                projection = Math.Max(0, Math.Min(1, projection));
                Vector3 onSegment = new Vector3(start.X + horizontal.X * projection,
                    start.Y + (end.Y - start.Y) * projection,
                    start.Z + horizontal.Z * projection);
                Vector3 offset = point - onSegment;
                float horizontalOffset = (float)Math.Sqrt(offset.X * offset.X + offset.Z * offset.Z);
                if (horizontalOffset > RecordedCorridorTolerance ||
                    Math.Abs(offset.Y) > RecordedHeightTolerance) return false;
            }
            return true;
        }

        private static bool LiveShortcutClear(Vector3 start, Vector3 end)
        {
            if (Vector3.Distance(start, end) <= 2f) return true;
            foreach (float height in new[] { 0.75f, 1.4f })
            {
                Vector3 raisedStart = start + Vector3.Up * height;
                Vector3 raisedEnd = end + Vector3.Up * height;
                try
                {
                    if (Playfield.Raycast(raisedStart, raisedEnd, out Vector3 hit, out _) &&
                        Vector3.Distance(hit, raisedEnd) > 1f) return false;
                }
                catch { } // Recorded-corridor evidence remains available if scene rays are unavailable.
            }
            return true;
        }

        private void CompleteStage(LogisticsRouteCatalog.Stage stage)
        {
            _movement.Release(MovementOwner.LogisticsTravel);
            if (stage.ExpectedNextPlayfield > 0)
            {
                _awaitingZone = true;
                _zoneDeadline = DateTime.UtcNow.AddSeconds(30);
                _crossingSubmitted = _crossingStopped = false;
                _crossingStarted = DateTime.MinValue;
                _say($"At the surveyed doorway in PF {stage.Playfield}; trying a bounded forward crossing, " +
                    $"then waiting for PF {stage.ExpectedNextPlayfield}.");
            }
            else if (stage.Name == "ToTarget") ArrivedAtTarget();
            else if (stage.Name == "ToOrigin") Finish("Returned to the surveyed mission terminal area; route test complete.");
        }

        private void TickDoorwayCrossing(LogisticsRouteCatalog.Stage stage, Vector3 player)
        {
            if (_crossingStopped) return;
            if (!_crossingSubmitted)
            {
                Vector3 last = V(stage.Points[stage.Points.Count - 1]);
                Vector3 previous = V(stage.Points[stage.Points.Count - 2]);
                Vector3 direction = last - previous;
                direction.Y = 0;
                if (Vector3.Distance(direction, Vector3.Zero) < 0.15f ||
                    Vector3.Distance(player, last) > 2.5f ||
                    ReversesNearDoorway(stage))
                {
                    _crossingStopped = true;
                    _say("Automatic doorway crossing held: recorded approach direction or position is uncertain. " +
                        "Cross manually within the route test timeout.");
                    return;
                }
                direction = direction.Normalize();
                _crossingTarget = last + direction * 2f;
                _crossingTarget.Y = last.Y;
                if (Vector3.Distance(player, _crossingTarget) > 4.5f ||
                    !_movement.SetDestination(MovementOwner.LogisticsTravel, _crossingTarget))
                {
                    _crossingStopped = true;
                    _say("Automatic doorway crossing was declined. Cross manually within the route test timeout.");
                    return;
                }
                _crossingSubmitted = true;
                _crossingStarted = _lastSubmit = DateTime.UtcNow;
                _say($"Bounded doorway crossing target ({LocalRoutePlanner.Coordinates(_crossingTarget)}); " +
                    "zone arrival remains unverified until the playfield changes.");
                return;
            }
            if (DateTime.UtcNow - _crossingStarted > TimeSpan.FromSeconds(6))
            {
                _movement.Release(MovementOwner.LogisticsTravel);
                _crossingStopped = true;
                _say("Bounded crossing did not trigger zoning; cross the doorway manually within the route test timeout.");
                return;
            }
            if (!SMovementController.IsNavigating() &&
                DateTime.UtcNow - _lastSubmit > TimeSpan.FromSeconds(1))
            {
                _movement.SetDestination(MovementOwner.LogisticsTravel, _crossingTarget);
                _lastSubmit = DateTime.UtcNow;
            }
        }

        private static bool ReversesNearDoorway(LogisticsRouteCatalog.Stage stage)
        {
            // Some surveys include a turn back toward the room after reaching the threshold.
            // Extending that final movement would send the character away from the zone.
            for (int i = stage.Points.Count - 2; i >= Math.Max(1, stage.Points.Count - 3); i--)
            {
                Vector3 current = V(stage.Points[i + 1]) - V(stage.Points[i]);
                Vector3 prior = V(stage.Points[i]) - V(stage.Points[i - 1]);
                current.Y = prior.Y = 0;
                float currentLength = Vector3.Distance(current, Vector3.Zero);
                float priorLength = Vector3.Distance(prior, Vector3.Zero);
                if (currentLength > 0.15f && priorLength > 0.15f &&
                    current.X * prior.X + current.Z * prior.Z < -0.25f * currentLength * priorLength)
                    return true;
            }
            return false;
        }

        private void ArrivedAtTarget()
        {
            _atTarget = true;
            _movement.Release(MovementOwner.LogisticsTravel);
            Vector3 target = V(_route.DestinationPosition);
            string liveTarget;
            if (_route.Purpose == "bank")
            {
                SimpleItem terminal = FindVerifiedBankTerminal();
                liveTarget = terminal == null ? "live bank terminal not visible at the surveyed position" :
                    $"live bank terminal {terminal.Identity} visible at the surveyed position";
            }
            else
            {
                Dynel actor = FindVerifiedShopActor();
                liveTarget = actor == null ? "shop actor identity not verified by the live dynel list" :
                    $"surveyed shop actor {actor.Identity} visible";
            }
            string action = _route.Purpose == "bank"
                ? "Use /rkm logistics bank test or bank store [1-20] for verified Keep-item transfers, " +
                  "or use the bank manually; then /rkm logistics return"
                : "Use /rkm logistics shop test to sell one verified Reject item, " +
                  "or use the shop manually; then /rkm logistics return";
            _say($"Reached the surveyed {_route.Site}/{_route.Purpose} position in PF {_route.DestinationPlayfield}. " +
                $"{liveTarget}. {action}; no item was moved by the route test.");
        }

        private void SetStage(int index)
        {
            _stageIndex = index;
            _pointIndex = 0;
            _awaitingZone = false;
            _crossingSubmitted = _crossingStopped = false;
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
