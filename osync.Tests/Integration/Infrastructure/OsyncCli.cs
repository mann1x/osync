using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace osync.Tests.Integration.Infrastructure;

/// <summary>Runs the osync CLI as a separate process, exactly like a user would.</summary>
public static class OsyncCli
{
    private static readonly Regex AnsiEscape = new(@"\x1B(?:\[[0-?]*[ -/]*[@-~]|\][^\x07]*\x07|[@-Z\\-_])", RegexOptions.Compiled);

    private static readonly string WorkDir = Directory.CreateTempSubdirectory("osync-tests-").FullName;

    public static async Task<OsyncResult> RunAsync(string arguments, TimeSpan? timeout = null, bool withHostSettings = true,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var osync = TestEnvironment.OsyncPath;
        var isDll = osync.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);

        var psi = new ProcessStartInfo(isDll ? "dotnet" : osync)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true, // closed immediately: tests never interact
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = WorkDir,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        if (isDll) psi.ArgumentList.Add(osync);
        foreach (var arg in SplitArguments(arguments)) psi.ArgumentList.Add(arg);

        // Local operations of osync (and the ollama/xollama CLI it calls) target the test's local server and
        // models dir. Both variable families are set: xOllama reads XOLLAMA_*, which also takes precedence in osync.
        if (withHostSettings)
        {
            psi.Environment["OLLAMA_HOST"] = TestEnvironment.LocalUrl;
            psi.Environment["XOLLAMA_HOST"] = TestEnvironment.LocalUrl;
        }
        else
        {
            // Exercise osync's own discovery of the local server on the default ports
            psi.Environment.Remove("OLLAMA_HOST");
            psi.Environment.Remove("XOLLAMA_HOST");
        }
        if (TestEnvironment.ModelsDir != null)
        {
            psi.Environment["OLLAMA_MODELS"] = TestEnvironment.ModelsDir;
            psi.Environment["XOLLAMA_MODELS"] = TestEnvironment.ModelsDir;
        }
        psi.Environment["NO_COLOR"] = "1";
        if (environment != null)
            foreach (var (name, value) in environment)
                psi.Environment[name] = value;

        var stopwatch = Stopwatch.StartNew();
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start osync");
        process.StandardInput.Close();

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromMinutes(3));
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
            throw new TimeoutException($"osync {arguments} did not finish within {timeout ?? TimeSpan.FromMinutes(3)}");
        }

        return new OsyncResult(
            arguments,
            process.ExitCode,
            AnsiEscape.Replace(await stdout, ""),
            AnsiEscape.Replace(await stderr, ""),
            stopwatch.Elapsed);
    }

    /// <summary>Splits a command line on spaces, honoring double quotes.</summary>
    public static List<string> SplitArguments(string commandLine)
    {
        var args = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false, hasToken = false;
        foreach (var c in commandLine)
        {
            if (c == '"') { inQuotes = !inQuotes; hasToken = true; }
            else if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (hasToken) { args.Add(current.ToString()); current.Clear(); hasToken = false; }
            }
            else { current.Append(c); hasToken = true; }
        }
        if (hasToken) args.Add(current.ToString());
        return args;
    }
}

public sealed record OsyncResult(string Arguments, int ExitCode, string Output, string Error, TimeSpan Duration)
{
    public string AllOutput => Output + Error;

    public override string ToString() =>
        $"osync {Arguments}\nexit code: {ExitCode} ({Duration.TotalSeconds:F1}s)\n--- stdout ---\n{Output}\n--- stderr ---\n{Error}";
}
