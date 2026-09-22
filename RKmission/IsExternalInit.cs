namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// Polyfill required for C# init accessors on .NET Framework 4.8.
    /// This type is provided by newer target frameworks and is needed so
    /// record-like init-only properties compile correctly in this project.
    /// </summary>
    internal static class IsExternalInit
    {
    }
}
