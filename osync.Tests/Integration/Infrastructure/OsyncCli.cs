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

        ApplyEnvironment(psi, withHostSettings, environment);

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

    private static void ApplyEnvironment(ProcessStartInfo psi, bool withHostSettings, IReadOnlyDictionary<string, string>? environment)
    {
        // Local operations of osync (and the ollama/xollama CLI it calls) target the test's local server and
        // models dir. Both variable families are set: xOllama reads XOLLAMA_*, which also takes precedence in osync.
        if (withHostSettings)
        {
            psi.Environment["OLLAMA_HOST"] = TestEnvironment.LocalUrl;
            psi.Environment["XOLLAMA_HOST"] = TestEnvironment.LocalUrl;
        }
        else
        {
            // Exercise osync's own discovery of the local server (settings file, default ports)
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
    }

    /// <summary>Whether <see cref="RunInTerminalAsync"/> can run here: Linux with util-linux script(1).</summary>
    public static bool TerminalAvailable { get; } = OperatingSystem.IsLinux() &&
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Any(dir => File.Exists(Path.Combine(dir, "script")));

    /// <summary>
    /// Runs an interactive osync command (e.g. manage) in a pseudo terminal of 120x30 (script(1)), waits for
    /// <paramref name="readyText"/> on the screen, then types <paramref name="keys"/> (see <see cref="TerminalKeys"/>)
    /// with a pause between keys. The result holds everything written to the terminal, without escape sequences.
    /// </summary>
    public static async Task<OsyncResult> RunInTerminalAsync(string arguments, IReadOnlyList<string> keys, string readyText,
        bool withHostSettings = true, IReadOnlyDictionary<string, string>? environment = null, TimeSpan? timeout = null)
    {
        var osync = TestEnvironment.OsyncPath;
        var command = new List<string>();
        if (osync.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) command.Add("dotnet");
        command.Add(osync);
        command.AddRange(SplitArguments(arguments));
        var shellCommand = "stty cols 120 rows 30; exec " + string.Join(" ", command.Select(ShellQuote));

        var psi = new ProcessStartInfo("script")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = WorkDir
        };
        psi.ArgumentList.Add("-qec");
        psi.ArgumentList.Add(shellCommand);
        psi.ArgumentList.Add("/dev/null");
        ApplyEnvironment(psi, withHostSettings, environment);
        psi.Environment["TERM"] = "xterm-256color";

        var stopwatch = Stopwatch.StartNew();
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start script");
        var screen = new StringBuilder();
        var reader = Task.Run(async () =>
        {
            var buffer = new char[4096];
            int read;
            while ((read = await process.StandardOutput.ReadAsync(buffer)) > 0)
                lock (screen) screen.Append(buffer, 0, read);
        });
        var stderr = process.StandardError.ReadToEndAsync();
        string Screen() { lock (screen) return AnsiEscape.Replace(screen.ToString(), ""); }

        var limit = timeout ?? TimeSpan.FromMinutes(1);
        using var cts = new CancellationTokenSource(limit);
        try
        {
            while (!Screen().Contains(readyText, StringComparison.Ordinal))
            {
                if (process.HasExited) break;
                await Task.Delay(200, cts.Token);
            }
            foreach (var key in keys)
            {
                if (process.HasExited) break;
                await Task.Delay(TimeSpan.FromMilliseconds(500), cts.Token);
                await process.StandardInput.WriteAsync(key);
                await process.StandardInput.FlushAsync();
            }
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
            throw new TimeoutException($"osync {arguments} did not finish within {limit}\n--- screen ---\n{Screen()}");
        }
        await reader;

        return new OsyncResult(arguments, process.ExitCode, Screen(), AnsiEscape.Replace(await stderr, ""), stopwatch.Elapsed);
    }

    private static string ShellQuote(string value) => "'" + value.Replace("'", "'\\''") + "'";

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

/// <summary>
/// Keys for <see cref="OsyncCli.RunInTerminalAsync"/>, written as space-separated names: Ctrl+A..Ctrl+Z, Tab,
/// Shift+Tab, Enter, Esc, Space, Backspace, Up, Down, Left, Right, Home, End, F1, F2, or text:abc for typed text.
/// </summary>
public static class TerminalKeys
{
    public static List<string> Parse(string spec) => spec.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Key).ToList();

    private static string Key(string name)
    {
        if (name.StartsWith("text:", StringComparison.Ordinal)) return name["text:".Length..];
        if (name.StartsWith("Ctrl+", StringComparison.OrdinalIgnoreCase) && name.Length == 6 && char.IsAsciiLetter(name[5]))
            return ((char)(char.ToUpperInvariant(name[5]) - 'A' + 1)).ToString();
        return name switch
        {
            "Tab" => "\t",
            "Shift+Tab" => "\x1b[Z",
            "Enter" => "\r",
            "Esc" => "\x1b",
            "Space" => " ",
            "Backspace" => "\x7f",
            "Up" => "\x1b[A",
            "Down" => "\x1b[B",
            "Right" => "\x1b[C",
            "Left" => "\x1b[D",
            "Home" => "\x1b[H",
            "End" => "\x1b[F",
            "F1" => "\x1bOP",
            "F2" => "\x1bOQ",
            _ => throw new ArgumentException($"Unknown key '{name}'")
        };
    }
}
