using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace osync.Tests.Integration.Infrastructure;

/// <summary>
/// The integration-test base model: a GGUF (Assets/test-model.json) plus the Modelfile that turns it into
/// an Ollama model. Tests create every model they need from it through the Ollama API.
/// </summary>
public sealed class TestModelAsset
{
    public string GgufPath { get; }
    public string FileName { get; }
    public string Digest { get; }
    public string Template { get; }
    public Dictionary<string, object> Parameters { get; }

    private TestModelAsset(string ggufPath, string fileName, string digest, string template, Dictionary<string, object> parameters)
    {
        GgufPath = ggufPath;
        FileName = fileName;
        Digest = digest;
        Template = template;
        Parameters = parameters;
    }

    private static readonly Lazy<(TestModelAsset? Asset, string? Error)> _instance = new(Load);

    public static TestModelAsset? Instance => _instance.Value.Asset;
    public static string? UnavailableReason => _instance.Value.Error;

    private static (TestModelAsset?, string?) Load()
    {
        try
        {
            var lockPath = Path.Combine(TestEnvironment.AssetsDir, "test-model.json");
            using var lockDoc = JsonDocument.Parse(File.ReadAllText(lockPath));
            var fileName = lockDoc.RootElement.GetProperty("file").GetString()!;
            var expectedSha = lockDoc.RootElement.GetProperty("sha256").GetString();

            var gguf = FindGguf(fileName);
            if (gguf == null)
                return (null, $"Test model '{fileName}' not found. Run scripts/get-test-model (.sh or .ps1) or set OSYNC_TEST_MODEL_GGUF.");

            var sha = ComputeSha256(gguf);
            if (!string.IsNullOrEmpty(expectedSha) && !sha.Equals(expectedSha, StringComparison.OrdinalIgnoreCase))
                return (null, $"Test model '{gguf}' has sha256 {sha}, expected {expectedSha}.");

            var (template, parameters) = ParseModelfile(Path.Combine(TestEnvironment.AssetsDir, "Modelfile"));
            return (new TestModelAsset(gguf, fileName, "sha256:" + sha, template, parameters), null);
        }
        catch (Exception ex)
        {
            return (null, $"Test model unavailable: {ex.Message}");
        }
    }

    private static string? FindGguf(string fileName)
    {
        var fromEnv = Environment.GetEnvironmentVariable("OSYNC_TEST_MODEL_GGUF");
        if (!string.IsNullOrWhiteSpace(fromEnv))
            return File.Exists(fromEnv) ? fromEnv : null;

        // Walk up from the test output directory to the repository (osync.sln) and look in osync.Tests/Assets
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "osync.sln")))
            {
                var candidate = Path.Combine(dir.FullName, "osync.Tests", "Assets", fileName);
                return File.Exists(candidate) ? candidate : null;
            }
        }
        return null;
    }

    private static string ComputeSha256(string path)
    {
        // Cache the hash next to the file, keyed by size and timestamp, to avoid rehashing ~90 MB every run
        var info = new FileInfo(path);
        var cachePath = path + ".sha256";
        var key = $"{info.Length}:{info.LastWriteTimeUtc.Ticks}";
        try
        {
            if (File.Exists(cachePath))
            {
                var parts = File.ReadAllText(cachePath).Trim().Split(' ');
                if (parts.Length == 2 && parts[1] == key) return parts[0];
            }
        }
        catch { /* recompute */ }

        using var stream = File.OpenRead(path);
        var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        try { File.WriteAllText(cachePath, $"{hash} {key}"); } catch { /* read-only location */ }
        return hash;
    }

    /// <summary>Minimal Modelfile parser for the directives the test Modelfile uses: FROM, TEMPLATE, PARAMETER.</summary>
    private static (string Template, Dictionary<string, object> Parameters) ParseModelfile(string path)
    {
        var text = File.ReadAllText(path).Replace("\r\n", "\n");
        var template = "";
        var tm = Regex.Match(text, "^TEMPLATE \"\"\"(.*?)\"\"\"", RegexOptions.Singleline | RegexOptions.Multiline);
        if (tm.Success) template = tm.Groups[1].Value;

        var parameters = new Dictionary<string, object>();
        foreach (Match m in Regex.Matches(text, "^PARAMETER (\\S+) (.+)$", RegexOptions.Multiline))
        {
            var name = m.Groups[1].Value;
            var raw = m.Groups[2].Value.Trim();
            if (raw.StartsWith('"') && raw.EndsWith('"'))
            {
                var value = raw[1..^1];
                if (name == "stop")
                {
                    if (!parameters.TryGetValue("stop", out var list))
                        parameters["stop"] = list = new List<string>();
                    ((List<string>)list).Add(value);
                }
                else
                {
                    parameters[name] = value;
                }
            }
            else if (long.TryParse(raw, out var l)) parameters[name] = l;
            else if (double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d)) parameters[name] = d;
            else parameters[name] = raw;
        }
        return (template, parameters);
    }
}
