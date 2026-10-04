using System;
using System.Text.RegularExpressions;

namespace ManagerLoot
{
    // A family request is only the unsuffixed RKM name. A numbered rule remains exact.
    internal static class ManagedBagFamily
    {
        public const string Keep = "RKM Keep";
        public const string Mission = "RKM Mission";
        public const string Sell = "RKM Sell";
        public const string Archive = "RKM Archive";

        private static bool IsFamily(string name) =>
            string.Equals(name, Keep, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, Sell, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, Archive, StringComparison.OrdinalIgnoreCase);

        public static bool Matches(string bagName, string requested)
        {
            if (string.IsNullOrWhiteSpace(bagName) || string.IsNullOrWhiteSpace(requested)) return false;
            bagName = bagName.Trim();
            requested = requested.Trim();
            if (string.Equals(bagName, requested, StringComparison.OrdinalIgnoreCase)) return true;
            if (!IsFamily(requested)) return false;
            string digits = string.Equals(requested, Archive, StringComparison.OrdinalIgnoreCase) ? @"\d{3,}" : @"\d{2,}";
            Match suffix = Regex.Match(bagName, "^" + Regex.Escape(requested) + " (?<number>" + digits + ")$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return suffix.Success && int.TryParse(suffix.Groups["number"].Value, out int number) && number > 0;
        }

        public static int Order(string bagName, string requested)
        {
            if (string.Equals(bagName?.Trim(), requested?.Trim(), StringComparison.OrdinalIgnoreCase)) return 0;
            Match suffix = Regex.Match(bagName ?? "", @" (\d+)$");
            return suffix.Success && int.TryParse(suffix.Groups[1].Value, out int number)
                ? number : int.MaxValue;
        }

        public static bool IsProtected(string bagName) =>
            Matches(bagName, Keep) ||
            string.Equals(bagName?.Trim(), Mission, StringComparison.OrdinalIgnoreCase);
    }
}
