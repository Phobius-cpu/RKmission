using System;
using RKmission.Adapters;

namespace RKmission.Plugins;

public sealed class RkMissionPlugin
{
    private readonly MissionRunner _runner;
    private readonly AoSharpMissionAdapter _adapter;
    private DateTime _nextTickAt = DateTime.MinValue;

    public RkMissionPlugin(AoSharpMissionAdapter adapter, MissionConfig? config = null)
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _runner = new MissionRunner(_adapter, config);
    }

    public void OnStart()
    {
        Logger.Info("RkMissionPlugin started.");
        // TODO: register chat commands and game update hooks for the actual AOSharp runtime.
    }

    public void OnStop()
    {
        _runner.Stop("plugin stopped");
        Logger.Info("RkMissionPlugin stopped.");
    }

    public void OnUpdate()
    {
        if (DateTime.UtcNow < _nextTickAt)
            return;

        _nextTickAt = DateTime.UtcNow.Add(_runnerConfigTickInterval());
        _runner.Tick();
    }

    public void HandleCommand(string commandText)
    {
        var command = commandText?.Trim();
        if (string.IsNullOrWhiteSpace(command))
            return;

        switch (command.ToLowerInvariant())
        {
            case "start":
                _runner.Start();
                break;
            case "stop":
                _runner.Stop();
                break;
            case "status":
                Logger.Info($"State={_runner.State}, Rooms={_runner.VisitedRooms}");
                break;
            default:
                Logger.Warn($"Unknown command: {command}");
                break;
        }
    }

    private TimeSpan _runnerConfigTickInterval()
    {
        return TimeSpan.FromMilliseconds(250);
    }
}
