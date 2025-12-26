using System.Diagnostics;
using System.Text;

namespace osync.Tests.Infrastructure;

public class OsyncRunner
{
    private readonly TestConfiguration _config;

    public OsyncRunner(TestConfiguration config)
    {
        _config = config;
    }

    public async Task<OsyncResult> RunAsync(string arguments, int? timeoutMs = null)
    {
        var timeout = timeoutMs ?? _config.TestTimeout;
        var osyncPath = _config.OsyncExecutablePath ?? FindOsyncExecutable();

        var startInfo = new ProcessStartInfo
        {
            FileName = osyncPath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var output = new StringBuilder();
        var error = new StringBuilder();

        using var process = new Process { StartInfo = startInfo };

        process.OutputDataReceived += (s, e) =>
        {
            if (e.Data != null)
            {
                output.AppendLine(e.Data);
                if (_config.VerboseOutput)
                    Console.WriteLine($"[OUT] {e.Data}");
            }
        };

        process.ErrorDataReceived += (s, e) =>
        {
            if (e.Data != null)
            {
                error.AppendLine(e.Data);
                if (_config.VerboseOutput)
                    Console.WriteLine($"[ERR] {e.Data}");
            }
        };

        var startTime = DateTime.UtcNow;
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var completed = await Task.Run(() => process.WaitForExit(timeout));
        var duration = DateTime.UtcNow - startTime;

        if (!completed)
        {
            try { process.Kill(); } catch { }
            throw new TimeoutException($"Command '{arguments}' timed out after {timeout}ms");
        }

        return new OsyncResult
        {
            ExitCode = process.ExitCode,
            Output = output.ToString(),
            Error = error.ToString(),
            Duration = duration,
            Arguments = arguments
        };
    }

    private string FindOsyncExecutable()
    {
        // Look in build output directories
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var possiblePaths = new[]
        {
            // Look in osync project output
            Path.Combine(baseDir, "..", "..", "..", "..", "osync", "bin", "Debug", "net8.0", "osync.exe"),
            Path.Combine(baseDir, "..", "..", "..", "..", "osync", "bin", "Release", "net8.0", "osync.exe"),
            // Look in current directory
            Path.Combine(baseDir, "osync.exe"),
            Path.Combine(baseDir, "osync"),
            // System PATH
            "osync.exe",
            "osync"
        };

        foreach (var path in possiblePaths)
        {
            var fullPath = Path.GetFullPath(path);
            if (File.Exists(fullPath))
            {
                Console.WriteLine($"Found osync executable at: {fullPath}");
                return fullPath;
            }
        }

        throw new FileNotFoundException(
            "Could not find osync executable. " +
            "Please build the osync project or set OsyncExecutablePath in test-config.json");
    }
}

public class OsyncResult
{
    public int ExitCode { get; set; }
    public string Output { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
    public TimeSpan Duration { get; set; }
    public string Arguments { get; set; } = string.Empty;

    public bool IsSuccess => ExitCode == 0;
}
