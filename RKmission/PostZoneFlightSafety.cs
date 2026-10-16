using System;
using AOSharp.Common.GameData;
using AOSharp.Core;

namespace RKmission
{
    internal sealed class PostZoneFlightSafety : IDisposable
    {
        private enum Phase { Idle, HorizontalClearance, DiagonalClimb }
        private const float HorizontalDeparture = 8f;
        private readonly MovementArbiter _movement;
        private readonly Action<string> _say;
        private bool _fromDungeon, _fromFGrid;
        private Vector3? _observedOutdoorExit, _zoningOutdoorExit;
        private Vector3 _landing, _outward, _target;
        private float _bestProgress;
        private int _corridorAttempt;
        private DateTime _started, _lastProgress;
        private Phase _phase;

        public bool IsActive => _phase != Phase.Idle;

        public PostZoneFlightSafety(MovementArbiter movement, Action<string> say)
        {
            _movement = movement; _say = say;
            Game.TeleportStarted += Started;
            Game.TeleportEnded += Ended;
        }

        public void ObserveExpectedOutdoorExit(Vector3 entrance)
        {
            if (AcceptedMissions.Finite(entrance)) _observedOutdoorExit = entrance;
        }

        private void Started(object sender, EventArgs args)
        {
            _fromDungeon = Playfield.IsDungeon;
            _fromFGrid = Playfield.ModelIdentity.Instance == (int)PlayfieldId.FixerGrid;
            _zoningOutdoorExit = _fromDungeon ? _observedOutdoorExit : null;
        }

        internal static bool IsCityReturnPlayfield(int playfield) =>
            playfield == 540 || playfield == 655 || playfield == 800;

        private void Ended(object sender, EventArgs args)
        {
            _phase = Phase.Idle;
            if (IsCityReturnPlayfield(Playfield.ModelIdentity.Instance) ||
                (!_fromDungeon && !_fromFGrid) || Playfield.IsDungeon ||
                Playfield.ModelIdentity.Instance == (int)PlayfieldId.FixerGrid ||
                DynelManager.LocalPlayer?.MovementState != MovementState.Fly)
                return;
            if (!_zoningOutdoorExit.HasValue)
            {
                _say((_fromFGrid ? "FGrid exit" : "Mission exit") +
                    " flight safety held: no verified outdoor exit anchor is available, so no direction will be guessed.");
                return;
            }
            _landing = DynelManager.LocalPlayer.Position;
            _outward = _landing - _zoningOutdoorExit.Value; _outward.Y = 0;
            if (_outward.Magnitude < 0.5f)
            {
                _say("Mission exit flight safety held: the landing does not establish an outward corridor from the verified entrance anchor.");
                return;
            }
            _outward = _outward.Normalize(); _corridorAttempt = 0;
            if (!SelectHorizontalCorridor(_landing))
            {
                _say("Mission exit flight safety stopped: no geometry-clear 8 m outward corridor was found around the verified exit direction.");
                return;
            }
            _bestProgress = 0; _started = _lastProgress = DateTime.UtcNow;
            _phase = Phase.HorizontalClearance;
            _say($"Mission exit flight safety: first clearing horizontally 8.0 m at altitude {_landing.Y:0.0} " +
                $"along the verified entrance-to-landing direction; target=({_target.X:0.0},{_target.Y:0.0},{_target.Z:0.0}).");
        }

        private bool SelectHorizontalCorridor(Vector3 from)
        {
            float[] offsets = { 0f, 30f, -30f };
            while (_corridorAttempt < offsets.Length)
            {
                float radians = offsets[_corridorAttempt++] * (float)Math.PI / 180f;
                Vector3 direction = new Vector3(
                    _outward.X * (float)Math.Cos(radians) - _outward.Z * (float)Math.Sin(radians), 0,
                    _outward.X * (float)Math.Sin(radians) + _outward.Z * (float)Math.Cos(radians));
                Vector3 candidate = new Vector3(from.X + direction.X * HorizontalDeparture,
                    from.Y, from.Z + direction.Z * HorizontalDeparture);
                if (!LocalRoutePlanner.ClearSegment(from, candidate)) continue;
                _outward = direction; _target = candidate; return true;
            }
            return false;
        }

        public bool Tick()
        {
            if (_phase == Phase.Idle) return false;
            SimpleChar player = DynelManager.LocalPlayer;
            if (player == null || Game.IsZoning || player.MovementState != MovementState.Fly)
                return Stop("live Fly state or stable outdoor playfield was lost");
            DateTime now = DateTime.UtcNow;
            float progress = LocalRoutePlanner.HorizontalDistance(_landing, player.Position);
            if (progress > _bestProgress + 0.35f)
            { _bestProgress = progress; _lastProgress = now; }

            if (_phase == Phase.HorizontalClearance)
            {
                if (progress >= 5f || Vector3.Distance(player.Position, _target) <= 1.5f)
                {
                    _target = new Vector3(player.Position.X + _outward.X * 18f,
                        player.Position.Y + 36f, player.Position.Z + _outward.Z * 18f);
                    if (!LocalRoutePlanner.ClearSegment(player.Position, _target))
                        return Stop("horizontal clearance succeeded, but the diagonal climb corridor is obstructed");
                    _phase = Phase.DiagonalClimb; _started = _lastProgress = now;
                    _say($"Mission exit flight safety: horizontal clearance verified ({progress:0.0} m); " +
                        $"starting diagonal climb to ({_target.X:0.0},{_target.Y:0.0},{_target.Z:0.0}).");
                }
                else if (now - _lastProgress > TimeSpan.FromSeconds(3))
                {
                    _movement.Halt(MovementOwner.OutdoorTravel);
                    if (!SelectHorizontalCorridor(player.Position))
                        return Stop("horizontal departure stalled and bounded geometry-safe recovery was exhausted");
                    _lastProgress = now;
                    _say($"Mission exit flight safety: horizontal progress stalled; trying bounded corridor {_corridorAttempt}/3 relative to the verified outward direction.");
                }
                else if (now - _started > TimeSpan.FromSeconds(12))
                    return Stop("horizontal departure did not confirm 5 m clearance within 12 seconds");
            }
            else if (Vector3.Distance(player.Position, _target) <= 4f)
                return Stop("horizontal clearance and diagonal climb completed");
            else if (now - _lastProgress > TimeSpan.FromSeconds(4) || now - _started > TimeSpan.FromSeconds(15))
                return Stop("diagonal climb stalled or exhausted its bounded deadline");

            if (_movement.Owner != MovementOwner.OutdoorTravel || !AOSharp.Pathfinding.SMovementController.IsNavigating())
                _movement.SetDestination(MovementOwner.OutdoorTravel, _target);
            return true;
        }

        private bool Stop(string reason)
        {
            _movement.Release(MovementOwner.OutdoorTravel); _phase = Phase.Idle;
            _say("Mission exit flight safety: " + reason + "; movement released safely.");
            return false;
        }

        public void Dispose()
        {
            Game.TeleportStarted -= Started; Game.TeleportEnded -= Ended;
            if (IsActive) _movement.Release(MovementOwner.OutdoorTravel);
        }
    }
}
