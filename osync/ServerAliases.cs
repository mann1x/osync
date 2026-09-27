using System.Text.RegularExpressions;

namespace osync
{
    /// <summary>
    /// Server aliases from the settings file (<see cref="OsyncSettings.Aliases"/>): a name that stands for a
    /// server URL wherever osync expects a server. "gpu" is the server itself, "gpu/model:tag" a model on it.
    /// An alias takes precedence over a model namespace with the same name.
    /// </summary>
    internal static class ServerAliases
    {
        private static readonly Regex ValidName = new(@"^[A-Za-z][A-Za-z0-9_-]{0,31}$", RegexOptions.Compiled);

        /// <summary>Names an alias cannot have: they already mean something to osync.</summary>
        private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase) { "localhost", "local", "http", "https", "hf", "huggingface" };

        /// <summary>Why <paramref name="name"/> cannot be an alias, or null when it can.</summary>
        public static string? ValidateName(string name)
        {
            if (!ValidName.IsMatch(name))
                return "an alias starts with a letter and has only letters, digits, '-' and '_' (at most 32)";
            if (Reserved.Contains(name))
                return $"'{name}' is reserved";
            return null;
        }

        /// <summary>Expands an alias with the aliases of the settings file (see <see cref="TryExpand(string?, IReadOnlyDictionary{string, string}, out string)"/>).</summary>
        public static bool TryExpand(string? input, out string expanded) =>
            TryExpand(input, OsyncSettings.Current.Aliases, out expanded);

        /// <summary>
        /// "alias" → its URL; "alias/model:tag" → URL/model:tag. Returns false (and <paramref name="input"/>
        /// unchanged) when <paramref name="input"/> does not start with a defined alias.
        /// </summary>
        public static bool TryExpand(string? input, IReadOnlyDictionary<string, string> aliases, out string expanded)
        {
            expanded = input ?? "";
            if (string.IsNullOrWhiteSpace(input) || aliases.Count == 0) return false;

            var trimmed = input.Trim();
            var slash = trimmed.IndexOf('/');
            var name = slash < 0 ? trimmed : trimmed[..slash];
            if (name.Length == 0) return false;

            string? url = null;
            foreach (var (alias, target) in aliases)
            {
                if (string.Equals(alias, name, StringComparison.OrdinalIgnoreCase))
                {
                    url = target;
                    break;
                }
            }
            if (url == null) return false;

            var rest = slash < 0 ? "" : trimmed[(slash + 1)..];
            expanded = rest.Length == 0 ? url.TrimEnd('/') : url.TrimEnd('/') + "/" + rest;
            return true;
        }

        /// <summary>A server reference: a URL (http:// or https://) or an alias.</summary>
        public static bool IsServerReference(string? input) =>
            !string.IsNullOrWhiteSpace(input) &&
            (input.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
             input.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
             TryExpand(input, out _));

        /// <summary>Expands an alias, or returns <paramref name="input"/> unchanged.</summary>
        public static string Expand(string input) => TryExpand(input, out var expanded) ? expanded : input;
    }
}
