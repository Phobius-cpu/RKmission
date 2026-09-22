using System;

namespace RKmission;

public static class Logger
{
    public static void Info(string message)
    {
        Console.WriteLine($"[RKmission] {message}");
    }

    public static void Warn(string message)
    {
        Console.WriteLine($"[RKmission][WARN] {message}");
    }

    public static void Error(string message)
    {
        Console.WriteLine($"[RKmission][ERROR] {message}");
    }
}
