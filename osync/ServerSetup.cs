namespace osync
{
    /// <summary>
    /// Interactive configuration of the local server (Ollama or xOllama, host, port), used by `osync install`.
    /// Defaults come from the saved settings, else from what answers on the default local ports.
    /// </summary>
    internal static class ServerSetup
    {
        /// <summary>Result of probing a server URL: reachable or not, its flavor and version.</summary>
        internal sealed record Probe(bool Reachable, ServerFlavor Flavor, string? Version);

        /// <summary>
        /// Asks for the server type, host and port, tests the connection and stores the answers in
        /// <paramref name="settings"/>. Returns false when the input ended before all answers were given.
        /// </summary>
        public static bool Configure(OsyncSettings settings, TextReader input, TextWriter output, Func<string, Probe> probe)
        {
            output.WriteLine("Local server");
            output.WriteLine("------------");

            // Defaults: saved settings first, else what runs on the default local ports
            var defaultFlavor = settings.ConfiguredFlavor;
            if (defaultFlavor == null)
            {
                var ollama = probe($"http://localhost:{OllamaServer.OllamaDefaultPort}");
                var xollama = probe($"http://localhost:{OllamaServer.XOllamaDefaultPort}");
                if (ollama.Reachable) output.WriteLine($"Found {OllamaServer.DisplayName(ollama.Flavor)} {ollama.Version} on localhost:{OllamaServer.OllamaDefaultPort}");
                if (xollama.Reachable) output.WriteLine($"Found {OllamaServer.DisplayName(xollama.Flavor)} {xollama.Version} on localhost:{OllamaServer.XOllamaDefaultPort}");
                defaultFlavor = ollama.Reachable ? ollama.Flavor : xollama.Reachable ? xollama.Flavor : ServerFlavor.Ollama;
            }

            var flavorAnswer = Ask(input, output,
                $"Server type: 1) Ollama  2) xOllama [{(defaultFlavor == ServerFlavor.XOllama ? "2" : "1")}]: ",
                a => a is "1" or "2" or "ollama" or "xollama");
            if (flavorAnswer == null) return false;
            var flavor = flavorAnswer.Length == 0
                ? defaultFlavor.Value
                : flavorAnswer is "2" or "xollama" ? ServerFlavor.XOllama : ServerFlavor.Ollama;

            var defaultHost = string.IsNullOrWhiteSpace(settings.Server.Host) ? "localhost" : settings.Server.Host!;
            var hostAnswer = Ask(input, output, $"Host or IP [{defaultHost}]: ",
                a => Uri.CheckHostName(a) != UriHostNameType.Unknown);
            if (hostAnswer == null) return false;
            var host = hostAnswer.Length == 0 ? defaultHost : hostAnswer;

            var flavorPort = flavor == ServerFlavor.XOllama ? OllamaServer.XOllamaDefaultPort : OllamaServer.OllamaDefaultPort;
            // Keep a saved custom port only if the flavor did not change
            var defaultPort = settings.ConfiguredFlavor == flavor && settings.Server.Port != null ? settings.Server.Port.Value : flavorPort;
            var portAnswer = Ask(input, output, $"Port [{defaultPort}]: ",
                a => int.TryParse(a, out var p) && p is > 0 and < 65536);
            if (portAnswer == null) return false;
            var port = portAnswer.Length == 0 ? defaultPort : int.Parse(portAnswer);

            settings.Server.Flavor = flavor == ServerFlavor.XOllama ? "xollama" : "ollama";
            settings.Server.Host = host;
            settings.Server.Port = port == flavorPort ? null : port;

            var url = OllamaServer.ToClientUrl($"{host}:{port}", port);
            var result = probe(url);
            if (!result.Reachable)
                output.WriteLine($"Warning: no server answers at {url} right now (saved anyway).");
            else if (result.Flavor != flavor && result.Flavor != ServerFlavor.Unknown)
                output.WriteLine($"Warning: {url} is {OllamaServer.DisplayName(result.Flavor)} {result.Version}, not {OllamaServer.DisplayName(flavor)}.");
            else
                output.WriteLine($"✓ Connected to {OllamaServer.DisplayName(result.Flavor)} {result.Version} at {url}");
            return true;
        }

        /// <summary>
        /// Prompts until the answer is empty (= default) or valid, at most 3 times. Returns "" for the default
        /// (also after 3 invalid answers) and null when the input has ended.
        /// </summary>
        private static string? Ask(TextReader input, TextWriter output, string prompt, Func<string, bool> isValid)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                output.Write(prompt);
                var line = input.ReadLine();
                if (line == null) return null;
                var answer = line.Trim();
                if (answer.Equals("ollama", StringComparison.OrdinalIgnoreCase) || answer.Equals("xollama", StringComparison.OrdinalIgnoreCase))
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
