namespace RKmission;

public sealed class MissionConfig
{
    public TimeSpan TickInterval { get; init; } = TimeSpan.FromMilliseconds(250);
    public TimeSpan InteractionCooldown { get; init; } = TimeSpan.FromSeconds(1.5);
    public TimeSpan MaxDuration { get; init; } = TimeSpan.FromMinutes(30);
    public int MaxRooms { get; init; } = 500;
    public int MaxFailures { get; init; } = 8;
    public float InteractionDistance { get; init; } = 3.5f;
    public bool IgnoreLockedObjectsWithoutTool { get; init; } = true;
}
