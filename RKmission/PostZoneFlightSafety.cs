using System;
using AOSharp.Common.GameData;
using AOSharp.Core;

namespace RKmission
{
    // Brief post-zone flight escape. It never runs on foot, inside Grid/FGrid,
    // or during ordinary terminal/mission approach. The lateral component avoids
    // the old straight vertical climb behavior.
    internal sealed class PostZoneFlightSafety : IDisposable
    {
        private readonly MovementArbiter _movement;
        private readonly Action<string> _say;
        private bool _fromDungeon, _fromFGrid, _active;
        private Vector3 _target;
        private DateTime _started;

        public bool IsActive => _active;

        public PostZoneFlightSafety(MovementArbiter movement, Action<string> say)
        {
            _movement = movement; _say = say;
            Game.TeleportStarted += Started;
            Game.TeleportEnded += Ended;
        }

        private void Started(object sender, EventArgs args)
        {
            _fromDungeon = Playfield.IsDungeon;
            _fromFGrid = Playfield.ModelIdentity.Instance == (int)PlayfieldId.FixerGrid;
        }

        private void Ended(object sender, EventArgs args)
        {
            if ((!_fromDungeon && !_fromFGrid) || Playfield.IsDungeon ||
                Playfield.ModelIdentity.Instance == (int)PlayfieldId.FixerGrid ||
                DynelManager.LocalPlayer?.MovementState != MovementState.Fly)
            { _active = false; return; }

            Vector3 p = DynelManager.LocalPlayer.Position;
            // 36 m altitude gain with 18 m horizontal displacement gives a
            // diagonal escape and leaves the normal flight planner at a safer height.
            _target = new Vector3(p.X + 18f, p.Y + 36f, p.Z + 12f);
            _started = DateTime.UtcNow;
            _active = true;
            _say((_fromFGrid ? "FGrid exit" : "Mission exit") +
                $" flight safety: diagonal climb to ({_target.X:0.0},{_target.Y:0.0},{_target.Z:0.0}) before normal routing.");
        }

        public bool Tick()
        {
            if (!_active) return false;
            var player = DynelManager.LocalPlayer;
            if (player == null || Game.IsZoning || player.MovementState != MovementState.Fly)
            { _active = false; _movement.Release(MovementOwner.OutdoorTravel); return false; }
            if (Vector3.Distance(player.Position, _target) <= 4f || DateTime.UtcNow - _started > TimeSpan.FromSeconds(15))
            {
                _movement.Release(MovementOwner.OutdoorTravel);
                _active = false;
                return false;
            }
            if (_movement.Owner != MovementOwner.OutdoorTravel || !AOSharp.Pathfinding.SMovementController.IsNavigating())
                _movement.SetDestination(MovementOwner.OutdoorTravel, _target);
            return true;
        }

        public void Dispose()
        {
            Game.TeleportStarted -= Started;
            Game.TeleportEnded -= Ended;
            if (_active) _movement.Release(MovementOwner.OutdoorTravel);
        }
    }
}
