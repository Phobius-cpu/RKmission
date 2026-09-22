using System;
using System.Collections.Generic;
using System.Linq;
using RKmission.Models;

namespace RKmission;

public sealed class MissionRunner
{
    private readonly IMissionWorld _world;
    private readonly MissionConfig _config;
    private readonly RoomTracker _roomTracker = new();
    private readonly MissionPlanner _planner = new();

    private DateTime _startedAt;
    private DateTime _lastInteractionAt = DateTime.MinValue;
    private int _failureCount;
    private MissionObject? _currentTarget;

    public MissionRunner(IMissionWorld world, MissionConfig? config = null)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _config = config ?? new MissionConfig();
        State = MissionState.Idle;
    }

    public MissionState State { get; private set; }
    public int VisitedRooms => _roomTracker.Count;

    public void Start()
    {
        _roomTracker.Clear();
        _failureCount = 0;
        _currentTarget = null;
        _startedAt = DateTime.UtcNow;
        _lastInteractionAt = DateTime.MinValue;
        State = MissionState.Exploring;
        _world.Say("RKmission: exploration started.");
    }

    public void Stop(string reason = "stopped")
    {
        _world.StopMoving();
        _currentTarget = null;
        State = MissionState.Stopped;
        _world.Say($"RKmission: {reason}.");
    }

    public void Tick()
    {
        if (State is MissionState.Idle or MissionState.Stopped or MissionState.Complete or MissionState.Stuck)
            return;

        if (!_world.IsInMission || !_world.IsAlive)
        {
            Stop("mission is no longer active");
            return;
        }

        if (DateTime.UtcNow - _startedAt > _config.MaxDuration)
        {
            Stop("maximum duration reached");
            return;
        }

        foreach (var roomKey in _world.VisibleRoomKeys)
            _roomTracker.MarkVisited(roomKey);

        if (_roomTracker.Count >= _config.MaxRooms)
        {
            Stop("maximum room count reached");
            return;
        }

        var nearby = _world.NearbyObjects ?? Array.Empty<MissionObject>();
        _currentTarget = _planner.SelectNextTarget(nearby, _config.IgnoreLockedObjectsWithoutTool);

        if (_currentTarget is null)
        {
            State = MissionState.Exploring;
            _world.ExploreNextArea();
            return;
        }

        if (_currentTarget.Distance > _config.InteractionDistance)
        {
            State = MissionState.Approaching;
            _world.MoveTo(_currentTarget);
            return;
        }

        if (_currentTarget.IsLocked && !_world.HasUsableKeyOrLockpick(_currentTarget))
        {
            _world.Say($"RKmission: no usable key or lockpick for {_currentTarget.Name}; skipping.");
            _currentTarget = null;
            RegisterFailure();
            return;
        }

        if (DateTime.UtcNow - _lastInteractionAt < _config.InteractionCooldown)
            return;

        State = MissionState.Opening;
        _lastInteractionAt = DateTime.UtcNow;

        var success = _world.TryInteract(_currentTarget);
        if (!success)
        {
            RegisterFailure();
            return;
        }

        if (_currentTarget.Kind == MissionObjectKind.Chest)
        {
            State = MissionState.Looting;
            _world.TryLoot(_currentTarget);
        }

        _currentTarget = null;
        State = MissionState.Exploring;
    }

    private void RegisterFailure()
    {
        _failureCount++;
        if (_failureCount >= _config.MaxFailures)
        {
            Stop("too many interaction failures");
            return;
        }

        State = MissionState.Exploring;
    }
}
