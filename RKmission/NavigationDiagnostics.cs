using System;

namespace RKmission
{
    // Routine successful navigation detail is opt-in. Operational messages keep
    // using the normal logger and are never affected by this switch.
    internal sealed class NavigationDiagnostics
    {
        private readonly Action<string> _say;
        public bool NavDebugEnabled { get; private set; }

        public NavigationDiagnostics(Action<string> say) { _say = say; }
        public void SetNavDebug(bool enabled) { NavDebugEnabled = enabled; }
        public void NavTrace(string message)
        {
            if (NavDebugEnabled) _say(message);
        }
    }
}
