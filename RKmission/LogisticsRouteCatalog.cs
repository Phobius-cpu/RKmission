using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AOSharp.Common.GameData;
using Newtonsoft.Json;

namespace RKmission
{
    // Read-only survey data. A route is not permission to enter a building or
    // transact: the live origin, zoning destination and target must be checked
    // by a future logistics controller before any movement or item action.
    internal sealed class LogisticsRouteCatalog
    {
        internal sealed class Stage
        {
            public string Name { get; set; }
            public int Playfield { get; set; }
            public List<float[]> Points { get; set; }
            public int ExpectedNextPlayfield { get; set; }
        }

        internal sealed class Route
        {
            public string Site { get; set; }
            public string Purpose { get; set; }
            public string SourceTrace { get; set; }
            public int OriginPlayfield { get; set; }
            public float[] OriginPosition { get; set; }
            public string OriginTerminalIdentity { get; set; }
            public string OriginEvidence { get; set; }
            public int DestinationPlayfield { get; set; }
            public float[] DestinationPosition { get; set; }
            public string DestinationIdentity { get; set; }
            public string TransactionEvidence { get; set; }
            public List<Stage> Stages { get; set; }
            public bool OriginBoundByProbe => OriginEvidence == "Probe start";
        }

        private sealed class RouteFile
        {
            public List<Route> Routes { get; set; }
        }

        private readonly List<Route> _routes = new List<Route>();
        public IReadOnlyList<Route> Routes => _routes;
        public Route Find(string site, string purpose) => _routes.FirstOrDefault(x =>
            string.Equals(x.Site, site, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Purpose, purpose, StringComparison.OrdinalIgnoreCase));

        public string FindUniqueOriginSite(int playfield, string terminalIdentity, Vector3 terminalPosition)
        {
            if (playfield <= 0 || string.IsNullOrWhiteSpace(terminalIdentity)) return null;
            string[] matches = _routes.Where(route => route.OriginPlayfield == playfield &&
                    string.Equals(route.OriginTerminalIdentity, terminalIdentity,
                        StringComparison.OrdinalIgnoreCase) &&
                    Vector3.Distance(terminalPosition, V(route.OriginPosition)) <= 15f)
                .Select(route => route.Site).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }

        public string FindSharedLogisticsSite(int playfield)
        {
            string[] matches = _routes.Where(route => route.OriginPlayfield == playfield)
                .GroupBy(route => route.Site, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Any(route => route.Purpose == "bank") &&
                    group.Any(route => route.Purpose == "shop"))
                .Select(group => group.Key).ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }

        public Route FindOrigin(string site, string purpose) => Find(site, purpose);

        public bool TryFindTerminalApproach(string site, int playfield, string terminalIdentity,
            Vector3 terminalPosition, Vector3 playerPosition, out Vector3 approach)
        {
            approach = Vector3.Zero;
            Vector3[] candidates = _routes.Where(route => !string.IsNullOrWhiteSpace(site) &&
                    route.OriginPlayfield == playfield &&
                    string.Equals(route.Site, site, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(route.OriginTerminalIdentity, terminalIdentity,
                        StringComparison.OrdinalIgnoreCase))
                .Select(route => V(route.OriginPosition))
                // Recorded probe origins are reachable player positions. Keep
                // only points inside AO terminal interaction range but outside
                // the terminal centre/collision body.
                .Where(point => Vector3.Distance(point, terminalPosition) >= 1.5f &&
                    Vector3.Distance(point, terminalPosition) <= 3f)
                .OrderBy(point => Vector3.Distance(point, playerPosition)).ToArray();
            if (candidates.Length > 0)
            {
                approach = candidates[0];
                return true;
            }
            // An arbitrary roller has no survey point. Derive a conservative
            // standoff on the player's visible side of the exact live actor;
            // never submit the collidable terminal centre as a destination.
            Vector3 away = playerPosition - terminalPosition;
            float horizontal = (float)Math.Sqrt(away.X * away.X + away.Z * away.Z);
            if (horizontal < 0.1f) return false;
            const float standoff = 2.25f;
            approach = new Vector3(terminalPosition.X + away.X / horizontal * standoff,
                terminalPosition.Y, terminalPosition.Z + away.Z / horizontal * standoff);
            return true;
        }

        public bool TryFindTerminalLandingStaging(string site, int playfield, string terminalIdentity,
            Vector3 terminalPosition, Vector3 playerPosition, out Vector3 staging)
        {
            staging = Vector3.Zero;
            if (!TryFindTerminalApproach(site, playfield, terminalIdentity, terminalPosition,
                playerPosition, out Vector3 approach)) return false;

            // Descend outside the terminal collision body and with a material
            // lateral component. Never use the player's airborne Y as evidence
            // of ground height and never request a blind vertical plunge.
            Vector3 away = approach - terminalPosition;
            float horizontal = (float)Math.Sqrt(away.X * away.X + away.Z * away.Z);
            if (horizontal < 0.1f) return false;
            const float landingRadius = 5f;
            const float groundClearance = 0.5f;
            staging = new Vector3(terminalPosition.X + away.X / horizontal * landingRadius,
                terminalPosition.Y + groundClearance,
                terminalPosition.Z + away.Z / horizontal * landingRadius);
            return true;
        }

        public LogisticsRouteCatalog(string pluginDir, Action<string> say)
        {
            string path = Path.Combine(pluginDir, "Data", "LogisticsRoutes.json");
            try
            {
                if (!File.Exists(path)) { say("No bundled logistics route survey was found."); return; }
                var file = JsonConvert.DeserializeObject<RouteFile>(File.ReadAllText(path));
                if (file?.Routes == null || file.Routes.Count == 0)
                    throw new InvalidDataException("route list is empty");
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (Route route in file.Routes)
                {
                    Validate(route);
                    if (!names.Add(route.Site + ":" + route.Purpose))
                        throw new InvalidDataException("duplicate site/purpose route");
                }
                _routes.AddRange(file.Routes);
                say($"Loaded {_routes.Count} surveyed bank/shop route(s); use /rkm logistics routes for endpoint evidence.");
            }
            catch (Exception ex) { _routes.Clear(); say("Bundled logistics routes unavailable: " + ex.Message); }
        }

        public void Report(Action<string> say)
        {
            if (_routes.Count == 0) { say("No valid bundled logistics routes are available."); return; }
            foreach (Route route in _routes.OrderBy(x => x.OriginPlayfield).ThenBy(x => x.Purpose))
                say($"Logistics survey {route.Site}/{route.Purpose}: PF {route.OriginPlayfield} -> " +
                    $"PF {route.DestinationPlayfield} -> PF {route.OriginPlayfield}; " +
                    $"{route.Stages.Sum(x => x.Points.Count)} points, " +
                    $"origin={(route.OriginBoundByProbe ? "recorded" : "inferred")}, " +
                    $"transaction={route.TransactionEvidence}; source={route.SourceTrace}.");
        }

        private static void Validate(Route route)
        {
            if (route == null || string.IsNullOrWhiteSpace(route.Site) ||
                (route.Purpose != "bank" && route.Purpose != "shop") ||
                string.IsNullOrWhiteSpace(route.SourceTrace) ||
                string.IsNullOrWhiteSpace(route.OriginTerminalIdentity) ||
                string.IsNullOrWhiteSpace(route.DestinationIdentity) ||
                route.OriginPlayfield <= 0 || route.DestinationPlayfield <= 0 ||
                !ValidPoint(route.OriginPosition) || !ValidPoint(route.DestinationPosition) ||
                route.Stages == null || (route.Stages.Count != 4 && route.Stages.Count != 1))
                throw new InvalidDataException("incomplete logistics route");
            bool direct = route.Stages.Count == 1;
            if (direct && (route.Purpose != "shop" || route.OriginPlayfield != route.DestinationPlayfield ||
                route.Stages[0].Name != "AtTarget"))
                throw new InvalidDataException("invalid same-playfield shop route");
            if (!direct && (route.Stages[0].Name != "ToEntry" || route.Stages[1].Name != "ToTarget" ||
                route.Stages[2].Name != "ToExit" || route.Stages[3].Name != "ToOrigin" ||
                route.Stages[0].Playfield != route.OriginPlayfield ||
                route.Stages[1].Playfield != route.DestinationPlayfield ||
                route.Stages[2].Playfield != route.DestinationPlayfield ||
                route.Stages[3].Playfield != route.OriginPlayfield ||
                route.Stages[0].ExpectedNextPlayfield != route.DestinationPlayfield ||
                route.Stages[2].ExpectedNextPlayfield != route.OriginPlayfield))
                throw new InvalidDataException("logistics route stage or zone mismatch");
            foreach (Stage stage in route.Stages)
            {
                if (stage?.Points == null || stage.Points.Count < (direct ? 1 : 2) ||
                    stage.Points.Any(x => !ValidPoint(x)))
                    throw new InvalidDataException("invalid logistics route point");
                for (int i = 1; i < stage.Points.Count; i++)
                    if (Vector3.Distance(V(stage.Points[i - 1]), V(stage.Points[i])) > 4f)
                        throw new InvalidDataException("unobserved gap in logistics route");
            }
            if (Vector3.Distance(V(route.Stages[0].Points[0]), V(route.OriginPosition)) > 1f ||
                Vector3.Distance(V(route.Stages[direct ? 0 : 1].Points.Last()), V(route.DestinationPosition)) > 1f ||
                Vector3.Distance(V(route.Stages.Last().Points.Last()), V(route.OriginPosition)) > 3f)
                throw new InvalidDataException("logistics route endpoint mismatch");
        }

        private static bool ValidPoint(float[] point) => point != null && point.Length == 3 &&
            point.All(x => !float.IsNaN(x) && !float.IsInfinity(x));
        private static Vector3 V(float[] point) => new Vector3(point[0], point[1], point[2]);
    }
}
