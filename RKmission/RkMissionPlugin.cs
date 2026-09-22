using System;

namespace RKmission;

/// <summary>
/// Host integration facade. Wire these methods to the exact plugin lifecycle and
/// command/event APIs exposed by the checked-out AOSharp.NewBots version.
/// </summary>
public sealed class RkMissionPlugin
{
    private readonly MissionRunner _runner;
    private DateTime _nextTick;

    public RkMissionPlugin(IMissionWorld world)
    {
        _runner = new MissionRunner(world);
    }

    public void OnStart() { /* Register commands and update callback here. */ }

    public void OnStop() => _runner.Stop("plugin unloaded");

    public void OnUpdate()
    {
        if (DateTime.UtcNow < _nextTick) return;
        _nextTick = DateTime.UtcNow.AddMilliseconds(250);
        _runner.Tick();
    }

    public void HandleCommand(string command)
    {
        switch (command.Trim().ToLowerInvariant())
        {
            case "start": _runner.Start(); break;
            case "stop": _runner.Stop(); break;
            case "status": _runnerStatus(); break;
        }
    }

    private void _runnerStatus() => Console.WriteLine($"RKmission: {_runner.State}, rooms={_runner.VisitedRooms}");
}
