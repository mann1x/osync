using System.Diagnostics;

namespace osync.Tests.Integration.Infrastructure;

/// <summary>
/// Integration-test configuration, read from environment variables.
///
///   OSYNC_TEST_LOCAL        Local Ollama server URL (default: OLLAMA_HOST, then http://localhost:11434)
///   OSYNC_TEST_REMOTE1      First remote Ollama server URL (remote tests are skipped when unset)
///   OSYNC_TEST_REMOTE2      Second remote Ollama server URL (remote-to-remote tests are skipped when unset)
///   OSYNC_TEST_MODELS_DIR   Models directory of the local server, passed to osync as OLLAMA_MODELS
///                           (default: OLLAMA_MODELS if set)
///   OSYNC_TEST_MODEL_GGUF   Path of the base model GGUF (default: osync.Tests/Assets/&lt;file&gt; in the repo)
///   OSYNC_TEST_REGISTRY     "1" enables tests that download from registry.ollama.ai / huggingface.co
///   OSYNC_TEST_EXCLUSIVE    "1" declares the remote servers dedicated to tests (enables e.g. "unload all")
///   OSYNC_TEST_OSYNC        osync executable or dll to test (default: osync.dll built next to the tests)
/// </summary>
public static class TestEnvironment
{
    public static string LocalUrl { get; } = Normalize(
        Env("OSYNC_TEST_LOCAL") ?? Env("OLLAMA_HOST") ?? "http://localhost:11434");

    public static string? Remote1Url { get; } = Env("OSYNC_TEST_REMOTE1") is { } r1 ? Normalize(r1) : null;
    public static string? Remote2Url { get; } = Env("OSYNC_TEST_REMOTE2") is { } r2 ? Normalize(r2) : null;

    public static string? ModelsDir { get; } = Env("OSYNC_TEST_MODELS_DIR") ?? Env("OLLAMA_MODELS");

    public static bool RegistryEnabled { get; } = Env("OSYNC_TEST_REGISTRY") == "1";
    public static bool ExclusiveServers { get; } = Env("OSYNC_TEST_EXCLUSIVE") == "1";

    public static string OsyncPath { get; } = Env("OSYNC_TEST_OSYNC") ?? Path.Combine(AppContext.BaseDirectory, "osync.dll");

    public static string AssetsDir { get; } = Path.Combine(AppContext.BaseDirectory, "Assets");

    /// <summary>Server URL for a logical server name used in feature files: local, remote1, remote2.</summary>
    public static string? ServerUrl(string server) => server switch
    {
        "local" => LocalUrl,
        "remote1" => Remote1Url,
        "remote2" => Remote2Url,
        _ => throw new ArgumentException($"Unknown server '{server}' (expected local, remote1 or remote2)")
    };

    private static readonly Dictionary<string, bool> _reachable = new();

    public static bool IsReachable(string? url)
    {
        if (string.IsNullOrEmpty(url)) return false;
        lock (_reachable)
        {
            if (_reachable.TryGetValue(url, out var cached)) return cached;
            bool ok;
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                ok = http.GetAsync($"{url}/api/version").GetAwaiter().GetResult().IsSuccessStatusCode;
            }
            catch
            {
                ok = false;
            }
            _reachable[url] = ok;
            return ok;
        }
    }

    private static bool? _cliAvailable;

    /// <summary>Whether the ollama CLI is on PATH (osync shells out to it for local operations).</summary>
    public static bool OllamaCliAvailable
    {
        get
        {
            if (_cliAvailable.HasValue) return _cliAvailable.Value;
            try
            {
                using var p = Process.Start(new ProcessStartInfo("ollama", "--version")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                })!;
                p.WaitForExit(10000);
                _cliAvailable = true;
            }
            catch
            {
                _cliAvailable = false;
            }
            return _cliAvailable.Value;
        }
    }

    private static string? Env(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string Normalize(string url)
    {
        url = url.TrimEnd('/');
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            url = "http://" + url;
        if (url.StartsWith("http://0.0.0.0", StringComparison.Ordinal))
            url = "http://localhost" + url["http://0.0.0.0".Length..];
        // Add the default port when none is given
        var hostPart = url[(url.IndexOf("://", StringComparison.Ordinal) + 3)..];
        if (!hostPart.Contains(':'))
            url += ":11434";
        return url;
    }
}
