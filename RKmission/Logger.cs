using System;
using System.Collections.Generic;

namespace RKmission.Commands;

public static class MissionCommands
{
    public static IReadOnlyDictionary<string, Action> CommandMap => new Dictionary<string, Action>
    {
        ["start"] = () => { },
        ["stop"] = () => { },
        ["status"] = () => { }
    };

    public static string Normalize(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        return input.Trim().ToLowerInvariant();
    }
}
