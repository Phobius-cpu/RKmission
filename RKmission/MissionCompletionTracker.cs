using System;
using System.Collections.Generic;
using System.Linq;
using AOSharp.Common.GameData;
using AOSharp.Core;

namespace RKmission
{
    // A handled objective is evidence, not mission completion. Require an
    // exact bound quest to disappear after our final action and remain absent.
    internal sealed class MissionCompletionTracker
    {
        private DateTime _absentSince;

        public bool Reconcile(Identity missionId, bool finalActionRecent,
            bool allObjectiveStepsProven, bool serverCompletion, bool deletedByUser,
            out string evidence)
        {
            evidence = null;
            List<Mission> live = Mission.List;
            if (deletedByUser || !finalActionRecent ||
                !(allObjectiveStepsProven || serverCompletion) ||
                live == null || live.Any(x => x.Identity == missionId))
            {
                _absentSince = DateTime.MinValue;
                return false;
            }
            if (_absentSince == DateTime.MinValue)
            { _absentSince = DateTime.UtcNow; return false; }
            if (DateTime.UtcNow - _absentSince < TimeSpan.FromSeconds(2)) return false;
            evidence = "Bound quest absent for 2 s after final action plus " +
                (serverCompletion ? "server completion text" : "proof for every objective step") +
                "; no manual deletion observed";
            return true;
        }
    }
}
