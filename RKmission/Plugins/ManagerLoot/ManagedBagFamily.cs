using System;
using System.Text.RegularExpressions;

namespace ManagerLoot
{
    internal static class ManagedBagFamily
    {
        public const string Keep = "RK Loot";
        public const string Sell = "RK Junk";
        public const string LegacyKeep = "RKM Keep";
        public const string LegacySell = "RKM Sell";

        private static string Normalize(string value) => string.IsNullOrWhiteSpace(value) ? string.Empty :
            Regex.Replace(value.Trim(), @"\s+", " ");
        private static bool Same(string left, string right) =>
            string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);
        private static bool IsRoot(string requested, string canonical, string legacy) =>
            Same(requested, canonical) || Same(requested, legacy);
        private static bool TryMember(string name, string root, out int number)
        {
            number = 0;
            string normalized = Normalize(name);
            if (Same(normalized, root)) return true;
            Match suffix = Regex.Match(normalized, "^" + Regex.Escape(root) + @"\s+(?<number>\d{2,})$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return suffix.Success && int.TryParse(suffix.Groups["number"].Value, out number) && number > 0;
        }

        public static bool Matches(string bagName, string requested)
        {
            if (string.IsNullOrWhiteSpace(bagName) || string.IsNullOrWhiteSpace(requested)) return false;
            if (Same(bagName, requested)) return true;
            if (IsRoot(requested, Keep, LegacyKeep))
                return TryMember(bagName, Keep, out _) || TryMember(bagName, LegacyKeep, out _);
            if (IsRoot(requested, Sell, LegacySell))
                return TryMember(bagName, Sell, out _) || TryMember(bagName, LegacySell, out _);
            return false;
        }

        public static int Order(string bagName, string requested)
        {
            if (IsRoot(requested, Keep, LegacyKeep))
            {
                if (TryMember(bagName, Keep, out int canonical)) return canonical;
                if (TryMember(bagName, LegacyKeep, out int legacy)) return 1000000 + legacy;
            }
            if (IsRoot(requested, Sell, LegacySell))
            {
                if (TryMember(bagName, Sell, out int canonical)) return canonical;
                if (TryMember(bagName, LegacySell, out int legacy)) return 1000000 + legacy;
            }
            return Same(bagName, requested) ? 0 : int.MaxValue;
        }

        public static bool IsKeep(string bagName) => Matches(bagName, Keep);
        public static bool IsSell(string bagName) => Matches(bagName, Sell);
        public static bool IsManaged(string bagName) => IsKeep(bagName) || IsSell(bagName);
        public static bool IsProtected(string bagName) => IsKeep(bagName);
    }
}
