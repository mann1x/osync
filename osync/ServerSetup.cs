namespace osync
{
    /// <summary>
    /// Configuration of the local server (Ollama, xOllama or both side by side, host, ports), used by
    /// `osync install` and `osync setup server`. Defaults come from the saved settings, else from what answers on
    /// the default local ports.
    /// </summary>
    internal static class ServerSetup
    {
        /// <summary>Result of probing a server URL: reachable or not, its flavor and version.</summary>
        internal sealed record Probe(bool Reachable, ServerFlavor Flavor, string? Version);

        /// <summary>Aliases osync creates for the two servers when both run side by side.</summary>
        public const string OllamaAlias = "ollama";
        public const string XOllamaAlias = "xollama";

        /// <summary>
        /// Install-time configuration: asks only when it is not clear which server to use. Nothing is asked when
        /// XOLLAMA_HOST / OLLAMA_HOST is set or the settings already name a server; when exactly one server answers
        /// on the default local ports it is used; when none or both answer, the user is asked (if
        /// <paramref name="interactive"/>). Returns true when <paramref name="settings"/> changed and must be saved.
        /// </summary>
        public static bool AutoConfigure(OsyncSettings settings, TextReader input, TextWriter output, Func<string, Probe> probe,
            Func<string, string?> env, bool interactive)
        {
            var overriding = OllamaServer.OverridingEnvironmentVariable(env, settings);
            if (overriding != null)
            {
                output.WriteLine($"Local server: {overriding}={env(overriding)} (it takes precedence over the settings; " +
                                 "osync setup server lets the settings win)");
                return false;
            }

            if (settings.ConfiguredServerUrl != null)
            {
                output.WriteLine($"Local server: {Describe(settings)} (change it with: osync setup server)");
                return false;
            }

            var ollama = probe($"http://localhost:{OllamaServer.OllamaDefaultPort}");
            var xollama = probe($"http://localhost:{OllamaServer.XOllamaDefaultPort}");
            if (ollama.Reachable != xollama.Reachable)
            {
                var (found, port) = ollama.Reachable ? (ollama, OllamaServer.OllamaDefaultPort) : (xollama, OllamaServer.XOllamaDefaultPort);
                var flavor = found.Flavor == ServerFlavor.Unknown ? (ollama.Reachable ? ServerFlavor.Ollama : ServerFlavor.XOllama) : found.Flavor;
                settings.Server.Flavor = FlavorName(flavor);
                settings.Server.Host = null;
                settings.Server.Port = port == DefaultPort(flavor) ? null : port;
                settings.Server.Both = null;
                output.WriteLine($"{Out.Paint("✓", p => p.Success, bold: true)} Found {OllamaServer.DisplayName(flavor)} {found.Version} on localhost:{port}: using it (change it with: osync setup server)");
                return true;
            }

            if (!interactive)
            {
                output.WriteLine(ollama.Reachable
                    ? "Ollama and xOllama both answer on this machine: run 'osync setup server' to choose."
                    : "No Ollama or xOllama server answers on this machine: run 'osync setup server' once it runs elsewhere.");
                return false;
            }
            return Configure(settings, input, output, probe, ollama, xollama, env);
        }

        /// <summary>
        /// Asks for the server type (Ollama, xOllama or both), host and ports, tests the connection and stores the
        /// answers in <paramref name="settings"/>. Returns false when the input ended before all answers were given.
        /// </summary>
        public static bool Configure(OsyncSettings settings, TextReader input, TextWriter output, Func<string, Probe> probe,
            Func<string, string?>? env = null) =>
            Configure(settings, input, output, probe, null, null, env ?? Environment.GetEnvironmentVariable);

        private static bool Configure(OsyncSettings settings, TextReader input, TextWriter output, Func<string, Probe> probe,
            Probe? ollama, Probe? xollama, Func<string, string?> env)
        {
            output.WriteLine(Out.Heading("Local server"));

            // Defaults: saved settings first, else what runs on the default local ports
            ServerChoice? defaultChoice = settings.Server.Both == true ? ServerChoice.Both
                : settings.ConfiguredFlavor switch { ServerFlavor.Ollama => ServerChoice.Ollama, ServerFlavor.XOllama => ServerChoice.XOllama, _ => null };
            if (defaultChoice == null)
            {
                ollama ??= probe($"http://localhost:{OllamaServer.OllamaDefaultPort}");
                xollama ??= probe($"http://localhost:{OllamaServer.XOllamaDefaultPort}");
                if (ollama.Reachable) output.WriteLine($"Found {OllamaServer.DisplayName(ollama.Flavor)} {ollama.Version} on localhost:{OllamaServer.OllamaDefaultPort}");
                if (xollama.Reachable) output.WriteLine($"Found {OllamaServer.DisplayName(xollama.Flavor)} {xollama.Version} on localhost:{OllamaServer.XOllamaDefaultPort}");
                defaultChoice = ollama.Reachable && xollama.Reachable ? ServerChoice.Both
                    : xollama.Reachable && !ollama.Reachable ? ServerChoice.XOllama
                    : ServerChoice.Ollama;
            }

            var choiceAnswer = Ask(input, output,
                $"Server type: 1) Ollama  2) xOllama  3) Both (side by side) [{(int)defaultChoice.Value}]: ",
                a => a is "1" or "2" or "3" or "ollama" or "xollama" or "both");
            if (choiceAnswer == null) return false;
            var choice = choiceAnswer switch
            {
                "" => defaultChoice.Value,
                "1" or "ollama" => ServerChoice.Ollama,
                "2" or "xollama" => ServerChoice.XOllama,
                _ => ServerChoice.Both
            };

            var defaultHost = string.IsNullOrWhiteSpace(settings.Server.Host) ? "localhost" : settings.Server.Host!;
            var hostAnswer = Ask(input, output, $"Host or IP [{defaultHost}]: ",
                a => Uri.CheckHostName(a) != UriHostNameType.Unknown);
            if (hostAnswer == null) return false;
            var host = hostAnswer.Length == 0 ? defaultHost : hostAnswer;

            if (choice != ServerChoice.Both)
            {
                var flavor = choice == ServerChoice.XOllama ? ServerFlavor.XOllama : ServerFlavor.Ollama;
                var flavorPort = DefaultPort(flavor);
                // Keep a saved custom port only if the flavor did not change
                var defaultPort = settings.ConfiguredFlavor == flavor && settings.Server.Both != true && settings.Server.Port != null
                    ? settings.Server.Port.Value : flavorPort;
                var port = AskPort(input, output, "Port", defaultPort);
                if (port == null) return false;

                settings.Server.Flavor = FlavorName(flavor);
                settings.Server.Host = host;
                settings.Server.Port = port == flavorPort ? null : port;
                var removed = RemoveBothAliases(settings, host);
                settings.Server.Both = null;
                if (removed.Count > 0)
                    output.WriteLine($"Removed the aliases {string.Join(", ", removed)} (only one server now).");

                Report(output, probe, flavor, host, port.Value);
                return AskEnvironmentPrecedence(settings, input, output, env);
            }

            var ollamaPort = AskPort(input, output, "Ollama port", AliasPort(settings, OllamaAlias) ?? OllamaServer.OllamaDefaultPort);
            if (ollamaPort == null) return false;
            var xollamaPort = AskPort(input, output, "xOllama port", AliasPort(settings, XOllamaAlias) ?? OllamaServer.XOllamaDefaultPort);
            if (xollamaPort == null) return false;
            var currentDefault = settings.ConfiguredFlavor == ServerFlavor.XOllama ? "2" : "1";
            var defaultAnswer = Ask(input, output, $"Default server (used when no server is given): 1) Ollama  2) xOllama [{currentDefault}]: ",
                a => a is "1" or "2" or "ollama" or "xollama");
            if (defaultAnswer == null) return false;
            var defaultFlavor = (defaultAnswer.Length == 0 ? currentDefault : defaultAnswer) is "2" or "xollama"
                ? ServerFlavor.XOllama : ServerFlavor.Ollama;

            var defaultServerPort = defaultFlavor == ServerFlavor.XOllama ? xollamaPort.Value : ollamaPort.Value;
            settings.Server.Flavor = FlavorName(defaultFlavor);
            settings.Server.Host = host;
            settings.Server.Port = defaultServerPort == DefaultPort(defaultFlavor) ? null : defaultServerPort;
            settings.Server.Both = true;
            settings.Aliases[OllamaAlias] = Url(host, ollamaPort.Value);
            settings.Aliases[XOllamaAlias] = Url(host, xollamaPort.Value);
            output.WriteLine($"Aliases: {Out.Paint(OllamaAlias, p => p.Heading)} = {Url(host, ollamaPort.Value)}, " +
                             $"{Out.Paint(XOllamaAlias, p => p.Heading)} = {Url(host, xollamaPort.Value)} (e.g. osync ls -d {XOllamaAlias})");

            Report(output, probe, ServerFlavor.Ollama, host, ollamaPort.Value);
            Report(output, probe, ServerFlavor.XOllama, host, xollamaPort.Value);
            return AskEnvironmentPrecedence(settings, input, output, env);
        }

        /// <summary>
        /// When XOLLAMA_HOST / OLLAMA_HOST is set, asks whether it or the configured server decides the local
        /// server (<see cref="OsyncSettings.ServerSettings.IgnoreEnvironment"/>). Returns false at the end of input.
        /// </summary>
        private static bool AskEnvironmentPrecedence(OsyncSettings settings, TextReader input, TextWriter output, Func<string, string?> env)
        {
            var variable = OllamaServer.EnvironmentServerVariable(env);
            if (variable == null) return true;

            output.WriteLine($"{variable}={env(variable)} is set in the environment.");
            var answer = Ask(input, output, $"Local server: 1) the one configured here  2) {variable} [1]: ", a => a is "1" or "2");
            if (answer == null) return false;
            settings.Server.IgnoreEnvironment = answer is "" or "1" ? true : null;
            output.WriteLine(settings.Server.IgnoreEnvironment == true
                ? $"The settings take precedence over {variable}."
                : $"{variable} takes precedence over the settings.");
            return true;
        }

        private enum ServerChoice { Ollama = 1, XOllama = 2, Both = 3 }

        /// <summary>Short description of the configured local server(s), e.g. "Ollama @ localhost:11434".</summary>
        public static string Describe(OsyncSettings settings)
        {
            var url = settings.ConfiguredServerUrl;
            if (url == null) return "auto-detect (localhost:11434, then localhost:22434)";
            var flavor = settings.ConfiguredFlavor;
            var main = $"{(flavor == null ? "auto" : OllamaServer.DisplayName(flavor.Value))} @ {new Uri(url).Authority}";
            if (settings.Server.Both != true) return main;
            var other = flavor == ServerFlavor.XOllama ? OllamaAlias : XOllamaAlias;
            return settings.Aliases.TryGetValue(other, out var otherUrl)
                ? $"{main} (default), {OllamaServer.DisplayName(flavor == ServerFlavor.XOllama ? ServerFlavor.Ollama : ServerFlavor.XOllama)} @ {new Uri(otherUrl).Authority} (alias {other})"
                : $"{main} (default)";
        }

        private static void Report(TextWriter output, Func<string, Probe> probe, ServerFlavor flavor, string host, int port)
        {
            var url = Url(host, port);
            var result = probe(url);
            if (!result.Reachable)
                output.WriteLine($"{Out.Paint("Warning:", p => p.Warning, bold: true)} no server answers at {url} right now (saved anyway).");
            else if (result.Flavor != flavor && result.Flavor != ServerFlavor.Unknown)
                output.WriteLine($"{Out.Paint("Warning:", p => p.Warning, bold: true)} {url} is {OllamaServer.DisplayName(result.Flavor)} {result.Version}, not {OllamaServer.DisplayName(flavor)}.");
            else
                output.WriteLine($"{Out.Paint("✓", p => p.Success, bold: true)} Connected to {OllamaServer.DisplayName(result.Flavor)} {result.Version} at {url}");
        }

        private static int? AskPort(TextReader input, TextWriter output, string label, int defaultPort)
        {
            var answer = Ask(input, output, $"{label} [{defaultPort}]: ", a => int.TryParse(a, out var p) && p is > 0 and < 65536);
            if (answer == null) return null;
            return answer.Length == 0 ? defaultPort : int.Parse(answer);
        }

        private static int? AliasPort(OsyncSettings settings, string alias) =>
            settings.Aliases.TryGetValue(alias, out var url) && Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Port : null;

        /// <summary>Removes the "ollama"/"xollama" aliases created for side-by-side servers on <paramref name="host"/>.</summary>
        private static List<string> RemoveBothAliases(OsyncSettings settings, string host)
        {
            var removed = new List<string>();
            if (settings.Server.Both != true) return removed;
            foreach (var alias in new[] { OllamaAlias, XOllamaAlias })
            {
                if (settings.Aliases.TryGetValue(alias, out var url) && Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                    (string.Equals(uri.Host, host, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(uri.Host, settings.Server.Host ?? "localhost", StringComparison.OrdinalIgnoreCase)))
                {
                    settings.Aliases.Remove(alias);
                    removed.Add(alias);
                }
            }
            return removed;
        }

        internal static string FlavorName(ServerFlavor flavor) => flavor == ServerFlavor.XOllama ? "xollama" : "ollama";

        internal static int DefaultPort(ServerFlavor flavor) =>
            flavor == ServerFlavor.XOllama ? OllamaServer.XOllamaDefaultPort : OllamaServer.OllamaDefaultPort;

        internal static string Url(string host, int port) => OllamaServer.ToClientUrl($"{host}:{port}", port);

        /// <summary>
        /// Prompts until the answer is empty (= default) or valid, at most 3 times. Returns "" for the default
        /// (also after 3 invalid answers) and null when the input has ended.
        /// </summary>
        internal static string? Ask(TextReader input, TextWriter output, string prompt, Func<string, bool> isValid)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                output.Write(prompt);
                var line = input.ReadLine();
                if (line == null) return null;
                var answer = line.Trim();
                if (answer.Equals("ollama", StringComparison.OrdinalIgnoreCase) || answer.Equals("xollama", StringComparison.OrdinalIgnoreCase) ||
                    answer.Equals("both", StringComparison.OrdinalIgnoreCase))
                    answer = answer.ToLowerInvariant();
                if (answer.Length == 0 || isValid(answer)) return answer;
                output.WriteLine("  Invalid answer, please try again.");
            }
            output.WriteLine("  Using the default.");
            return "";
        }

        /// <summary>Probes a server over HTTP: /api/version, then flavor detection.</summary>
        public static Probe ProbeServer(string url)
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
                using var response = http.GetAsync($"{url}/api/version").GetAwaiter().GetResult();
                if (!response.IsSuccessStatusCode) return new Probe(false, ServerFlavor.Unknown, null);
                using var doc = System.Text.Json.JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                var version = doc.RootElement.TryGetProperty("version", out var v) ? v.GetString() : null;
                return new Probe(true, OllamaServer.GetFlavor(url), version);
            }
            catch
            {
                return new Probe(false, ServerFlavor.Unknown, null);
            }
        }
    }
}
