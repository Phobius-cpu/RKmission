using System;
using System.Collections.Generic;

namespace RKmission.Models;

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
    float Distance,
    bool RequiresKey = false,
    bool RequiresLockpick = false,
    string RoomKey = "");
