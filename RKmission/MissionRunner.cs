using System;
using System.Collections.Generic;
using System.Linq;

namespace RKmission;

public enum MissionState
{
    Idle,
    Exploring,
    Approaching,
    Opening,
    Looting,
    Complete,
    Stuck,
    Stopped
}

public enum MissionObjectKind
{
    Door,
    Chest
}

public sealed record MissionObject(
    long Id,
    MissionObjectKind Kind,
    string Name,
    bool IsLocked,
    bool IsOpen,
    float Distance);

public sealed class MissionOptions
{
    public TimeSpan TickInterval { get; init; } = TimeSpan.FromMilliseconds(250);
    public TimeSpan InteractionCooldown { get; init; } = TimeSpan.FromSeconds(1.5);
    public TimeSpan MaxDuration { get; init; } = TimeSpan.FromMinutes(30);
    public int MaxRooms { get; init; } = 500;
    public int MaxFailures { get; init; } = 8;
    public float InteractionDistance { get; init; } = 3.5f;
}

public interface IMissionWorld
{
    bool IsInMission { get; }
    bool IsAlive { get; }
    IReadOnlyCollection<string> VisibleRoomKeys { get; }
    IReadOnlyCollection<MissionObject> NearbyObjects { get; }
    bool HasUsableKeyOrLockpick(MissionObject target);
    void MoveTo(MissionObject target);
    void ExploreNextArea();
    bool TryInteract(MissionObject target);
    bool TryLoot(MissionObject target);
    void StopMoving();
    void Say(string message);
}

public sealed class MissionRunner
{
    private readonly IMissionWorld _world;
    private readonly MissionOptions _options;
    private readonly HashSet<string> _visitedRooms = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _started;
    private DateTime _lastInteraction;
    private MissionObject? _target;
    private int _failures;

    public MissionRunner(IMissionWorld world, MissionOptions? options = null)
    {
        _world = world;
        _options = options ?? new MissionOptions();
        State = MissionState.Idle;
    }

    public MissionState State { get; private set; }
    public int VisitedRooms => _visitedRooms.Count;

    public void Start()
    {
        _visitedRooms.Clear();
        _failures = 0;
        _target = null;
        _started = DateTime.UtcNow;
        _lastInteraction = DateTime.MinValue;
        State = MissionState.Exploring;
        _world.Say("RKmission: exploration started.");
    }

    public void Stop(string reason = "stopped")
    {
        _world.StopMoving();
        _target = null;
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
        if (DateTime.UtcNow - _started > _options.MaxDuration || _visitedRooms.Count >= _options.MaxRooms)
        {
            Stop("safety limit reached");
            return;
        }

        foreach (var room in _world.VisibleRoomKeys)
            _visitedRooms.Add(room);

        _target = SelectTarget();
        if (_target is null)
        {
            State = MissionState.Exploring;
            _world.ExploreNextArea();
            return;
        }

        if (_target.Distance > _options.InteractionDistance)
        {
            State = MissionState.Approaching;
            _world.MoveTo(_target);
            return;
        }

        if (_target.IsLocked && !_world.HasUsableKeyOrLockpick(_target))
        {
            _world.Say($"RKmission: no usable key or lockpick for {_target.Name}; skipping.");
            _target = null;
            RegisterFailure();
            return;
        }

        if (DateTime.UtcNow - _lastInteraction < _options.InteractionCooldown)
            return;

        State = MissionState.Opening;
        _lastInteraction = DateTime.UtcNow;
        if (!_world.TryInteract(_target))
        {
            RegisterFailure();
            return;
        }

        if (_target.Kind == MissionObjectKind.Chest)
        {
            State = MissionState.Looting;
            _world.TryLoot(_target);
        }
        _target = null;
        State = MissionState.Exploring;
    }

    private MissionObject? SelectTarget() => _world.NearbyObjects
        .Where(x => !x.IsOpen)
        .OrderBy(x => x.Distance)
        .FirstOrDefault();

    private void RegisterFailure()
    {
        if (++_failures >= _options.MaxFailures)
            Stop("too many interaction failures");
        else
            State = MissionState.Exploring;
    }
}
