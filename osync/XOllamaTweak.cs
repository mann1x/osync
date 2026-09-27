using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace osync
{
    /// <summary>
    /// xOllama's model settings (the model's xollama.json layer: engine, KV cache types, dynamic slots, DCA,
    /// session pooling, council, devices, ...) and `xollama tweak model`, the xOllama command that edits them.
    /// The settings, their rules and the questions live in the xollama CLI (cmd/tweak in the xOllama repository),
    /// so osync runs it rather than keeping a copy of its table that would fall behind; osync only reads the
    /// settings back from /api/show (its "xollama" field) to display them.
    /// </summary>
    internal static class XOllamaTweak
    {
        /// <summary>What the tweak dialog offers: a label and the tweak flags it passes (a bare flag scopes the walk).</summary>
        public static readonly (string Label, string Flags)[] Scopes =
        {
            ("Every setting", ""),
            ("KV cache types (--kv-k)", "--kv-k"),
            ("Dynamic slots (--slots)", "--slots"),
            ("DCA, context past the trained length (--dca)", "--dca"),
            ("Session affinity and prefix pooling (--session-affinity)", "--session-affinity"),
            ("Council (--council)", "--council"),
            ("GPU / devices (--device-backend)", "--device-backend"),
            ("Engine (--engine)", "--engine"),
            ("Remove the xOllama settings (--clear)", "--clear")
        };

        /// <summary>Index of the scope that removes the settings.</summary>
        public static int ClearScope => Scopes.Length - 1;

        /// <summary>Arguments of `xollama tweak model` for <paramref name="model"/>: the scope's flags, then the typed ones.</summary>
        public static string Arguments(string model, string scopeFlags, string? extraFlags)
        {
            var parts = new List<string> { "tweak", "model", model };
            if (!string.IsNullOrWhiteSpace(scopeFlags)) parts.Add(scopeFlags.Trim());
            if (!string.IsNullOrWhiteSpace(extraFlags)) parts.Add(extraFlags.Trim());
            return string.Join(" ", parts);
        }

        /// <summary>
        /// The settings a model states, as "path value" rows in the order the server wrote them ("kv.k" "q8_0",
        /// "council.enabled" "on"). The schema version is left out: it is computed, not a setting. Null or an
        /// empty object gives no rows (the model states nothing, so the server's environment decides).
        /// </summary>
        public static List<(string Path, string Value)> Flatten(JsonElement config)
        {
            var rows = new List<(string, string)>();
            if (config.ValueKind == JsonValueKind.Object)
                Flatten(config, "", rows);
            return rows;
        }

        private static void Flatten(JsonElement element, string prefix, List<(string, string)> rows)
        {
            foreach (var property in element.EnumerateObject())
            {
                var path = prefix.Length == 0 ? property.Name : prefix + "." + property.Name;
                if (path == "version") continue;
                var value = property.Value;
                switch (value.ValueKind)
                {
                    case JsonValueKind.Object:
                        Flatten(value, path, rows);
                        break;
                    case JsonValueKind.Array:
                        var items = value.EnumerateArray().Select(Scalar).ToList();
                        if (items.Count > 0) rows.Add((path, string.Join(",", items)));
                        break;
                    case JsonValueKind.Null:
                        break;
                    default:
                        rows.Add((path, Scalar(value)));
                        break;
                }
            }
        }

        private static string Scalar(JsonElement value) => value.ValueKind switch
        {
            JsonValueKind.True => "on",
            JsonValueKind.False => "off",
            JsonValueKind.String => value.GetString() ?? "",
            JsonValueKind.Number => value.TryGetInt64(out var n)
                ? n.ToString(CultureInfo.InvariantCulture)
                : value.GetDouble().ToString("G", CultureInfo.InvariantCulture),
            _ => value.GetRawText()
        };

        /// <summary>One line per setting, long values (prompts) shortened to <paramref name="maxValue"/> characters.</summary>
        public static IEnumerable<string> Describe(IReadOnlyList<(string Path, string Value)> rows, int maxValue = 50)
        {
            if (rows.Count == 0) yield break;
            var width = rows.Max(r => r.Path.Length);
            foreach (var (path, value) in rows)
            {
                var text = value.ReplaceLineEndings(" ");
                if (text.Length > maxValue) text = text[..(maxValue - 3)] + "...";
                yield return $"{path.PadRight(width)}  {text}";
            }
        }

        /// <summary>
        /// Runs `xollama tweak model` on the console against the server <paramref name="url"/> (local or remote),
        /// with the console's input and output, so its questions are answered as usual. Returns the exit code.
        /// </summary>
        public static int Run(string cli, string url, string arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = cli,
                Arguments = arguments,
                UseShellExecute = false
            };
            OllamaServer.ApplyCliEnvironment(startInfo, url);
            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start {cli}");
            process.WaitForExit();
            return process.ExitCode;
        }
    }
}
