using System.Text.RegularExpressions;

namespace osync.Tests.Integration.Infrastructure;

/// <summary>
/// Per-scenario state (injected by Reqnroll). Every model a scenario touches lives under a unique
/// prefix, so scenarios are independent of each other and of the developer's own models, and cleanup
/// can remove everything the scenario (or osync) created, even if a step failed halfway.
/// </summary>
public sealed class ScenarioState
{
    /// <summary>Common prefix of all test model names; stale leftovers are removed at test-run start.</summary>
    public const string TestModelPrefix = "osync-t-";

    private static readonly string RunId = Guid.NewGuid().ToString("N")[..6];
    private static int _counter;

    private static readonly Regex Placeholder = new(@"\{([a-z0-9][a-z0-9._-]*)\}", RegexOptions.Compiled);

    public ScenarioState()
    {
        Prefix = $"{TestModelPrefix}{RunId}-{Interlocked.Increment(ref _counter):D3}-";
    }

    /// <summary>Prefix of every model created by this scenario, e.g. osync-t-1a2b3c-007-.</summary>
    public string Prefix { get; }

    public OsyncResult? LastResult { get; set; }

    private string? _configDir;

    /// <summary>
    /// Settings directory (OSYNC_CONFIG_DIR) for every osync run of this scenario: empty unless the scenario
    /// writes a settings file, so the developer's own preferences never influence the tests.
    /// </summary>
    public string ConfigDir => _configDir ??= Directory.CreateTempSubdirectory("osync-test-config-").FullName;

    /// <summary>Registry models (real names, outside the prefix) this scenario pulled and must remove again.</summary>
    public List<(string Server, string Model)> ExtraCleanup { get; } = new();

    /// <summary>
    /// Resolves placeholders in feature text:
    ///   {local} {remote1} {remote2}      server URL, e.g. http://localhost:11435
    ///   {remote1.hostport}               server without scheme, e.g. localhost:11435
    ///   {remote1.host} {remote1.port}    its host and port
    ///   {prefix}                         this scenario's model-name prefix
    ///   {anything-else}                  a model name unique to this scenario: {prefix}anything-else
    /// </summary>
    public string Resolve(string text) => Placeholder.Replace(text, m =>
    {
        var key = m.Groups[1].Value;
        switch (key)
        {
            case "local":
            case "remote1":
            case "remote2":
                return TestEnvironment.ServerUrl(key) ?? throw new InvalidOperationException($"Server '{key}' is not configured");
            case "prefix":
                return Prefix;
        }
        foreach (var part in new[] { ".hostport", ".host", ".port" })
        {
            if (!key.EndsWith(part, StringComparison.Ordinal)) continue;
            var url = TestEnvironment.ServerUrl(key[..^part.Length])
                      ?? throw new InvalidOperationException($"Server '{key}' is not configured");
            var uri = new Uri(url);
            return part switch { ".host" => uri.Host, ".port" => uri.Port.ToString(), _ => uri.Authority };
        }
        return Prefix + key;
    });

    public static OllamaApi Api(string server) =>
        new(TestEnvironment.ServerUrl(server) ?? throw new InvalidOperationException($"Server '{server}' is not configured"));
}
