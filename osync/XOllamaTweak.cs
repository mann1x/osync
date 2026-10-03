using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace osync
{
    /// <summary>
    /// xOllama's model settings (the model's xollama.json layer: engine, KV cache types, dynamic slots, DCA,
    /// session pooling, council, devices, engine policies, ...) and `xollama tweak`, the xOllama command that
    /// edits them, the server's defaults for them, its GPU policy and its environment overrides.
    /// The settings, their rules and the questions live in the xollama CLI (cmd/tweak in the xOllama repository),
    /// so osync runs it rather than keeping a copy of its table that would fall behind; osync only reads the
    /// settings back to display them: the model's from /api/show (its "xollama" field), the server's defaults
    /// from /api/xollama/settings.
    /// </summary>
    internal static class XOllamaTweak
    {
        /// <summary>
        /// One option of the tweak dialog: the xollama tweak subcommand it runs ("model", "server gpu", ...), the
        /// flags it passes (a bare flag scopes the walk to that feature) and whether it runs once per model.
        /// </summary>
        public sealed record Scope(string Label, string Command, string Flags = "", bool PerModel = true);

        /// <summary>The options about the chosen model(s).</summary>
        public static readonly Scope[] Scopes =
        {
            new("Every setting", "model"),
            new("KV cache types (--kv-k)", "model", "--kv-k"),
            new("Dynamic slots (--slots)", "model", "--slots"),
            new("DCA, context past the trained length (--dca)", "model", "--dca"),
            new("Session affinity and prefix pooling (--session-affinity)", "model", "--session-affinity"),
            new("Council (--council)", "model", "--council"),
            new("GPU / devices (--device-backend)", "model", "--device-backend"),
            new("Engine (--engine)", "model", "--engine"),
            new("Engine policies: KV residency, rolling window, fit, VRAM target, MTP", "model",
                "--kv-residency --kv-rolling-window --fit --vram-target --mtp-policy"),
            new("Drafter's speculative type (--spec-type)", "model", "--spec-type"),
            new("Media engines: image, speech-to-text, text-to-speech, video (--image --stt --tts --video)", "model",
                "--image --stt --tts --video"),
            new("Show what the model runs with, its own or the server's (tweak show model)", "show model"),
            new("Remove the xOllama settings (--clear)", "model", "--clear")
        };

        /// <summary>
        /// The default voice of a speech model, picked from the voices the server lists (offered for one model with
        /// speech, after <see cref="Scopes"/>): it sets --tts-voice.
        /// </summary>
        public static readonly Scope VoiceScope = new("Speech: default voice, picked from the model's voices (--tts-voice)", "model", "--tts-voice");

        /// <summary>Index of the scope that removes the settings.</summary>
        public static int ClearScope => Scopes.Length - 1;

        /// <summary>
        /// The options about the server itself. xOllama answers its settings only from its own machine, so they
        /// are offered only for a server on this one (<see cref="IsOnThisMachine"/>).
        /// </summary>
        public static readonly Scope[] ServerScopes =
        {
            new("Server: defaults for every model, the API key, the GPUs (tweak server)", "server", PerModel: false),
            new("Server: GPUs - priority, backend, link speed, split (tweak server gpu)", "server gpu", PerModel: false),
            new("Server: environment variables, without the environment (tweak envs)", "envs", PerModel: false),
            new("Server: what is set, and where it comes from (tweak show server)", "show server", PerModel: false)
        };

        /// <summary>
        /// Arguments of `xollama tweak` for <paramref name="scope"/>: the command, the model, the scope's flags, then
        /// the typed ones, one element per argument (a quoted value stays one argument, see <see cref="SplitFlags"/>).
        /// </summary>
        public static List<string> Arguments(Scope scope, string? model, string? extraFlags)
        {
            var parts = new List<string> { "tweak" };
            parts.AddRange(scope.Command.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            if (scope.PerModel && model != null) parts.Add(model);
            parts.AddRange(SplitFlags(scope.Flags) ?? new List<string>());
            parts.AddRange(SplitFlags(extraFlags) ?? throw new FormatException("A quote in the flags is not closed."));
            return parts;
        }

        /// <summary>Arguments of `xollama tweak model` for <paramref name="model"/>: the scope's flags, then the typed ones.</summary>
        public static List<string> Arguments(string model, string scopeFlags, string? extraFlags) =>
            Arguments(new Scope("", "model", scopeFlags), model, extraFlags);

        /// <summary>Whether <paramref name="scope"/> only shows settings, so it takes no flags that set one.</summary>
        public static bool IsShowScope(Scope scope) => scope.Command.StartsWith("show", StringComparison.Ordinal);

        /// <summary>
        /// Splits typed flags into arguments the way a shell does: whitespace separates them, single or double
        /// quotes keep spaces in one (<c>--council-instructions="be brief"</c>, <c>'@C:\my dir\file'</c>) and are
        /// removed, a backslash is literal (Windows paths) except before a double quote inside double quotes.
        /// Null when a quote is not closed.
        /// </summary>
        public static List<string>? SplitFlags(string? flags)
        {
            var args = new List<string>();
            if (string.IsNullOrWhiteSpace(flags)) return args;
            var current = new System.Text.StringBuilder();
            var inArg = false;
            char quote = '\0';
            for (var i = 0; i < flags.Length; i++)
            {
                var c = flags[i];
                if (quote != '\0')
                {
                    if (c == quote) quote = '\0';
                    else if (c == '\\' && quote == '"' && i + 1 < flags.Length && flags[i + 1] == '"') current.Append(flags[++i]);
                    else current.Append(c);
                }
                else if (char.IsWhiteSpace(c))
                {
                    if (inArg) args.Add(current.ToString());
                    current.Clear();
                    inArg = false;
                }
                else
                {
                    inArg = true;
                    if (c is '"' or '\'') quote = c;
                    else current.Append(c);
                }
            }
            if (quote != '\0') return null;
            if (inArg) args.Add(current.ToString());
            return args;
        }

        /// <summary>
        /// Whether <paramref name="url"/> reaches the server over loopback, the only way xOllama accepts changes to
        /// its own settings (defaults, GPUs, environment variables): localhost, 127.x, ::1 or 0.0.0.0.
        /// </summary>
        public static bool IsOnThisMachine(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
            var host = uri.Host.Trim('[', ']');
            if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
            return System.Net.IPAddress.TryParse(host, out var ip) &&
                   (System.Net.IPAddress.IsLoopback(ip) || ip.Equals(System.Net.IPAddress.Any) || ip.Equals(System.Net.IPAddress.IPv6Any));
        }

        /// <summary>
        /// The server's defaults for every model's settings (POST /api/xollama/settings with no changes reads
        /// them), as "path value" rows; empty when it has none, null when the server does not answer (an older
        /// xOllama, a remote client, a wrong API key). Sends the API key the xollama CLI would (<see cref="ApiKey"/>).
        /// </summary>
        public static List<(string Path, string Value)>? FetchServerDefaults(string url)
        {
            try
            {
                using var client = new HttpClient { BaseAddress = new Uri(url), Timeout = TimeSpan.FromSeconds(5) };
                using var request = new HttpRequestMessage(HttpMethod.Post, "api/xollama/settings");
                if (ApiKey() is { } key)
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);
                using var response = client.SendAsync(request).GetAwaiter().GetResult();
                if (!response.IsSuccessStatusCode) return null;
                using var doc = JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                return ServerDefaults(doc.RootElement);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// The xOllama API key, in the xollama CLI's order: XOLLAMA_API_KEY, else the file `tweak server --api-key`
        /// writes (~/.ollama/xollama-api-key, %USERPROFILE%\.ollama\xollama-api-key on Windows). Null when neither has one.
        /// </summary>
        public static string? ApiKey(string? environmentKey = null, string? keyFile = null)
        {
            environmentKey ??= Environment.GetEnvironmentVariable("XOLLAMA_API_KEY");
            if (!string.IsNullOrWhiteSpace(environmentKey)) return environmentKey.Trim();
            keyFile ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ollama", "xollama-api-key");
            try
            {
                var key = File.Exists(keyFile) ? File.ReadAllText(keyFile).Trim() : "";
                return key.Length > 0 ? key : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>The "defaults" of a /api/xollama/settings answer as rows (none when it states none).</summary>
        public static List<(string Path, string Value)> ServerDefaults(JsonElement settings) =>
            settings.ValueKind == JsonValueKind.Object && settings.TryGetProperty("defaults", out var defaults)
                ? Flatten(defaults)
                : new List<(string, string)>();

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

        /// <summary>
        /// Settings that are name-to-value maps rather than groups of settings: one row, "name=value" pairs sorted by
        /// name, as `xollama show` prints them (the extra voices of a speech engine, its client-to-model voice names).
        /// </summary>
        private static readonly HashSet<string> MapSettings = new(StringComparer.Ordinal)
        {
            "media.tts.voices", "media.tts.voice_map"
        };

        private static void Flatten(JsonElement element, string prefix, List<(string, string)> rows)
        {
            foreach (var property in element.EnumerateObject())
            {
                var path = prefix.Length == 0 ? property.Name : prefix + "." + property.Name;
                if (path == "version") continue;
                var value = property.Value;
                switch (value.ValueKind)
                {
                    case JsonValueKind.Object when MapSettings.Contains(path):
                        var pairs = value.EnumerateObject()
                            .Where(p => p.Value.ValueKind != JsonValueKind.Null)
                            .Select(p => $"{p.Name}={Scalar(p.Value)}")
                            .Order(StringComparer.Ordinal)
                            .ToList();
                        if (pairs.Count > 0) rows.Add((path, string.Join(",", pairs)));
                        break;
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
        /// Runs `xollama tweak` on the console against the server <paramref name="url"/> (local or remote),
        /// with the console's input and output, so its questions are answered as usual. Returns the exit code.
        /// </summary>
        public static int Run(string cli, string url, IEnumerable<string> arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = cli,
                UseShellExecute = false
            };
            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);
            OllamaServer.ApplyCliEnvironment(startInfo, url);
            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start {cli}");
            process.WaitForExit();
            return process.ExitCode;
        }
    }
}
