using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace osync
{
    /// <summary>Which server implementation answers at a URL.</summary>
    internal enum ServerFlavor
    {
        Unknown,
        Ollama,
        /// <summary>xOllama (github.com/mann1x/xollama): Ollama fork, same API, default port 22434.</summary>
        XOllama
    }

    /// <summary>
    /// Resolution of the local server URL, server flavor detection (Ollama / xOllama) and the CLI binary
    /// osync shells out to for local operations. Single source of truth for all commands.
    /// </summary>
    internal static class OllamaServer
    {
        public const int OllamaDefaultPort = 11434;
        public const int XOllamaDefaultPort = 22434;

        private static readonly HttpClient ProbeClient = new() { Timeout = TimeSpan.FromMilliseconds(1500) };
        private static readonly ConcurrentDictionary<string, ServerFlavor> FlavorCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object LocalLock = new();
        private static string? _localUrl;
        private static string? _cliName;

        /// <summary>
        /// Server URL for a command: the -d destination when given, otherwise the local server.
        /// </summary>
        public static string ResolveHost(string? destination) =>
            string.IsNullOrWhiteSpace(destination)
                ? LocalUrl
                : OsyncProgram.NormalizeServerUrl(destination);

        /// <summary>
        /// Local server URL: XOLLAMA_HOST, then OLLAMA_HOST, then whichever of localhost:11434 (Ollama)
        /// or localhost:22434 (xOllama) answers, defaulting to localhost:11434.
        /// </summary>
        public static string LocalUrl
        {
            get
            {
                lock (LocalLock)
                {
                    return _localUrl ??= DetermineLocalUrl();
                }
            }
        }

        private static string DetermineLocalUrl()
        {
            var xollamaHost = Environment.GetEnvironmentVariable("XOLLAMA_HOST");
            if (!string.IsNullOrWhiteSpace(xollamaHost))
                return ToClientUrl(xollamaHost, XOllamaDefaultPort);

            var ollamaHost = Environment.GetEnvironmentVariable("OLLAMA_HOST");
            if (!string.IsNullOrWhiteSpace(ollamaHost))
                return ToClientUrl(ollamaHost, OllamaDefaultPort);

            var ollamaUrl = $"http://localhost:{OllamaDefaultPort}";
            if (Responds(ollamaUrl)) return ollamaUrl;
            var xollamaUrl = $"http://localhost:{XOllamaDefaultPort}";
            if (Responds(xollamaUrl)) return xollamaUrl;
            return ollamaUrl;
        }

        /// <summary>
        /// Turns a host setting (bind address or URL) into a URL a client can connect to:
        /// adds the scheme and the default port, and maps bind-all addresses (0.0.0.0, ::) to localhost.
        /// </summary>
        public static string ToClientUrl(string host, int defaultPort)
        {
            var value = host.Trim().TrimEnd('/');
            if (!value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                value = "http://" + value;
            }

            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
                return value;

            var hostName = uri.Host;
            if (hostName is "0.0.0.0" or "[::]" or "::" or "[::0]" or "::0")
                hostName = "localhost";

            // Uri reports the scheme default (80/443) when no port was written
            var authority = value[(value.IndexOf("://", StringComparison.Ordinal) + 3)..];
            var slash = authority.IndexOf('/');
            if (slash >= 0) authority = authority[..slash];
            var hasExplicitPort = authority.StartsWith('[')
                ? authority.Contains("]:")
                : authority.Contains(':');
            var port = hasExplicitPort ? uri.Port : defaultPort;

            var builder = new UriBuilder(uri.Scheme, hostName, port);
            return builder.Uri.ToString().TrimEnd('/');
        }

        private static readonly ConcurrentDictionary<string, int> DefaultPortCache = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Port to use for a server given without one: 11434 (Ollama). When the host is reachable but refuses
        /// connections on 11434 and accepts them on 22434, it is an xOllama server and 22434 is used.
        /// Unreachable hosts keep 11434 (the error then names the expected Ollama port). Cached per host.
        /// </summary>
        public static int DefaultPortFor(string host) =>
            DefaultPortCache.GetOrAdd(host, h =>
                TryConnect(h, OllamaDefaultPort) == ConnectResult.Refused && TryConnect(h, XOllamaDefaultPort) == ConnectResult.Connected
                    ? XOllamaDefaultPort
                    : OllamaDefaultPort);

        private enum ConnectResult { Connected, Refused, Failed }

        private static ConnectResult TryConnect(string host, int port)
        {
            try
            {
                using var client = new System.Net.Sockets.TcpClient();
                using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(1500));
                client.ConnectAsync(host, port, cts.Token).AsTask().GetAwaiter().GetResult();
                return ConnectResult.Connected;
            }
            catch (System.Net.Sockets.SocketException ex) when (ex.SocketErrorCode == System.Net.Sockets.SocketError.ConnectionRefused)
            {
                return ConnectResult.Refused;
            }
            catch
            {
                return ConnectResult.Failed;
            }
        }

        /// <summary>Detects whether the server at <paramref name="url"/> is Ollama or xOllama (cached).</summary>
        public static ServerFlavor GetFlavor(string url) =>
            FlavorCache.GetOrAdd(url.TrimEnd('/'), DetectFlavor);

        private static ServerFlavor DetectFlavor(string url)
        {
            try
            {
                // xOllama answers GET /api/xollama with {"xollama":true,"version":"..."}; Ollama returns 404
                using var response = ProbeClient.GetAsync($"{url}/api/xollama").GetAwaiter().GetResult();
                if (response.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                    if (doc.RootElement.TryGetProperty("xollama", out var flag) && flag.ValueKind == JsonValueKind.True)
                        return ServerFlavor.XOllama;
                }

                // Older xOllama builds without the identity route report it in the version string
                using var version = ProbeClient.GetAsync($"{url}/api/version").GetAwaiter().GetResult();
                if (!version.IsSuccessStatusCode) return ServerFlavor.Unknown;
                var body = version.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                return body.Contains("xollama", StringComparison.OrdinalIgnoreCase) ? ServerFlavor.XOllama : ServerFlavor.Ollama;
            }
            catch
            {
                return ServerFlavor.Unknown;
            }
        }

        public static string DisplayName(ServerFlavor flavor) => flavor == ServerFlavor.XOllama ? "xOllama" : "Ollama";

        /// <summary>
        /// CLI binary for local operations: OSYNC_OLLAMA_CLI when set; otherwise "xollama" when the local
        /// server is xOllama (or only xollama is installed), else "ollama".
        /// </summary>
        public static string CliName
        {
            get
            {
                if (_cliName != null) return _cliName;

                var configured = Environment.GetEnvironmentVariable("OSYNC_OLLAMA_CLI");
                if (!string.IsNullOrWhiteSpace(configured))
                    return _cliName = configured.Trim();

                bool hasOllama = IsOnPath("ollama");
                bool hasXOllama = IsOnPath("xollama");
                if (hasXOllama && (!hasOllama || GetFlavor(LocalUrl) == ServerFlavor.XOllama))
                    return _cliName = "xollama";
                return _cliName = "ollama";
            }
        }

        /// <summary>
        /// Creates a process for the local CLI (ollama / xollama) that talks to the local server osync resolved.
        /// </summary>
        public static Process CreateCliProcess(string arguments)
        {
            var process = new Process();
            process.StartInfo.FileName = CliName;
            process.StartInfo.Arguments = arguments;
            ApplyCliEnvironment(process.StartInfo);
            return process;
        }

        /// <summary>Points the ollama/xollama CLI at the resolved local server (each CLI reads its own variable).</summary>
        public static void ApplyCliEnvironment(ProcessStartInfo startInfo)
        {
            var hostPort = new Uri(LocalUrl).Authority;
            startInfo.Environment["OLLAMA_HOST"] = hostPort;
            startInfo.Environment["XOLLAMA_HOST"] = hostPort;
        }

        /// <summary>Models directory from the environment: XOLLAMA_MODELS (xOllama's override) then OLLAMA_MODELS.</summary>
        public static string? ModelsDirFromEnvironment()
        {
            foreach (var name in new[] { "XOLLAMA_MODELS", "OLLAMA_MODELS" })
            {
                var value = Environment.GetEnvironmentVariable(name);
                if (string.IsNullOrEmpty(value) && OperatingSystem.IsWindows())
                    value = Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User);
                if (!string.IsNullOrEmpty(value))
                    return value;
            }
            return null;
        }

        private static bool Responds(string url)
        {
            try
            {
                using var response = ProbeClient.GetAsync($"{url}/api/version").GetAwaiter().GetResult();
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsOnPath(string executable)
        {
            var names = OperatingSystem.IsWindows()
                ? new[] { executable + ".exe", executable + ".cmd", executable }
                : new[] { executable };
            var path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                foreach (var name in names)
                {
                    try
                    {
                        if (File.Exists(Path.Combine(dir.Trim('"'), name)))
                            return true;
                    }
                    catch
                    {
                        // Ignore malformed PATH entries
                    }
                }
            }
            return false;
        }
    }
}
