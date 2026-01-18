// Ignore Spelling: osync ollama modelfile statusline versionline

using PowerArgs.Samples;
using PowerArgs;
using System;
using System.Net;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text;
using System.Reflection.Emit;
using TqdmSharp;
using static PowerArgs.Ansi.Cursor;
using static System.Net.WebRequestMethods;
using System.Net.Http.Json;
using System.IO;
using System.Xml.Linq;
using System.Reflection;
using Born2Code.Net;
using static PrettyConsole.Console;
using Console = PrettyConsole.Console;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Globalization;
using System.Text.RegularExpressions;
using Windows.Devices.Power;
using Microsoft.VisualBasic;
using static System.Runtime.InteropServices.JavaScript.JSType;
using System.Drawing;
using Spectre.Console;
using ByteSizeLib;

namespace osync
{
    /// <summary>
    /// Module initializer to fix terminal detection issues before any library loads.
    /// This runs before Main() and before any static constructors in the assembly.
    /// </summary>
    internal static class TerminalInitializer
    {
        // Store original TERM to restore on exit
        internal static string? OriginalTerm { get; private set; }

        [ModuleInitializer]
        internal static void Initialize()
        {
            // Skip on Windows - no terminal detection issues there
            if (OperatingSystem.IsWindows())
                return;

            try
            {
                var term = System.Environment.GetEnvironmentVariable("TERM") ?? "";

                // Store original TERM for restoration on exit
                OriginalTerm = term;

                // Force TERM=xterm-16color on non-Windows to avoid capability detection hangs
                // Some .NET console libraries (Spectre.Console, PrettyConsole) query terminal
                // capabilities with escape sequences that hang over SSH/tmux with 256-color terms
                // Using xterm-16color is safe and still provides good color support
                if (!string.IsNullOrEmpty(term) && term != "dumb")
                {
                    System.Environment.SetEnvironmentVariable("TERM", "xterm-16color");
                }
            }
            catch
            {
                // Ignore any errors in module initialization
            }
        }

        /// <summary>
        /// Restore the original TERM environment variable
        /// </summary>
        internal static void RestoreTerm()
        {
            if (!OperatingSystem.IsWindows() && OriginalTerm != null)
            {
                try
                {
                    System.Environment.SetEnvironmentVariable("TERM", OriginalTerm);
                }
                catch
                {
                    // Ignore errors
                }
            }
        }
    }

    [ArgExceptionBehavior(ArgExceptionPolicy.DontHandleExceptions), TabCompletion(typeof(LocalModelsTabCompletionSource), HistoryToSave = 10, REPL = true, REPLWelcomeMessage = "Type a command or 'quit' (Ctrl+C) to exit.")]
    public class OsyncProgram
    {
        public static string AppVersion = "1.2.9";
        static HttpClient client = new HttpClient() { Timeout = TimeSpan.FromDays(1) };
        public static bool isInteractiveMode = false;
        public string ollama_models = "";
        long btvalue = 0;
        string separator = Path.DirectorySeparatorChar.ToString();

        // Flag to indicate we're running from manage mode - errors should throw instead of Exit
        internal bool ThrowOnError { get; set; } = false;

        public static string GetAppVersion()
        {
            return AppVersion;
        }

        public static string GetBuildVersion()
        {
            // Get build timestamp from executable file
            // Use AppContext.BaseDirectory for single-file apps (Assembly.Location returns empty string)
            try
            {
                string exePath = Path.Combine(AppContext.BaseDirectory,
                    OperatingSystem.IsWindows() ? "osync.exe" : "osync");
                if (System.IO.File.Exists(exePath))
                {
                    var fileInfo = new FileInfo(exePath);
                    var buildTime = fileInfo.LastWriteTime;
                    return $"b{buildTime:yyyyMMdd-HHmm}";
                }
                // Fallback: try to get from any dll in the directory
                var dllPath = Path.Combine(AppContext.BaseDirectory, "osync.dll");
                if (System.IO.File.Exists(dllPath))
                {
                    var fileInfo = new FileInfo(dllPath);
                    var buildTime = fileInfo.LastWriteTime;
                    return $"b{buildTime:yyyyMMdd-HHmm}";
                }
                return "b00000000-0000";
            }
            catch
            {
                return "b00000000-0000";
            }
        }

        public static string GetFullVersion()
        {
            return $"v{AppVersion} ({GetBuildVersion()})";
        }

        [HelpHook, ArgShortcut("-?"), ArgShortcut("-h"), ArgDescription("Shows this help")]
        public bool Help { get; set; }

        [ArgActionMethod, ArgDescription("Copy a model locally or to a remote server (alias: cp)"), ArgShortcut("cp")]
        public void Copy(CopyArgs args)
        {
            ActionCopy(args.Source, args.Destination, args.BufferSize);
        }

        [ArgActionMethod, ArgDescription("List the models on a local or remote ollama server (alias: ls)"), ArgShortcut("ls")]
        public void List(ListArgs args)
        {
            string sortMode = "name"; // default
            if (args.SortBySize) sortMode = "size_desc";
            else if (args.SortBySizeAsc) sortMode = "size_asc";
            else if (args.SortByTime) sortMode = "time_desc";
            else if (args.SortByTimeAsc) sortMode = "time_asc";

            ActionList(args.Pattern, args.Destination, sortMode);
        }

        [ArgActionMethod, ArgDescription("Remove models matching pattern locally or on remote server (aliases: rm, delete, del)"), ArgShortcut("rm"), ArgShortcut("delete"), ArgShortcut("del")]
        public void Remove(RemoveArgs args)
        {
            if (string.IsNullOrWhiteSpace(args.Pattern))
            {
                Console.WriteLine("Error: Model pattern is required");
                Console.WriteLine("Usage: osync rm <model-pattern> [-d <server-url>]");
                System.Environment.Exit(1);
            }
            ActionRemove(args.Pattern, args.Destination);
        }

        [ArgActionMethod, ArgDescription("Rename a model by copying to new name and deleting original (aliases: mv, ren)"), ArgShortcut("mv"), ArgShortcut("ren")]
        public void Rename(RenameArgs args)
        {
            ActionRename(args.Source, args.NewName);
        }

        [ArgActionMethod, ArgDescription("Update models to their latest versions locally or on remote server")]
        public void Update(UpdateArgs args)
        {
            ActionUpdate(args.Pattern, args.Destination);
        }

        [ArgActionMethod, ArgDescription("Pull (download) a model from the registry locally or to a remote server")]
        public void Pull(PullArgs args)
        {
            if (string.IsNullOrWhiteSpace(args.ModelName))
            {
                Console.WriteLine("Error: Model name is required");
                Console.WriteLine("Usage: osync pull <model-name> [-d <server-url>]");
                System.Environment.Exit(1);
            }
            ActionPull(args.ModelName, args.Destination);
        }

        [ArgActionMethod, ArgDescription("Show information about a model locally or on a remote server")]
        public void Show(ShowArgs args)
        {
            if (string.IsNullOrWhiteSpace(args.ModelName))
            {
                Console.WriteLine("Error: Model name is required");
                Console.WriteLine("Usage: osync show <model-name> [-d <server-url>] [options]");
                System.Environment.Exit(1);
            }
            ActionShow(args.ModelName, args.Destination, args.License, args.Modelfile, args.Parameters, args.System, args.Template, args.Verbose);
        }

        [ArgActionMethod, ArgDescription("Run an interactive chat session with a model (alias: chat)"), ArgShortcut("chat")]
        public async Task Run(RunArgs args)
        {
            if (string.IsNullOrWhiteSpace(args.ModelName))
            {
                Console.WriteLine("Error: Model name is required");
                Console.WriteLine("Usage: osync run <model-name> [-d <server-url>] [options]");
                System.Environment.Exit(1);
            }

            // Get Ollama host from environment variable or argument
            var ollamaHost = args.Destination
                ?? System.Environment.GetEnvironmentVariable("OLLAMA_HOST")
                ?? "http://localhost:11434";

            // If OLLAMA_HOST is 0.0.0.0 (bind address), replace with localhost
            if (ollamaHost == "0.0.0.0" || ollamaHost == "0.0.0.0:11434")
            {
                ollamaHost = "http://localhost:11434";
            }

            // Normalize URL to add protocol and default port
            ollamaHost = NormalizeServerUrl(ollamaHost);

            // Ensure model has a tag
            var modelName = args.ModelName;
            if (!modelName.Contains(':'))
            {
                modelName += ":latest";
            }

            try
            {
                var session = new ChatSession(ollamaHost, modelName, args);
                await session.RunAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner error: {ex.InnerException.Message}");
                }
                System.Environment.Exit(1);
            }
        }

        [ArgActionMethod, ArgDescription("Show running models and their status")]
        public async Task Ps(PsArgs args)
        {
            // Get Ollama host from environment variable or argument
            var ollamaHost = args.Destination
                ?? System.Environment.GetEnvironmentVariable("OLLAMA_HOST")
                ?? "http://localhost:11434";

            // If OLLAMA_HOST is 0.0.0.0 (bind address), replace with localhost
            if (ollamaHost == "0.0.0.0" || ollamaHost == "0.0.0.0:11434")
            {
                ollamaHost = "http://localhost:11434";
            }

            // Normalize URL to add protocol and default port
            ollamaHost = NormalizeServerUrl(ollamaHost);

            bool isLocalHost = string.IsNullOrEmpty(args.Destination) ||
                              ollamaHost.Contains("localhost") ||
                              ollamaHost.Contains("127.0.0.1");

            try
            {
                // If running locally, show GPU and ollama process stats first
                if (isLocalHost)
                {
                    await ShowGpuStatsAsync();
                    ShowOllamaProcessStats();
                }

                using var httpClient = new HttpClient
                {
                    BaseAddress = new Uri(ollamaHost),
                    Timeout = Timeout.InfiniteTimeSpan
                };

                var response = await httpClient.GetAsync("/api/ps");
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                var status = JsonSerializer.Deserialize<ProcessStatusResponse>(json);

                if (status?.Models == null || status.Models.Count == 0)
                {
                    Console.WriteLine("No models currently loaded in memory");
                    return;
                }

                // Get console width and calculate column widths dynamically
                int consoleWidth = 120; // Default width
                try
                {
                    consoleWidth = System.Console.WindowWidth;
                    if (consoleWidth < 80) consoleWidth = 80;
                    if (consoleWidth > 300) consoleWidth = 300;
                }
                catch { /* Use default if console width unavailable */ }

                // Fixed column widths for ID, SIZE, VRAM USAGE, CONTEXT, UNTIL
                int idWidth = 14;
                int sizeWidth = 20;
                int vramWidth = 15;
                int contextWidth = 8;
                int untilWidth = 20;
                int spacing = 5; // spaces between columns
                int fixedColumnsWidth = idWidth + sizeWidth + vramWidth + contextWidth + untilWidth + spacing;
                int nameWidth = Math.Max(20, consoleWidth - fixedColumnsWidth - 1);

                Console.WriteLine("");
                Console.WriteLine("Loaded Models:");
                Console.WriteLine(new string('-', consoleWidth - 1));
                Console.WriteLine($"{"NAME".PadRight(nameWidth)} {"ID".PadRight(idWidth)} {"SIZE".PadRight(sizeWidth)} {"VRAM USAGE".PadRight(vramWidth)} {"CONTEXT".PadRight(contextWidth)} {"UNTIL".PadRight(untilWidth)}");
                Console.WriteLine(new string('-', consoleWidth - 1));

                foreach (var model in status.Models)
                {
                    var name = TruncateStringEnd(model.Name, nameWidth);
                    var id = GetShortDigest(model.Digest);
                    var size = FormatModelSize(model.Size, model.Details?.ParameterSize);

                    // Add percentage if VRAM usage is less than total size
                    string vramUsage;
                    if (model.SizeVram < model.Size && model.Size > 0)
                    {
                        double percentage = ((double)model.SizeVram / model.Size) * 100;
                        vramUsage = $"{FormatBytes(model.SizeVram)} ({percentage:F0}%)";
                    }
                    else
                    {
                        vramUsage = FormatBytes(model.SizeVram);
                    }

                    var context = model.ContextLength > 0 ? model.ContextLength.ToString() : "N/A";
                    var until = FormatUntil(model.ExpiresAt);

                    Console.WriteLine($"{name.PadRight(nameWidth)} {id.PadRight(idWidth)} {size.PadRight(sizeWidth)} {vramUsage.PadRight(vramWidth)} {context.PadRight(contextWidth)} {until.PadRight(untilWidth)}");
                }

                Console.WriteLine(new string('-', consoleWidth - 1));
                Console.WriteLine("");
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"Error: Could not connect to Ollama server at {ollamaHost}");
                Console.WriteLine($"Details: {ex.Message}");
                System.Environment.Exit(1);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner error: {ex.InnerException.Message}");
                }
                System.Environment.Exit(1);
            }
        }

        [ArgActionMethod, ArgDescription("Real-time system and GPU monitoring (aliases: monitor, psm)"), ArgShortcut("monitor"), ArgShortcut("psm")]
        public void PsMonitor(PsMonitorArgs args)
        {
            // Check if running in redirected console
            if (System.Console.IsInputRedirected || System.Console.IsOutputRedirected)
            {
                Console.WriteLine("Error: Monitor mode requires an interactive terminal.");
                Console.WriteLine("Cannot run in piped/redirected mode.");
                return;
            }

            // Parse time durations
            int refreshSeconds = ParseTimeDuration(args.Loop, 5);
            // For history, plain integers are treated as minutes
            int historyMinutes = ParseTimeDuration(args.History, 5 * 60, plainIntegerAsMinutes: true) / 60;

            // Validate ranges
            if (refreshSeconds < 1) refreshSeconds = 1;
            if (refreshSeconds > 300) refreshSeconds = 300; // Max 5 minutes
            if (historyMinutes < 1) historyMinutes = 1;
            if (historyMinutes > 60) historyMinutes = 60;

            var destination = !string.IsNullOrEmpty(args.Destination) ? NormalizeServerUrl(args.Destination) : null;

            var monitorUI = new PsMonitorUI(destination, refreshSeconds, historyMinutes);
            monitorUI.Run();
        }

        [ArgActionMethod, ArgDescription("Test D3DKMT GPU monitoring (Windows only)"), ArgShortcut("d3dkmt")]
        public void D3dkmtTest(D3dkmtTestArgs args)
        {
            if (!OperatingSystem.IsWindows())
            {
                Console.WriteLine("D3DKMT is only available on Windows.");
                return;
            }

            Console.WriteLine("=== D3DKMT Diagnostic Test ===\n");

            var monitor = new D3DKMTProcessGpuMonitor();
            Console.WriteLine("Initializing D3DKMT...");
            var initialized = monitor.Initialize();

            Console.WriteLine(monitor.GetDiagnostics());

            if (!initialized)
            {
                Console.WriteLine("ERROR: D3DKMT failed to initialize.");
                return;
            }

            // Find process to test
            var ollamaPatterns = new[] { "ollama", "ollama_llama_server", "llama-server" };
            List<int> testPids = new();

            if (args.Pid.HasValue)
            {
                testPids.Add(args.Pid.Value);
            }
            else
            {
                // Find all Ollama-related processes (main server and GPU runners)
                var allProcs = System.Diagnostics.Process.GetProcesses();
                var ollamaProcs = allProcs
                    .Where(p =>
                    {
                        try
                        {
                            var name = p.ProcessName.ToLowerInvariant();
                            return ollamaPatterns.Any(pattern => name.Contains(pattern));
                        }
                        catch { return false; }
                    })
                    .ToList();

                // Dispose non-matching processes
                foreach (var p in allProcs)
                {
                    if (!ollamaProcs.Contains(p))
                        p.Dispose();
                }

                if (ollamaProcs.Count == 0)
                {
                    Console.WriteLine("No Ollama processes found. Please specify a PID with: osync d3dkmt <pid>");
                    // Use current process as fallback
                    var curPid = System.Diagnostics.Process.GetCurrentProcess().Id;
                    testPids.Add(curPid);
                    Console.WriteLine($"Using current process (PID {curPid}) for testing.\n");
                }
                else
                {
                    Console.WriteLine($"Found {ollamaProcs.Count} Ollama-related process(es):");
                    foreach (var p in ollamaProcs)
                    {
                        Console.WriteLine($"  PID {p.Id}: {p.ProcessName}");
                        testPids.Add(p.Id);
                        p.Dispose();
                    }
                    System.Console.WriteLine();
                }
            }

            // Test each process
            foreach (var testPid in testPids)
            {
                Console.WriteLine($"--- Testing PID {testPid} ---");
                Console.WriteLine(monitor.TestQueryProcess(testPid));
            }

            if (testPids.Count == 0)
            {
                Console.WriteLine("No processes to query.");
                monitor.Dispose();
                return;
            }

            // Now test the full metrics query with delta calculation
            Console.WriteLine("\n=== Full Metrics Query ===");

            // First query to establish baseline
            Console.WriteLine("First query (establishing baseline)...");
            var metrics1 = monitor.GetProcessGpuMetrics(testPids);
            Console.WriteLine($"  Got {metrics1.Count} results");

            // Wait a bit
            Console.WriteLine("Waiting 2 seconds...");
            System.Threading.Thread.Sleep(2000);

            // Second query to get delta
            Console.WriteLine("Second query (calculating utilization)...");
            var metrics2 = monitor.GetProcessGpuMetrics(testPids);

            Console.WriteLine($"\n=== Results ===");
            foreach (var m in metrics2)
            {
                Console.WriteLine($"PID {m.ProcessId} ({m.ProcessName}):");
                Console.WriteLine($"  GPU Index: {m.GpuIndex}");
                Console.WriteLine($"  GPU Utilization: {m.GpuUtilization:F2}%");
                Console.WriteLine($"  Dedicated Memory: {m.DedicatedMemoryMB:F2} MB");
                Console.WriteLine($"  Running Time: {m.RunningTimeNs}");
            }

            if (metrics2.Count == 0)
            {
                Console.WriteLine("No GPU metrics returned. The process may not be using the GPU.");
            }

            monitor.Dispose();
        }

        private int ParseTimeDuration(string input, int defaultSeconds, bool plainIntegerAsMinutes = false)
        {
            if (string.IsNullOrWhiteSpace(input))
                return defaultSeconds;

            input = input.Trim().ToLowerInvariant();

            // Try parsing as plain integer
            if (int.TryParse(input, out int plainValue))
            {
                // For history, plain integer = minutes; for loop, plain integer = seconds
                return plainIntegerAsMinutes ? plainValue * 60 : plainValue;
            }

            int totalSeconds = 0;
            var regex = new System.Text.RegularExpressions.Regex(@"(\d+)\s*(h|m|s)?", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var matches = regex.Matches(input);

            if (matches.Count == 0)
                return defaultSeconds;

            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                if (!int.TryParse(match.Groups[1].Value, out int value))
                    continue;

                string unit = match.Groups[2].Value.ToLowerInvariant();
                totalSeconds += unit switch
                {
                    "h" => value * 3600,
                    "m" => value * 60,
                    "s" => value,
                    "" => value, // No unit = seconds
                    _ => value
                };
            }

            return totalSeconds > 0 ? totalSeconds : defaultSeconds;
        }

        // Helper methods for ps command
        private string TruncateString(string str, int maxLength)
        {
            if (string.IsNullOrEmpty(str) || str.Length <= maxLength)
                return str ?? "";

            return str.Substring(0, maxLength - 3) + "...";
        }

        /// <summary>
        /// Truncates a string from the beginning, preserving the end (useful for model names where the tag is important)
        /// </summary>
        private string TruncateStringEnd(string str, int maxLength)
        {
            if (string.IsNullOrEmpty(str) || str.Length <= maxLength)
                return str ?? "";

            // Keep the end of the string with "..." prefix
            return "..." + str.Substring(str.Length - maxLength + 3);
        }

        private string GetShortDigest(string digest)
        {
            if (string.IsNullOrEmpty(digest))
                return "N/A";

            // Return first 12 characters of digest (like Docker)
            return digest.Length >= 12 ? digest.Substring(0, 12) : digest;
        }

        private string FormatModelSize(long sizeBytes, string? parameterSize)
        {
            var diskSize = FormatBytes(sizeBytes);
            if (!string.IsNullOrEmpty(parameterSize))
            {
                return $"{diskSize} ({parameterSize})";
            }
            return diskSize;
        }

        private string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        private string FormatDuration(TimeSpan duration)
        {
            if (duration.TotalSeconds >= 60)
                return $"{(int)duration.TotalMinutes}m {duration.Seconds}s";
            return $"{duration.TotalSeconds:F1}s";
        }

        /// <summary>
        /// Show ollama process CPU and memory usage
        /// </summary>
        private void ShowOllamaProcessStats()
        {
            try
            {
                var processes = Process.GetProcessesByName("ollama");
                if (processes.Length == 0)
                {
                    // Try alternative names
                    processes = Process.GetProcessesByName("ollama_llama_server");
                }

                if (processes.Length == 0)
                    return;

                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[bold]Ollama Process:[/]");
                AnsiConsole.WriteLine(new string('-', 80));

                foreach (var proc in processes)
                {
                    try
                    {
                        proc.Refresh();
                        var memoryMB = proc.WorkingSet64 / (1024.0 * 1024.0);
                        var privateMB = proc.PrivateMemorySize64 / (1024.0 * 1024.0);

                        // Get CPU usage (requires two samples)
                        var cpuPercent = GetProcessCpuUsage(proc);
                        var cpuColor = GetPercentageColor(cpuPercent);

                        // Format memory nicely
                        string memoryStr;
                        if (memoryMB >= 1024)
                            memoryStr = $"{memoryMB / 1024:F1} GB";
                        else
                            memoryStr = $"{memoryMB:F0} MB";

                        string privateStr;
                        if (privateMB >= 1024)
                            privateStr = $"{privateMB / 1024:F1} GB";
                        else
                            privateStr = $"{privateMB:F0} MB";

                        AnsiConsole.MarkupLine($"  PID: {proc.Id,-8} Memory: {memoryStr} (Private: {privateStr})  CPU: [{cpuColor}]{cpuPercent:F1}%[/]");
                    }
                    catch
                    {
                        // Skip processes we can't access
                    }
                    finally
                    {
                        proc.Dispose();
                    }
                }

                AnsiConsole.WriteLine(new string('-', 80));
            }
            catch
            {
                // Silently ignore errors getting process info
            }
        }

        /// <summary>
        /// Get CPU usage for a process (approximate, single sample)
        /// </summary>
        private double GetProcessCpuUsage(Process proc)
        {
            try
            {
                var startTime = DateTime.UtcNow;
                var startCpuUsage = proc.TotalProcessorTime;

                System.Threading.Thread.Sleep(100); // Brief sample

                proc.Refresh();
                var endTime = DateTime.UtcNow;
                var endCpuUsage = proc.TotalProcessorTime;

                var cpuUsedMs = (endCpuUsage - startCpuUsage).TotalMilliseconds;
                var totalMsPassed = (endTime - startTime).TotalMilliseconds;
                var cpuUsageTotal = cpuUsedMs / (System.Environment.ProcessorCount * totalMsPassed) * 100;

                return Math.Min(cpuUsageTotal, 100);
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Show GPU statistics if available (NVIDIA or AMD)
        /// </summary>
        private async Task ShowGpuStatsAsync()
        {
            // Try NVIDIA first
            var nvidiaStats = await GetNvidiaGpuStatsAsync();
            if (nvidiaStats != null && nvidiaStats.Count > 0)
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[bold]GPU Status (NVIDIA):[/]");
                AnsiConsole.WriteLine(new string('-', 80));

                foreach (var gpu in nvidiaStats)
                {
                    var memUsedGB = gpu.MemoryUsed / 1024.0;
                    var memTotalGB = gpu.MemoryTotal / 1024.0;
                    var memPercent = gpu.MemoryTotal > 0 ? (gpu.MemoryUsed * 100.0 / gpu.MemoryTotal) : 0;
                    var powerPercent = gpu.PowerLimit > 0 ? (gpu.PowerDraw * 100.0 / gpu.PowerLimit) : 0;

                    AnsiConsole.MarkupLine($"  [bold][[{gpu.Index}]][/] {Markup.Escape(gpu.Name)}");

                    var computeColor = GetPercentageColor(gpu.GpuUtilization);
                    var memColor = GetPercentageColor(memPercent);
                    AnsiConsole.MarkupLine($"      Compute: [{computeColor}]{gpu.GpuUtilization}%[/]  Memory: {memUsedGB:F1}/{memTotalGB:F1} GB ([{memColor}]{memPercent:F0}%[/])");

                    if (gpu.Temperature > 0 || gpu.PowerDraw > 0)
                    {
                        var tempColor = GetTemperatureColor(gpu.Temperature);
                        var powerColor = GetPercentageColor(powerPercent);
                        var powerInfo = gpu.PowerLimit > 0 ? $"{gpu.PowerDraw:F0}/{gpu.PowerLimit:F0}W" : $"{gpu.PowerDraw:F0}W";
                        AnsiConsole.MarkupLine($"      Temp: [{tempColor}]{gpu.Temperature}°C[/]  Power: [{powerColor}]{powerInfo}[/]  Clock: {gpu.ClockSM} MHz");
                    }

                    // Show ollama memory usage if detected
                    if (gpu.ProcessMemoryUsed > 0)
                    {
                        var ollamaMemGB = gpu.ProcessMemoryUsed / 1024.0;
                        var ollamaPercent = gpu.MemoryTotal > 0 ? (gpu.ProcessMemoryUsed * 100.0 / gpu.MemoryTotal) : 0;
                        var ollamaColor = GetPercentageColor(ollamaPercent);
                        if (ollamaMemGB >= 1.0)
                            AnsiConsole.MarkupLine($"      Ollama VRAM: [{ollamaColor}]{ollamaMemGB:F1} GB ({ollamaPercent:F0}%)[/]");
                        else
                            AnsiConsole.MarkupLine($"      Ollama VRAM: [{ollamaColor}]{gpu.ProcessMemoryUsed:F0} MB ({ollamaPercent:F0}%)[/]");
                    }
                }

                AnsiConsole.WriteLine(new string('-', 80));
                return;
            }

            // Try AMD (via rocm-smi)
            var amdStats = await GetAmdGpuStatsAsync();
            if (amdStats != null && amdStats.Count > 0)
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[bold]GPU Status (AMD):[/]");
                AnsiConsole.WriteLine(new string('-', 80));

                foreach (var gpu in amdStats)
                {
                    var memUsedGB = gpu.MemoryUsed / 1024.0;
                    var memTotalGB = gpu.MemoryTotal / 1024.0;
                    var memPercent = gpu.MemoryTotal > 0 ? (gpu.MemoryUsed * 100.0 / gpu.MemoryTotal) : 0;

                    AnsiConsole.MarkupLine($"  [bold][[{gpu.Index}]][/] {Markup.Escape(gpu.Name)}");

                    var computeColor = GetPercentageColor(gpu.GpuUtilization);
                    var memColor = GetPercentageColor(memPercent);
                    AnsiConsole.MarkupLine($"      Compute: [{computeColor}]{gpu.GpuUtilization}%[/]  Memory: {memUsedGB:F1}/{memTotalGB:F1} GB ([{memColor}]{memPercent:F0}%[/])");

                    if (gpu.Temperature > 0 || gpu.PowerDraw > 0)
                    {
                        var tempColor = GetTemperatureColor(gpu.Temperature);
                        AnsiConsole.MarkupLine($"      Temp: [{tempColor}]{gpu.Temperature}°C[/]  Power: {gpu.PowerDraw:F0}W  Clock: {gpu.ClockSM} MHz");
                    }

                    // Show ollama memory usage if detected (for AMD)
                    if (gpu.ProcessMemoryUsed > 0)
                    {
                        var ollamaMemGB = gpu.ProcessMemoryUsed / 1024.0;
                        var ollamaPercent = gpu.MemoryTotal > 0 ? (gpu.ProcessMemoryUsed * 100.0 / gpu.MemoryTotal) : 0;
                        var ollamaColor = GetPercentageColor(ollamaPercent);
                        if (ollamaMemGB >= 1.0)
                            AnsiConsole.MarkupLine($"      Ollama VRAM: [{ollamaColor}]{ollamaMemGB:F1} GB ({ollamaPercent:F0}%)[/]");
                        else
                            AnsiConsole.MarkupLine($"      Ollama VRAM: [{ollamaColor}]{gpu.ProcessMemoryUsed:F0} MB ({ollamaPercent:F0}%)[/]");
                    }
                }

                AnsiConsole.WriteLine(new string('-', 80));
            }
        }

        /// <summary>
        /// Get color for a percentage value (0-100)
        /// </summary>
        private static string GetPercentageColor(double percent)
        {
            return percent switch
            {
                < 50 => "green",
                < 75 => "yellow",
                < 90 => "orange1",
                _ => "red"
            };
        }

        /// <summary>
        /// Get color for temperature value (in Celsius)
        /// </summary>
        private static string GetTemperatureColor(int temp)
        {
            return temp switch
            {
                < 60 => "green",
                < 70 => "yellow",
                < 80 => "orange1",
                _ => "red"
            };
        }

        private class GpuStats
        {
            public int Index { get; set; }
            public string Name { get; set; } = "";
            public int GpuUtilization { get; set; }
            public double MemoryUsed { get; set; } // MB
            public double MemoryTotal { get; set; } // MB
            public int Temperature { get; set; } // Celsius
            public double PowerDraw { get; set; } // Watts
            public double PowerLimit { get; set; } // Watts
            public int ClockSM { get; set; } // MHz
            public double ProcessMemoryUsed { get; set; } // MB (ollama's usage)
        }

        /// <summary>
        /// Get NVIDIA GPU stats using nvidia-smi
        /// </summary>
        private async Task<List<GpuStats>?> GetNvidiaGpuStatsAsync()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "nvidia-smi",
                    Arguments = "--query-gpu=index,name,utilization.gpu,memory.used,memory.total,temperature.gpu,power.draw,power.limit,clocks.current.sm --format=csv,noheader,nounits",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return null;

                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
                    return null;

                var stats = new List<GpuStats>();
                var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

                foreach (var line in lines)
                {
                    var parts = line.Split(',').Select(p => p.Trim()).ToArray();
                    if (parts.Length >= 9)
                    {
                        var gpu = new GpuStats
                        {
                            Index = int.TryParse(parts[0], out var idx) ? idx : 0,
                            Name = parts[1],
                            GpuUtilization = int.TryParse(parts[2], out var util) ? util : 0,
                            MemoryUsed = double.TryParse(parts[3], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var memUsed) ? memUsed : 0,
                            MemoryTotal = double.TryParse(parts[4], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var memTotal) ? memTotal : 0,
                            Temperature = int.TryParse(parts[5], out var temp) ? temp : 0,
                            PowerDraw = double.TryParse(parts[6], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var power) ? power : 0,
                            PowerLimit = double.TryParse(parts[7], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var powerLimit) ? powerLimit : 0,
                            ClockSM = int.TryParse(parts[8], out var clock) ? clock : 0
                        };
                        stats.Add(gpu);
                    }
                }

                // Try to get ollama's GPU memory usage
                if (stats.Count > 0)
                {
                    await GetNvidiaProcessMemoryAsync(stats);
                }

                return stats;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Get ollama's GPU memory usage from nvidia-smi
        /// </summary>
        private async Task GetNvidiaProcessMemoryAsync(List<GpuStats> stats)
        {
            try
            {
                // First, get UUID mapping for each GPU index
                var psiUuid = new ProcessStartInfo
                {
                    FileName = "nvidia-smi",
                    Arguments = "--query-gpu=index,uuid --format=csv,noheader",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                var uuidMapping = new Dictionary<string, int>(); // UUID -> GPU index

                using (var processUuid = Process.Start(psiUuid))
                {
                    if (processUuid != null)
                    {
                        var uuidOutput = await processUuid.StandardOutput.ReadToEndAsync();
                        await processUuid.WaitForExitAsync();

                        if (processUuid.ExitCode == 0 && !string.IsNullOrWhiteSpace(uuidOutput))
                        {
                            foreach (var line in uuidOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                            {
                                var parts = line.Split(',').Select(p => p.Trim()).ToArray();
                                if (parts.Length >= 2 && int.TryParse(parts[0], out var gpuIdx))
                                {
                                    uuidMapping[parts[1]] = gpuIdx;
                                }
                            }
                        }
                    }
                }

                // Query compute apps for process memory
                var psi = new ProcessStartInfo
                {
                    FileName = "nvidia-smi",
                    Arguments = "--query-compute-apps=gpu_uuid,pid,used_memory,name --format=csv,noheader,nounits",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return;

                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
                    return;

                var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var parts = line.Split(',').Select(p => p.Trim()).ToArray();
                    if (parts.Length >= 4)
                    {
                        var gpuUuid = parts[0];
                        var procName = parts[3].ToLower();
                        if (procName.Contains("ollama") || procName.Contains("llama"))
                        {
                            var memUsed = double.TryParse(parts[2], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var mem) ? mem : 0;

                            // Find the matching GPU by UUID
                            if (uuidMapping.TryGetValue(gpuUuid, out var gpuIdx) && gpuIdx < stats.Count)
                            {
                                stats[gpuIdx].ProcessMemoryUsed += memUsed;
                            }
                            else if (stats.Count > 0)
                            {
                                // Fallback to first GPU
                                stats[0].ProcessMemoryUsed += memUsed;
                            }
                        }
                    }
                }
            }
            catch
            {
                // Ignore errors
            }
        }

        /// <summary>
        /// Get AMD GPU stats using rocm-smi (Linux) or similar
        /// </summary>
        private async Task<List<GpuStats>?> GetAmdGpuStatsAsync()
        {
            try
            {
                // rocm-smi is the AMD equivalent of nvidia-smi on Linux
                var psi = new ProcessStartInfo
                {
                    FileName = "rocm-smi",
                    Arguments = "--showuse --showmeminfo vram --showtemp --showpower --showclocks --json",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return null;

                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
                    return null;

                // Parse JSON output from rocm-smi
                var stats = new List<GpuStats>();

                try
                {
                    using var doc = JsonDocument.Parse(output);
                    var root = doc.RootElement;

                    int index = 0;
                    foreach (var prop in root.EnumerateObject())
                    {
                        if (prop.Name.StartsWith("card"))
                        {
                            var card = prop.Value;
                            var gpu = new GpuStats { Index = index++ };

                            if (card.TryGetProperty("GPU use (%)", out var util))
                                gpu.GpuUtilization = int.TryParse(util.GetString(), out var u) ? u : 0;

                            if (card.TryGetProperty("VRAM Total Memory (B)", out var vramTotal))
                                gpu.MemoryTotal = double.TryParse(vramTotal.GetString(), out var mt) ? mt / (1024 * 1024) : 0;

                            if (card.TryGetProperty("VRAM Total Used Memory (B)", out var vramUsed))
                                gpu.MemoryUsed = double.TryParse(vramUsed.GetString(), out var mu) ? mu / (1024 * 1024) : 0;

                            if (card.TryGetProperty("Temperature (Sensor edge) (C)", out var temp))
                                gpu.Temperature = int.TryParse(temp.GetString(), out var t) ? t : 0;

                            if (card.TryGetProperty("Average Graphics Package Power (W)", out var power))
                                gpu.PowerDraw = double.TryParse(power.GetString(), out var p) ? p : 0;

                            if (card.TryGetProperty("sclk clock speed:", out var clock))
                                gpu.ClockSM = int.TryParse(clock.GetString()?.Replace("Mhz", "").Trim(), out var c) ? c : 0;

                            if (card.TryGetProperty("Card series", out var name))
                                gpu.Name = name.GetString() ?? "AMD GPU";

                            stats.Add(gpu);
                        }
                    }
                }
                catch
                {
                    // JSON parsing failed, rocm-smi output format may have changed
                }

                return stats;
            }
            catch
            {
                return null;
            }
        }

        private string FormatUntil(DateTime expiresAt)
        {
            var now = DateTime.Now;
            var timeSpan = expiresAt - now;

            if (timeSpan.TotalMinutes < 1)
            {
                return "Less than a minute";
            }
            else if (timeSpan.TotalMinutes < 2)
            {
                return "About a minute from now";
            }
            else if (timeSpan.TotalMinutes < 60)
            {
                return $"{(int)timeSpan.TotalMinutes} minutes from now";
            }
            else if (timeSpan.TotalHours < 2)
            {
                return "About an hour from now";
            }
            else if (timeSpan.TotalHours < 24)
            {
                return $"{(int)timeSpan.TotalHours} hours from now";
            }
            else
            {
                return $"{(int)timeSpan.TotalDays} days from now";
            }
        }

        [ArgActionMethod, ArgDescription("Load a model into memory")]
        public async Task Load(LoadArgs args)
        {
            if (string.IsNullOrWhiteSpace(args.ModelName))
            {
                Console.WriteLine("Error: Model name is required");
                Console.WriteLine("Usage: osync load <model-name> [-d <server-url>]");
                Console.WriteLine("   or: osync load <server-url>/<model-name>");
                System.Environment.Exit(1);
            }

            string modelName = args.ModelName;
            string ollamaHost;

            // Check if ModelName is a URL with embedded model name (e.g., http://host:port/modelname)
            if (modelName.StartsWith("http://") || modelName.StartsWith("https://"))
            {
                var (serverUrl, parsedModel) = ParseRemoteSource(modelName);
                ollamaHost = serverUrl;
                modelName = parsedModel;
            }
            else
            {
                // Get Ollama host from -d argument, environment variable, or default
                ollamaHost = !string.IsNullOrEmpty(args.Destination)
                    ? args.Destination
                    : System.Environment.GetEnvironmentVariable("OLLAMA_HOST")
                    ?? "http://localhost:11434";

                // If OLLAMA_HOST is 0.0.0.0 (bind address), replace with localhost
                if (ollamaHost == "0.0.0.0" || ollamaHost == "0.0.0.0:11434")
                {
                    ollamaHost = "http://localhost:11434";
                }

                // Normalize URL to add protocol and default port
                ollamaHost = NormalizeServerUrl(ollamaHost);
            }

            // Ensure model has a tag
            if (!modelName.Contains(':'))
            {
                modelName += ":latest";
            }

            try
            {
                Console.WriteLine($"Loading model '{modelName}' into memory...");

                var stopwatch = System.Diagnostics.Stopwatch.StartNew();

                using var httpClient = new HttpClient
                {
                    BaseAddress = new Uri(ollamaHost),
                    Timeout = Timeout.InfiniteTimeSpan
                };

                // Send generate request with minimal prompt to get load_duration in response
                // Using num_predict=1 to minimize generation while still getting timing info
                var preloadRequest = new
                {
                    model = modelName,
                    prompt = ".",
                    stream = false,
                    options = new { num_predict = 1 }
                };

                var jsonContent = JsonSerializer.Serialize(preloadRequest);
                var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/api/generate")
                {
                    Content = httpContent
                };

                var response = await httpClient.SendAsync(requestMessage);
                response.EnsureSuccessStatusCode();

                // Parse the response to get load_duration
                var responseContent = await response.Content.ReadAsStringAsync();

                stopwatch.Stop();
                var elapsed = stopwatch.Elapsed;
                string timeStr = FormatDuration(elapsed);

                // Try to extract load_duration from response (in nanoseconds)
                string apiTimeStr = "";
                try
                {
                    using var doc = JsonDocument.Parse(responseContent);
                    if (doc.RootElement.TryGetProperty("load_duration", out var loadDurationElement))
                    {
                        var loadDurationNs = loadDurationElement.GetInt64();
                        var loadDuration = TimeSpan.FromTicks(loadDurationNs / 100); // nanoseconds to ticks (100ns per tick)
                        apiTimeStr = $" (API: {FormatDuration(loadDuration)})";
                    }
                }
                catch { /* Ignore parsing errors */ }

                // Verify model is loaded using /api/ps
                bool verified = false;
                try
                {
                    var psResponse = await httpClient.GetAsync("/api/ps");
                    if (psResponse.IsSuccessStatusCode)
                    {
                        var psJson = await psResponse.Content.ReadAsStringAsync();
                        var status = JsonSerializer.Deserialize<ProcessStatusResponse>(psJson);
                        if (status?.Models != null)
                        {
                            verified = status.Models.Any(m =>
                                m.Name.Equals(modelName, StringComparison.OrdinalIgnoreCase));
                        }
                    }
                }
                catch { /* Ignore verification errors */ }

                if (verified)
                    Console.WriteLine($"✓ Model '{modelName}' loaded successfully ({timeStr}){apiTimeStr}");
                else
                    Console.WriteLine($"✓ Model '{modelName}' loaded ({timeStr}){apiTimeStr}");
            }
            catch (HttpRequestException ex)
            {
                // Check if it's a 404 (model not found) vs connection error
                if (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    Console.WriteLine($"Error: Model '{modelName}' not found");
                    Console.WriteLine($"Details: The model does not exist on the server. Check the model name and try again.");
                }
                else if (ex.StatusCode.HasValue)
                {
                    Console.WriteLine($"Error: Request failed with status {(int)ex.StatusCode.Value} ({ex.StatusCode.Value})");
                    Console.WriteLine($"Details: {ex.Message}");
                }
                else
                {
                    Console.WriteLine($"Error: Could not connect to Ollama server at {ollamaHost}");
                    Console.WriteLine($"Details: {ex.Message}");
                }
                System.Environment.Exit(1);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner error: {ex.InnerException.Message}");
                }
                System.Environment.Exit(1);
            }
        }

        [ArgActionMethod, ArgDescription("Unload a model (or all models) from memory")]
        public async Task Unload(UnloadArgs args)
        {
            string? inputModelName = args.ModelName;
            string ollamaHost;

            // Check if ModelName is a URL with embedded model name (e.g., http://host:port/modelname)
            if (!string.IsNullOrWhiteSpace(inputModelName) && (inputModelName.StartsWith("http://") || inputModelName.StartsWith("https://")))
            {
                var (serverUrl, parsedModel) = ParseRemoteSource(inputModelName);
                ollamaHost = serverUrl;
                inputModelName = parsedModel;
            }
            else
            {
                // Get Ollama host from -d argument, environment variable, or default
                ollamaHost = !string.IsNullOrEmpty(args.Destination)
                    ? args.Destination
                    : System.Environment.GetEnvironmentVariable("OLLAMA_HOST")
                    ?? "http://localhost:11434";

                // If OLLAMA_HOST is 0.0.0.0 (bind address), replace with localhost
                if (ollamaHost == "0.0.0.0" || ollamaHost == "0.0.0.0:11434")
                {
                    ollamaHost = "http://localhost:11434";
                }

                // Normalize URL to add protocol and default port
                ollamaHost = NormalizeServerUrl(ollamaHost);
            }

            try
            {
                using var httpClient = new HttpClient
                {
                    BaseAddress = new Uri(ollamaHost),
                    Timeout = Timeout.InfiniteTimeSpan
                };

                List<string> modelsToUnload = new List<string>();

                // If model name is specified, unload just that model
                if (!string.IsNullOrWhiteSpace(inputModelName))
                {
                    var modelName = inputModelName;
                    if (!modelName.Contains(':'))
                    {
                        modelName += ":latest";
                    }
                    modelsToUnload.Add(modelName);
                }
                else
                {
                    // Get all loaded models from /api/ps
                    Console.WriteLine("Fetching loaded models...");
                    var psResponse = await httpClient.GetAsync("/api/ps");
                    psResponse.EnsureSuccessStatusCode();

                    var json = await psResponse.Content.ReadAsStringAsync();
                    var status = JsonSerializer.Deserialize<ProcessStatusResponse>(json);

                    if (status?.Models == null || status.Models.Count == 0)
                    {
                        Console.WriteLine("No models currently loaded in memory");
                        return;
                    }

                    modelsToUnload.AddRange(status.Models.Select(m => m.Name));
                }

                // Unload each model
                int unloadedCount = 0;
                foreach (var modelName in modelsToUnload)
                {
                    Console.WriteLine($"Unloading model '{modelName}'...");

                    var unloadRequest = new
                    {
                        model = modelName,
                        keep_alive = 0
                    };

                    var jsonContent = JsonSerializer.Serialize(unloadRequest);
                    var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                    var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
                    {
                        Content = httpContent
                    };

                    var response = await httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead);
                    response.EnsureSuccessStatusCode();

                    // Consume the response
                    await response.Content.ReadAsStringAsync();

                    // Verify model is unloaded using /api/ps
                    bool verified = false;
                    try
                    {
                        // Brief delay to allow Ollama to process the unload
                        await Task.Delay(100);

                        var psResponse = await httpClient.GetAsync("/api/ps");
                        if (psResponse.IsSuccessStatusCode)
                        {
                            var psJson = await psResponse.Content.ReadAsStringAsync();
                            var status = JsonSerializer.Deserialize<ProcessStatusResponse>(psJson);
                            if (status?.Models != null)
                            {
                                // Model is unloaded if it's NOT in the list
                                verified = !status.Models.Any(m =>
                                    m.Name.Equals(modelName, StringComparison.OrdinalIgnoreCase));
                            }
                            else
                            {
                                // No models loaded = verified
                                verified = true;
                            }
                        }
                    }
                    catch { /* Ignore verification errors */ }

                    if (verified)
                    {
                        Console.WriteLine($"✓ Model '{modelName}' unloaded successfully");
                        unloadedCount++;
                    }
                    else
                    {
                        Console.WriteLine($"✓ Model '{modelName}' unloaded");
                        unloadedCount++;
                    }
                }

                if (modelsToUnload.Count > 1)
                {
                    Console.WriteLine($"\nUnloaded {unloadedCount} models");
                }
            }
            catch (HttpRequestException ex)
            {
                // Check if it's a specific error vs connection error
                if (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    Console.WriteLine($"Error: Model not found on the server");
                    Console.WriteLine($"Details: {ex.Message}");
                }
                else if (ex.StatusCode.HasValue)
                {
                    Console.WriteLine($"Error: Request failed with status {(int)ex.StatusCode.Value} ({ex.StatusCode.Value})");
                    Console.WriteLine($"Details: {ex.Message}");
                }
                else
                {
                    Console.WriteLine($"Error: Could not connect to Ollama server at {ollamaHost}");
                    Console.WriteLine($"Details: {ex.Message}");
                }
                System.Environment.Exit(1);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner error: {ex.InnerException.Message}");
                }
                System.Environment.Exit(1);
            }
        }

        [ArgActionMethod, ArgDescription("Run quantization comparison tests on model variants")]
        public async Task Qc(QcArgs args)
        {
            Init();

            // Get Ollama host from environment variable or argument
            var ollamaHost = args.Destination
                ?? System.Environment.GetEnvironmentVariable("OLLAMA_HOST")
                ?? "http://localhost:11434";

            // If OLLAMA_HOST is 0.0.0.0 (bind address), replace with localhost
            if (ollamaHost == "0.0.0.0" || ollamaHost == "0.0.0.0:11434")
            {
                ollamaHost = "http://localhost:11434";
            }
            else
            {
                // Normalize URL with default protocol and port
                ollamaHost = NormalizeServerUrl(ollamaHost);
            }

            // Validate server URL if it's not localhost
            if (!ollamaHost.Contains("localhost") && !ollamaHost.Contains("127.0.0.1"))
            {
                if (!ValidateServerUrl(ollamaHost, silent: false))
                {
                    System.Environment.Exit(1);
                }
            }

            var qcCommand = new QcCommand(args, ollamaHost);
            var exitCode = await qcCommand.ExecuteAsync();
            System.Environment.Exit(exitCode);
        }

        [ArgActionMethod, ArgDescription("View quantization comparison results from qc command")]
        public async Task QcView(QcViewArgs args)
        {
            Init();

            var qcViewCommand = new QcViewCommand(args);
            var exitCode = await qcViewCommand.ExecuteAsync();
            System.Environment.Exit(exitCode);
        }

        [ArgActionMethod, ArgDescription("Run context length benchmarks with optional tool calling")]
        public async Task Bench(BenchArgs args)
        {
            Init();

            // Get Ollama host from argument, environment variable, or default
            var ollamaHost = !string.IsNullOrEmpty(args.Destination)
                ? args.Destination
                : System.Environment.GetEnvironmentVariable("OLLAMA_HOST")
                    ?? "http://localhost:11434";

            // If OLLAMA_HOST is 0.0.0.0 (bind address), replace with localhost
            if (ollamaHost == "0.0.0.0" || ollamaHost == "0.0.0.0:11434")
            {
                ollamaHost = "http://localhost:11434";
            }
            else
            {
                // Normalize URL with default protocol and port
                ollamaHost = NormalizeServerUrl(ollamaHost);
            }

            // Validate server URL if it's not localhost (skip for special flags)
            if (!args.HelpCloud && !args.ShowTools && !args.GenerateSuite)
            {
                if (!ollamaHost.Contains("localhost") && !ollamaHost.Contains("127.0.0.1"))
                {
                    if (!ValidateServerUrl(ollamaHost, silent: false))
                    {
                        System.Environment.Exit(1);
                    }
                }
            }

            var benchCommand = new BenchCommand(args, ollamaHost);
            var exitCode = await benchCommand.ExecuteAsync();
            System.Environment.Exit(exitCode);
        }

        [ArgActionMethod, ArgDescription("View benchmark results from bench command")]
        public async Task BenchView(BenchViewArgs args)
        {
            Init();

            var benchViewCommand = new BenchViewCommand(args);
            var exitCode = await benchViewCommand.ExecuteAsync();
            System.Environment.Exit(exitCode);
        }

        [ArgActionMethod, ArgDescription("Interactive TUI for model management")]
        public void Manage(ManageArgs args)
        {
            Init();

            var destination = args.Destination;

            // Normalize and validate destination if provided
            if (!string.IsNullOrEmpty(destination))
            {
                destination = NormalizeServerUrl(destination);
                if (!ValidateServerUrl(destination, silent: true))
                {
                    System.Environment.Exit(1);
                }
            }

            // Launch TUI
            var manageUI = new ManageUI(this, destination);
            manageUI.Run();
        }

        [ArgActionMethod, ArgDescription("Clear the console screen (interactive mode only)")]
        public void Clear()
        {
            if (!isInteractiveMode)
            {
                Console.WriteLine("Error: The 'clear' command is only available in interactive mode.");
                Console.WriteLine("Start interactive mode by running 'osync' without arguments.");
                System.Environment.Exit(1);
            }

            Console.Clear();
            Console.WriteLine($"osync {GetFullVersion()}");
            Console.WriteLine("");
        }

        [ArgActionMethod, ArgDescription("Exit interactive mode"), ArgShortcut("exit")]
        public void Quit()
        {
            // PowerArgs REPL mode will handle this by exiting when Environment.Exit is called
            // Note: Due to PowerArgs limitations, Ctrl+D may show a character on Windows - use 'quit' or 'exit' instead
            System.Environment.Exit(0);
        }

        [ArgActionMethod, ArgDescription("Install osync to user directory, add to PATH, and optionally configure shell completion")]
        public void Install()
        {
            ActionInstall();
        }

        [ArgActionMethod, ArgDescription("Show osync version information"), ArgShortcut("-v")]
        public void ShowVersion(VersionArgs args)
        {
            Console.WriteLine($"osync {GetFullVersion()}");

            if (!args.Verbose)
                return;

            System.Console.WriteLine();

            // Get binary path and installation status
            string binaryPath = System.Environment.ProcessPath ?? "unknown";
            Console.WriteLine($"Binary path: {binaryPath}");

            // Check installation status
            bool isWindows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);
            string installDir = isWindows
                ? Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), ".osync")
                : Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), ".local", "bin");

            string binaryDir = Path.GetDirectoryName(binaryPath) ?? "";
            bool isRunningFromInstallDir = Path.GetFullPath(binaryDir).Equals(Path.GetFullPath(installDir), StringComparison.OrdinalIgnoreCase);

            // Check if osync exists in install directory
            string installedBinaryPath = isWindows
                ? Path.Combine(installDir, "osync.exe")
                : Path.Combine(installDir, "osync");
            bool installedBinaryExists = System.IO.File.Exists(installedBinaryPath);

            if (isRunningFromInstallDir)
            {
                Console.WriteLine($"Installed: Yes ({installDir})");
            }
            else if (installedBinaryExists)
            {
                // Get version and build of installed binary and compare
                var (installedVersion, installedBuild) = GetInstalledBinaryVersionInfo(installedBinaryPath);
                string currentVersion = AppVersion;
                string currentBuild = GetBuildVersion();

                if (installedVersion != null)
                {
                    string installedFullVersion = installedBuild != null
                        ? $"v{installedVersion} ({installedBuild})"
                        : $"v{installedVersion}";

                    int comparison = CompareVersions(currentVersion, currentBuild, installedVersion, installedBuild);
                    if (comparison > 0)
                        Console.WriteLine($"Installed: Yes ({installDir}) - installed {installedFullVersion} is older");
                    else if (comparison < 0)
                        Console.WriteLine($"Installed: Yes ({installDir}) - installed {installedFullVersion} is newer");
                    else
                        Console.WriteLine($"Installed: Yes ({installDir}) - same version");
                }
                else
                {
                    Console.WriteLine($"Installed: Yes ({installDir}) - version unknown");
                }
            }
            else
            {
                Console.WriteLine($"Installed: No ({installDir})");
            }

            // Detect shell type and version
            System.Console.WriteLine();
            var (shellType, shellVersion) = DetectShellInfo();
            Console.WriteLine($"Shell: {shellType} {shellVersion}");

            // Check tab completion status
            var (completionInstalled, completionCanInstall, completionPath) = CheckTabCompletionStatus(shellType);
            if (completionInstalled)
            {
                Console.WriteLine($"Tab completion: Installed ({completionPath})");
            }
            else if (completionCanInstall)
            {
                Console.WriteLine($"Tab completion: Not installed (can be installed via 'osync install')");
            }
            else
            {
                Console.WriteLine($"Tab completion: Not available for {shellType}");
            }
        }

        private (string shellType, string shellVersion) DetectShellInfo()
        {
            bool isWindows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);

            // Check SHELL environment variable (Unix)
            string? shellEnv = System.Environment.GetEnvironmentVariable("SHELL");

            // Check PSModulePath for PowerShell detection
            string? psModulePath = System.Environment.GetEnvironmentVariable("PSModulePath");
            bool isPowerShell = !string.IsNullOrEmpty(psModulePath);

            // Check for bash-specific variables
            string? bashVersion = System.Environment.GetEnvironmentVariable("BASH_VERSION");
            if (!string.IsNullOrEmpty(bashVersion))
            {
                return ("bash", bashVersion);
            }

            // Check for zsh-specific variables
            string? zshVersion = System.Environment.GetEnvironmentVariable("ZSH_VERSION");
            if (!string.IsNullOrEmpty(zshVersion))
            {
                return ("zsh", zshVersion);
            }

            // PowerShell detection
            if (isPowerShell)
            {
                // Try to get PowerShell version
                if (isWindows)
                {
                    // Try pwsh first, then powershell
                    string? pwshPath = FindExecutableInPath("pwsh");
                    if (pwshPath != null)
                    {
                        var (version, edition) = GetPowerShellVersion(pwshPath);
                        return ($"PowerShell {edition}", version.ToString());
                    }
                    string? psPath = FindExecutableInPath("powershell");
                    if (psPath != null)
                    {
                        var (version, edition) = GetPowerShellVersion(psPath);
                        return ($"PowerShell {edition}", version.ToString());
                    }
                }
                else
                {
                    string? pwshPath = FindExecutableInPath("pwsh");
                    if (pwshPath != null)
                    {
                        var (version, edition) = GetPowerShellVersion(pwshPath);
                        return ($"PowerShell {edition}", version.ToString());
                    }
                }
                return ("PowerShell", "unknown");
            }

            // Windows Command Prompt detection
            if (isWindows)
            {
                string? comspec = System.Environment.GetEnvironmentVariable("COMSPEC");
                if (!string.IsNullOrEmpty(comspec) && comspec.EndsWith("cmd.exe", StringComparison.OrdinalIgnoreCase))
                {
                    // Get Windows version as proxy for cmd version
                    return ("cmd", System.Environment.OSVersion.Version.ToString());
                }
            }

            // Fall back to SHELL env var
            if (!string.IsNullOrEmpty(shellEnv))
            {
                string shellName = Path.GetFileName(shellEnv);
                return (shellName, "unknown");
            }

            return ("unknown", "");
        }

        private string? FindExecutableInPath(string name)
        {
            bool isWindows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);
            string[] extensions = isWindows ? new[] { ".exe", ".cmd", ".bat", "" } : new[] { "" };
            string? pathEnv = System.Environment.GetEnvironmentVariable("PATH");

            if (string.IsNullOrEmpty(pathEnv))
                return null;

            char separator = isWindows ? ';' : ':';
            foreach (string dir in pathEnv.Split(separator))
            {
                foreach (string ext in extensions)
                {
                    string fullPath = Path.Combine(dir, name + ext);
                    if (System.IO.File.Exists(fullPath))
                        return fullPath;
                }
            }
            return null;
        }

        private (bool installed, bool canInstall, string path) CheckTabCompletionStatus(string shellType)
        {
            bool isWindows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);
            string homeDir = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);

            switch (shellType.ToLowerInvariant())
            {
                case "bash":
                    // Check common bash completion paths
                    string[] bashPaths = {
                        "/etc/bash_completion.d/osync",
                        Path.Combine(homeDir, ".local/share/bash-completion/completions/osync"),
                        Path.Combine(homeDir, ".bash_completion.d/osync")
                    };
                    foreach (var path in bashPaths)
                    {
                        if (System.IO.File.Exists(path))
                            return (true, true, path);
                    }
                    // Check if sourced in bashrc
                    string bashrc = Path.Combine(homeDir, ".bashrc");
                    if (System.IO.File.Exists(bashrc))
                    {
                        string content = System.IO.File.ReadAllText(bashrc);
                        if (content.Contains("# osync bash completion script") || content.Contains("# osync completion"))
                            return (true, true, bashrc);
                    }
                    return (false, true, "");

                case "zsh":
                    // Check common zsh completion paths
                    string[] zshPaths = {
                        Path.Combine(homeDir, ".zfunc/_osync"),
                        "/usr/local/share/zsh/site-functions/_osync"
                    };
                    foreach (var path in zshPaths)
                    {
                        if (System.IO.File.Exists(path))
                            return (true, true, path);
                    }
                    return (false, true, "");

                case "powershell core":
                case "powershell desktop":
                case var ps when ps.StartsWith("PowerShell", StringComparison.OrdinalIgnoreCase):
                    // Check PowerShell profile for completion
                    string psProfile = isWindows
                        ? Path.Combine(homeDir, "Documents", "PowerShell", "Microsoft.PowerShell_profile.ps1")
                        : Path.Combine(homeDir, ".config", "powershell", "Microsoft.PowerShell_profile.ps1");
                    if (System.IO.File.Exists(psProfile))
                    {
                        string content = System.IO.File.ReadAllText(psProfile);
                        if (content.Contains("# osync PowerShell completion - START") || content.Contains("# osync tab completion"))
                            return (true, true, psProfile);
                    }
                    // Also check Windows PowerShell profile
                    if (isWindows)
                    {
                        string wpProfile = Path.Combine(homeDir, "Documents", "WindowsPowerShell", "Microsoft.PowerShell_profile.ps1");
                        if (System.IO.File.Exists(wpProfile))
                        {
                            string content = System.IO.File.ReadAllText(wpProfile);
                            if (content.Contains("# osync PowerShell completion - START") || content.Contains("# osync tab completion"))
                                return (true, true, wpProfile);
                        }
                    }
                    return (false, true, "");

                case "cmd":
                    // cmd.exe doesn't support custom tab completion
                    return (false, false, "");

                default:
                    return (false, false, "");
            }
        }

        private (string? version, string? build) GetInstalledBinaryVersionInfo(string binaryPath)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = binaryPath,
                    Arguments = "ShowVersion",  // Use the actual command name, not alias
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process == null) return (null, null);

                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(5000);

                // Parse version and build from output like "osync v1.2.6 (b20260110-1156)"
                // Use specific pattern to avoid matching IP addresses
                var match = System.Text.RegularExpressions.Regex.Match(output, @"osync\s+v?(\d+\.\d+\.\d+)\s*\((b\d{8}-\d{4})\)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    return (match.Groups[1].Value, match.Groups[2].Value);
                }

                // Try without build number
                var versionMatch = System.Text.RegularExpressions.Regex.Match(output, @"osync\s+v?(\d+\.\d+\.\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (versionMatch.Success)
                {
                    return (versionMatch.Groups[1].Value, null);
                }
                return (null, null);
            }
            catch
            {
                return (null, null);
            }
        }

        private int CompareVersions(string version1, string? build1, string version2, string? build2)
        {
            // Remove 'v' prefix if present
            version1 = version1.TrimStart('v');
            version2 = version2.TrimStart('v');

            // Split into parts
            var parts1 = version1.Split('.').Select(p => int.TryParse(p, out int v) ? v : 0).ToArray();
            var parts2 = version2.Split('.').Select(p => int.TryParse(p, out int v) ? v : 0).ToArray();

            // Pad arrays to same length
            int maxLen = Math.Max(parts1.Length, parts2.Length);
            Array.Resize(ref parts1, maxLen);
            Array.Resize(ref parts2, maxLen);

            // Compare version parts
            for (int i = 0; i < maxLen; i++)
            {
                if (parts1[i] > parts2[i]) return 1;
                if (parts1[i] < parts2[i]) return -1;
            }

            // Versions are equal, compare build numbers if available
            // Build format: b20260110-1156 (bYYYYMMDD-HHMM)
            if (!string.IsNullOrEmpty(build1) && !string.IsNullOrEmpty(build2))
            {
                // Extract date and time from build strings
                var buildDate1 = ParseBuildDateTime(build1);
                var buildDate2 = ParseBuildDateTime(build2);

                if (buildDate1.HasValue && buildDate2.HasValue)
                {
                    return buildDate1.Value.CompareTo(buildDate2.Value);
                }
            }

            return 0;
        }

        private DateTime? ParseBuildDateTime(string build)
        {
            // Parse build format: b20260110-1156 (bYYYYMMDD-HHMM)
            var match = System.Text.RegularExpressions.Regex.Match(build, @"b(\d{4})(\d{2})(\d{2})-(\d{2})(\d{2})");
            if (match.Success)
            {
                try
                {
                    int year = int.Parse(match.Groups[1].Value);
                    int month = int.Parse(match.Groups[2].Value);
                    int day = int.Parse(match.Groups[3].Value);
                    int hour = int.Parse(match.Groups[4].Value);
                    int minute = int.Parse(match.Groups[5].Value);
                    return new DateTime(year, month, day, hour, minute, 0);
                }
                catch
                {
                    return null;
                }
            }
            return null;
        }

        [ArgShortcut("-bt"), ArgDescription("Bandwidth throttling (B, KB, MB, GB)"), ArgExample("-bt 10MB", "Throttle bandwidth to 10MB"), ArgDefaultValue(0)]
        public string BandwidthThrottling { get; set; } = string.Empty;

        public long ParseBandwidthThrottling(string value)
        {
        try
            {
                if (long.TryParse(value, out long result))
                {
                    return result;
                }
                if (value.EndsWith("B", StringComparison.OrdinalIgnoreCase) && Tools.IsNumeric(value.Substring(value.Length-1, 1)))
                {
                    return long.Parse(value.Substring(0, value.Length - 1));
                }

                if (value.EndsWith("KB", StringComparison.OrdinalIgnoreCase)
                    || value.EndsWith("MB", StringComparison.OrdinalIgnoreCase)
                    || value.EndsWith("GB", StringComparison.OrdinalIgnoreCase))
                {

                    string numericPart = value.Substring(0, value.Length - 2);
                    long numericValue = long.Parse(numericPart);

                    char unit = value[value.Length - 2];
                    switch (char.ToLower(unit))
                    {
                        case 'k':
                            return numericValue * 1024;
                        case 'm':
                            return numericValue * 1024 * 1024;
                        case 'g':
                            return numericValue * 1024 * 1024 * 1024;
                        default:
                            Console.WriteLine($"Error: invalid bandwidth throttling format (unit={unit}).");
                            System.Environment.Exit(1);
                            return 0;
                    }
                }
                else
                {
                    Console.WriteLine($"Error: invalid bandwidth throttling format.");
                    System.Environment.Exit(1);
                    return 0;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Exception, invalid bandwidth throttling format: {e.Message}");
                System.Environment.Exit(1);
                return 0;
            }
        }
        public static bool GetPlatformColor()
        {
            string thisOs = System.Environment.OSVersion.Platform.ToString();

            if (thisOs == "Win32NT")
            {
                return false;
            }
            else if (thisOs == "Unix" && System.Environment.OSVersion.VersionString.Contains("Darwin"))
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        public static void SetCursorVisible(bool visible)
        {
            try
            {
                System.Console.CursorVisible = visible;
            }
            catch
            {
                // Ignore - cursor visibility not supported in this terminal
            }
        }

        /// <summary>
        /// Convert model parameter value to proper type for JSON serialization.
        /// Ollama API expects specific types for parameters (integers, floats, booleans).
        /// </summary>
        private static object ConvertParameterValue(string key, string value)
        {
            // Integer parameters
            var intParams = new HashSet<string> {
                "num_ctx", "num_gpu", "num_predict", "top_k", "seed", "mirostat",
                "num_keep", "num_thread", "num_batch", "main_gpu", "low_vram",
                "vocab_only", "repeat_last_n"
            };

            // Float parameters
            var floatParams = new HashSet<string> {
                "temperature", "top_p", "repeat_penalty", "min_p", "typical_p",
                "presence_penalty", "frequency_penalty", "tfs_z", "mirostat_tau",
                "mirostat_eta", "rope_frequency_base", "rope_frequency_scale"
            };

            // Boolean parameters
            var boolParams = new HashSet<string> {
                "numa", "penalize_newline", "f16_kv", "use_mlock", "use_mmap",
                "low_vram", "vocab_only"
            };

            // Try to convert to integer
            if (intParams.Contains(key))
            {
                if (int.TryParse(value, out int intVal))
                    return intVal;
                // Try as float and convert to int
                if (double.TryParse(value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double dVal))
                    return (int)dVal;
            }

            // Try to convert to float
            if (floatParams.Contains(key))
            {
                if (double.TryParse(value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double floatVal))
                    return floatVal;
            }

            // Try to convert to boolean
            if (boolParams.Contains(key))
            {
                if (bool.TryParse(value, out bool boolVal))
                    return boolVal;
                // Also handle "1"/"0" as true/false
                if (value == "1" || value.ToLower() == "true")
                    return true;
                if (value == "0" || value.ToLower() == "false")
                    return false;
            }

            // For unknown parameters, try to auto-detect type
            if (int.TryParse(value, out int autoInt))
                return autoInt;
            if (double.TryParse(value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double autoFloat))
                return autoFloat;
            if (bool.TryParse(value, out bool autoBool))
                return autoBool;

            // Return as string (default)
            return value;
        }

        public bool ValidateServer(string RemoteServer, bool silent = false)
        {
            try
            {
                // Normalize the URL with default protocol and port
                RemoteServer = NormalizeServerUrl(RemoteServer);

                Uri RemoteUri = new Uri(RemoteServer);
                if ((RemoteUri.Scheme == "http" || RemoteUri.Scheme == "https") == false) { return false; };

                client.BaseAddress = new Uri(RemoteServer);

                RunCheckServer(silent).GetAwaiter().GetResult();

            }
            catch (UriFormatException)
            {
                Console.WriteLine("Error: remote server URL is not valid");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Could not test url {RemoteServer}: {ex.Message}");
                return false;
            }
            return true;
        }

        /// <summary>
        /// Normalize a server URL by adding default protocol (http://) and port (11434) if not specified.
        /// </summary>
        internal static string NormalizeServerUrl(string serverUrl)
        {
            if (string.IsNullOrWhiteSpace(serverUrl))
                return serverUrl;

            var url = serverUrl.Trim();

            // Add default protocol if not specified
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                url = "http://" + url;
            }

            try
            {
                var uri = new Uri(url);

                // Add default port 11434 if no port is specified (port will be -1 or 80/443 for default)
                if (uri.Port == -1 || uri.Port == 80 || uri.Port == 443)
                {
                    // Check if original URL explicitly had a port
                    var hostPart = serverUrl;
                    if (hostPart.Contains("://"))
                        hostPart = hostPart.Substring(hostPart.IndexOf("://") + 3);

                    // If no explicit port in the original, add default
                    if (!hostPart.Contains(":") || hostPart.EndsWith(":"))
                    {
                        var builder = new UriBuilder(uri)
                        {
                            Port = 11434
                        };
                        url = builder.ToString().TrimEnd('/');
                    }
                }

                return url.TrimEnd('/');
            }
            catch
            {
                return url;
            }
        }

        /// <summary>
        /// Check if a string looks like a remote server URL or address (IP, hostname, or URL).
        /// This is used to distinguish between a remote server and a local model name.
        ///
        /// Detection rules:
        /// - Has protocol (http:// or https://) → remote server
        /// - Is an IP address (192.168.0.100) → remote server
        /// - Has port number (host:11434) → remote server
        /// - Ends with "/" (host/) → remote server (model names cannot end with /)
        /// - Starts with "localhost" → remote server
        /// - Plain hostname without port or trailing slash → treated as model name
        /// </summary>
        internal static bool LooksLikeRemoteServer(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return false;

            var trimmed = input.Trim();

            // Already has protocol
            if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return true;

            // Skip known model prefixes that are not remote servers
            if (trimmed.StartsWith("hf.co/", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("huggingface.co/", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("registry.ollama.ai/", StringComparison.OrdinalIgnoreCase))
                return false;

            // Ends with "/" - model names cannot end with slash, so this is a server URL
            // e.g., "myserver/" → http://myserver:11434
            if (trimmed.EndsWith("/"))
                return true;

            // Check if it looks like an IP address (with optional port)
            // Pattern: numbers and dots, optionally followed by :port
            var ipPattern = @"^\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}(:\d+)?(/.*)?$";
            if (System.Text.RegularExpressions.Regex.IsMatch(trimmed, ipPattern))
                return true;

            // Check if it looks like a hostname with port (hostname:port)
            // Must have a colon followed by numbers and no slash before the colon
            var hostPortPattern = @"^[a-zA-Z][a-zA-Z0-9\-\.]*:\d+(\/.*)?$";
            if (System.Text.RegularExpressions.Regex.IsMatch(trimmed, hostPortPattern))
                return true;

            // Check if it looks like "localhost" with optional port
            if (trimmed.StartsWith("localhost", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        private bool ValidateServerUrl(string serverUrl, bool silent = false)
        {
            try
            {
                // Normalize the URL with default protocol and port
                serverUrl = NormalizeServerUrl(serverUrl);

                Uri serverUri = new Uri(serverUrl);
                if (serverUri.Scheme != "http" && serverUri.Scheme != "https")
                {
                    return false;
                }

                // Create a temporary client for validation to avoid modifying global client state
                using (var tempClient = new HttpClient() { Timeout = TimeSpan.FromSeconds(10) })
                {
                    tempClient.BaseAddress = new Uri(serverUrl);
                    var response = tempClient.GetAsync("api/version").GetAwaiter().GetResult();
                    var statusCode = (int)response.StatusCode;

                    if (statusCode >= 100 && statusCode < 400)
                    {
                        return true;
                    }
                    else if (statusCode >= 500 && statusCode <= 510)
                    {
                        if (!silent)
                            Console.WriteLine($"Error: the server {serverUrl} has thrown an internal error. ollama instance is not available");
                        return false;
                    }
                    else
                    {
                        if (!silent)
                            Console.WriteLine($"Error: the server {serverUrl} answered with HTTP status code: {statusCode}");
                        return false;
                    }
                }
            }
            catch (UriFormatException)
            {
                if (!silent)
                    Console.WriteLine($"Error: server URL is not valid: {serverUrl}");
                return false;
            }
            catch (Exception ex)
            {
                if (!silent)
                    Console.WriteLine($"Could not connect to server {serverUrl}: {ex.Message}");
                return false;
            }
        }

        static async Task<HttpStatusCode> GetPs()
        {
            HttpResponseMessage response = await client.GetAsync(
                $"api/ps");
            return response.StatusCode;
        }

        static async Task RunCheckServer(bool silent = false)
        {
            try
            {
                HttpResponseMessage response = await client.GetAsync(
                    $"api/version");
                var statusCode = (int)response.StatusCode;

                if (statusCode >= 100 && statusCode < 400)
                {
                    if (!silent)
                    {
                        string versionjson = response.Content.ReadAsStringAsync().Result;
                        RootVersion? _version = VersionReader.Read<RootVersion>(versionjson);
                        Console.WriteLine("");

                        Console.WriteLine($"The remote ollama server is active and running v{_version?.version ?? "unknown"}");
                    }
                }
                else if (statusCode >= 500 && statusCode <= 510)
                {
                    Console.WriteLine("Error: the remote server has thrown an internal error. ollama instance is not available");
                    System.Environment.Exit(1);
                }
                else
                {
                    Console.WriteLine($"Error: the remote ollama server has answered with HTTP status code: {statusCode}");
                    System.Environment.Exit(1);
                }

            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
        }
        static async Task<HttpStatusCode> BlobHead(string digest, string? serverUrl = null)
        {
            HttpRequestMessage request;
            if (!string.IsNullOrEmpty(serverUrl))
            {
                request = new(HttpMethod.Head, $"{serverUrl.TrimEnd('/')}/api/blobs/{digest}");
            }
            else
            {
                request = new(HttpMethod.Head, $"api/blobs/{digest}");
            }
            HttpResponseMessage response = await client.SendAsync(request);
            return response.StatusCode;
        }
        static async Task<HttpStatusCode> BlobUpload(string digest, string blobfile)
        {
            HttpResponseMessage response = await client.GetAsync(
                $"api/blobs/{digest}");
            return response.StatusCode;
        }
        static async Task RunBlobUpload(string digest, string blobfile, long btvalue, CancellationToken cancellationToken = default, bool throwOnError = false, string? serverUrl = null)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var statusCode = (int)await BlobHead(digest, serverUrl);
                cancellationToken.ThrowIfCancellationRequested();

                if (statusCode == 200)
                {
                    Console.WriteLine($"skipping upload for already created layer {digest}");
                }
                else if (statusCode == 404)
                {
                    Console.WriteLine($"uploading layer {digest}");

                    using (FileStream f = new FileStream(blobfile, FileMode.Open, FileAccess.Read))
                    {
                        Stopwatch stopwatch = new Stopwatch();

                        try
                        {
                            SetCursorVisible(false);

                            Stream originalFileStream = f;
                            Stream destFileStream = new ThrottledStream(originalFileStream, btvalue);

                            int block_size = 1024;
                            int total_size = (int)(new FileInfo(blobfile).Length/block_size);
                            var blob_size = ByteSize.FromKibiBytes(total_size);                            
                            var bar = new Tqdm.ProgressBar(total: (int)blob_size.MebiBytes, useColor: GetPlatformColor(), useExpMovingAvg: false, printsPerSecond: 10);
                            bool finished = false;
                            int tick = 0;
                            var streamcontent = new ProgressableStreamContent(new StreamContent(destFileStream), (sent, total) => {
                                tick++;
                                if (tick < 40) { return; }
                                tick = 0;
                                double elapsedTimeInSeconds = stopwatch.Elapsed.TotalSeconds;
                                double speedInBytesPerSecond = sent / elapsedTimeInSeconds;
                                try
                                {
                                    int percentage = (int)((sent * 100.0) / total);
                                    var sent_size = ByteSize.FromKibiBytes(sent / block_size);
                                    percentage = (int)sent_size.MebiBytes;
                                    bar.SetLabel($"({ByteSize.FromKibiBytes(sent / block_size).ToString("#")} / {ByteSize.FromKibiBytes(total / block_size).ToString("#")}) uploading at {ByteSize.FromKibiBytes(speedInBytesPerSecond / block_size).ToString("#")}/s");
                                    if (!finished)
                                    {
                                        bar.Progress(percentage);
                                    }
                                    else
                                    {
                                        bar.Progress((int)blob_size.MebiBytes);
                                    }
                                }
                                catch
                                {
                                    // Silently ignore progress bar errors (console handle issues)
                                }

                            });

                            stopwatch.Start();
                            string postUrl = !string.IsNullOrEmpty(serverUrl)
                                ? $"{serverUrl.TrimEnd('/')}/api/blobs/{digest}"
                                : $"api/blobs/{digest}";
                            var response = await client.PostAsync(postUrl, streamcontent, cancellationToken);
                            finished = true;

                            SetCursorVisible(true);

                            if (response.IsSuccessStatusCode)
                            {
                                bar.Finish();
                                Console.WriteLine("success uploading layer");
                            }
                            else if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
                            {
                                var errorMsg = "Error: upload failed invalid digest, check both ollama are running the same version.";
                                Console.WriteLine(errorMsg);
                                if (throwOnError) throw new Exception(errorMsg);
                                System.Environment.Exit(1);
                            }
                            else
                            {
                                var errorMsg = $"Error: upload failed: {response.ReasonPhrase}";
                                Console.WriteLine(errorMsg);
                                if (throwOnError) throw new Exception(errorMsg);
                                System.Environment.Exit(1);
                            }

                            streamcontent.Dispose();
                        }
                        catch (Exception e) when (e is OperationCanceledException || e is TaskCanceledException)
                        {
                            SetCursorVisible(true);
                            throw; // Re-throw cancellation to allow proper handling
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine($"Error: {e.Message}");
                            SetCursorVisible(true);
                            if (throwOnError) throw;
                            System.Environment.Exit(1);
                        }
                        finally
                        {
                            stopwatch.Stop();
                        }
                    }

                }
                else
                {
                    var errorMsg = $"Error: the remote server has answered with HTTP status code: {statusCode} for layer {digest}";
                    Console.WriteLine(errorMsg);
                    if (throwOnError) throw new Exception(errorMsg);
                    System.Environment.Exit(1);
                }

            }
            catch (Exception e) when (e is OperationCanceledException || e is TaskCanceledException)
            {
                throw; // Re-throw cancellation
            }
            catch (Exception e)
            {
                Console.WriteLine($"Exception: error during blob upload: {e.Message}");
                if (throwOnError) throw;
            }
        }

        static async Task RunCreateModel(string Modelname, Dictionary<string, string> files, string? template = null, string? system = null, List<string>? parameters = null, string? serverUrl = null, bool throwOnError = false)
        {
            int exitcode = 0;
            try
            {
                var modelCreate = new Dictionary<string, object>
                {
                    { "model", Modelname },
                    { "files", files }
                };

                if (!string.IsNullOrEmpty(template))
                    modelCreate["template"] = template;
                if (!string.IsNullOrEmpty(system))
                    modelCreate["system"] = system;
                if (parameters != null && parameters.Count > 0)
                {
                    var paramDict = new Dictionary<string, object>();
                    foreach (var param in parameters)
                    {
                        var parts = param.Split(new[] { ' ' }, 2);
                        if (parts.Length == 2)
                        {
                            var key = parts[0];
                            var value = parts[1];

                            // "stop" parameter must be an array
                            if (key == "stop")
                            {
                                // Check if there's an existing stop array to add to
                                if (paramDict.ContainsKey("stop") && paramDict["stop"] is List<string> existingStops)
                                {
                                    existingStops.Add(value);
                                }
                                else
                                {
                                    paramDict["stop"] = new List<string> { value };
                                }
                            }
                            else
                            {
                                // Convert to proper type based on parameter name
                                paramDict[key] = ConvertParameterValue(key, value);
                            }
                        }
                    }
                    if (paramDict.Count > 0)
                        modelCreate["parameters"] = paramDict;
                }

                string data = System.Text.Json.JsonSerializer.Serialize(modelCreate);

                SetCursorVisible(false);

                try
                {
                    var body = new StringContent(data, Encoding.UTF8, "application/json");
                    string createUrl = !string.IsNullOrEmpty(serverUrl)
                        ? $"{serverUrl.TrimEnd('/')}/api/create"
                        : $"api/create";
                    var postmessage = new HttpRequestMessage(HttpMethod.Post, createUrl);
                    postmessage.Content = body;
                    var response = await client.SendAsync(postmessage, HttpCompletionOption.ResponseHeadersRead);
                    var stream = await response.Content.ReadAsStreamAsync();
                    string laststatus = "";
                    using (var streamReader = new StreamReader(stream))
                    {
                        while (!streamReader.EndOfStream)
                        {
                            string? statusline = await streamReader.ReadLineAsync();
                            if (statusline != null)
                            {
                                RootStatus? status = StatusReader.Read<RootStatus>(statusline);
                                if (status?.status != null)
                                {
                                    laststatus = status.status;
                                    Console.WriteLine(status.status);
                                }
                            }
                        }
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        Console.WriteLine($"Error: could not create '{Modelname}' on the remote server (HTTP status {(int)response.StatusCode}): {response.ReasonPhrase}");
                        exitcode = 1;
                    }
                    else if (laststatus != "success")
                    {
                        exitcode = 1;
                    }
                }
                catch (HttpRequestException e)
                {
                    Console.WriteLine($"HttpRequestException: could not create '{Modelname}' on the remote server: {e.Message}");
                    exitcode = 1;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Exception: could not create '{Modelname}' on the remote server: {e.Message}");
                exitcode = 1;
            }
            finally
            {
                SetCursorVisible(true);
                // Only exit if not in manage mode (throwOnError=false means we should exit on error)
                if (!throwOnError)
                {
                    System.Environment.Exit(exitcode);
                }
                else if (exitcode != 0)
                {
                    // In manage mode, throw exception instead of exiting
                    throw new Exception($"Blob upload failed with exit code {exitcode}");
                }
            }
        }
        public static string GetPlatformPath(string inputPath, string separator)
        {
            if (inputPath != "*")
            {
                return inputPath;
            }
            else
            {
                string thisOs = System.Environment.OSVersion.Platform.ToString();

                if (thisOs == "Win32NT")
                {
                    return $"{System.Environment.GetEnvironmentVariable("USERPROFILE")}{separator}.ollama{separator}models";
                }
                else if (thisOs == "Unix" && System.Environment.OSVersion.VersionString.Contains("Darwin"))
                {
                    return $"~{separator}.ollama{separator}models";
                }
                else
                {
                    return $"{separator}usr{separator}share{separator}ollama{separator}.ollama{separator}models";
                }
            }
        }
        
        public static string ModelBase(string modelName)
        {
            string[] parts = modelName.Split('/');

            if (modelName.Contains("/"))
            {
                return parts[0];
            }
            else
            {
                return "";
            }
        }

        /// <summary>
        /// Parse a remote URL into server URL and model name components.
        /// Model name may be empty if the URL doesn't contain one.
        /// </summary>
        private (string serverUrl, string modelName) ParseRemoteSource(string source, bool requireModelName = false)
        {
            // Format: http://server:port/modelname:tag or http://server:port/namespace/modelname:tag
            try
            {
                // Normalize the URL first to add protocol and port
                var normalizedSource = NormalizeServerUrl(source);

                Uri uri = new Uri(normalizedSource);
                string serverUrl = $"{uri.Scheme}://{uri.Host}:{uri.Port}";
                string modelName = uri.AbsolutePath.TrimStart('/');

                if (requireModelName && string.IsNullOrEmpty(modelName))
                {
                    Console.WriteLine("Error: model name must be specified in the URL (e.g., http://server:port/modelname:tag)");
                    System.Environment.Exit(1);
                }

                return (serverUrl, modelName);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing remote URL: {ex.Message}");
                System.Environment.Exit(1);
                return (null!, null!);
            }
        }

        public void ActionCopy(string Source, string Destination, string? BufferSize = null, CancellationToken cancellationToken = default)
        {
            Init();

            Debug.WriteLine("Copy: you entered model '{0}' and destination '{1}'. OLLAMA_MODELS={2}", Source, Destination, ollama_models);

            // Check for cancellation before starting
            cancellationToken.ThrowIfCancellationRequested();

            // Detect if source and destination are remote or local
            // Use LooksLikeRemoteServer to detect IP addresses and hostnames without protocol
            bool isSourceRemote = Source.StartsWith("http://") || Source.StartsWith("https://");
            bool isDestinationRemote = LooksLikeRemoteServer(Destination);

            if (isSourceRemote && isDestinationRemote)
            {
                // Remote to Remote copy - use streaming buffer
                var (sourceServer, sourceModel) = ParseRemoteSource(Source, requireModelName: true);
                var (destServer, destModel) = ParseRemoteSource(Destination, requireModelName: false);

                // If destination has no model name, use source model name
                if (string.IsNullOrEmpty(destModel))
                {
                    destModel = sourceModel;
                }

                Console.WriteLine($"Copying '{sourceModel}' from {sourceServer} to '{destModel}' on {destServer}...");
                ActionCopyRemoteToRemoteStreaming(sourceServer, sourceModel, destServer, destModel, BufferSize).GetAwaiter().GetResult();
            }
            else if (isSourceRemote && !isDestinationRemote)
            {
                // Remote to Local copy
                var (sourceServer, sourceModel) = ParseRemoteSource(Source, requireModelName: true);

                Console.WriteLine($"Copying '{sourceModel}' from {sourceServer} to local '{Destination}'...");
                ActionCopyRemoteToLocal(sourceServer, sourceModel, Destination);
            }
            else if (!isSourceRemote && isDestinationRemote)
            {
                // Local to Remote copy (including hf.co models which are "local" in terms of source)
                var (destServer, destModel) = ParseRemoteSource(Destination, requireModelName: false);

                // If destination has no model name, derive from source
                if (string.IsNullOrEmpty(destModel))
                {
                    // Extract model name from source (handle hf.co/namespace/model:tag format)
                    destModel = GetModelNameFromSource(Source);
                }

                if (!Directory.Exists(ollama_models))
                {
                    Console.WriteLine($"Error: ollama models directory not found at: {ollama_models}");
                    System.Environment.Exit(1);
                }

                if (!ValidateServerUrl(destServer))
                {
                    System.Environment.Exit(1);
                }

                ActionCopyLocalToRemote(Source, destServer, destModel, cancellationToken);
            }
            else
            {
                // Local to Local copy
                ActionCopyLocal(Source, Destination);
            }
        }

        /// <summary>
        /// Extract a model name from a source string (handles hf.co/namespace/model:tag format)
        /// </summary>
        private string GetModelNameFromSource(string source)
        {
            // Handle hf.co/namespace/model:tag format
            if (source.StartsWith("hf.co/", StringComparison.OrdinalIgnoreCase) ||
                source.StartsWith("huggingface.co/", StringComparison.OrdinalIgnoreCase))
            {
                // Extract the model name part (last segment after the prefix)
                var parts = source.Split('/');
                if (parts.Length >= 3)
                {
                    // Format: hf.co/namespace/model:tag -> return model:tag or namespace/model:tag
                    var modelPart = string.Join("/", parts.Skip(1)); // Skip "hf.co"
                    // Clean up the model name - remove special characters that aren't valid
                    modelPart = modelPart.Replace("-GGUF", "").Replace("-gguf", "");
                    return modelPart;
                }
            }

            // For regular model names, just return as-is
            return source;
        }

        private void ActionCopyLocal(string source, string destination)
        {
            // Apply :latest tag if not specified
            string sourceModel = source;
            string destModel = destination;

            if (!sourceModel.Contains(":"))
            {
                sourceModel = $"{sourceModel}:latest";
            }

            if (!destModel.Contains(":"))
            {
                destModel = $"{destModel}:latest";
            }

            // Check if destination already exists
            var checkProcess = new Process();
            checkProcess.StartInfo.FileName = "ollama";
            checkProcess.StartInfo.Arguments = "list";
            checkProcess.StartInfo.CreateNoWindow = true;
            checkProcess.StartInfo.UseShellExecute = false;
            checkProcess.StartInfo.RedirectStandardOutput = true;
            checkProcess.StartInfo.RedirectStandardError = true;

            try
            {
                checkProcess.Start();
                string output = checkProcess.StandardOutput.ReadToEnd();
                checkProcess.WaitForExit();

                if (output.Contains(destModel))
                {
                    Console.WriteLine($"Error: destination model '{destModel}' already exists");
                    Console.WriteLine("Please choose a different name or remove the existing model first.");
                    System.Environment.Exit(1);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Warning: could not check if destination exists: {e.Message}");
            }

            Console.WriteLine($"Copying model '{sourceModel}' to '{destModel}'...");

            // Copy using ollama cp
            var copyProcess = new Process();
            copyProcess.StartInfo.FileName = "ollama";
            copyProcess.StartInfo.Arguments = $"cp {sourceModel} {destModel}";
            copyProcess.StartInfo.CreateNoWindow = true;
            copyProcess.StartInfo.UseShellExecute = false;
            copyProcess.StartInfo.RedirectStandardOutput = true;
            copyProcess.StartInfo.RedirectStandardError = true;

            try
            {
                copyProcess.Start();
                copyProcess.WaitForExit();

                if (copyProcess.ExitCode != 0)
                {
                    string error = copyProcess.StandardError.ReadToEnd();
                    Console.WriteLine($"Error: failed to copy model: {error}");
                    System.Environment.Exit(1);
                }

                Console.WriteLine($"Successfully copied '{sourceModel}' to '{destModel}'");
            }
            catch (Exception e)
            {
                Console.WriteLine($"Exception: failed to copy model: {e.Message}");
                System.Environment.Exit(1);
            }
        }

        private void ActionCopyLocalToRemote(string Source, string destServer, string destModel, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!Directory.Exists(ollama_models))
            {
                Console.WriteLine($"Error: ollama models directory not found at: {ollama_models}");
                System.Environment.Exit(1);
            }

            if (!ValidateServerUrl(destServer))
            {
                System.Environment.Exit(1);
            }

            string modelBase = ModelBase(Source);

            string modelDir;
            string blobDir = $"{ollama_models}{separator}blobs";
            string manifest_file = Source;
            if (!Source.Contains(":"))
            {
                manifest_file = $"{manifest_file}{separator}latest";
            }
            else
            {
                manifest_file = Source.Replace(":", separator);
            }

            // Convert forward slashes to platform separator for path construction
            manifest_file = manifest_file.Replace("/", separator);

            if (modelBase == "hub")
            {
                modelDir = Path.Combine(ollama_models, "manifests", manifest_file);
            }
            else if (modelBase == "hf.co")
            {
                // HuggingFace models are stored directly under manifests/hf.co/... (not under registry.ollama.ai)
                modelDir = Path.Combine(ollama_models, "manifests", manifest_file);
            }
            else if (modelBase == "")
            {
                modelDir = Path.Combine(ollama_models, "manifests", "registry.ollama.ai", "library", manifest_file);
            }
            else
            {
                modelDir = Path.Combine(ollama_models, "manifests", "registry.ollama.ai", manifest_file);
            }

            if (!System.IO.File.Exists(modelDir))
            {
                // If model not found and no tag specified, try with :latest
                if (!Source.Contains(":"))
                {
                    string latestSource = $"{Source}:latest";
                    string latestManifestFile = latestSource.Replace(":", separator).Replace("/", separator);
                    string latestModelDir;

                    if (modelBase == "hub")
                    {
                        latestModelDir = Path.Combine(ollama_models, "manifests", latestManifestFile);
                    }
                    else if (modelBase == "hf.co")
                    {
                        latestModelDir = Path.Combine(ollama_models, "manifests", latestManifestFile);
                    }
                    else if (modelBase == "")
                    {
                        latestModelDir = Path.Combine(ollama_models, "manifests", "registry.ollama.ai", "library", latestManifestFile);
                    }
                    else
                    {
                        latestModelDir = Path.Combine(ollama_models, "manifests", "registry.ollama.ai", latestManifestFile);
                    }

                    if (System.IO.File.Exists(latestModelDir))
                    {
                        // Found with :latest tag, update variables
                        Source = latestSource;
                        manifest_file = latestManifestFile;
                        modelDir = latestModelDir;
                    }
                    else
                    {
                        Console.WriteLine($"Error: model '{Source}' not found (tried '{Source}' and '{latestSource}')");
                        System.Environment.Exit(1);
                    }
                }
                else
                {
                    Console.WriteLine($"Error: model '{Source}' not found at: {modelDir}");
                    System.Environment.Exit(1);
                }
            }

            Console.WriteLine($"Copying model '{Source}' to '{destModel}' on {destServer}...");

            // Parse modelfile to extract template, system, and parameters
            var templateBuilder = new StringBuilder();
            var parameters = new List<string>();
            string? system = null;
            bool inTemplate = false;

            var p = new Process();
            p.StartInfo.FileName = "ollama";
            p.StartInfo.Arguments = @" show " + Source + " --modelfile";
            p.StartInfo.CreateNoWindow = true;
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            p.StartInfo.RedirectStandardError = false;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.RedirectStandardInput = false;

            var modelfileLines = new List<string>();
            p.OutputDataReceived += (a, b) => {
                if (b != null && b.Data != null)
                {
                    if (!b.Data.StartsWith("failed to get console mode"))
                    {
                        modelfileLines.Add(b.Data);
                    }
                }
            };

            var stdOutput = new StringBuilder();
            p.OutputDataReceived += (sender, args) => stdOutput.AppendLine(args.Data);

            try
            {
                p.Start();
                p.BeginOutputReadLine();
                p.WaitForExit();
            }
            catch (Exception e)
            {
                Console.WriteLine($"Exception: get Modelfile from ollama show failed: {e.Message}");
                System.Environment.Exit(1);
            }

            if (p.ExitCode != 0)
            {
                Console.WriteLine($"Error: get Modelfile from ollama show failed: {stdOutput.ToString()}");
                System.Environment.Exit(1);
            }

            stdOutput.Clear();
            stdOutput = null;

            // Parse the modelfile lines
            for (int i = 0; i < modelfileLines.Count; i++)
            {
                string line = modelfileLines[i];

                if (line.StartsWith("#"))
                    continue;

                if (line.StartsWith("TEMPLATE "))
                {
                    inTemplate = true;
                    string templateStart = line.Substring(9).TrimStart('"');
                    templateBuilder.AppendLine(templateStart);
                    continue;
                }

                if (inTemplate)
                {
                    if (line.EndsWith("\""))
                    {
                        templateBuilder.Append(line.TrimEnd('"'));
                        inTemplate = false;
                    }
                    else
                    {
                        templateBuilder.AppendLine(line);
                    }
                    continue;
                }

                if (line.StartsWith("SYSTEM "))
                {
                    system = line.Substring(7).Trim().Trim('"');
                    continue;
                }

                if (line.StartsWith("PARAMETER "))
                {
                    parameters.Add(line.Substring(10));
                    continue;
                }
            }

            string? template = templateBuilder.Length > 0 ? templateBuilder.ToString() : null;

            RootManifest? manifest = ManifestReader.Read<RootManifest>(modelDir);
            if (manifest?.layers == null)
            {
                Console.WriteLine("Error: Invalid manifest file");
                System.Environment.Exit(1);
                return;
            }

            // Build files dictionary and upload blobs
            var files = new Dictionary<string, string>();
            int modelIndex = 0;
            int adapterIndex = 0;
            int projectorIndex = 0;

            foreach (Layer layer in manifest.layers)
            {
                if (layer.mediaType.StartsWith("application/vnd.ollama.image.model"))
                {
                    var digest = layer.digest;
                    var hash = digest.Substring(7);
                    var blobfile = $"{blobDir}{separator}sha256-{hash}";
                    RunBlobUpload(digest, blobfile, btvalue, cancellationToken, ThrowOnError, destServer).GetAwaiter().GetResult();

                    string filename = modelIndex == 0 ? "model.gguf" : $"model_{modelIndex}.gguf";
                    files[filename] = digest;
                    modelIndex++;
                }
                else if (layer.mediaType.StartsWith("application/vnd.ollama.image.projector"))
                {
                    var digest = layer.digest;
                    var hash = digest.Substring(7);
                    var blobfile = $"{blobDir}{separator}sha256-{hash}";
                    RunBlobUpload(digest, blobfile, btvalue, cancellationToken, ThrowOnError, destServer).GetAwaiter().GetResult();

                    string filename = projectorIndex == 0 ? "projector.gguf" : $"projector_{projectorIndex}.gguf";
                    files[filename] = digest;
                    projectorIndex++;
                }
                else if (layer.mediaType.StartsWith("application/vnd.ollama.image.adapter"))
                {
                    var digest = layer.digest;
                    var hash = digest.Substring(7);
                    var blobfile = $"{blobDir}{separator}sha256-{hash}";
                    RunBlobUpload(digest, blobfile, btvalue, cancellationToken, ThrowOnError, destServer).GetAwaiter().GetResult();

                    string filename = adapterIndex == 0 ? "adapter.gguf" : $"adapter_{adapterIndex}.gguf";
                    files[filename] = digest;
                    adapterIndex++;
                }
            }

            Debug.WriteLine($"Creating model with files: {string.Join(", ", files.Keys)}");

            RunCreateModel(destModel, files, template, system, parameters, destServer, ThrowOnError).GetAwaiter().GetResult();
        }

        private async Task ActionCopyRemoteToRemoteStreaming(string sourceServer, string sourceModel, string destServer, string destModel, string? bufferSizeStr)
        {
            try
            {
                // Validate both servers without modifying global client state
                if (!ValidateServerUrl(sourceServer))
                {
                    Console.WriteLine($"Error: cannot connect to source server {sourceServer}");
                    System.Environment.Exit(1);
                }

                if (!ValidateServerUrl(destServer))
                {
                    Console.WriteLine($"Error: cannot connect to destination server {destServer}");
                    System.Environment.Exit(1);
                }

                // Parse buffer size (default 512MB)
                long bufferSize = 512 * 1024 * 1024; // 512MB default
                if (!string.IsNullOrEmpty(bufferSizeStr))
                {
                    bufferSize = ParseSize(bufferSizeStr);
                }
                Console.WriteLine($"Using buffer size: {ByteSize.FromBytes(bufferSize).ToString("#.##")}");

                // Create dedicated client for source server
                var sourceClient = new HttpClient() { Timeout = TimeSpan.FromHours(1) };
                sourceClient.BaseAddress = new Uri(sourceServer);

                Console.WriteLine($"Fetching model information from source server...");

                // Get modelfile string
                var (modelfile, modelfileErr) = await GetRemoteModelfileStringWithClient(sourceClient, sourceModel);

                if (modelfile == null)
                {
                    Console.WriteLine($"Error: Could not retrieve model from source server");
                    if (modelfileErr != null)
                    {
                        Console.WriteLine($"Details: {modelfileErr}");
                    }
                    System.Environment.Exit(1);
                }

                // Extract blob digests from modelfile
                var blobDigests = ExtractBlobDigestsFromModelfile(modelfile);

                if (blobDigests.Count == 0)
                {
                    Console.WriteLine($"Error: No blob references found in modelfile");
                    System.Environment.Exit(1);
                }

                Console.WriteLine($"Found {blobDigests.Count} blob(s) to transfer");

                // Get modelfile information for creating model on destination
                var (template, system, parameters) = await GetRemoteModelfileWithClient(sourceClient, sourceModel);

                // Stream each blob from source to destination through memory buffer
                var files = new Dictionary<string, string>();
                int modelIndex = 0;

                foreach (var digest in blobDigests)
                {
                    Console.WriteLine($"\nTransferring blob {digest.Substring(0, 19)}...");

                    await StreamBlobSourceToDestination(
                        sourceServer,
                        destServer,
                        sourceModel,
                        digest,
                        bufferSize
                    );

                    // Track files for model creation
                    string filename = modelIndex == 0 ? "model.gguf" : $"model_{modelIndex}.gguf";
                    files[filename] = digest;
                    modelIndex++;
                }

                // Create model on destination server
                Console.WriteLine($"\nCreating model '{destModel}' on destination server...");
                var destClient = new HttpClient() { Timeout = TimeSpan.FromHours(1) };
                destClient.BaseAddress = new Uri(destServer);

                await RunCreateModelWithClient(destClient, destModel, files, template, system, parameters);

                Console.WriteLine($"\nSuccessfully copied '{sourceModel}' from {sourceServer} to '{destModel}' on {destServer}");

                sourceClient.Dispose();
                destClient.Dispose();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nError during copy: {ex.Message}");
                throw;
            }
        }

        private void ActionCopyRemoteToLocal(string sourceServer, string sourceModel, string destModel)
        {
            ActionCopyRemoteToLocalAsync(sourceServer, sourceModel, destModel).GetAwaiter().GetResult();
        }

        private async Task ActionCopyRemoteToLocalAsync(string sourceServer, string sourceModel, string destModel)
        {
            // Validate source server
            if (!ValidateServerUrl(sourceServer))
            {
                Console.WriteLine($"Error: cannot connect to source server {sourceServer}");
                System.Environment.Exit(1);
            }

            // Ensure local ollama models directory exists
            if (!Directory.Exists(ollama_models))
            {
                Console.WriteLine($"Error: ollama models directory not found at: {ollama_models}");
                System.Environment.Exit(1);
            }

            // Create dedicated client for source server
            var sourceClient = new HttpClient() { Timeout = TimeSpan.FromHours(1) };
            sourceClient.BaseAddress = new Uri(sourceServer);

            Console.WriteLine($"Fetching model information from {sourceServer}...");

            // Get modelfile string to extract blob digests
            var (modelfile, modelfileErr) = await GetRemoteModelfileStringWithClient(sourceClient, sourceModel);

            if (modelfile == null)
            {
                Console.WriteLine($"Error: Could not retrieve model '{sourceModel}' from source server");
                if (modelfileErr != null)
                {
                    Console.WriteLine($"Details: {modelfileErr}");
                }
                System.Environment.Exit(1);
            }

            // Extract blob digests from modelfile
            var blobDigests = ExtractBlobDigestsFromModelfile(modelfile);

            if (blobDigests.Count == 0)
            {
                Console.WriteLine($"Error: No blob references found in modelfile");
                System.Environment.Exit(1);
            }

            Console.WriteLine($"Found {blobDigests.Count} blob(s) to download");

            // Get modelfile information for creating model locally
            var (template, system, _) = await GetRemoteModelfileWithClient(sourceClient, sourceModel);
            // Get parameters as raw JSON to preserve types (arrays, numbers, etc.)
            var parametersRaw = await GetRemoteModelParametersRaw(sourceClient, sourceModel);

            // Download each blob from source to local blobs directory
            string blobDir = Path.Combine(ollama_models, "blobs");
            Directory.CreateDirectory(blobDir);

            var files = new Dictionary<string, string>();
            int modelIndex = 0;

            foreach (var digest in blobDigests)
            {
                Console.WriteLine($"\nDownloading blob {digest.Substring(0, 19)}...");

                // Download blob from source server
                await DownloadBlobToLocal(sourceClient, sourceServer, sourceModel, digest, blobDir);

                // Track files for model creation
                string filename = modelIndex == 0 ? "model.gguf" : $"model_{modelIndex}.gguf";
                files[filename] = digest;
                modelIndex++;
            }

            // Create model locally using ollama create
            Console.WriteLine($"\nCreating local model '{destModel}'...");
            await CreateLocalModelFromBlobs(destModel, files, template, system, parametersRaw);

            Console.WriteLine($"\nSuccessfully copied '{sourceModel}' from {sourceServer} to local '{destModel}'");

            sourceClient.Dispose();
        }

        /// <summary>
        /// Downloads a blob from the Ollama registry to the local blobs directory.
        /// Uses the same approach as remote-to-remote copy.
        /// </summary>
        private async Task DownloadBlobToLocal(HttpClient sourceClient, string sourceServer, string sourceModel, string digest, string blobDir)
        {
            // Convert digest to local blob filename (sha256:xxx -> sha256-xxx)
            string blobFilename = digest.Replace(":", "-");
            string blobPath = Path.Combine(blobDir, blobFilename);

            // Check if blob already exists locally
            if (System.IO.File.Exists(blobPath))
            {
                var existingInfo = new FileInfo(blobPath);
                Console.WriteLine($"  Blob already exists locally ({ByteSize.FromBytes(existingInfo.Length)})");
                return;
            }

            // Build registry path from model name
            string modelNameWithoutTag = sourceModel.Contains(":") ? sourceModel.Split(':')[0] : sourceModel;
            string registryPath = modelNameWithoutTag.Contains("/")
                ? modelNameWithoutTag  // Already has namespace like "mannix/model"
                : $"library/{modelNameWithoutTag}";  // Standard library model

            // Create a client for registry access
            var registryClient = new HttpClient() { Timeout = TimeSpan.FromHours(2) };
            registryClient.DefaultRequestHeaders.UserAgent.ParseAdd("ollama/0.5.0");

            try
            {
                // Construct registry URLs to try (same as remote-to-remote copy)
                var registryUrls = new[]
                {
                    $"https://registry.ollama.ai/v2/{registryPath}/blobs/{digest}",
                    $"https://registry.ollama.com/v2/{registryPath}/blobs/{digest}",
                    // Try without library prefix
                    $"https://registry.ollama.ai/v2/{modelNameWithoutTag}/blobs/{digest}",
                    $"https://registry.ollama.com/v2/{modelNameWithoutTag}/blobs/{digest}",
                    // Fallback to direct blob access
                    $"https://registry.ollama.ai/v2/blobs/{digest}",
                    $"https://registry.ollama.com/v2/blobs/{digest}"
                };

                HttpResponseMessage? downloadResponse = null;

                foreach (var url in registryUrls)
                {
                    try
                    {
                        downloadResponse = await registryClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                        if (downloadResponse.IsSuccessStatusCode)
                        {
                            break; // Successfully found the blob
                        }
                    }
                    catch
                    {
                        continue;
                    }
                }

                if (downloadResponse == null || !downloadResponse.IsSuccessStatusCode)
                {
                    Console.WriteLine($"  Error: Blob not found in Ollama registry");
                    Console.WriteLine($"  Note: This model was likely created locally on the remote server");
                    Console.WriteLine($"        and is not available in the public registry.");
                    Console.WriteLine($"  To copy custom models, use rsync or scp to copy the files directly.");
                    throw new Exception($"Blob {digest} not available in registry");
                }

                var totalBytes = downloadResponse.Content.Headers.ContentLength ?? 0;
                var totalSize = ByteSize.FromBytes(totalBytes);

                Console.WriteLine($"  Downloading {totalSize} from registry...");

                using var contentStream = await downloadResponse.Content.ReadAsStreamAsync();
                using var fileStream = new FileStream(blobPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

                var buffer = new byte[81920];
                long totalRead = 0;
                int bytesRead;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                long lastReport = 0;

                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead);
                    totalRead += bytesRead;

                    // Report progress every 50MB or so
                    if (totalRead - lastReport > 50 * 1024 * 1024)
                    {
                        var percent = totalBytes > 0 ? (totalRead * 100.0 / totalBytes) : 0;
                        var speed = totalRead / sw.Elapsed.TotalSeconds;
                        Console.Write($"\r  Downloaded {ByteSize.FromBytes(totalRead)} / {totalSize} ({percent:F1}%) - {ByteSize.FromBytes(speed).ToString("#.#")}/s    ");
                        lastReport = totalRead;
                    }
                }

                var finalSpeed = totalRead / sw.Elapsed.TotalSeconds;
                Console.WriteLine($"\r  Downloaded {ByteSize.FromBytes(totalRead)} in {sw.Elapsed.TotalSeconds:F1}s ({ByteSize.FromBytes(finalSpeed).ToString("#.#")}/s)    ");
            }
            catch (Exception ex) when (!(ex.Message.Contains("not available in registry")))
            {
                // Clean up partial file
                if (System.IO.File.Exists(blobPath))
                {
                    System.IO.File.Delete(blobPath);
                }
                throw new Exception($"Failed to download blob {digest}: {ex.Message}", ex);
            }
            finally
            {
                registryClient.Dispose();
            }
        }

        /// <summary>
        /// Creates a local model from downloaded blobs using ollama create.
        /// </summary>
        private async Task CreateLocalModelFromBlobs(string modelName, Dictionary<string, string> files, string? template, string? system, Dictionary<string, object>? parameters)
        {
            // Create model using ollama create API - use local Ollama URL
            string localOllamaUrl = System.Environment.GetEnvironmentVariable("OLLAMA_HOST")
                ?? "http://localhost:11434";

            // Handle bind-all addresses (0.0.0.0 or ::0) - convert to localhost for client connections
            if (localOllamaUrl.Contains("0.0.0.0") || localOllamaUrl.Contains("::0") || localOllamaUrl.Contains("[::]"))
            {
                localOllamaUrl = localOllamaUrl.Replace("0.0.0.0", "localhost").Replace("::0", "localhost").Replace("[::]", "localhost");
            }

            if (!localOllamaUrl.StartsWith("http"))
            {
                localOllamaUrl = "http://" + localOllamaUrl;
            }

            // Ensure port is present (default Ollama port is 11434)
            if (!localOllamaUrl.Contains(":11434") && !localOllamaUrl.Contains(":80") && !System.Text.RegularExpressions.Regex.IsMatch(localOllamaUrl, @":\d{2,5}$"))
            {
                localOllamaUrl = localOllamaUrl.TrimEnd('/') + ":11434";
            }

            // Build request using same format as RunCreateModel (model + files dictionary)
            var modelCreate = new Dictionary<string, object>
            {
                { "model", modelName },
                { "files", files }
            };

            if (!string.IsNullOrEmpty(template))
                modelCreate["template"] = template;
            if (!string.IsNullOrEmpty(system))
                modelCreate["system"] = system;
            if (parameters != null && parameters.Count > 0)
            {
                // Parameters are already properly typed (arrays, numbers, strings)
                modelCreate["parameters"] = parameters;
            }

            var json = JsonSerializer.Serialize(modelCreate);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            try
            {
                using var localClient = new HttpClient() { BaseAddress = new Uri(localOllamaUrl), Timeout = TimeSpan.FromMinutes(30) };
                var postMessage = new HttpRequestMessage(HttpMethod.Post, "api/create");
                postMessage.Content = content;
                var response = await localClient.SendAsync(postMessage, HttpCompletionOption.ResponseHeadersRead);
                var stream = await response.Content.ReadAsStreamAsync();

                string lastStatus = "";
                using (var streamReader = new StreamReader(stream))
                {
                    while (!streamReader.EndOfStream)
                    {
                        string? statusLine = await streamReader.ReadLineAsync();
                        if (statusLine != null)
                        {
                            RootStatus? status = StatusReader.Read<RootStatus>(statusLine);
                            if (status?.status != null)
                            {
                                lastStatus = status.status;
                                Console.WriteLine($"  {status.status}");
                            }
                        }
                    }
                }

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Error creating model: {response.StatusCode}");
                    System.Environment.Exit(1);
                }
                else if (lastStatus != "success")
                {
                    Console.WriteLine($"Error: model creation failed with status: {lastStatus}");
                    System.Environment.Exit(1);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating model: {ex.Message}");
                System.Environment.Exit(1);
            }
        }

        private async Task<RootManifest?> GetRemoteModelManifest(string modelName)
        {
            try
            {
                var showRequest = new { name = modelName, verbose = true };
                string showJson = JsonSerializer.Serialize(showRequest);
                var content = new StringContent(showJson, Encoding.UTF8, "application/json");

                var response = await client.PostAsync("api/show", content);

                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                string json = await response.Content.ReadAsStringAsync();

                // Try to parse the response to see what we get
                try
                {
                    var showResponse = JsonSerializer.Deserialize<JsonDocument>(json);
                    if (showResponse == null) return null;

                    // Check if there's a model_info section with layers
                    if (showResponse.RootElement.TryGetProperty("model_info", out var modelInfo))
                    {
                        // Try to construct a manifest from model_info
                        var manifest = new RootManifest
                        {
                            schemaVersion = 2,
                            mediaType = "application/vnd.docker.distribution.manifest.v2+json",
                            layers = new List<Layer>()
                        };

                        // Extract layers if available
                        if (modelInfo.TryGetProperty("layers", out var layersElement))
                        {
                            foreach (var layer in layersElement.EnumerateArray())
                            {
                                var layerObj = new Layer
                                {
                                    mediaType = layer.TryGetProperty("media_type", out var mt) ? mt.GetString() ?? "application/vnd.ollama.image.model" : "application/vnd.ollama.image.model",
                                    digest = layer.TryGetProperty("digest", out var d) ? d.GetString() ?? "" : "",
                                    size = layer.TryGetProperty("size", out var s) ? s.GetInt64() : 0
                                };
                                manifest.layers.Add(layerObj);
                            }
                        }

                        if (manifest.layers.Count > 0)
                        {
                            return manifest;
                        }
                    }
                }
                catch (Exception parseEx)
                {
                    Console.WriteLine($"Error parsing show response: {parseEx.Message}");
                    Console.WriteLine($"Response: {json.Substring(0, Math.Min(500, json.Length))}...");
                }

                return null;
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error getting model manifest: {e.Message}");
                return null;
            }
        }

        private async Task<(string? template, string? system, List<string>? parameters)> GetRemoteModelfile(string modelName)
        {
            try
            {
                var showRequest = new { name = modelName };
                string showJson = JsonSerializer.Serialize(showRequest);
                var content = new StringContent(showJson, Encoding.UTF8, "application/json");

                var response = await client.PostAsync("api/show", content);

                if (!response.IsSuccessStatusCode)
                {
                    return (null, null, null);
                }

                string json = await response.Content.ReadAsStringAsync();
                var showResponse = JsonSerializer.Deserialize<JsonDocument>(json);
                if (showResponse == null) return (null, null, null);

                string? template = null;
                string? system = null;
                List<string> parameters = new List<string>();

                if (showResponse.RootElement.TryGetProperty("template", out var templateElement))
                {
                    template = templateElement.GetString();
                }

                if (showResponse.RootElement.TryGetProperty("system", out var systemElement))
                {
                    system = systemElement.GetString();
                }

                if (showResponse.RootElement.TryGetProperty("parameters", out var parametersElement))
                {
                    // Parameters can be either a string or an object
                    if (parametersElement.ValueKind == JsonValueKind.String)
                    {
                        // It's a string, parse it line by line
                        var paramStr = parametersElement.GetString();
                        if (!string.IsNullOrEmpty(paramStr))
                        {
                            var lines = paramStr.Split('\n');
                            foreach (var line in lines)
                            {
                                if (!string.IsNullOrWhiteSpace(line))
                                {
                                    parameters.Add(line.Trim());
                                }
                            }
                        }
                    }
                    else if (parametersElement.ValueKind == JsonValueKind.Object)
                    {
                        // It's an object, enumerate properties
                        foreach (var param in parametersElement.EnumerateObject())
                        {
                            parameters.Add($"{param.Name} {param.Value.GetRawText()}");
                        }
                    }
                }

                return (template, system, parameters);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error getting modelfile: {e.Message}");
                return (null, null, null);
            }
        }

        private async Task<(string? modelfile, string? error)> GetRemoteModelfileString(string modelName)
        {
            return await GetRemoteModelfileStringWithClient(client, modelName);
        }

        private async Task<(string? modelfile, string? error)> GetRemoteModelfileStringWithClient(HttpClient httpClient, string modelName)
        {
            try
            {
                var showRequest = new { name = modelName };
                string showJson = JsonSerializer.Serialize(showRequest);
                var content = new StringContent(showJson, Encoding.UTF8, "application/json");

                var response = await httpClient.PostAsync("api/show", content);

                if (!response.IsSuccessStatusCode)
                {
                    string error = await response.Content.ReadAsStringAsync();
                    return (null, error);
                }

                string json = await response.Content.ReadAsStringAsync();
                var showResponse = JsonSerializer.Deserialize<JsonDocument>(json);
                if (showResponse == null) return (null, "Failed to parse response");

                // Get the modelfile string
                if (showResponse.RootElement.TryGetProperty("modelfile", out var modelfileElement))
                {
                    return (modelfileElement.GetString(), null);
                }

                return (null, "No modelfile found in response");
            }
            catch (Exception e)
            {
                return (null, e.Message);
            }
        }

        private async Task<(string? template, string? system, List<string>? parameters)> GetRemoteModelfileWithClient(HttpClient httpClient, string modelName)
        {
            try
            {
                var showRequest = new { name = modelName };
                string showJson = JsonSerializer.Serialize(showRequest);
                var content = new StringContent(showJson, Encoding.UTF8, "application/json");

                var response = await httpClient.PostAsync("api/show", content);

                if (!response.IsSuccessStatusCode)
                {
                    return (null, null, null);
                }

                string json = await response.Content.ReadAsStringAsync();
                var showResponse = JsonSerializer.Deserialize<JsonDocument>(json);
                if (showResponse == null)
                {
                    return (null, null, null);
                }

                string? template = null;
                string? system = null;
                List<string> parameters = new List<string>();

                if (showResponse.RootElement.TryGetProperty("template", out var templateElement))
                {
                    template = templateElement.GetString();
                }

                if (showResponse.RootElement.TryGetProperty("system", out var systemElement))
                {
                    system = systemElement.GetString();
                }

                if (showResponse.RootElement.TryGetProperty("parameters", out var parametersElement))
                {
                    // Parameters can be either a string or an object
                    if (parametersElement.ValueKind == JsonValueKind.String)
                    {
                        // It's a string, parse it line by line
                        var paramStr = parametersElement.GetString();
                        if (!string.IsNullOrEmpty(paramStr))
                        {
                            var lines = paramStr.Split('\n');
                            foreach (var line in lines)
                            {
                                if (!string.IsNullOrWhiteSpace(line))
                                {
                                    parameters.Add(line.Trim());
                                }
                            }
                        }
                    }
                    else if (parametersElement.ValueKind == JsonValueKind.Object)
                    {
                        // It's an object, enumerate properties
                        foreach (var param in parametersElement.EnumerateObject())
                        {
                            parameters.Add($"{param.Name} {param.Value.GetRawText()}");
                        }
                    }
                }

                return (template, system, parameters);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error getting modelfile: {e.Message}");
                return (null, null, null);
            }
        }

        /// <summary>
        /// Gets remote model parameters as a raw dictionary preserving JSON types.
        /// </summary>
        private async Task<Dictionary<string, object>?> GetRemoteModelParametersRaw(HttpClient httpClient, string modelName)
        {
            try
            {
                var showRequest = new { name = modelName };
                string showJson = JsonSerializer.Serialize(showRequest);
                var content = new StringContent(showJson, Encoding.UTF8, "application/json");

                var response = await httpClient.PostAsync("api/show", content);

                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                string json = await response.Content.ReadAsStringAsync();
                var showResponse = JsonSerializer.Deserialize<JsonDocument>(json);
                if (showResponse == null)
                {
                    return null;
                }

                if (showResponse.RootElement.TryGetProperty("parameters", out var parametersElement))
                {
                    if (parametersElement.ValueKind == JsonValueKind.Object)
                    {
                        var result = new Dictionary<string, object>();
                        foreach (var param in parametersElement.EnumerateObject())
                        {
                            // Deserialize each value to its proper type
                            result[param.Name] = JsonSerializer.Deserialize<object>(param.Value.GetRawText())!;
                        }
                        return result;
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private async Task RunCreateModelWithClient(HttpClient httpClient, string Modelname, Dictionary<string, string> files, string? template = null, string? system = null, List<string>? parameters = null)
        {
            try
            {
                var modelCreate = new Dictionary<string, object>
                {
                    { "model", Modelname },
                    { "files", files }
                };

                if (!string.IsNullOrEmpty(template))
                    modelCreate["template"] = template;
                if (!string.IsNullOrEmpty(system))
                    modelCreate["system"] = system;
                if (parameters != null && parameters.Count > 0)
                {
                    var paramDict = new Dictionary<string, object>();
                    foreach (var param in parameters)
                    {
                        var parts = param.Split(new[] { ' ' }, 2);
                        if (parts.Length == 2)
                        {
                            var key = parts[0];
                            var value = parts[1];

                            // "stop" parameter must be an array
                            if (key == "stop")
                            {
                                // Check if there's an existing stop array to add to
                                if (paramDict.ContainsKey("stop") && paramDict["stop"] is List<string> existingStops)
                                {
                                    existingStops.Add(value);
                                }
                                else
                                {
                                    paramDict["stop"] = new List<string> { value };
                                }
                            }
                            else
                            {
                                // Convert to proper type based on parameter name
                                paramDict[key] = ConvertParameterValue(key, value);
                            }
                        }
                    }
                    if (paramDict.Count > 0)
                        modelCreate["parameters"] = paramDict;
                }

                string data = System.Text.Json.JsonSerializer.Serialize(modelCreate);

                var body = new StringContent(data, Encoding.UTF8, "application/json");
                var postmessage = new HttpRequestMessage(HttpMethod.Post, $"api/create");
                postmessage.Content = body;
                var response = await httpClient.SendAsync(postmessage, HttpCompletionOption.ResponseHeadersRead);
                var stream = await response.Content.ReadAsStreamAsync();
                string laststatus = "";
                using (var streamReader = new StreamReader(stream))
                {
                    while (!streamReader.EndOfStream)
                    {
                        string? statusline = await streamReader.ReadLineAsync();
                        if (statusline != null)
                        {
                            RootStatus? status = StatusReader.Read<RootStatus>(statusline);
                            if (status?.status != null)
                            {
                                laststatus = status.status;
                                Console.WriteLine(status.status);
                            }
                        }
                    }
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"Could not create '{Modelname}' on the remote server (HTTP status {(int)response.StatusCode}): {response.ReasonPhrase}");
                }
                else if (laststatus != "success")
                {
                    throw new Exception($"Model creation did not complete successfully (status: {laststatus})");
                }
            }
            catch (HttpRequestException e)
            {
                throw new Exception($"Could not create '{Modelname}' on the remote server: {e.Message}", e);
            }
        }

        private List<string> ExtractBlobDigestsFromModelfile(string modelfile)
        {
            var digests = new List<string>();

            if (string.IsNullOrEmpty(modelfile))
            {
                return digests;
            }

            // Parse the modelfile line by line
            var lines = modelfile.Split('\n');

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();

                // Look for FROM statements with blob paths
                // Format: FROM /path/to/blobs/sha256-<hash>
                if (trimmedLine.StartsWith("FROM "))
                {
                    var path = trimmedLine.Substring(5).Trim();

                    // Extract the sha256 digest from the path
                    // Path format: /usr/share/ollama/.ollama/models/blobs/sha256-<hash>
                    var blobMatch = Regex.Match(path, @"sha256-([a-f0-9]{64})");
                    if (blobMatch.Success)
                    {
                        string digest = $"sha256:{blobMatch.Groups[1].Value}";
                        if (!digests.Contains(digest))
                        {
                            digests.Add(digest);
                        }
                    }
                }
                // Also check for ADAPTER statements which may reference blobs
                else if (trimmedLine.StartsWith("ADAPTER "))
                {
                    var path = trimmedLine.Substring(8).Trim();

                    var blobMatch = Regex.Match(path, @"sha256-([a-f0-9]{64})");
                    if (blobMatch.Success)
                    {
                        string digest = $"sha256:{blobMatch.Groups[1].Value}";
                        if (!digests.Contains(digest))
                        {
                            digests.Add(digest);
                        }
                    }
                }
            }

            return digests;
        }

        private string TransformModelfileForLocalCreate(string modelfile)
        {
            if (string.IsNullOrEmpty(modelfile))
            {
                return modelfile;
            }

            // Replace file paths with blob digest references
            // Transform: FROM /path/to/blobs/sha256-<hash>
            // To:        FROM @sha256:<hash>

            var result = Regex.Replace(
                modelfile,
                @"(FROM|ADAPTER)\s+[^\s]*sha256-([a-f0-9]{64})",
                "$1 @sha256:$2"
            );

            return result;
        }

        private long ParseSize(string sizeStr)
        {
            if (string.IsNullOrEmpty(sizeStr))
            {
                return 512 * 1024 * 1024; // Default 512MB
            }

            sizeStr = sizeStr.Trim().ToUpper();

            // Extract number and unit
            long multiplier = 1;
            string numericPart = sizeStr;

            if (sizeStr.EndsWith("GB"))
            {
                multiplier = 1024 * 1024 * 1024;
                numericPart = sizeStr.Substring(0, sizeStr.Length - 2);
            }
            else if (sizeStr.EndsWith("MB"))
            {
                multiplier = 1024 * 1024;
                numericPart = sizeStr.Substring(0, sizeStr.Length - 2);
            }
            else if (sizeStr.EndsWith("KB"))
            {
                multiplier = 1024;
                numericPart = sizeStr.Substring(0, sizeStr.Length - 2);
            }

            if (long.TryParse(numericPart.Trim(), out long value))
            {
                return value * multiplier;
            }

            return 512 * 1024 * 1024; // Default if parsing fails
        }

        private async Task StreamBlobSourceToDestination(string sourceServer, string destServer, string sourceModel, string digest, long bufferSize)
        {
            var destClient = new HttpClient() { Timeout = TimeSpan.FromHours(1) };
            destClient.BaseAddress = new Uri(destServer);

            try
            {
                // Check if blob exists on destination
                var headResponse = await destClient.SendAsync(new HttpRequestMessage(HttpMethod.Head, $"api/blobs/{digest}"));
                if (headResponse.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    Console.WriteLine($"  Blob already exists on destination - skipping");
                    return;
                }

                Console.WriteLine($"  Blob {digest.Substring(0, Math.Min(12, digest.Length))}... not on destination, fetching from registry");

                var registryClient = new HttpClient() { Timeout = TimeSpan.FromHours(1) };
                registryClient.DefaultRequestHeaders.Add("Accept", "application/vnd.docker.distribution.manifest.v2+json, application/vnd.oci.image.manifest.v1+json");

                // Parse model name to extract namespace and model
                // Format: [namespace/]model:tag or just model:tag
                string modelNameWithoutTag = sourceModel.Split(':')[0]; // Remove tag
                string tag = sourceModel.Contains(':') ? sourceModel.Split(':')[1] : "latest";
                string registryPath;

                if (modelNameWithoutTag.Contains("/"))
                {
                    // Namespaced model (e.g., "myuser/mymodel")
                    registryPath = modelNameWithoutTag;
                }
                else
                {
                    // Library model (e.g., "llama3") - uses "library" namespace
                    registryPath = $"library/{modelNameWithoutTag}";
                }

                Console.WriteLine($"  Registry path: {registryPath}:{tag}");

                // First, try to get the manifest to understand the blob structure
                var manifestUrls = new[]
                {
                    $"https://registry.ollama.ai/v2/{registryPath}/manifests/{tag}",
                    $"https://registry.ollama.com/v2/{registryPath}/manifests/{tag}"
                };

                Console.WriteLine($"  Attempting to fetch manifest...");
                foreach (var manifestUrl in manifestUrls)
                {
                    try
                    {
                        var manifestResponse = await registryClient.GetAsync(manifestUrl);
                        if (manifestResponse.IsSuccessStatusCode)
                        {
                            Console.WriteLine($"  Manifest found at: {manifestUrl}");
                            var manifestContent = await manifestResponse.Content.ReadAsStringAsync();
                            Console.WriteLine($"  Manifest preview: {manifestContent.Substring(0, Math.Min(200, manifestContent.Length))}...");
                            break;
                        }
                        else
                        {
                            Console.WriteLine($"  Manifest not found at: {manifestUrl} (HTTP {manifestResponse.StatusCode})");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"  Manifest fetch failed: {ex.Message}");
                    }
                }

                // Now try to download the blob
                Console.WriteLine($"  Attempting to download blob...");

                // Construct registry URLs to try
                var registryUrls = new[]
                {
                    $"https://registry.ollama.ai/v2/{registryPath}/blobs/{digest}",
                    $"https://registry.ollama.com/v2/{registryPath}/blobs/{digest}",
                    // Try without library prefix
                    $"https://registry.ollama.ai/v2/{modelNameWithoutTag}/blobs/{digest}",
                    $"https://registry.ollama.com/v2/{modelNameWithoutTag}/blobs/{digest}",
                    // Fallback to direct blob access
                    $"https://registry.ollama.ai/v2/blobs/{digest}",
                    $"https://registry.ollama.com/v2/blobs/{digest}"
                };

                HttpResponseMessage? downloadResponse = null;
                string? successUrl = null;

                foreach (var url in registryUrls)
                {
                    try
                    {
                        Console.WriteLine($"  Trying: {url}");
                        downloadResponse = await registryClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                        Console.WriteLine($"    Response: HTTP {(int)downloadResponse.StatusCode} {downloadResponse.StatusCode}");

                        if (downloadResponse.IsSuccessStatusCode)
                        {
                            successUrl = url;
                            Console.WriteLine($"  Success! Blob found at: {url}");
                            break; // Successfully found the blob
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"    Exception: {ex.Message}");
                        continue;
                    }
                }

                if (downloadResponse == null || !downloadResponse.IsSuccessStatusCode)
                {
                    Console.WriteLine("");
                    Console.WriteLine($"  Error: Blob not found in Ollama registry after trying all URLs");
                    Console.WriteLine($"  Last HTTP status: {downloadResponse?.StatusCode}");
                    Console.WriteLine($"  Digest: {digest}");
                    Console.WriteLine($"  Source model: {sourceModel}");
                    Console.WriteLine("");
                    Console.WriteLine($"  Note: This model was likely created locally and is not available in the registry.");
                    Console.WriteLine($"  Remote-to-remote copy only works for models originally from the Ollama registry.");
                    throw new Exception("Blob not available in registry. Cannot complete remote-to-remote copy.");
                }

                long totalSize = downloadResponse.Content.Headers.ContentLength ?? 0;
                var blobSizeBytes = ByteSize.FromBytes(totalSize);

                Console.WriteLine($"transferring layer {digest}");
                Console.WriteLine($"using {ByteSize.FromBytes(bufferSize).ToString("#.##")} memory buffer");

                // Create a memory buffer stream that limits buffering and enables simultaneous download/upload
                var pipeStream = new BufferedPipeStream(bufferSize);
                var downloadStream = await downloadResponse.Content.ReadAsStreamAsync();

                // Setup progress tracking
                int block_size = 1024;
                int total_size_blocks = (int)(totalSize / block_size);
                var blob_size = ByteSize.FromKibiBytes(total_size_blocks);
                var bar = new Tqdm.ProgressBar(total: (int)blob_size.MebiBytes, useColor: GetPlatformColor(), useExpMovingAvg: false, printsPerSecond: 10);
                var stopwatch = new Stopwatch();
                stopwatch.Start();

                long bytesTransferred = 0;
                var progressLock = new object();
                int tick = 0;
                bool finished = false;

                // Start download task - reads from registry and writes to pipe
                var downloadTask = Task.Run(async () =>
                {
                    try
                    {
                        byte[] buffer = new byte[Math.Min(81920, bufferSize)]; // 80KB chunks
                        int bytesRead;
                        while ((bytesRead = await downloadStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await pipeStream.WriteAsync(buffer, 0, bytesRead);
                        }
                        pipeStream.CompleteWriting();
                    }
                    catch (Exception ex)
                    {
                        pipeStream.SetException(ex);
                        throw;
                    }
                });

                // Start upload task - reads from pipe and uploads to destination
                var uploadTask = Task.Run(async () =>
                {
                    try
                    {
                        SetCursorVisible(false);

                        var uploadStream = new ProgressTrackingStream(pipeStream, totalSize, (transferred) =>
                        {
                            lock (progressLock)
                            {
                                bytesTransferred = transferred;
                                tick++;
                                if (tick < 40) { return; }
                                tick = 0;

                                double elapsedTimeInSeconds = stopwatch.Elapsed.TotalSeconds;
                                double speedInBytesPerSecond = transferred / elapsedTimeInSeconds;

                                try
                                {
                                    var sent_size = ByteSize.FromKibiBytes(transferred / block_size);
                                    int percentage = (int)sent_size.MebiBytes;
                                    bar.SetLabel($"({ByteSize.FromKibiBytes(transferred / block_size).ToString("#")} / {ByteSize.FromKibiBytes(totalSize / block_size).ToString("#")}) transferring at {ByteSize.FromKibiBytes(speedInBytesPerSecond / block_size).ToString("#")}/s");
                                    if (!finished)
                                    {
                                        bar.Progress(percentage);
                                    }
                                    else
                                    {
                                        bar.Progress((int)blob_size.MebiBytes);
                                    }
                                }
                                catch
                                {
                                    // Silently ignore progress bar errors
                                }
                            }
                        });

                        var uploadContent = new StreamContent(uploadStream);
                        uploadContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                        uploadContent.Headers.ContentLength = totalSize;

                        var uploadResponse = await destClient.PostAsync($"api/blobs/{digest}", uploadContent);

                        finished = true;
                        SetCursorVisible(true);

                        if (!uploadResponse.IsSuccessStatusCode)
                        {
                            throw new Exception($"Upload failed with HTTP {uploadResponse.StatusCode}");
                        }

                        return uploadResponse;
                    }
                    catch (Exception ex)
                    {
                        SetCursorVisible(true);
                        throw new Exception($"Upload error: {ex.Message}", ex);
                    }
                });

                // Wait for both download and upload to complete
                await Task.WhenAll(downloadTask, uploadTask);

                var finalUploadResponse = await uploadTask;

                bar.Finish();

                if (!finalUploadResponse.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Error: Failed to upload to destination (HTTP {finalUploadResponse.StatusCode})");
                    System.Environment.Exit(1);
                }

                Console.WriteLine("success transferring layer");

                downloadStream.Dispose();

                registryClient.Dispose();
            }
            finally
            {
                destClient.Dispose();
            }
        }

        private async Task TransferBlobRemoteToRemote(string sourceServer, string destServer, string digest)
        {
            var originalBaseAddress = client.BaseAddress;

            try
            {
                // Check if blob already exists on destination
                client.BaseAddress = new Uri(destServer);
                var statusCode = (int)await BlobHead(digest);

                if (statusCode == 200)
                {
                    Console.WriteLine($"skipping upload for already created layer {digest}");
                    return;
                }
                else if (statusCode != 404)
                {
                    Console.WriteLine($"Error: unexpected status code {statusCode} when checking blob on destination");
                    System.Environment.Exit(1);
                }

                // Download blob from source
                Console.WriteLine($"downloading layer {digest} from source");
                client.BaseAddress = new Uri(sourceServer);

                var downloadResponse = await client.GetAsync($"api/blobs/{digest}", HttpCompletionOption.ResponseHeadersRead);

                if (!downloadResponse.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Error: failed to download blob from source (HTTP {(int)downloadResponse.StatusCode})");
                    System.Environment.Exit(1);
                }

                // Upload blob to destination
                Console.WriteLine($"uploading layer {digest} to destination");
                client.BaseAddress = new Uri(destServer);

                using (var sourceStream = await downloadResponse.Content.ReadAsStreamAsync())
                {
                    Stopwatch stopwatch = new Stopwatch();

                    try
                    {
                        SetCursorVisible(false);

                        Stream destFileStream = new ThrottledStream(sourceStream, btvalue);

                        long totalSize = downloadResponse.Content.Headers.ContentLength ?? 0;
                        var blob_size = ByteSize.FromBytes(totalSize);
                        var bar = new Tqdm.ProgressBar(total: (int)blob_size.MebiBytes, useColor: GetPlatformColor(), useExpMovingAvg: false, printsPerSecond: 10);
                        bool finished = false;
                        int tick = 0;

                        var streamcontent = new ProgressableStreamContent(new StreamContent(destFileStream), (sent, total) => {
                            tick++;
                            if (tick < 40) { return; }
                            tick = 0;
                            double elapsedTimeInSeconds = stopwatch.Elapsed.TotalSeconds;
                            double speedInBytesPerSecond = sent / elapsedTimeInSeconds;
                            try
                            {
                                int percentage = (int)((sent * 100.0) / total);
                                var sent_size = ByteSize.FromBytes(sent);
                                percentage = (int)sent_size.MebiBytes;
                                bar.SetLabel($"({ByteSize.FromBytes(sent).ToString("#")} / {ByteSize.FromBytes(total).ToString("#")}) uploading at {ByteSize.FromBytes(speedInBytesPerSecond).ToString("#")}/s");
                                if (!finished)
                                {
                                    bar.Progress(percentage);
                                }
                                else
                                {
                                    bar.Progress((int)blob_size.MebiBytes);
                                }
                            }
                            catch
                            {
                                // Silently ignore progress bar errors
                            }
                        });

                        stopwatch.Start();
                        var uploadResponse = await client.PostAsync($"api/blobs/{digest}", streamcontent);
                        finished = true;

                        SetCursorVisible(true);

                        if (uploadResponse.IsSuccessStatusCode)
                        {
                            bar.Finish();
                            Console.WriteLine("success uploading layer");
                        }
                        else if (uploadResponse.StatusCode == System.Net.HttpStatusCode.BadRequest)
                        {
                            Console.WriteLine("Error: upload failed invalid digest, check both ollama are running the same version.");
                            System.Environment.Exit(1);
                        }
                        else
                        {
                            Console.WriteLine($"Error: upload failed: {uploadResponse.ReasonPhrase}");
                            System.Environment.Exit(1);
                        }

                        streamcontent.Dispose();
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"Error: {e.Message}");
                        SetCursorVisible(true);
                        System.Environment.Exit(1);
                    }
                    finally
                    {
                        stopwatch.Stop();
                    }
                }
            }
            finally
            {
                client.BaseAddress = originalBaseAddress;
            }
        }

        public void ActionList(string? Pattern, string Destination, string sortMode = "name")
        {
            Init();

            // If pattern looks like a URL or IP address, treat it as destination
            if (!string.IsNullOrEmpty(Pattern) && LooksLikeRemoteServer(Pattern))
            {
                Destination = Pattern;
                Pattern = null;
            }

            // If pattern is provided but doesn't contain a wildcard, append * for prefix match
            // This makes "osync ls code" work the same as "osync ls code*" (models starting with "code")
            // To search for "contains", use leading wildcard: "osync ls *code" or "osync ls *code*"
            if (!string.IsNullOrEmpty(Pattern) && !Pattern.Contains("*"))
            {
                Pattern = Pattern + "*";
            }

            Debug.WriteLine("List: you entered pattern '{0}' and destination '{1}'. OLLAMA_MODELS={2}", Pattern ?? "*", Destination, ollama_models);

            bool localList = string.IsNullOrEmpty(Destination);

            if (localList)
            {
                if (!Directory.Exists(ollama_models))
                {
                    Console.WriteLine($"Error: ollama models directory not found at: {ollama_models}");
                    System.Environment.Exit(1);
                }
                ListLocalModels(Pattern ?? "*", sortMode);
            }
            else
            {
                // Normalize URL with default protocol and port
                Destination = NormalizeServerUrl(Destination);
                if (!ValidateServerUrl(Destination, silent: true))
                {
                    System.Environment.Exit(1);
                }
                ListRemoteModels(Destination, Pattern ?? "*", sortMode).GetAwaiter().GetResult();
            }
        }

        private string WildcardToRegex(string pattern)
        {
            if (string.IsNullOrEmpty(pattern))
            {
                return ".*";
            }
            string escaped = Regex.Escape(pattern);
            string regexPattern = "^" + escaped.Replace("\\*", ".*") + "$";
            return regexPattern;
        }

        private bool MatchesPattern(string modelName, string pattern)
        {
            if (string.IsNullOrEmpty(pattern))
            {
                return true;
            }

            // First try exact match with the pattern
            string regexPattern = WildcardToRegex(pattern);
            if (Regex.IsMatch(modelName, regexPattern, RegexOptions.IgnoreCase))
            {
                return true;
            }

            // If pattern doesn't contain a tag (no ':'), also try matching with :latest
            if (!pattern.Contains(":"))
            {
                string patternWithLatest = pattern + ":latest";
                string regexPatternWithLatest = WildcardToRegex(patternWithLatest);
                if (Regex.IsMatch(modelName, regexPatternWithLatest, RegexOptions.IgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private void ListLocalModels(string pattern, string sortMode = "name")
        {
            var models = new List<LocalModelInfo>();
            string manifestsDir = Path.Combine(ollama_models, "manifests");

            if (!Directory.Exists(manifestsDir))
            {
                Console.WriteLine($"No local models found.");
                return;
            }

            // Scan all hosts (registry.ollama.ai, hf.co, hub, etc.)
            foreach (string hostDir in Directory.GetDirectories(manifestsDir))
            {
                string host = Path.GetFileName(hostDir);

                // Scan all namespaces within each host
                foreach (string namespaceDir in Directory.GetDirectories(hostDir))
                {
                    string ns = Path.GetFileName(namespaceDir);

                    // Scan all models within each namespace
                    foreach (string modelDir in Directory.GetDirectories(namespaceDir))
                    {
                        string model = Path.GetFileName(modelDir);

                        // Tags are files directly in the model directory
                        foreach (string tagFile in Directory.GetFiles(modelDir))
                        {
                            string tag = Path.GetFileName(tagFile);

                            // Build display name based on host/namespace
                            string fullModelName;
                            if (host == "registry.ollama.ai" && ns == "library")
                            {
                                // Official library models: just "model:tag"
                                fullModelName = $"{model}:{tag}";
                            }
                            else if (host == "registry.ollama.ai")
                            {
                                // User namespace models: "namespace/model:tag"
                                fullModelName = $"{ns}/{model}:{tag}";
                            }
                            else
                            {
                                // Other registries: "host/namespace/model:tag"
                                fullModelName = $"{host}/{ns}/{model}:{tag}";
                            }

                            if (MatchesPattern(fullModelName, pattern))
                            {
                                var fileInfo = new FileInfo(tagFile);
                                long totalSize = 0;
                                string modelId = "";

                                try
                                {
                                    RootManifest? manifest = ManifestReader.Read<RootManifest>(tagFile);
                                    if (manifest?.layers != null)
                                    {
                                        totalSize = manifest.layers.Sum(l => l.size);
                                    }

                                    // Compute SHA256 of manifest file content (same as ollama ls)
                                    var manifestBytes = System.IO.File.ReadAllBytes(tagFile);
                                    using var sha256 = System.Security.Cryptography.SHA256.Create();
                                    var hashBytes = sha256.ComputeHash(manifestBytes);
                                    var fullDigest = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
                                    modelId = fullDigest.Substring(0, 12);
                                }
                                catch { }

                                models.Add(new LocalModelInfo
                                {
                                    Name = fullModelName,
                                    Id = modelId,
                                    Size = totalSize,
                                    ModifiedAt = fileInfo.LastWriteTime
                                });
                            }
                        }
                    }
                }
            }

            if (models.Count == 0)
            {
                Console.WriteLine($"No models found matching pattern: {pattern ?? "*"}");
                return;
            }

            PrintModelTable(SortModels(models, sortMode));
        }

        private List<LocalModelInfo> SortModels(List<LocalModelInfo> models, string sortMode)
        {
            return sortMode switch
            {
                "size_desc" => models.OrderByDescending(m => m.Size).ToList(),
                "size_asc" => models.OrderBy(m => m.Size).ToList(),
                "time_desc" => models.OrderByDescending(m => m.ModifiedAt).ToList(),
                "time_asc" => models.OrderBy(m => m.ModifiedAt).ToList(),
                _ => models.OrderBy(m => m.Name).ToList()
            };
        }

        private async Task ListRemoteModels(string serverUrl, string pattern, string sortMode = "name")
        {
            try
            {
                // Set the global client's BaseAddress for the API call
                client.BaseAddress = new Uri(serverUrl);

                HttpResponseMessage response = await client.GetAsync("api/tags");

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Error: failed to get models from remote server (HTTP {(int)response.StatusCode})");
                    System.Environment.Exit(1);
                }

                string json = await response.Content.ReadAsStringAsync();
                var modelsResponse = JsonSerializer.Deserialize<OllamaModelsResponse>(json);

                if (modelsResponse?.models == null || modelsResponse.models.Count == 0)
                {
                    Console.WriteLine("No models found on remote server.");
                    return;
                }

                var filteredModels = modelsResponse.models
                    .Where(m => MatchesPattern(m.name, pattern))
                    .Select(m => new LocalModelInfo
                    {
                        Name = m.name,
                        Id = m.digest?.StartsWith("sha256:") == true ? m.digest.Substring(7, 12) : m.digest?.Substring(0, Math.Min(12, m.digest.Length)) ?? "",
                        Size = m.size,
                        ModifiedAt = m.modified_at
                    })
                    .ToList();

                if (filteredModels.Count == 0)
                {
                    Console.WriteLine($"No models found matching pattern: {pattern ?? "*"}");
                    return;
                }

                PrintModelTable(SortModels(filteredModels, sortMode));
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error: failed to list remote models: {e.Message}");
                System.Environment.Exit(1);
            }
        }

        private void PrintModelTable(List<LocalModelInfo> models)
        {
            int nameWidth = Math.Max(50, models.Max(m => m.Name.Length) + 2);
            int idWidth = 16;
            int sizeWidth = 10;

            System.Console.WriteLine($"{"NAME".PadRight(nameWidth)}{"ID".PadRight(idWidth)}{"SIZE".PadRight(sizeWidth)}MODIFIED");

            foreach (var model in models)
            {
                var size = ByteSize.FromBytes(model.Size);
                string sizeStr = size.GigaBytes >= 1 ? $"{size.GigaBytes:F0} GB" : $"{size.MegaBytes:F0} MB";
                string timeAgo = GetTimeAgo(model.ModifiedAt);
                System.Console.WriteLine($"{model.Name.PadRight(nameWidth)}{model.Id.PadRight(idWidth)}{sizeStr.PadRight(sizeWidth)}{timeAgo}");
            }
        }

        private string GetTimeAgo(DateTime dateTime)
        {
            var timeSpan = DateTime.Now - dateTime;

            if (timeSpan.TotalMinutes < 1)
                return "just now";
            if (timeSpan.TotalMinutes < 60)
                return $"{(int)timeSpan.TotalMinutes} minutes ago";
            if (timeSpan.TotalHours < 24)
                return $"{(int)timeSpan.TotalHours} hours ago";
            if (timeSpan.TotalDays < 30)
                return $"{(int)timeSpan.TotalDays} days ago";
            if (timeSpan.TotalDays < 365)
                return $"{(int)(timeSpan.TotalDays / 30)} months ago";
            return $"{(int)(timeSpan.TotalDays / 365)} years ago";
        }

        public void ActionRemove(string? Pattern, string Destination)
        {
            Init();

            // If pattern looks like a URL or IP address, treat it as destination
            if (!string.IsNullOrEmpty(Pattern) && LooksLikeRemoteServer(Pattern))
            {
                Destination = Pattern;
                Pattern = null;
            }

            if (string.IsNullOrEmpty(Pattern))
            {
                Console.WriteLine("Error: pattern is required for remove command");
                System.Environment.Exit(1);
                return;
            }

            Debug.WriteLine("Remove: you entered pattern '{0}' and destination '{1}'. OLLAMA_MODELS={2}", Pattern, Destination, ollama_models);

            bool localRemove = string.IsNullOrEmpty(Destination);

            if (localRemove)
            {
                if (!Directory.Exists(ollama_models))
                {
                    Console.WriteLine($"Error: ollama models directory not found at: {ollama_models}");
                    System.Environment.Exit(1);
                }
                RemoveLocalModels(Pattern);
            }
            else
            {
                // Normalize URL with default protocol and port
                Destination = NormalizeServerUrl(Destination);
                if (!ValidateServerUrl(Destination, silent: true))
                {
                    System.Environment.Exit(1);
                }
                RemoveRemoteModels(Pattern, Destination).GetAwaiter().GetResult();
            }
        }

        private void RemoveLocalModels(string pattern)
        {
            var modelsToRemove = new List<string>();
            string manifestsDir = Path.Combine(ollama_models, "manifests");

            if (!Directory.Exists(manifestsDir))
            {
                Console.WriteLine($"No local models found.");
                return;
            }

            // Scan all hosts (registry.ollama.ai, hf.co, hub, etc.)
            foreach (string hostDir in Directory.GetDirectories(manifestsDir))
            {
                string host = Path.GetFileName(hostDir);

                // Scan all namespaces within each host
                foreach (string namespaceDir in Directory.GetDirectories(hostDir))
                {
                    string ns = Path.GetFileName(namespaceDir);

                    // Scan all models within each namespace
                    foreach (string modelDir in Directory.GetDirectories(namespaceDir))
                    {
                        string model = Path.GetFileName(modelDir);

                        // Tags are files directly in the model directory
                        foreach (string tagFile in Directory.GetFiles(modelDir))
                        {
                            string tag = Path.GetFileName(tagFile);

                            // Build display name based on host/namespace
                            string fullModelName;
                            if (host == "registry.ollama.ai" && ns == "library")
                            {
                                fullModelName = $"{model}:{tag}";
                            }
                            else if (host == "registry.ollama.ai")
                            {
                                fullModelName = $"{ns}/{model}:{tag}";
                            }
                            else
                            {
                                fullModelName = $"{host}/{ns}/{model}:{tag}";
                            }

                            if (MatchesPattern(fullModelName, pattern))
                            {
                                modelsToRemove.Add(fullModelName);
                            }
                        }
                    }
                }
            }

            if (modelsToRemove.Count == 0)
            {
                // If no models found, pattern has no wildcards, and no tag specified, try with :latest
                if (!pattern.Contains("*") && !pattern.Contains(":"))
                {
                    string latestPattern = $"{pattern}:latest";

                    // Retry scan with :latest pattern
                    foreach (string hostDir in Directory.GetDirectories(manifestsDir))
                    {
                        string host = Path.GetFileName(hostDir);
                        foreach (string namespaceDir in Directory.GetDirectories(hostDir))
                        {
                            string ns = Path.GetFileName(namespaceDir);
                            foreach (string modelDir in Directory.GetDirectories(namespaceDir))
                            {
                                string model = Path.GetFileName(modelDir);
                                foreach (string tagFile in Directory.GetFiles(modelDir))
                                {
                                    string tag = Path.GetFileName(tagFile);
                                    string fullModelName;
                                    if (host == "registry.ollama.ai" && ns == "library")
                                    {
                                        fullModelName = $"{model}:{tag}";
                                    }
                                    else if (host == "registry.ollama.ai")
                                    {
                                        fullModelName = $"{ns}/{model}:{tag}";
                                    }
                                    else
                                    {
                                        fullModelName = $"{host}/{ns}/{model}:{tag}";
                                    }

                                    if (MatchesPattern(fullModelName, latestPattern))
                                    {
                                        modelsToRemove.Add(fullModelName);
                                    }
                                }
                            }
                        }
                    }

                    if (modelsToRemove.Count == 0)
                    {
                        Console.WriteLine($"No models found matching pattern: {pattern} (tried '{pattern}' and '{latestPattern}')");
                        return;
                    }
                }
                else
                {
                    Console.WriteLine($"No models found matching pattern: {pattern}");
                    return;
                }
            }

            Console.WriteLine($"Removing {modelsToRemove.Count} model(s)...");

            foreach (var modelName in modelsToRemove)
            {
                try
                {
                    // Use ollama rm command to properly remove the model
                    var p = new Process();
                    p.StartInfo.FileName = "ollama";
                    p.StartInfo.Arguments = $"rm {modelName}";
                    p.StartInfo.CreateNoWindow = true;
                    p.StartInfo.UseShellExecute = false;
                    p.StartInfo.RedirectStandardOutput = true;
                    p.StartInfo.RedirectStandardError = true;

                    p.Start();
                    // Read and discard stdout/stderr to prevent ANSI codes leaking to console
                    p.StandardOutput.ReadToEnd();
                    string error = p.StandardError.ReadToEnd();
                    p.WaitForExit();

                    if (p.ExitCode == 0)
                    {
                        Console.WriteLine($"deleted '{modelName}'");
                    }
                    else
                    {
                        Console.WriteLine($"failed to delete '{modelName}': {error}");
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Error deleting '{modelName}': {e.Message}");
                }
            }
        }

        private async Task RemoveRemoteModels(string pattern, string destination)
        {
            // Create dedicated HttpClient with BaseAddress
            var remoteClient = new HttpClient() { Timeout = TimeSpan.FromMinutes(5) };
            remoteClient.BaseAddress = new Uri(destination);

            try
            {
                // First get the list of models
                HttpResponseMessage response = await remoteClient.GetAsync("api/tags");

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Error: failed to get models from remote server (HTTP {(int)response.StatusCode})");
                    System.Environment.Exit(1);
                }

                string json = await response.Content.ReadAsStringAsync();
                var modelsResponse = JsonSerializer.Deserialize<OllamaModelsResponse>(json);

                if (modelsResponse?.models == null || modelsResponse.models.Count == 0)
                {
                    Console.WriteLine("No models found on remote server.");
                    return;
                }

                var modelsToRemove = modelsResponse.models
                    .Where(m => MatchesPattern(m.name, pattern))
                    .Select(m => m.name)
                    .ToList();

                if (modelsToRemove.Count == 0)
                {
                    // If no models found, pattern has no wildcards, and no tag specified, try with :latest
                    if (!pattern.Contains("*") && !pattern.Contains(":"))
                    {
                        string latestPattern = $"{pattern}:latest";
                        modelsToRemove = modelsResponse.models
                            .Where(m => MatchesPattern(m.name, latestPattern))
                            .Select(m => m.name)
                            .ToList();

                        if (modelsToRemove.Count == 0)
                        {
                            Console.WriteLine($"No models found matching pattern: {pattern} (tried '{pattern}' and '{latestPattern}')");
                            return;
                        }
                    }
                    else
                    {
                        Console.WriteLine($"No models found matching pattern: {pattern}");
                        return;
                    }
                }

                Console.WriteLine($"Removing {modelsToRemove.Count} model(s) from remote server...");

                foreach (var modelName in modelsToRemove)
                {
                    try
                    {
                        var deleteRequest = new { model = modelName };
                        string deleteJson = JsonSerializer.Serialize(deleteRequest);
                        var content = new StringContent(deleteJson, Encoding.UTF8, "application/json");

                        var request = new HttpRequestMessage(HttpMethod.Delete, "api/delete");
                        request.Content = content;

                        var deleteResponse = await remoteClient.SendAsync(request);

                        if (deleteResponse.IsSuccessStatusCode)
                        {
                            Console.WriteLine($"deleted '{modelName}'");
                        }
                        else if (deleteResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
                        {
                            Console.WriteLine($"model '{modelName}' not found");
                        }
                        else
                        {
                            Console.WriteLine($"failed to delete '{modelName}': HTTP {(int)deleteResponse.StatusCode}");
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"Error deleting '{modelName}': {e.Message}");
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error: failed to remove remote models: {e.Message}");
                System.Environment.Exit(1);
            }
            finally
            {
                remoteClient.Dispose();
            }
        }

        public void ActionRename(string source, string newName)
        {
            Init();

            // Apply :latest tag if not specified
            string sourceModel = source;
            string targetModel = newName;

            if (!sourceModel.Contains(":"))
            {
                sourceModel = $"{sourceModel}:latest";
            }

            if (!targetModel.Contains(":"))
            {
                targetModel = $"{targetModel}:latest";
            }

            Debug.WriteLine("Rename: source '{0}' to '{1}'", sourceModel, targetModel);

            // Check if destination already exists
            var checkProcess = new Process();
            checkProcess.StartInfo.FileName = "ollama";
            checkProcess.StartInfo.Arguments = "list";
            checkProcess.StartInfo.CreateNoWindow = true;
            checkProcess.StartInfo.UseShellExecute = false;
            checkProcess.StartInfo.RedirectStandardOutput = true;
            checkProcess.StartInfo.RedirectStandardError = true;

            try
            {
                checkProcess.Start();
                string output = checkProcess.StandardOutput.ReadToEnd();
                checkProcess.WaitForExit();

                if (output.Contains(targetModel))
                {
                    Console.WriteLine($"Error: destination model '{targetModel}' already exists");
                    Console.WriteLine("Please choose a different name or remove the existing model first.");
                    System.Environment.Exit(1);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Warning: could not check if destination exists: {e.Message}");
            }

            Console.WriteLine($"Renaming model '{sourceModel}' to '{targetModel}'...");

            // Step 1: Copy the model using ollama cp
            Console.WriteLine($"Step 1/3: Copying '{sourceModel}' to '{targetModel}'...");
            var copyProcess = new Process();
            copyProcess.StartInfo.FileName = "ollama";
            copyProcess.StartInfo.Arguments = $"cp {sourceModel} {targetModel}";
            copyProcess.StartInfo.CreateNoWindow = true;
            copyProcess.StartInfo.UseShellExecute = false;
            copyProcess.StartInfo.RedirectStandardOutput = true;
            copyProcess.StartInfo.RedirectStandardError = true;

            try
            {
                copyProcess.Start();
                copyProcess.WaitForExit();

                if (copyProcess.ExitCode != 0)
                {
                    string error = copyProcess.StandardError.ReadToEnd();
                    Console.WriteLine($"Error: failed to copy model: {error}");
                    System.Environment.Exit(1);
                }

                Console.WriteLine($"Successfully copied to '{targetModel}'");
            }
            catch (Exception e)
            {
                Console.WriteLine($"Exception: failed to copy model: {e.Message}");
                System.Environment.Exit(1);
            }

            // Step 2: Verify the new model exists
            Console.WriteLine($"Step 2/3: Verifying '{targetModel}' exists...");
            var listProcess = new Process();
            listProcess.StartInfo.FileName = "ollama";
            listProcess.StartInfo.Arguments = "list";
            listProcess.StartInfo.CreateNoWindow = true;
            listProcess.StartInfo.UseShellExecute = false;
            listProcess.StartInfo.RedirectStandardOutput = true;
            listProcess.StartInfo.RedirectStandardError = true;

            bool targetExists = false;
            try
            {
                listProcess.Start();
                string output = listProcess.StandardOutput.ReadToEnd();
                listProcess.WaitForExit();

                // Check if target model appears in the list
                targetExists = output.Contains(targetModel);

                if (!targetExists)
                {
                    Console.WriteLine($"Error: failed to verify '{targetModel}' - model not found after copy");
                    System.Environment.Exit(1);
                }

                Console.WriteLine($"Verified: '{targetModel}' exists");
            }
            catch (Exception e)
            {
                Console.WriteLine($"Exception: failed to verify model: {e.Message}");
                System.Environment.Exit(1);
            }

            // Step 3: Delete the original model
            Console.WriteLine($"Step 3/3: Deleting original '{sourceModel}'...");
            var deleteProcess = new Process();
            deleteProcess.StartInfo.FileName = "ollama";
            deleteProcess.StartInfo.Arguments = $"rm {sourceModel}";
            deleteProcess.StartInfo.CreateNoWindow = true;
            deleteProcess.StartInfo.UseShellExecute = false;
            deleteProcess.StartInfo.RedirectStandardOutput = true;
            deleteProcess.StartInfo.RedirectStandardError = true;

            try
            {
                deleteProcess.Start();
                deleteProcess.WaitForExit();

                if (deleteProcess.ExitCode != 0)
                {
                    string error = deleteProcess.StandardError.ReadToEnd();
                    Console.WriteLine($"Warning: failed to delete original model: {error}");
                    Console.WriteLine($"The model was successfully copied to '{targetModel}', but the original '{sourceModel}' still exists.");
                    System.Environment.Exit(1);
                }

                Console.WriteLine($"Successfully deleted '{sourceModel}'");
                Console.WriteLine($"\nRename complete: '{source}' → '{newName}'");
            }
            catch (Exception e)
            {
                Console.WriteLine($"Exception: failed to delete original model: {e.Message}");
                Console.WriteLine($"The model was successfully copied to '{targetModel}', but the original '{sourceModel}' still exists.");
                System.Environment.Exit(1);
            }
        }

        public void ActionUpdate(string Pattern, string Destination)
        {
            Init();

            // If pattern looks like a URL or IP address, treat it as destination and extract model name if present
            if (!string.IsNullOrEmpty(Pattern) && LooksLikeRemoteServer(Pattern))
            {
                // Check if URL contains a model name (e.g., http://server:port/model:tag)
                var (serverUrl, modelName) = ParseRemoteSource(Pattern);

                if (!string.IsNullOrEmpty(modelName))
                {
                    // URL contains a model name, use it as the pattern
                    Destination = serverUrl;
                    Pattern = modelName;
                }
                else
                {
                    // URL is just the server, update all models
                    Destination = Pattern;
                    Pattern = "*";
                }
            }

            // If pattern is null or empty, default to updating all models
            if (string.IsNullOrEmpty(Pattern))
            {
                Pattern = "*";
            }

            Debug.WriteLine("Update: pattern '{0}', destination '{1}'", Pattern, Destination);

            bool localUpdate = string.IsNullOrEmpty(Destination);

            if (localUpdate)
            {
                if (!Directory.Exists(ollama_models))
                {
                    Console.WriteLine($"Error: ollama models directory not found at: {ollama_models}");
                    System.Environment.Exit(1);
                }
                UpdateLocalModels(Pattern);
            }
            else
            {
                // Normalize URL with default protocol and port
                Destination = NormalizeServerUrl(Destination);
                if (!ValidateServerUrl(Destination))
                {
                    System.Environment.Exit(1);
                }
                UpdateRemoteModels(Pattern, Destination).GetAwaiter().GetResult();
            }
        }

        public void ActionPull(string modelName, string destination)
        {
            Init();

            bool isHuggingFaceModel = false;

            // Convert HuggingFace URL to ollama format if needed
            if (IsHuggingFaceUrl(modelName))
            {
                modelName = ConvertHuggingFaceUrl(modelName);
                isHuggingFaceModel = true;
            }
            else if (modelName.StartsWith("hf.co/", StringComparison.OrdinalIgnoreCase))
            {
                isHuggingFaceModel = true;
            }

            // Check for wildcard in tag
            if (modelName.Contains(":") && modelName.Contains("*"))
            {
                // Wildcard tag expansion - pull multiple models
                ActionPullWithWildcardAsync(modelName, destination, isHuggingFaceModel).GetAwaiter().GetResult();
                return;
            }

            // Add :latest tag ONLY if:
            // 1. No tag is specified (doesn't contain ':')
            // 2. NOT a HuggingFace model (HF models always have explicit tags)
            if (!modelName.Contains(":") && !isHuggingFaceModel)
            {
                modelName = $"{modelName}:latest";
            }

            Debug.WriteLine($"Pull: pulling model '{modelName}' to '{destination ?? "local"}'");

            // Route to local or remote pull
            if (string.IsNullOrEmpty(destination))
            {
                PullLocalModel(modelName);
            }
            else
            {
                // Normalize URL with default protocol and port
                destination = NormalizeServerUrl(destination);
                if (!ValidateServerUrl(destination))
                {
                    System.Environment.Exit(1);
                }
                PullRemoteModel(modelName, destination).GetAwaiter().GetResult();
            }
        }

        /// <summary>
        /// Handles pull with wildcard tag patterns (e.g., "gemma3:1b-it-q*", "hf.co/user/repo:IQ2*").
        /// </summary>
        private async Task ActionPullWithWildcardAsync(string modelPattern, string? destination, bool isHuggingFaceModel)
        {
            // Split into model source and tag pattern
            var colonIndex = modelPattern.LastIndexOf(':');
            if (colonIndex < 0)
            {
                Console.WriteLine("Error: Invalid pattern format. Expected model:tag*");
                System.Environment.Exit(1);
                return;
            }

            var modelSource = modelPattern.Substring(0, colonIndex);
            var tagPattern = modelPattern.Substring(colonIndex + 1);

            Console.WriteLine($"Resolving tag pattern '{tagPattern}' for {modelSource}...");

            var resolver = new ModelTagResolver(client);
            var resolvedTags = await resolver.ResolveTagPatternAsync(modelSource, tagPattern);

            if (resolvedTags.Count == 0)
            {
                Console.WriteLine($"No tags found matching pattern '{tagPattern}'");
                System.Environment.Exit(1);
                return;
            }

            Console.WriteLine($"Found {resolvedTags.Count} matching tag(s):");
            foreach (var tag in resolvedTags)
            {
                Console.WriteLine($"  - {tag.Tag}");
            }
            System.Console.WriteLine();

            // Pull each matching model
            int successCount = 0;
            int failCount = 0;

            foreach (var tag in resolvedTags)
            {
                var fullModelName = tag.GetFullModelName();
                Console.WriteLine($"Pulling {fullModelName}...");

                try
                {
                    if (string.IsNullOrEmpty(destination))
                    {
                        PullLocalModel(fullModelName);
                    }
                    else
                    {
                        if (!ValidateServerUrl(destination, silent: true))
                        {
                            Console.WriteLine($"Error: Invalid destination URL: {destination}");
                            failCount++;
                            continue;
                        }
                        await PullRemoteModel(fullModelName, destination);
                    }
                    successCount++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error pulling {fullModelName}: {ex.Message}");
                    failCount++;
                }
                System.Console.WriteLine();
            }

            Console.WriteLine($"Pull complete: {successCount} succeeded, {failCount} failed");
        }

        private bool IsHuggingFaceUrl(string input)
        {
            return input.StartsWith("https://huggingface.co/", StringComparison.OrdinalIgnoreCase) ||
                   input.StartsWith("http://huggingface.co/", StringComparison.OrdinalIgnoreCase);
        }

        private string ConvertHuggingFaceUrl(string huggingFaceUrl)
        {
            try
            {
                // Parse URL: https://huggingface.co/{namespace}/{repo}/blob/{branch}/{filename}.gguf
                Uri uri = new Uri(huggingFaceUrl);
                string[] pathParts = uri.AbsolutePath.TrimStart('/').Split('/');

                if (pathParts.Length < 5)
                {
                    Console.WriteLine($"Error: Invalid HuggingFace URL format: {huggingFaceUrl}");
                    Console.WriteLine($"Expected format: https://huggingface.co/{{namespace}}/{{repo}}/blob/{{branch}}/{{filename}}.gguf");
                    System.Environment.Exit(1);
                }

                string namespacePart = pathParts[0];
                string repo = pathParts[1];
                string filename = pathParts[4];

                // Remove .gguf extension
                if (filename.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase))
                {
                    filename = filename.Substring(0, filename.Length - 5);
                }

                // Extract tag (quantization) from filename
                string tag = ExtractTagFromFilename(filename, repo);

                // Construct ollama-compatible format
                string ollamaFormat = $"hf.co/{namespacePart}/{repo}:{tag}";

                Console.WriteLine($"Converted HuggingFace URL to: {ollamaFormat}");
                return ollamaFormat;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: Failed to convert HuggingFace URL: {ex.Message}");
                System.Environment.Exit(1);
                return null;
            }
        }

        private string ExtractTagFromFilename(string filename, string repo)
        {
            // Common GGUF quantization patterns
            var quantizationPatterns = new[]
            {
                @"IQ\d+_[A-Z]+",          // IQ2_M, IQ3_XXS, IQ4_XS, etc.
                @"Q\d+_K_[A-Z]+",         // Q4_K_M, Q5_K_S, Q6_K, etc.
                @"Q\d+_K",                // Q4_K, Q5_K, etc.
                @"Q\d+_\d+",              // Q4_0, Q5_1, Q8_0, etc.
                @"F\d+",                  // F16, F32
                @"FP\d+",                 // FP16, FP32
                @"BF\d+",                 // BF16 (bfloat16)
                @"[A-Z]+\d+[A-Z]*"       // Catch-all for other formats
            };

            // Try each pattern to find quantization identifier
            foreach (var pattern in quantizationPatterns)
            {
                var match = Regex.Match(filename, pattern, RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    return match.Value.ToUpper();
                }
            }

            // Fallback: Try to extract the last segment after '-'
            var parts = filename.Split('-');
            if (parts.Length > 1)
            {
                var lastPart = parts[parts.Length - 1];

                // Check if last part looks like a quantization
                if (Regex.IsMatch(lastPart, @"[A-Z]+\d+|[A-Z]+_[A-Z]+", RegexOptions.IgnoreCase))
                {
                    return lastPart.ToUpper();
                }
            }

            // Ultimate fallback: Use the full filename
            Console.WriteLine($"Warning: Could not detect quantization format from filename '{filename}', using full filename as tag");
            return filename;
        }

        private void PullLocalModel(string modelName)
        {
            Console.WriteLine($"Pulling '{modelName}' locally...\n");

            var p = new Process();
            p.StartInfo.FileName = "ollama";
            p.StartInfo.Arguments = $"pull {modelName}";
            p.StartInfo.CreateNoWindow = false;
            p.StartInfo.UseShellExecute = false;
            // Don't redirect output - let it go directly to console for proper ANSI handling
            p.StartInfo.RedirectStandardOutput = false;
            p.StartInfo.RedirectStandardError = false;

            p.Start();
            p.WaitForExit();

            if (p.ExitCode != 0)
            {
                Console.WriteLine($"\nError: Failed to pull model '{modelName}'");
                System.Environment.Exit(1);
            }

            Console.WriteLine($"\n✓ Successfully pulled '{modelName}'");
        }

        private async Task PullRemoteModel(string modelName, string destination)
        {
            Console.WriteLine($"Pulling '{modelName}' to remote server {destination}...\n");

            var httpClient = new HttpClient() { Timeout = TimeSpan.FromHours(1) };
            httpClient.BaseAddress = new Uri(destination);

            try
            {
                var pullRequest = new
                {
                    name = modelName,
                    stream = true
                };

                var content = new StringContent(
                    JsonSerializer.Serialize(pullRequest),
                    Encoding.UTF8,
                    "application/json"
                );

                var response = await httpClient.PostAsync("api/pull", content);

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Error: Failed to pull model (HTTP {response.StatusCode})");
                    System.Environment.Exit(1);
                }

                using var stream = await response.Content.ReadAsStreamAsync();
                using var reader = new StreamReader(stream);

                string? lastDigest = null;
                string? lastStatus = null;
                string? lastDisplayedProgress = null;
                var completedDigests = new HashSet<string>();

                while (!reader.EndOfStream)
                {
                    var line = await reader.ReadLineAsync();
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    try
                    {
                        using var lineDoc = JsonDocument.Parse(line);
                        var root = lineDoc.RootElement;

                        if (root.TryGetProperty("error", out var errorProp))
                        {
                            Console.WriteLine($"\nError: {errorProp.GetString()}");
                            System.Environment.Exit(1);
                        }

                        if (root.TryGetProperty("status", out var statusProp))
                        {
                            string? status = statusProp.GetString();
                            string? digest = root.TryGetProperty("digest", out var digestProp) ? digestProp.GetString() : null;
                            long total = root.TryGetProperty("total", out var totalProp) ? totalProp.GetInt64() : 0;
                            long completed = root.TryGetProperty("completed", out var completedProp) ? completedProp.GetInt64() : 0;

                            if (status == null) continue;

                            // Check if this is a new digest or different status
                            bool isNewDigest = digest != null && digest != lastDigest;
                            bool isNewStatus = status != lastStatus || isNewDigest;

                            // Handle new status or digest
                            if (isNewStatus)
                            {
                                // Print newline if we were showing progress
                                if (lastDisplayedProgress != null)
                                {
                                    Console.WriteLine("");
                                    lastDisplayedProgress = null;
                                }

                                // Print status for non-pulling messages
                                if (!status.StartsWith("pulling ") || digest == null)
                                {
                                    Console.WriteLine(status);
                                }
                                else if (isNewDigest)
                                {
                                    // New digest being pulled
                                    string digestShort = digest.Length > 12 ? digest.Substring(0, 12) : digest;
                                    Console.Write($"pulling {digestShort}");
                                }

                                lastStatus = status;
                                lastDigest = digest;
                            }

                            // Show progress for pulling operations (but skip if already completed)
                            if (digest != null && status.StartsWith("pulling ") && total > 0 && !completedDigests.Contains(digest))
                            {
                                var completedSize = ByteSize.FromBytes(completed);
                                var totalSize = ByteSize.FromBytes(total);
                                int percentage = total > 0 ? (int)((completed * 100) / total) : 0;

                                string progressDisplay = $" {percentage}% ({completedSize.ToString("#.##")} / {totalSize.ToString("#.##")})";

                                // Only update if progress changed significantly (avoid too many updates)
                                if (progressDisplay != lastDisplayedProgress)
                                {
                                    Console.Write($"\r{status.Substring(0, Math.Min(status.Length, 40))}{progressDisplay}");
                                    lastDisplayedProgress = progressDisplay;
                                }

                                // If completed, print newline and mark as completed
                                if (completed >= total)
                                {
                                    Console.WriteLine($" 100%");
                                    lastDisplayedProgress = null;
                                    completedDigests.Add(digest);
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Skip malformed JSON lines
                    }
                }

                // Clean up any remaining progress line
                if (lastDisplayedProgress != null)
                {
                    Console.WriteLine("");
                }

                Console.WriteLine($"\n✓ Successfully pulled '{modelName}' to remote server");
            }
            finally
            {
                httpClient.Dispose();
            }
        }

        public void ActionShow(string modelName, string destination, bool license, bool modelfile, bool parameters, bool system, bool template, bool verbose)
        {
            Init();

            // Add :latest tag if not specified
            if (!modelName.Contains(":"))
            {
                modelName = $"{modelName}:latest";
            }

            Debug.WriteLine($"Show: showing model '{modelName}' at '{destination ?? "local"}'");

            // Route to local or remote show
            if (string.IsNullOrEmpty(destination))
            {
                ShowLocalModel(modelName, license, modelfile, parameters, system, template, verbose);
            }
            else
            {
                // Normalize URL with default protocol and port
                destination = NormalizeServerUrl(destination);
                if (!ValidateServerUrl(destination))
                {
                    System.Environment.Exit(1);
                }
                ShowRemoteModel(modelName, destination, license, modelfile, parameters, system, template, verbose).GetAwaiter().GetResult();
            }
        }

        private void ShowLocalModel(string modelName, bool license, bool modelfile, bool parameters, bool system, bool template, bool verbose)
        {
            // Build arguments for ollama show command
            var args = new List<string> { "show", modelName };

            if (license) args.Add("--license");
            if (modelfile) args.Add("--modelfile");
            if (parameters) args.Add("--parameters");
            if (system) args.Add("--system");
            if (template) args.Add("--template");
            if (verbose) args.Add("--verbose");

            var p = new Process();
            p.StartInfo.FileName = "ollama";
            p.StartInfo.Arguments = string.Join(" ", args);
            p.StartInfo.CreateNoWindow = false;
            p.StartInfo.UseShellExecute = false;
            // Don't redirect output - let it go directly to console for proper formatting
            p.StartInfo.RedirectStandardOutput = false;
            p.StartInfo.RedirectStandardError = false;

            p.Start();
            p.WaitForExit();

            if (p.ExitCode != 0)
            {
                Console.WriteLine($"Error: Failed to show model '{modelName}'");
                System.Environment.Exit(1);
            }
        }

        private async Task ShowRemoteModel(string modelName, string destination, bool license, bool modelfile, bool parameters, bool system, bool template, bool verbose)
        {
            // Create dedicated HttpClient with BaseAddress
            var httpClient = new HttpClient() { Timeout = TimeSpan.FromMinutes(5) };
            httpClient.BaseAddress = new Uri(destination);

            try
            {
                var showRequest = new
                {
                    name = modelName,
                    verbose = verbose
                };

                var content = new StringContent(
                    JsonSerializer.Serialize(showRequest),
                    Encoding.UTF8,
                    "application/json"
                );

                var response = await httpClient.PostAsync("api/show", content);

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Error: Failed to show model (HTTP {response.StatusCode})");
                    System.Environment.Exit(1);
                }

                string json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // If specific flags are set, show only those parts
                bool hasSpecificFlag = license || modelfile || parameters || system || template;

                if (license && root.TryGetProperty("license", out var licenseElement))
                {
                    Console.WriteLine(licenseElement.GetString() ?? "");
                }
                else if (modelfile && root.TryGetProperty("modelfile", out var modelfileElement))
                {
                    Console.WriteLine(modelfileElement.GetString() ?? "");
                }
                else if (parameters && root.TryGetProperty("parameters", out var parametersElement))
                {
                    Console.WriteLine(parametersElement.GetString() ?? "");
                }
                else if (system && root.TryGetProperty("system", out var systemElement))
                {
                    Console.WriteLine(systemElement.GetString() ?? "");
                }
                else if (template && root.TryGetProperty("template", out var templateElement))
                {
                    Console.WriteLine(templateElement.GetString() ?? "");
                }
                else if (!hasSpecificFlag)
                {
                    // Show default information (similar to ollama show without flags)
                    if (root.TryGetProperty("modelfile", out var mf))
                    {
                        Console.WriteLine(mf.GetString() ?? "");
                    }

                    if (verbose)
                    {
                        // Show detailed information
                        Console.WriteLine("");
                        if (root.TryGetProperty("license", out var lic))
                        {
                            Console.WriteLine("License:");
                            Console.WriteLine(lic.GetString() ?? "");
                            Console.WriteLine("");
                        }
                        if (root.TryGetProperty("parameters", out var param))
                        {
                            Console.WriteLine("Parameters:");
                            Console.WriteLine(param.GetString() ?? "");
                            Console.WriteLine("");
                        }
                        if (root.TryGetProperty("system", out var sys))
                        {
                            Console.WriteLine("System:");
                            Console.WriteLine(sys.GetString() ?? "");
                            Console.WriteLine("");
                        }
                        if (root.TryGetProperty("template", out var tmpl))
                        {
                            Console.WriteLine("Template:");
                            Console.WriteLine(tmpl.GetString() ?? "");
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error: Failed to show model: {e.Message}");
                System.Environment.Exit(1);
            }
            finally
            {
                httpClient.Dispose();
            }
        }

        private void UpdateLocalModels(string pattern)
        {
            var modelsToUpdate = new List<string>();
            string manifestsDir = Path.Combine(ollama_models, "manifests");

            if (!Directory.Exists(manifestsDir))
            {
                Console.WriteLine($"No local models found.");
                return;
            }

            // Scan all hosts (registry.ollama.ai, hf.co, hub, etc.)
            foreach (string hostDir in Directory.GetDirectories(manifestsDir))
            {
                string host = Path.GetFileName(hostDir);

                // Scan all namespaces within each host
                foreach (string namespaceDir in Directory.GetDirectories(hostDir))
                {
                    string ns = Path.GetFileName(namespaceDir);

                    // Scan all models within each namespace
                    foreach (string modelDir in Directory.GetDirectories(namespaceDir))
                    {
                        string model = Path.GetFileName(modelDir);

                        // Tags are files directly in the model directory
                        foreach (string tagFile in Directory.GetFiles(modelDir))
                        {
                            string tag = Path.GetFileName(tagFile);

                            // Build display name based on host/namespace
                            string fullModelName;
                            if (host == "registry.ollama.ai" && ns == "library")
                            {
                                fullModelName = $"{model}:{tag}";
                            }
                            else if (host == "registry.ollama.ai")
                            {
                                fullModelName = $"{ns}/{model}:{tag}";
                            }
                            else
                            {
                                fullModelName = $"{host}/{ns}/{model}:{tag}";
                            }

                            if (MatchesPattern(fullModelName, pattern))
                            {
                                modelsToUpdate.Add(fullModelName);
                            }
                        }
                    }
                }
            }

            if (modelsToUpdate.Count == 0)
            {
                Console.WriteLine($"No models found matching pattern: {pattern}");
                return;
            }

            Console.WriteLine($"Updating {modelsToUpdate.Count} model(s)...\n");

            foreach (var modelName in modelsToUpdate)
            {
                UpdateSingleLocalModel(modelName);
            }
        }

        private void UpdateSingleLocalModel(string modelName)
        {
            try
            {
                Console.WriteLine($"Updating '{modelName}'...");

                var p = new Process();
                p.StartInfo.FileName = "ollama";
                p.StartInfo.Arguments = $"pull {modelName}";
                p.StartInfo.CreateNoWindow = true;
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.RedirectStandardError = true;

                var output = new StringBuilder();
                var errorOutput = new StringBuilder();
                bool hadDownloadActivity = false;

                p.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        output.AppendLine(e.Data);
                        Console.WriteLine(e.Data);

                        // Check if there's actual download activity (pulling layers with progress)
                        if (e.Data.Contains("pulling") && (e.Data.Contains("%") || e.Data.Contains("B/")))
                        {
                            hadDownloadActivity = true;
                        }
                    }
                };

                p.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        errorOutput.AppendLine(e.Data);
                    }
                };

                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                p.WaitForExit();

                if (p.ExitCode == 0)
                {
                    string fullOutput = output.ToString().ToLower();

                    // Check various patterns that indicate model is already up to date
                    if (fullOutput.Contains("up to date") ||
                        fullOutput.Contains("already up-to-date") ||
                        fullOutput.Contains("already exists") ||
                        (!hadDownloadActivity && fullOutput.Contains("success")))
                    {
                        Console.WriteLine($"✓ '{modelName}' is already up to date\n");
                    }
                    else if (hadDownloadActivity)
                    {
                        Console.WriteLine($"✓ '{modelName}' updated successfully\n");
                    }
                    else
                    {
                        // If no clear indicator, assume it's up to date (no downloads happened)
                        Console.WriteLine($"✓ '{modelName}' is already up to date\n");
                    }
                }
                else
                {
                    string error = errorOutput.ToString();
                    Console.WriteLine($"✗ Failed to update '{modelName}': {error}\n");
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"✗ Error updating '{modelName}': {e.Message}\n");
            }
        }

        private async Task UpdateRemoteModels(string pattern, string destination)
        {
            // Create dedicated client for remote server
            var remoteClient = new HttpClient() { Timeout = TimeSpan.FromHours(1) };
            remoteClient.BaseAddress = new Uri(destination);

            try
            {
                // First get the list of models
                HttpResponseMessage response = await remoteClient.GetAsync("api/tags");

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Error: failed to get models from remote server (HTTP {(int)response.StatusCode})");
                    System.Environment.Exit(1);
                }

                string json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var models = doc.RootElement.GetProperty("models");

                var modelsToUpdate = new List<string>();
                foreach (var model in models.EnumerateArray())
                {
                    string? modelName = model.GetProperty("name").GetString();
                    if (modelName != null && MatchesPattern(modelName, pattern))
                    {
                        modelsToUpdate.Add(modelName);
                    }
                }

                if (modelsToUpdate.Count == 0)
                {
                    Console.WriteLine($"No models found matching pattern: {pattern}");
                    return;
                }

                Console.WriteLine($"Updating {modelsToUpdate.Count} model(s) on remote server...\n");

                foreach (var modelName in modelsToUpdate)
                {
                    await UpdateSingleRemoteModel(remoteClient, modelName, destination);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error: {e.Message}");
                System.Environment.Exit(1);
            }
            finally
            {
                remoteClient.Dispose();
            }
        }

        private async Task UpdateSingleRemoteModel(HttpClient httpClient, string modelName, string destination)
        {
            try
            {
                Console.WriteLine($"Updating '{modelName}' on remote server...");

                var pullRequest = new
                {
                    name = modelName,
                    stream = true
                };

                var content = new StringContent(
                    JsonSerializer.Serialize(pullRequest),
                    Encoding.UTF8,
                    "application/json"
                );

                var response = await httpClient.PostAsync("api/pull", content);

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"✗ Failed to update '{modelName}': HTTP {(int)response.StatusCode}\n");
                    return;
                }

                // Read the streaming response
                using var stream = await response.Content.ReadAsStreamAsync();
                using var reader = new StreamReader(stream);

                bool isUpToDate = false;
                bool hasError = false;
                bool hadDownloadActivity = false;
                string lastStatus = "";

                while (!reader.EndOfStream)
                {
                    var line = await reader.ReadLineAsync();
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    try
                    {
                        using var lineDoc = JsonDocument.Parse(line);
                        var root = lineDoc.RootElement;

                        if (root.TryGetProperty("status", out var statusProp))
                        {
                            lastStatus = statusProp.GetString() ?? "";
                            Console.WriteLine(lastStatus);

                            string statusLower = lastStatus.ToLower();

                            // Check for up-to-date messages
                            if (statusLower.Contains("up to date") ||
                                statusLower.Contains("already up-to-date") ||
                                statusLower.Contains("already exists"))
                            {
                                isUpToDate = true;
                            }

                            // Check for actual download activity
                            if (statusLower.Contains("pulling") || statusLower.Contains("downloading"))
                            {
                                // Check if there's a total/completed bytes indicator (actual download)
                                if (root.TryGetProperty("completed", out _) || root.TryGetProperty("total", out _))
                                {
                                    hadDownloadActivity = true;
                                }
                            }
                        }

                        if (root.TryGetProperty("error", out var errorProp))
                        {
                            hasError = true;
                            Console.WriteLine($"Error: {errorProp.GetString()}");
                        }
                    }
                    catch
                    {
                        // Skip malformed JSON lines
                    }
                }

                if (hasError)
                {
                    Console.WriteLine($"✗ Failed to update '{modelName}'\n");
                }
                else if (isUpToDate || !hadDownloadActivity)
                {
                    Console.WriteLine($"✓ '{modelName}' is already up to date\n");
                }
                else
                {
                    Console.WriteLine($"✓ '{modelName}' updated successfully\n");
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"✗ Error updating '{modelName}': {e.Message}\n");
            }
        }

        public void Init()
        {
            // Clear any tab completion options from the screen
            LocalModelsTabCompletionSource.ClearPreviousOptions();

            var env_models = System.Environment.GetEnvironmentVariable("OLLAMA_MODELS");
            if (env_models != null && env_models.Length > 0)
            {
                ollama_models = env_models;
            }
            if (env_models == null || env_models.Length < 1)
            {
                env_models = System.Environment.GetEnvironmentVariable("OLLAMA_MODELS", EnvironmentVariableTarget.User);
                if (env_models != null && env_models.Length > 0)
                {
                    ollama_models = env_models;
                }
            }
            if (ollama_models.Length < 1)
            {
                ollama_models = "*";
            }
            ollama_models = GetPlatformPath(ollama_models, separator);

            btvalue = ParseBandwidthThrottling(this.BandwidthThrottling);

        }

        public void ActionInstall()
        {
            Init();

            Console.WriteLine("Installing osync...\n");
            Console.WriteLine($"Installing osync... {System.Environment.ProcessPath}\n"); 

            // Detect operating system
            bool isWindows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);
            bool isLinux = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux);
            bool isMacOS = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX);

            // Get current application directory
            string currentExeDir = AppContext.BaseDirectory;
            string exeName = isWindows ? "osync.exe" : "osync";
            string currentExePath = Path.Combine(currentExeDir, exeName);

            // Determine installation directory
            string installDir;
            if (isWindows)
            {
                installDir = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), ".osync");
            }
            else // Linux or macOS
            {
                installDir = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), ".local", "bin");
            }

            // Create installation directory if it doesn't exist
            if (!Directory.Exists(installDir))
            {
                Directory.CreateDirectory(installDir);
                Console.WriteLine($"Created directory: {installDir}");
            }

            // Platform-specific optional dependencies (add files here if needed in future)
            // These are only copied if they exist in the source directory
            var optionalDependencies = new Dictionary<string, string[]>
            {
                // Example: { "osx", new[] { "libuv.dylib" } }
                // Currently no extra dependencies needed for any platform
                { "win", Array.Empty<string>() },
                { "linux", Array.Empty<string>() },
                { "osx", Array.Empty<string>() }
            };

            string platformKey = isWindows ? "win" : (isMacOS ? "osx" : "linux");

            try
            {
                // Check if we're already running from the install directory
                if (Path.GetFullPath(currentExeDir).Equals(Path.GetFullPath(installDir), StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"✓ osync is already installed in: {installDir}");
                }
                else
                {
                    // Copy the main executable
                    string sourceExePath = Path.Combine(currentExeDir, exeName);
                    string targetExePath = Path.Combine(installDir, exeName);

                    if (!System.IO.File.Exists(sourceExePath))
                    {
                        Console.WriteLine($"Error: Cannot find executable at {sourceExePath}");
                        System.Environment.Exit(1);
                    }

                    System.IO.File.Copy(sourceExePath, targetExePath, true);
                    Console.WriteLine($"✓ Copied osync to: {installDir}");

                    // Copy platform-specific optional dependencies if they exist
                    int depsCopied = 0;
                    if (optionalDependencies.TryGetValue(platformKey, out string[]? deps) && deps.Length > 0)
                    {
                        foreach (string depFile in deps)
                        {
                            string sourceDep = Path.Combine(currentExeDir, depFile);
                            if (System.IO.File.Exists(sourceDep))
                            {
                                string targetDep = Path.Combine(installDir, depFile);
                                System.IO.File.Copy(sourceDep, targetDep, true);
                                depsCopied++;
                                Console.WriteLine($"  + Copied dependency: {depFile}");
                            }
                        }
                    }

                    // On Unix systems, make the executable file executable
                    if (isLinux || isMacOS)
                    {
                        try
                        {
                            var chmodProcess = new Process
                            {
                                StartInfo = new ProcessStartInfo
                                {
                                    FileName = "chmod",
                                    Arguments = $"+x \"{targetExePath}\"",
                                    RedirectStandardOutput = true,
                                    RedirectStandardError = true,
                                    UseShellExecute = false,
                                    CreateNoWindow = true
                                }
                            };
                            chmodProcess.Start();
                            chmodProcess.WaitForExit();
                        }
                        catch
                        {
                            // chmod might fail, but that's okay
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error copying files: {ex.Message}");
                System.Environment.Exit(1);
            }

            // Add to PATH if not already there
            bool pathUpdated = AddToPath(installDir, isWindows, isLinux, isMacOS);

            if (pathUpdated)
            {
                Console.WriteLine($"✓ Added {installDir} to PATH");
            }
            else
            {
                Console.WriteLine($"✓ {installDir} is already in PATH");
            }

            // Check if shell completion is available before asking
            Console.WriteLine("");
            bool completionAvailable = true;
            string completionWarning = "";

            if (isWindows)
            {
                // Check PowerShell version first
                try
                {
                    string psExecutable = DetectPowerShellExecutable();
                    var (version, edition) = GetPowerShellVersion(psExecutable);

                    if (version.Major < 6)
                    {
                        completionAvailable = false;
                        completionWarning = $"Shell completion is not available (PowerShell {version} detected, requires 6.0+)";
                    }
                }
                catch (Exception ex)
                {
                    completionAvailable = false;
                    completionWarning = $"Could not detect PowerShell version: {ex.Message}";
                }
            }

            if (completionAvailable)
            {
                Console.Write("Would you like to install shell completion? (y/n): ");
                string? response = Console.ReadLine()?.Trim().ToLower();

                if (response == "y" || response == "yes")
                {
                    Console.WriteLine("");
                    if (isWindows)
                    {
                        InstallPowerShellCompletion();
                    }
                    else if (isLinux || isMacOS)
                    {
                        InstallBashCompletion();
                    }
                }
                else
                {
                    Console.WriteLine("Skipping shell completion installation.");
                }
            }
            else
            {
                Console.WriteLine(completionWarning);
                Console.WriteLine("\nTo install shell completion later:");
                if (isWindows)
                {
                    Console.WriteLine("  1. Install PowerShell 7+: https://github.com/PowerShell/PowerShell/releases");
                    Console.WriteLine("  2. Run: osync install");
                }
            }

            // Final instructions
            Console.WriteLine("\n" + new string('=', 60));
            Console.WriteLine("Installation complete!");
            Console.WriteLine(new string('=', 60));

            if (isWindows)
            {
                Console.WriteLine("\nNext steps:");
                Console.WriteLine("  1. Restart your terminal to reload PATH");
                Console.WriteLine("  2. Verify installation: osync --help");
            }
            else
            {
                Console.WriteLine("\nNext steps:");
                Console.WriteLine("  1. Reload your shell configuration:");
                if (isMacOS)
                {
                    Console.WriteLine("     source ~/.zshrc  (or ~/.bash_profile)");
                }
                else
                {
                    Console.WriteLine("     source ~/.bashrc");
                }
                Console.WriteLine("  2. Verify installation: osync --help");
            }
        }

        private bool AddToPath(string directory, bool isWindows, bool isLinux, bool isMacOS)
        {
            if (isWindows)
            {
                return AddToPathWindows(directory);
            }
            else if (isLinux || isMacOS)
            {
                return AddToPathUnix(directory, isMacOS);
            }
            return false;
        }

        private bool AddToPathWindows(string directory)
        {
            try
            {
                // Get user PATH environment variable
                string userPath = System.Environment.GetEnvironmentVariable("PATH", System.EnvironmentVariableTarget.User) ?? "";

                // Normalize the directory path
                string normalizedDirectory = Path.GetFullPath(directory);

                // Check if directory is already in PATH
                string[] paths = userPath.Split(';');
                foreach (string path in paths)
                {
                    if (string.IsNullOrWhiteSpace(path))
                        continue;

                    try
                    {
                        string normalizedPath = Path.GetFullPath(path.Trim());
                        if (normalizedPath.Equals(normalizedDirectory, StringComparison.OrdinalIgnoreCase))
                        {
                            return false; // Already in PATH
                        }
                    }
                    catch
                    {
                        // Skip invalid paths
                        continue;
                    }
                }

                // Add to PATH
                string newPath = string.IsNullOrEmpty(userPath) ? directory : userPath.TrimEnd(';') + ";" + directory;
                System.Environment.SetEnvironmentVariable("PATH", newPath, System.EnvironmentVariableTarget.User);

                // Broadcast environment variable change to the system
                BroadcastEnvironmentChange();

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Could not update PATH: {ex.Message}");
                Console.WriteLine($"Please add {directory} to your PATH manually.");
                return false;
            }
        }

        private void BroadcastEnvironmentChange()
        {
            try
            {
                // Notify the system that environment variables have changed
                // This allows currently running applications to refresh their environment
                const int HWND_BROADCAST = 0xffff;
                const uint WM_SETTINGCHANGE = 0x001a;
                IntPtr result;
                SendMessageTimeout(
                    new IntPtr(HWND_BROADCAST),
                    WM_SETTINGCHANGE,
                    IntPtr.Zero,
                    "Environment",
                    0,
                    1000,
                    out result);
            }
            catch
            {
                // If broadcast fails, it's not critical - PATH is still updated
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern IntPtr SendMessageTimeout(
            IntPtr hWnd,
            uint Msg,
            IntPtr wParam,
            string lParam,
            uint fuFlags,
            uint uTimeout,
            out IntPtr lpdwResult);

        private bool AddToPathUnix(string directory, bool isMacOS)
        {
            try
            {
                string homeDir = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
                string[] rcFiles;

                if (isMacOS)
                {
                    // macOS uses zsh by default since Catalina
                    rcFiles = new[] {
                        Path.Combine(homeDir, ".zshrc"),
                        Path.Combine(homeDir, ".bash_profile")
                    };
                }
                else
                {
                    // Linux typically uses bash
                    rcFiles = new[] {
                        Path.Combine(homeDir, ".bashrc"),
                        Path.Combine(homeDir, ".bash_profile")
                    };
                }

                string pathLine = $"\nexport PATH=\"{directory}:$PATH\"\n";

                foreach (string rcFile in rcFiles)
                {
                    // For macOS, try .zshrc first, if it exists use only that
                    // For Linux, try .bashrc first, if it exists use only that
                    if (System.IO.File.Exists(rcFile))
                    {
                        string content = System.IO.File.ReadAllText(rcFile);

                        // Check if already in file
                        if (content.Contains($"export PATH=\"{directory}:$PATH\"") ||
                            content.Contains($"export PATH='{directory}:$PATH'") ||
                            content.Contains($"PATH=\"{directory}:$PATH\"") ||
                            content.Contains($"PATH='{directory}:$PATH'"))
                        {
                            return false; // Already configured
                        }

                        // Add PATH export
                        System.IO.File.AppendAllText(rcFile, pathLine);
                        Console.WriteLine($"Added PATH export to: {rcFile}");
                        return true;
                    }
                }

                // If no rc file exists, create .bashrc (Linux) or .zshrc (macOS)
                string defaultRc = isMacOS ? rcFiles[0] : rcFiles[0];
                System.IO.File.WriteAllText(defaultRc, pathLine);
                Console.WriteLine($"Created {defaultRc} with PATH export");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Could not update shell configuration: {ex.Message}");
                Console.WriteLine($"Please add {directory} to your PATH manually by adding this line to your shell rc file:");
                Console.WriteLine($"  export PATH=\"{directory}:$PATH\"");
                return false;
            }
        }

        private void InstallBashCompletion()
        {
            try
            {
                // Get the completion script content
                string completionScript = GetBashCompletionScript();

                // Convert Windows line endings (CRLF) to Unix line endings (LF)
                completionScript = completionScript.Replace("\r\n", "\n");

                // Determine installation path
                string? installPath = null;
                string[] possiblePaths = new[]
                {
                    "/etc/bash_completion.d/osync",
                    Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), ".local/share/bash-completion/completions/osync"),
                    Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), ".bash_completion.d/osync")
                };

                // Try to write to system location first, then user locations
                foreach (var path in possiblePaths)
                {
                    try
                    {
                        string? directory = Path.GetDirectoryName(path);
                        if (directory != null && !Directory.Exists(directory))
                        {
                            Directory.CreateDirectory(directory);
                        }

                        System.IO.File.WriteAllText(path, completionScript);
                        installPath = path;
                        Console.WriteLine($"✓ Bash completion installed to: {path}");
                        break;
                    }
                    catch
                    {
                        // Try next location
                        continue;
                    }
                }

                if (installPath == null)
                {
                    // Fallback: Add to .bashrc
                    string bashrc = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), ".bashrc");
                    // Note: completionScript already has Unix line endings from conversion above
                    string sourceCommand = "\n# osync completion\n" + completionScript + "\n";

                    // Check if already exists
                    if (System.IO.File.Exists(bashrc))
                    {
                        string bashrcContent = System.IO.File.ReadAllText(bashrc);
                        if (!bashrcContent.Contains("# osync completion"))
                        {
                            System.IO.File.AppendAllText(bashrc, sourceCommand);
                            Console.WriteLine($"✓ Bash completion added to: {bashrc}");
                        }
                        else
                        {
                            Console.WriteLine($"✓ Bash completion already configured in: {bashrc}");
                        }
                    }
                    else
                    {
                        System.IO.File.WriteAllText(bashrc, sourceCommand);
                        Console.WriteLine($"✓ Bash completion added to: {bashrc}");
                    }
                }

                Console.WriteLine("\nTo activate completion, run:");
                Console.WriteLine("  source ~/.bashrc");
                Console.WriteLine("Or restart your terminal.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error installing bash completion: {ex.Message}");
                System.Environment.Exit(1);
            }
        }

        private void InstallPowerShellCompletion()
        {
            try
            {
                // Detect which PowerShell executable to use (pwsh or powershell)
                string psExecutable = DetectPowerShellExecutable();

                // Check PowerShell version
                var (version, edition) = GetPowerShellVersion(psExecutable);

                Console.WriteLine($"Detected: PowerShell {version} ({edition})");

                // Check if version is >= 6.0
                if (version.Major < 6)
                {
                    Console.WriteLine("\nError: PowerShell version 6.0 or higher is required for completion support.");
                    Console.WriteLine($"Current version: {version} ({edition})");
                    Console.WriteLine("\nPowerShell Desktop 5.x does not support the ArgumentCompleter used by osync.");
                    Console.WriteLine("\nPlease install PowerShell 7+ from:");
                    Console.WriteLine("  https://github.com/PowerShell/PowerShell/releases");
                    Console.WriteLine("\nOr use the interactive mode completion which works on all PowerShell versions:");
                    Console.WriteLine("  osync  (then press TAB)");
                    System.Environment.Exit(1);
                }

                // Get PowerShell profile path from the detected PowerShell
                string profilePath = GetPowerShellProfilePath(psExecutable);

                if (string.IsNullOrEmpty(profilePath))
                {
                    Console.WriteLine("Error: Could not determine PowerShell profile path");
                    System.Environment.Exit(1);
                }

                Console.WriteLine($"PowerShell profile: {profilePath}");

                // Create profile directory if it doesn't exist
                string? profileDir = Path.GetDirectoryName(profilePath);
                if (profileDir != null && !Directory.Exists(profileDir))
                {
                    Directory.CreateDirectory(profileDir);
                    Console.WriteLine($"Created profile directory: {profileDir}");
                }

                // Get completion script content
                string completionScript = GetPowerShellCompletionScript();

                // Check if profile exists
                bool profileExists = System.IO.File.Exists(profilePath);
                string profileContent = profileExists ? System.IO.File.ReadAllText(profilePath) : "";

                // Check if osync completion is already configured
                if (profileContent.Contains("# osync PowerShell completion - START"))
                {
                    Console.WriteLine("Removing existing osync completion configuration...");

                    // Remove old completion block using START and END markers
                    int startIndex = profileContent.IndexOf("# osync PowerShell completion - START");
                    if (startIndex >= 0)
                    {
                        // Find the end marker
                        int endIndex = profileContent.IndexOf("# osync PowerShell completion - END", startIndex);
                        if (endIndex >= 0)
                        {
                            // Include the END marker line in removal
                            endIndex = profileContent.IndexOf("\n", endIndex);
                            if (endIndex < 0)
                            {
                                endIndex = profileContent.Length;
                            }
                            else
                            {
                                endIndex++; // Include the newline
                            }

                            // Remove the completion block
                            profileContent = profileContent.Remove(startIndex, endIndex - startIndex);
                        }
                        else
                        {
                            // Old format without END marker - remove until next section or EOF
                            int legacyEndIndex = profileContent.IndexOf("\n# ", startIndex + 1);
                            if (legacyEndIndex < 0)
                            {
                                legacyEndIndex = profileContent.IndexOf("\nfunction ", startIndex + 1);
                            }
                            if (legacyEndIndex < 0)
                            {
                                legacyEndIndex = profileContent.Length;
                            }
                            profileContent = profileContent.Remove(startIndex, legacyEndIndex - startIndex);
                        }

                        // Remove trailing newlines
                        profileContent = profileContent.TrimEnd('\r', '\n');
                    }
                }

                // Add completion script to profile
                string completionBlock = "\n\n" + completionScript;
                profileContent += completionBlock;

                // Write updated profile
                System.IO.File.WriteAllText(profilePath, profileContent);

                if (profileExists)
                {
                    Console.WriteLine($"✓ Updated osync completion in profile: {profilePath}");
                }
                else
                {
                    Console.WriteLine($"✓ Created profile and added osync completion: {profilePath}");
                }

                Console.WriteLine("\nTo activate completion:");
                Console.WriteLine("  1. Restart PowerShell, or");
                Console.WriteLine($"  2. Run: . {profilePath}");
                Console.WriteLine("\nNote: You may need to set execution policy:");
                Console.WriteLine("  Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error installing PowerShell completion: {ex.Message}");
                System.Environment.Exit(1);
            }
        }

        private string DetectPowerShellExecutable()
        {
            // Detect which PowerShell is currently running by checking parent process
            try
            {
                var currentProcess = Process.GetCurrentProcess();
                var parentProcess = GetParentProcess(currentProcess);

                if (parentProcess != null)
                {
                    string parentName = parentProcess.ProcessName.ToLower();

                    Debug.WriteLine($"Parent process: {parentName} (PID: {parentProcess.Id})");

                    // Check if parent is pwsh (PowerShell Core/7+)
                    if (parentName == "pwsh")
                    {
                        Debug.WriteLine("Detected parent as pwsh (PowerShell Core)");
                        return parentProcess.MainModule?.FileName ?? "pwsh"; // Return full path to the actual executable
                    }
                    // Check if parent is powershell (Windows PowerShell 5.x)
                    else if (parentName == "powershell")
                    {
                        Debug.WriteLine("Detected parent as powershell (Windows PowerShell)");
                        return parentProcess.MainModule?.FileName ?? "powershell"; // Return full path to the actual executable
                    }
                    else
                    {
                        Debug.WriteLine($"Parent is not PowerShell, it's: {parentName}");
                    }
                }
                else
                {
                    Debug.WriteLine("Failed to get parent process");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Exception in parent process detection: {ex.Message}");
                // If parent process detection fails, fall through to availability check
            }

            // Fallback: Check which PowerShell is available
            // Try pwsh first (PowerShell Core/7+)
            Debug.WriteLine("Falling back to availability check");
            try
            {
                var p = new Process();
                p.StartInfo.FileName = "pwsh";
                p.StartInfo.Arguments = "-NoProfile -Command \"exit\"";
                p.StartInfo.CreateNoWindow = true;
                p.StartInfo.UseShellExecute = false;
                p.Start();
                p.WaitForExit();

                if (p.ExitCode == 0)
                {
                    return "pwsh";
                }
            }
            catch
            {
                // pwsh not found, try powershell
            }

            // Final fallback to powershell (Windows PowerShell 5.x)
            return "powershell";
        }

        private Process? GetParentProcess(Process process)
        {
            try
            {
                var parentPid = 0;
                var handle = process.Handle;

                // Use WMI to get parent process ID on Windows
                var query = $"SELECT ParentProcessId FROM Win32_Process WHERE ProcessId = {process.Id}";
                using (var searcher = new System.Management.ManagementObjectSearcher(query))
                {
                    foreach (System.Management.ManagementObject obj in searcher.Get())
                    {
                        parentPid = Convert.ToInt32(obj["ParentProcessId"]);
                        break;
                    }
                }

                if (parentPid > 0)
                {
                    return Process.GetProcessById(parentPid);
                }
            }
            catch
            {
                // Failed to get parent process
            }

            return null;
        }

        private (Version version, string edition) GetPowerShellVersion(string psExecutable)
        {
            try
            {
                Debug.WriteLine($"Getting PowerShell version for: {psExecutable}");

                var p = new Process();
                p.StartInfo.FileName = psExecutable;
                p.StartInfo.Arguments = "-NoProfile -Command \"$PSVersionTable.PSVersion.ToString(); $PSVersionTable.PSEdition\"";
                p.StartInfo.CreateNoWindow = true;
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.RedirectStandardError = true;

                p.Start();
                string output = p.StandardOutput.ReadToEnd();
                string error = p.StandardError.ReadToEnd();
                p.WaitForExit();

                Debug.WriteLine($"PowerShell version output: {output}");
                if (!string.IsNullOrEmpty(error))
                {
                    Debug.WriteLine($"PowerShell version error: {error}");
                }

                if (p.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                {
                    var lines = output.Trim().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    if (lines.Length >= 2)
                    {
                        Version version = Version.Parse(lines[0].Trim());
                        string edition = lines[1].Trim();
                        Debug.WriteLine($"Parsed version: {version}, edition: {edition}");
                        return (version, edition);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Exception getting PowerShell version: {ex.Message}");
                // Return default if detection fails
            }

            Debug.WriteLine("Failed to get version, returning default (5.1, Desktop)");
            return (new Version(5, 1), "Desktop");
        }

        private string GetPowerShellProfilePath(string psExecutable)
        {
            // Get profile path from the specified PowerShell executable using $PROFILE
            try
            {
                Debug.WriteLine($"Getting profile path for: {psExecutable}");

                var p = new Process();
                p.StartInfo.FileName = psExecutable;
                p.StartInfo.Arguments = "-NoProfile -Command \"$PROFILE\"";
                p.StartInfo.CreateNoWindow = true;
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.RedirectStandardError = true;

                p.Start();
                string output = p.StandardOutput.ReadToEnd();
                string error = p.StandardError.ReadToEnd();
                p.WaitForExit();

                Debug.WriteLine($"Profile path output: {output}");
                if (!string.IsNullOrEmpty(error))
                {
                    Debug.WriteLine($"Profile path error: {error}");
                }

                if (p.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                {
                    string profilePath = output.Trim();
                    Debug.WriteLine($"Using profile path: {profilePath}");
                    return profilePath;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Exception getting profile path: {ex.Message}");
                // Fallback to default path
            }

            // Fallback to default PowerShell profile location based on executable
            string documentsPath = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments);
            string executableLower = psExecutable.ToLower();

            if (executableLower.Contains("pwsh"))
            {
                // PowerShell Core/7+ uses "PowerShell" folder
                Debug.WriteLine("Falling back to pwsh profile path");
                return Path.Combine(documentsPath, "PowerShell", "Microsoft.PowerShell_profile.ps1");
            }
            else
            {
                // Windows PowerShell 5.x uses "WindowsPowerShell" folder
                Debug.WriteLine("Falling back to powershell profile path");
                return Path.Combine(documentsPath, "WindowsPowerShell", "Microsoft.PowerShell_profile.ps1");
            }
        }

        private string GetBashCompletionScript()
        {
            return @"#!/usr/bin/env bash
# osync bash completion script

_osync_completions() {
    local cur prev
    COMPREPLY=()
    cur=""${COMP_WORDS[COMP_CWORD]}""
    prev=""${COMP_WORDS[COMP_CWORD-1]}""

    # Available commands
    local commands=""cp copy ls list rm remove del delete mv ren rename update pull show run chat ps load unload manage qc qcview install quit""

    # First argument - complete commands
    if [[ ${COMP_CWORD} -eq 1 ]]; then
        COMPREPLY=( $(compgen -W ""${commands}"" -- ""${cur}"") )
        return 0
    fi

    # Get the command - use word splitting on COMP_LINE to handle colons
    local cmd_line=""${COMP_LINE}""
    local command
    read -ra cmd_words <<< ""${cmd_line}""
    if [[ ${#cmd_words[@]} -ge 2 ]]; then
        command=""${cmd_words[1]}""
    else
        return 0
    fi

    # Get the real current word from COMP_LINE (handles colons)
    local line_to_point=""${COMP_LINE:0:COMP_POINT}""
    local realcur=""${line_to_point##* }""

    # Extract remote server URL from the command line
    local server_url=""""
    if [[ ""${COMP_LINE}"" =~ -d[[:space:]]+(https?://[^[:space:]]+) ]]; then
        server_url=""${BASH_REMATCH[1]}""
    fi

    # Handle qcview file completion first (simple case)
    if [[ ""${command}"" == ""qcview"" ]]; then
        if [[ ""${cur}"" == -* ]]; then
            COMPREPLY=( $(compgen -W ""-F -Fo -O"" -- ""${cur}"") )
        else
            # File completion
            COMPREPLY=( $(compgen -f -- ""${cur}"") )
        fi
        return 0
    fi

    # Get models for completion
    local models
    if [[ -n ""${server_url}"" ]]; then
        models=$(osync ls -d ""${server_url}"" 2>/dev/null | grep -v ""^osync"" | grep -v ""^NAME"" | grep -v ""^Model"" | grep -v ""^$"" | awk '{print $1}')
    else
        models=$(osync ls 2>/dev/null | grep -v ""^osync"" | grep -v ""^NAME"" | grep -v ""^Model"" | grep -v ""^$"" | awk '{print $1}')
    fi

    # Function to complete models with colon support
    _do_model_completion() {
        # Generate completions using the real current word (may contain colons)
        COMPREPLY=( $(compgen -W ""${models}"" -- ""${realcur}"") )

        # If realcur contains a colon, strip the prefix from completions
        if [[ ""${realcur}"" == *:* && ${#COMPREPLY[@]} -gt 0 ]]; then
            local prefix=""${realcur%:*}:""
            local i
            for i in ""${!COMPREPLY[@]}""; do
                COMPREPLY[$i]=""${COMPREPLY[$i]#${prefix}}""
            done
        fi
    }

    # Complete based on command
    case ""${command}"" in
        cp|copy)
            if [[ ""${cur}"" == -* ]]; then
                COMPREPLY=( $(compgen -W ""-bt -BufferSize -d"" -- ""${cur}"") )
            else
                _do_model_completion
            fi
            ;;
        ls|list)
            if [[ ""${cur}"" == -* ]]; then
                COMPREPLY=( $(compgen -W ""--size --sizeasc --time --timeasc -d"" -- ""${cur}"") )
            else
                _do_model_completion
            fi
            ;;
        rm|remove|del|delete)
            if [[ ""${cur}"" == -* ]]; then
                COMPREPLY=( $(compgen -W ""-d"" -- ""${cur}"") )
            else
                _do_model_completion
            fi
            ;;
        mv|ren|rename)
            if [[ ""${cur}"" == -* ]]; then
                COMPREPLY=( $(compgen -W ""-d"" -- ""${cur}"") )
            else
                _do_model_completion
            fi
            ;;
        update)
            if [[ ""${cur}"" == -* ]]; then
                COMPREPLY=( $(compgen -W ""-d"" -- ""${cur}"") )
            else
                _do_model_completion
            fi
            ;;
        pull)
            if [[ ""${cur}"" == -* ]]; then
                COMPREPLY=( $(compgen -W ""-d"" -- ""${cur}"") )
            else
                _do_model_completion
            fi
            ;;
        show)
            if [[ ""${cur}"" == -* ]]; then
                COMPREPLY=( $(compgen -W ""--license --modelfile --parameters --system --template -v --verbose -d"" -- ""${cur}"") )
            else
                _do_model_completion
            fi
            ;;
        run|chat)
            if [[ ""${cur}"" == -* ]]; then
                COMPREPLY=( $(compgen -W ""-d --verbose --no-wordwrap --format --keepalive --dimensions --think --hide-thinking --insecure --truncate"" -- ""${cur}"") )
            else
                _do_model_completion
            fi
            ;;
        ps)
            if [[ ""${cur}"" == -* ]]; then
                COMPREPLY=( $(compgen -W ""-d"" -- ""${cur}"") )
            fi
            ;;
        load)
            if [[ ""${cur}"" == -* ]]; then
                COMPREPLY=( $(compgen -W ""-d"" -- ""${cur}"") )
            else
                _do_model_completion
            fi
            ;;
        unload)
            if [[ ""${cur}"" == -* ]]; then
                COMPREPLY=( $(compgen -W ""-d"" -- ""${cur}"") )
            else
                _do_model_completion
            fi
            ;;
        manage)
            if [[ ""${cur}"" == -* ]]; then
                COMPREPLY=( $(compgen -W ""-d"" -- ""${cur}"") )
            fi
            ;;
        qc)
            if [[ ""${cur}"" == -* ]]; then
                COMPREPLY=( $(compgen -W ""-M -D -O -B -Q -T -Te -S -To -Top -R -Fr --force --judge --mode --timeout --verbose --judge-ctxsize --ondemand"" -- ""${cur}"") )
            elif [[ ""${prev}"" == ""-M"" ]]; then
                _do_model_completion
            fi
            ;;
        *)
            ;;
    esac
}

complete -o filenames -o noquote -F _osync_completions osync";
        }

        private string GetPowerShellCompletionScript()
        {
            return @"# osync PowerShell completion - START
# This script is automatically installed by 'osync install'

# Function to get list of models using osync (supports both local and remote)
function Get-OsyncModels {
    param(
        [string]$ServerUrl = """"
    )

    try {
        # Build command arguments
        $args = @(""ls"")
        if ($ServerUrl) {
            $args += @(""-d"", $ServerUrl)
        }

        # Execute osync ls command
        $output = & osync $args 2>$null

        if ($output -and $output.Count -gt 0) {
            # Skip first 3 lines (version line, blank line, header line)
            $models = $output | Select-Object -Skip 3 | ForEach-Object {
                # Extract model name from the line (first column)
                if ($_ -match '^\s*(\S+)') {
                    $matches[1]
                }
            } | Where-Object { $_ }
            return $models
        }
    }
    catch {
        return @()
    }
    return @()
}

# Register argument completer for osync (native executable)
Register-ArgumentCompleter -Native -CommandName osync -ScriptBlock {
    param($wordToComplete, $commandAst, $cursorPosition)

    $commands = @('cp', 'copy', 'ls', 'list', 'rm', 'remove', 'del', 'delete', 'update', 'show', 'run', 'chat', 'ps', 'load', 'unload')

    # Get all words in the command line
    $words = $commandAst.ToString() -split '\s+'

    # If we're completing the first argument (command)
    if ($words.Count -eq 1 -or ($words.Count -eq 2 -and $wordToComplete)) {
        $commands | Where-Object { $_ -like ""$wordToComplete*"" } | ForEach-Object {
            [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_)
        }
        return
    }

    # Get the command (first argument after osync)
    $command = $words[1]

    # Extract remote server URL if present (look for http:// or https://)
    $serverUrl = """"
    foreach ($word in $words) {
        if ($word -match '^https?://') {
            $serverUrl = $word
            break
        }
    }

    # Get models for completion (from local or remote server)
    $models = Get-OsyncModels -ServerUrl $serverUrl

    # Complete based on command
    switch ($command) {
        { $_ -in @('cp', 'copy') } {
            if ($wordToComplete -like '-*') {
                @('-bt', '-BufferSize', '-d') | Where-Object { $_ -like ""$wordToComplete*"" } | ForEach-Object {
                    [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterName', $_)
                }
            }
            else {
                $models | Where-Object { $_ -like ""$wordToComplete*"" } | ForEach-Object {
                    [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', ""Model: $_"")
                }
            }
        }

        { $_ -in @('ls', 'list') } {
            if ($wordToComplete -like '-*') {
                @('--size', '--sizeasc', '--time', '--timeasc', '-d') | Where-Object { $_ -like ""$wordToComplete*"" } | ForEach-Object {
                    [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterName', $_)
                }
            }
            else {
                $models | Where-Object { $_ -like ""$wordToComplete*"" } | ForEach-Object {
                    [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', ""Model: $_"")
                }
            }
        }

        { $_ -in @('rm', 'remove', 'del', 'delete') } {
            if ($wordToComplete -like '-*') {
                @('-d') | Where-Object { $_ -like ""$wordToComplete*"" } | ForEach-Object {
                    [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterName', $_)
                }
            }
            else {
                $models | Where-Object { $_ -like ""$wordToComplete*"" } | ForEach-Object {
                    [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', ""Model: $_"")
                }
            }
        }

        'update' {
            if ($wordToComplete -like '-*') {
                @('-d') | Where-Object { $_ -like ""$wordToComplete*"" } | ForEach-Object {
                    [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterName', $_)
                }
            }
            else {
                $models | Where-Object { $_ -like ""$wordToComplete*"" } | ForEach-Object {
                    [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', ""Model: $_"")
                }
            }
        }

        'show' {
            if ($wordToComplete -like '-*') {
                @('--license', '--modelfile', '--parameters', '--system', '--template', '-v', '--verbose', '-d') |
                    Where-Object { $_ -like ""$wordToComplete*"" } | ForEach-Object {
                    [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterName', $_)
                }
            }
            else {
                $models | Where-Object { $_ -like ""$wordToComplete*"" } | ForEach-Object {
                    [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', ""Model: $_"")
                }
            }
        }

        { $_ -in @('run', 'chat') } {
            if ($wordToComplete -like '-*') {
                @('-d', '--verbose', '--no-wordwrap', '--format', '--keepalive', '--dimensions', '--think', '--hide-thinking', '--insecure', '--truncate') |
                    Where-Object { $_ -like ""$wordToComplete*"" } | ForEach-Object {
                    [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterName', $_)
                }
            }
            else {
                $models | Where-Object { $_ -like ""$wordToComplete*"" } | ForEach-Object {
                    [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', ""Model: $_"")
                }
            }
        }

        'ps' {
            if ($wordToComplete -like '-*') {
                @('-d') | Where-Object { $_ -like ""$wordToComplete*"" } | ForEach-Object {
                    [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterName', $_)
                }
            }
        }

        'load' {
            if ($wordToComplete -like '-*') {
                @('-d') | Where-Object { $_ -like ""$wordToComplete*"" } | ForEach-Object {
                    [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterName', $_)
                }
            }
            else {
                $models | Where-Object { $_ -like ""$wordToComplete*"" } | ForEach-Object {
                    [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', ""Model: $_"")
                }
            }
        }

        'unload' {
            if ($wordToComplete -like '-*') {
                @('-d') | Where-Object { $_ -like ""$wordToComplete*"" } | ForEach-Object {
                    [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterName', $_)
                }
            }
            else {
                $models | Where-Object { $_ -like ""$wordToComplete*"" } | ForEach-Object {
                    [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', ""Model: $_"")
                }
            }
        }

        default {
            # No completion
        }
    }
}

# osync PowerShell completion - END";
        }
    }
    internal class Program
    {
        /// <summary>
        /// Handles shell wildcard expansion for pattern-based commands.
        /// On Linux/Unix, unquoted wildcards like "llama*" get expanded by the shell to matching files.
        /// This method detects that scenario and consolidates to a single pattern with a warning.
        /// </summary>
        static string[] HandleShellExpansion(string[] args)
        {
            if (args.Length < 3) return args;

            // Commands that accept wildcard patterns
            string[] patternCommands = { "ls", "list", "rm", "remove", "del", "delete", "update" };
            string command = args[0].ToLower();

            if (!patternCommands.Contains(command)) return args;

            // Collect all positional (non-flag) arguments after the command
            var positionalArgs = new List<(int index, string value)>();
            var flagsRequiringValue = new HashSet<string> { "-d", "-bt", "-buffersize" };

            for (int i = 1; i < args.Length; i++)
            {
                string arg = args[i];

                // Skip flags and their values
                if (arg.StartsWith("-"))
                {
                    // If this flag requires a value, skip the next arg too
                    if (flagsRequiringValue.Contains(arg.ToLower()) && i + 1 < args.Length)
                        i++;
                    continue;
                }

                // Skip URLs (destination servers)
                if (arg.StartsWith("http://") || arg.StartsWith("https://"))
                    continue;

                positionalArgs.Add((i, arg));
            }

            // If we have multiple positional args, this might be shell expansion
            if (positionalArgs.Count > 1)
            {
                // Check if they look like shell-expanded local files/directories
                bool looksLikeShellExpansion = positionalArgs.Skip(1).Any(p =>
                    System.IO.File.Exists(p.value) || System.IO.Directory.Exists(p.value) ||
                    !p.value.Contains(":"));  // Model names typically have ":" for tags

                if (looksLikeShellExpansion)
                {
                    // Try to find common prefix for better message
                    string firstArg = positionalArgs[0].value;
                    string commonPrefix = FindCommonPrefix(positionalArgs.Select(p => p.value).ToList());

                    System.Console.ForegroundColor = ConsoleColor.Yellow;
                    System.Console.WriteLine($"Warning: Multiple arguments detected. Shell may have expanded your wildcard pattern.");
                    System.Console.WriteLine($"Tip: Use quotes to prevent shell expansion: osync {command} '{commonPrefix}*'");
                    System.Console.ResetColor();
                    System.Console.WriteLine();
                }

                // Keep only the first positional argument, remove extras
                var newArgs = new List<string>();
                var indicesToRemove = new HashSet<int>(positionalArgs.Skip(1).Select(p => p.index));

                for (int i = 0; i < args.Length; i++)
                {
                    if (!indicesToRemove.Contains(i))
                        newArgs.Add(args[i]);
                }

                return newArgs.ToArray();
            }

            return args;
        }

        /// <summary>
        /// Finds the longest common prefix among a list of strings.
        /// </summary>
        static string FindCommonPrefix(List<string> strings)
        {
            if (strings == null || strings.Count == 0) return "";
            if (strings.Count == 1) return strings[0];

            string prefix = strings[0];
            foreach (string s in strings.Skip(1))
            {
                while (!s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && prefix.Length > 0)
                {
                    prefix = prefix.Substring(0, prefix.Length - 1);
                }
                if (prefix.Length == 0) break;
            }
            return prefix;
        }

        static string[] ReorderArguments(string[] args)
        {
            if (args.Length < 2) return args;

            // Commands that have optional positional model/pattern arguments
            string[] commandsWithPositional = { "show", "pull", "ls", "list", "rm", "remove", "del", "delete", "update", "run", "chat", "ps", "load", "unload" };

            string command = args[0];
            if (!commandsWithPositional.Contains(command.ToLower())) return args;

            // Find the first unlabeled argument (doesn't start with - or --)
            int firstUnlabeledIndex = -1;
            for (int i = 1; i < args.Length; i++)
            {
                if (!args[i].StartsWith("-") && !args[i].StartsWith("http"))
                {
                    // Skip if it's a value for a previous flag
                    if (i > 1 && (args[i - 1] == "-d" || args[i - 1] == "-bt" || args[i - 1] == "-BufferSize" ||
                                  args[i - 1] == "--format" || args[i - 1] == "--keepalive" ||
                                  args[i - 1] == "--dimensions" || args[i - 1] == "--think"))
                        continue;

                    firstUnlabeledIndex = i;
                    break;
                }
            }

            // If we found an unlabeled argument that's not at position 1, move it
            if (firstUnlabeledIndex > 1)
            {
                var reordered = new List<string> { args[0], args[firstUnlabeledIndex] };
                for (int i = 1; i < args.Length; i++)
                {
                    if (i != firstUnlabeledIndex)
                        reordered.Add(args[i]);
                }
                return reordered.ToArray();
            }

            return args;
        }

        /// <summary>
        /// Configure terminal capabilities for Spectre.Console and other console libraries.
        /// This fixes hangs with xterm-256color and other 256-color terminal types in SSH/tmux.
        /// The issue is that some console libraries try to query terminal capabilities using
        /// escape sequences, which can hang when the terminal doesn't respond properly.
        /// </summary>
        static void ConfigureTerminalCapabilities()
        {
            try
            {
                // Get terminal info early
                var term = System.Environment.GetEnvironmentVariable("TERM") ?? "";
                var colorTerm = System.Environment.GetEnvironmentVariable("COLORTERM") ?? "";
                var sshConnection = System.Environment.GetEnvironmentVariable("SSH_CONNECTION");
                var tmux = System.Environment.GetEnvironmentVariable("TMUX");
                var sshTty = System.Environment.GetEnvironmentVariable("SSH_TTY");

                // Check for OSYNC_COLOR_MODE environment variable for user override
                var colorModeOverride = System.Environment.GetEnvironmentVariable("OSYNC_COLOR_MODE");

                // On Windows, console libraries usually work fine - let them auto-detect
                if (OperatingSystem.IsWindows())
                {
                    if (!string.IsNullOrEmpty(colorModeOverride))
                    {
                        ApplyColorModeOverride(colorModeOverride);
                    }
                    return;
                }

                // Check for known problematic environments (SSH, tmux, screen)
                bool isRemoteSession = !string.IsNullOrEmpty(sshConnection) || !string.IsNullOrEmpty(sshTty);
                bool isTmux = !string.IsNullOrEmpty(tmux);
                bool isScreen = term.StartsWith("screen", StringComparison.OrdinalIgnoreCase);

                // Detect 256-color TERM that causes hangs
                bool has256ColorTerm = term.Contains("256color", StringComparison.OrdinalIgnoreCase) ||
                                       term.Contains("256-color", StringComparison.OrdinalIgnoreCase);

                // The problematic case: 256-color terminal over SSH/tmux
                // Some console libraries try to query the terminal for capabilities using
                // escape sequences like DA1/DA2/XTVERSION, which can hang if the terminal
                // doesn't respond or the response isn't received properly over SSH.
                if ((isRemoteSession || isTmux || isScreen) && has256ColorTerm)
                {
                    // Downgrade TERM to avoid the problematic capability detection
                    // Keep color support but use a simpler terminal type
                    System.Environment.SetEnvironmentVariable("TERM", "xterm");

                    // Set COLORTERM to indicate we support colors (some apps check this)
                    if (string.IsNullOrEmpty(colorTerm))
                    {
                        System.Environment.SetEnvironmentVariable("COLORTERM", "256");
                    }
                }

                // Apply user override if specified
                if (!string.IsNullOrEmpty(colorModeOverride))
                {
                    ApplyColorModeOverride(colorModeOverride);
                    return;
                }

                // Configure Spectre.Console based on environment
                // Detect true color support from COLORTERM
                bool hasTrueColor = colorTerm.Equals("truecolor", StringComparison.OrdinalIgnoreCase) ||
                                   colorTerm.Equals("24bit", StringComparison.OrdinalIgnoreCase);

                if (isRemoteSession || isTmux || isScreen)
                {
                    if (hasTrueColor)
                    {
                        AnsiConsole.Profile.Capabilities.ColorSystem = ColorSystem.TrueColor;
                    }
                    else if (has256ColorTerm || colorTerm == "256")
                    {
                        AnsiConsole.Profile.Capabilities.ColorSystem = ColorSystem.EightBit;
                    }
                    else
                    {
                        AnsiConsole.Profile.Capabilities.ColorSystem = ColorSystem.Standard;
                    }

                    // Disable interactive features that might cause issues
                    AnsiConsole.Profile.Capabilities.Interactive = System.Console.IsInputRedirected == false;
                }

                // Handle dumb terminals
                if (term.Equals("dumb", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(term))
                {
                    AnsiConsole.Profile.Capabilities.ColorSystem = ColorSystem.NoColors;
                    AnsiConsole.Profile.Capabilities.Unicode = false;
                }

                // Disable Unicode on Linux console (not xterm/screen)
                if (term.Equals("linux", StringComparison.OrdinalIgnoreCase))
                {
                    AnsiConsole.Profile.Capabilities.Unicode = false;
                }
            }
            catch
            {
                // If anything fails during terminal configuration, continue with defaults
            }
        }

        /// <summary>
        /// Apply user-specified color mode override
        /// </summary>
        static void ApplyColorModeOverride(string colorModeOverride)
        {
            ColorSystem? colorSystem = colorModeOverride.ToLowerInvariant() switch
            {
                "none" => ColorSystem.NoColors,
                "0" => ColorSystem.NoColors,
                "legacy" => ColorSystem.Legacy,
                "8" => ColorSystem.Legacy,
                "standard" => ColorSystem.Standard,
                "16" => ColorSystem.Standard,
                "256" => ColorSystem.EightBit,
                "eightbit" => ColorSystem.EightBit,
                "true" => ColorSystem.TrueColor,
                "truecolor" => ColorSystem.TrueColor,
                "24bit" => ColorSystem.TrueColor,
                _ => null
            };

            if (colorSystem.HasValue)
            {
                AnsiConsole.Profile.Capabilities.ColorSystem = colorSystem.Value;
            }
        }

        /// <summary>
        /// Print a short help message showing only global options and available commands
        /// </summary>
        static void PrintShortHelp()
        {
            System.Console.WriteLine("Usage: osync <command> [options]");
            System.Console.WriteLine("");
            System.Console.WriteLine("Global Options:");
            System.Console.WriteLine("  -h, -?           Show this help");
            System.Console.WriteLine("  -bt <value>      Bandwidth throttling (B, KB, MB, GB)");
            System.Console.WriteLine("");
            System.Console.WriteLine("Commands:");
            System.Console.WriteLine("  copy, cp         Copy a model locally or to a remote server");
            System.Console.WriteLine("  list, ls         List models on a local or remote server");
            System.Console.WriteLine("  remove, rm       Remove models matching pattern");
            System.Console.WriteLine("  rename, mv       Rename a model (copy + delete)");
            System.Console.WriteLine("  update           Update models to latest versions");
            System.Console.WriteLine("  pull             Pull (download) a model from registry");
            System.Console.WriteLine("  show             Show information about a model");
            System.Console.WriteLine("  run, chat        Run an interactive chat session");
            System.Console.WriteLine("  ps               Show running models and their status");
            System.Console.WriteLine("  psmonitor        Real-time system and GPU monitoring");
            System.Console.WriteLine("  load             Load a model into memory");
            System.Console.WriteLine("  unload           Unload a model from memory");
            System.Console.WriteLine("  manage           Interactive TUI for model management");
            System.Console.WriteLine("  qc               Run quantization comparison tests");
            System.Console.WriteLine("  qcview           View quantization comparison results");
            System.Console.WriteLine("  bench            Run context length benchmarks");
            System.Console.WriteLine("  benchview        View benchmark results");
            System.Console.WriteLine("  install          Install osync to user directory");
            System.Console.WriteLine("  showversion, -v  Show version information");
            System.Console.WriteLine("");
            System.Console.WriteLine("Use 'osync <command> -h' for detailed help on a specific command.");
        }

        static void Main(string[] args)
        {
            try
            {
                // Configure terminal/console capabilities early, before any Spectre.Console output
                ConfigureTerminalCapabilities();

                // Skip startup banner for version command to avoid duplicate output
                bool isVersionCommand = args.Length > 0 &&
                    (args[0].Equals("showversion", StringComparison.OrdinalIgnoreCase) ||
                     args[0].Equals("-v", StringComparison.OrdinalIgnoreCase) ||
                     args[0].Equals("version", StringComparison.OrdinalIgnoreCase));

                if (!isVersionCommand)
                {
                    Console.WriteLine($"osync {OsyncProgram.GetFullVersion()}");
                    System.Console.WriteLine();
                }
                System.Console.ResetColor();

                // Handle shell wildcard expansion (Linux/macOS expand unquoted wildcards)
                args = HandleShellExpansion(args);
                args = ReorderArguments(args);

                // Check for global help request (osync -h without a command)
                if (args.Length == 1 && (args[0] == "-h" || args[0] == "-?" || args[0] == "--help"))
                {
                    PrintShortHelp();
                    return;
                }

                if (args.Length == 0)
                {
                    OsyncProgram.isInteractiveMode = true;
                }
                else
                {
                    OsyncProgram.isInteractiveMode = false;
                }

                Args.InvokeAction<OsyncProgram>(args);
            }
            catch (ArgException ex)
            {
                Console.WriteLine(ex.Message);
                System.Console.WriteLine();

                // Check if this is an unknown command error
                if (ex.Message.StartsWith("Unknown command:", StringComparison.OrdinalIgnoreCase))
                {
                    // Show compact global help for unknown commands
                    PrintShortHelp();
                }
                else if (args.Length > 0)
                {
                    // For known commands with bad arguments, show command-specific help
                    var commandName = args[0];
                    try
                    {
                        Args.InvokeAction<OsyncProgram>(new[] { commandName, "-h" });
                    }
                    catch
                    {
                        // If showing command help also fails, fall back to generic message
                        System.Console.WriteLine("Use 'osync -h' or 'osync -?' for usage information.");
                    }
                }
                else
                {
                    System.Console.WriteLine("Use 'osync -h' or 'osync -?' for usage information.");
                }
                System.Environment.Exit(1);
            }
            finally
            {
                // Reset ANSI escape sequences to prevent color bleed to terminal
                // This is especially important on Linux/macOS where Spectre.Console
                // may leave the terminal in a colored state
                // Only write raw ANSI on non-Windows - Windows CMD displays it as literal characters
                if (!OperatingSystem.IsWindows())
                {
                    System.Console.Write("\x1b[0m");
                }
                System.Console.ResetColor();

                // Restore original TERM environment variable on exit
                TerminalInitializer.RestoreTerm();
            }
        }
    }

    public static class Tools
    {
        public static bool IsNumeric(this string text) { long _out; return long.TryParse(text, out _out); }
    }

    public class LocalModelsTabCompletionSource : ITabCompletionSource
    {
        string[] models;
        private static int lastDisplayedLines = 0;

        public LocalModelsTabCompletionSource()
        {
            string model_re = "^(?<modelname>\\S*).*";
            var Modelsbuild = new StringBuilder();
            var p = new Process();
            p.StartInfo.FileName = "ollama";
            p.StartInfo.Arguments = @" ls";
            p.StartInfo.CreateNoWindow = true;
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            p.StartInfo.RedirectStandardError = false;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.RedirectStandardInput = false;
            p.OutputDataReceived += (a, b) =>
            {
                if (b != null && b.Data != null)
                {
                    if (!b.Data.StartsWith("failed to get console mode") &&
                        !b.Data.StartsWith("NAME"))
                    {
                        Match m = Regex.Match(b.Data, model_re, RegexOptions.IgnoreCase);
                        if (m.Groups["modelname"].Success)
                            Modelsbuild.AppendLine(m.Groups["modelname"].Value.ToString().Trim());
                    }
                }
            };
            try
            {
                p.Start();
                p.BeginOutputReadLine();
                p.WaitForExit();
            }
            catch
            {
                models = new string[] { "" };
            }

            models = Modelsbuild.ToString().Split('\n');
            Modelsbuild.Clear();
            Modelsbuild = null;
        }

        public static void ClearPreviousOptions()
        {
            if (lastDisplayedLines > 0)
            {
                try
                {
                    // The options are displayed BELOW the current line, so clear lines down from here
                    int currentLeft = System.Console.CursorLeft;
                    int currentTop = System.Console.CursorTop;

                    // Move down to the next line (where options start)
                    System.Console.SetCursorPosition(0, currentTop + 1);

                    // Clear each line of options
                    for (int i = 0; i < lastDisplayedLines; i++)
                    {
                        System.Console.Write(new string(' ', System.Console.WindowWidth));
                        if (i < lastDisplayedLines - 1)
                        {
                            System.Console.SetCursorPosition(0, System.Console.CursorTop + 1);
                        }
                    }

                    // Move cursor back to the original prompt position
                    System.Console.SetCursorPosition(currentLeft, currentTop);

                    lastDisplayedLines = 0;
                }
                catch
                {
                    // If clearing fails, just reset the counter
                    lastDisplayedLines = 0;
                }
            }
        }

        // Commands and flags that expect file arguments instead of model names
        private static readonly HashSet<string> FileArgumentCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "qcview",           // qcview command takes a file as first argument
            "-F", "/F",         // qcview -F flag
            "-T",               // qc -T (test suite file)
            "-O",               // qc -O (output file), qcview -O (output file)
            "--testsuite",      // qc --testsuite
            "--output"          // output file flags
        };

        public bool TryComplete(TabCompletionContext context, out string completion)
        {
            try
            {
                string candidate = context.CompletionCandidate ?? "";
                string previousToken = context.PreviousToken ?? "";

                // Check if the previous token indicates we should do file completion
                bool isFileCommand = FileArgumentCommands.Contains(previousToken);

                // Check if this looks like a file path (contains path separators or file extensions)
                bool looksLikeFilePath = candidate.Contains('/') || candidate.Contains('\\') ||
                                         candidate.Contains('.') || candidate.StartsWith("~");

                // If we're in a file command context, ONLY do file completion (skip model completion entirely)
                if (isFileCommand)
                {
                    return TryCompleteFile(candidate, out completion);
                }

                // If it looks like a file path, try file completion first
                if (looksLikeFilePath && TryCompleteFile(candidate, out completion))
                {
                    return true;
                }

                // Try to find matches starting with the candidate
                var match = from m in models where m.StartsWith(candidate, StringComparison.OrdinalIgnoreCase) select m;

                // If no matches, try alternative matching strategies
                if (match.Count() == 0)
                {
                    if (!candidate.Contains(":"))
                    {
                        // Case 1: candidate has no colon - try matching the part after ':' in model names
                        // This helps with Linux readline which treats ':' as a word boundary
                        match = from m in models
                                where m.Contains(":") &&
                                      m.Substring(m.IndexOf(':') + 1).StartsWith(candidate, StringComparison.OrdinalIgnoreCase)
                                select m;
                    }
                    else if (candidate.EndsWith(":"))
                    {
                        // Case 2: candidate ends with ':' (e.g., "qwen3:") - show all tags for that model
                        // This happens when user types model name and colon, wanting to see available tags
                        match = from m in models where m.StartsWith(candidate, StringComparison.OrdinalIgnoreCase) select m;
                    }
                }

                if (match.Count() == 1)
                {
                    // Clear options when completing to a single match
                    ClearPreviousOptions();
                    completion = match.Single().ToString().Trim();
                    return true;
                }
                else if (match.Count() > 1)
                {
                    // Find common prefix
                    var minimumLength = match.Min(x => x.Length);
                    int commonChars;
                    for (commonChars = 0; commonChars < minimumLength; commonChars++)
                    {
                        if (match.Select(x => x[commonChars]).Distinct().Count() > 1)
                        {
                            break;
                        }
                    }

                    string commonPrefix = match.First().ToString().Trim().Substring(0, commonChars);

                    // If user has typed to the common prefix, show all available options
                    if (commonChars == candidate.Length)
                    {
                        // Clear any previous options before displaying new ones
                        ClearPreviousOptions();

                        // Display all available options
                        System.Console.WriteLine("");
                        var sortedMatches = match.OrderBy(x => x).ToList();

                        foreach (var item in sortedMatches)
                        {
                            System.Console.WriteLine("  " + item);
                        }
                        System.Console.WriteLine("");

                        // Track how many lines we displayed (1 blank + items + 1 blank)
                        lastDisplayedLines = sortedMatches.Count + 2;

                        // Return the common prefix, PowerArgs will redraw the prompt below the options
                        completion = commonPrefix;
                        return true;
                    }
                    else
                    {
                        // Clear options when completing further (not showing new ones)
                        if (lastDisplayedLines > 0)
                        {
                            ClearPreviousOptions();
                        }
                    }

                    if (match != null && match.Any())
                    {
                        completion = commonPrefix;
                        return true;
                    }
                    else
                    {
                        completion = "";
                        return false;
                    }
                }
                else
                {
                    // No model matches - try file completion as fallback
                    if (TryCompleteFile(candidate, out completion))
                    {
                        return true;
                    }
                    completion = "";
                    return false;
                }
            }
            catch
            {
                completion = "";
                return false;
            }
        }

        /// <summary>
        /// Try to complete a file path from the file system
        /// </summary>
        private bool TryCompleteFile(string candidate, out string completion)
        {
            completion = "";
            try
            {
                // Track if original candidate was empty (for path formatting)
                bool wasEmpty = string.IsNullOrEmpty(candidate);
                string originalCandidate = candidate;

                if (wasEmpty)
                {
                    // Empty candidate - list files in current directory
                    candidate = ".";
                }

                // Expand ~ to home directory on Linux/macOS
                if (candidate.StartsWith("~"))
                {
                    string home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
                    candidate = home + candidate.Substring(1);
                }

                string directory;
                string filePrefix;

                // Determine directory and file prefix
                if (Directory.Exists(candidate) && !wasEmpty)
                {
                    // Candidate is a directory - list its contents
                    directory = candidate;
                    filePrefix = "";
                }
                else if (wasEmpty)
                {
                    // Empty candidate - use current directory, return just filenames
                    directory = ".";
                    filePrefix = "";
                }
                else
                {
                    // Candidate might be a partial path
                    directory = Path.GetDirectoryName(candidate) ?? ".";
                    filePrefix = Path.GetFileName(candidate);

                    if (string.IsNullOrEmpty(directory))
                        directory = ".";
                }

                if (!Directory.Exists(directory))
                {
                    return false;
                }

                // Get matching files and directories
                var entries = new List<string>();

                // Add directories (with trailing separator)
                foreach (var dir in Directory.GetDirectories(directory))
                {
                    string name = Path.GetFileName(dir);
                    if (string.IsNullOrEmpty(filePrefix) || name.StartsWith(filePrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        // If original was empty, return just the name; otherwise include path
                        string entryPath = wasEmpty ? name : Path.Combine(directory, name);
                        // Add trailing separator for directories
                        entries.Add(entryPath + Path.DirectorySeparatorChar);
                    }
                }

                // Add files
                foreach (var file in Directory.GetFiles(directory))
                {
                    string name = Path.GetFileName(file);
                    if (string.IsNullOrEmpty(filePrefix) || name.StartsWith(filePrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        // If original was empty, return just the name; otherwise include path
                        string entryPath = wasEmpty ? name : Path.Combine(directory, name);
                        entries.Add(entryPath);
                    }
                }

                if (entries.Count == 0)
                {
                    return false;
                }
                else if (entries.Count == 1)
                {
                    ClearPreviousOptions();
                    completion = entries[0];
                    return true;
                }
                else
                {
                    var sortedEntries = entries.OrderBy(x => x).ToList();

                    // Multiple matches - find common prefix
                    var minimumLength = sortedEntries.Min(x => x.Length);
                    int commonChars;
                    for (commonChars = 0; commonChars < minimumLength; commonChars++)
                    {
                        if (sortedEntries.Select(x => x[commonChars]).Distinct().Count() > 1)
                        {
                            break;
                        }
                    }

                    string commonPrefix = sortedEntries[0].Substring(0, commonChars);

                    // If candidate matches common prefix or is empty, show all options
                    if (commonChars == originalCandidate.Length || originalCandidate.Length < commonChars || wasEmpty)
                    {
                        ClearPreviousOptions();
                        System.Console.WriteLine("");
                        foreach (var entry in sortedEntries)
                        {
                            // Show just the filename for cleaner display
                            System.Console.WriteLine("  " + Path.GetFileName(entry.TrimEnd(Path.DirectorySeparatorChar)) +
                                (entry.EndsWith(Path.DirectorySeparatorChar.ToString()) ? "/" : ""));
                        }
                        System.Console.WriteLine("");
                        lastDisplayedLines = sortedEntries.Count + 2;
                    }

                    completion = commonPrefix;
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }
    }

    public static class ManifestReader
    {
        public static T? Read<T>(string filePath)
        {
            try
            {
                string text = System.IO.File.ReadAllText(filePath);
                return JsonSerializer.Deserialize<T>(text);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error parsing the manifest file: " + ex.Message);
                return default;
            }
        }
    }
    public static class StatusReader
    {
        public static T? Read<T>(string statusline)
        {
            try
            {
                return JsonSerializer.Deserialize<T>(statusline);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error parsing the status line: " + ex.Message);
                return default;
            }
        }
    }
    public static class VersionReader
    {
        public static T? Read<T>(string versionline)
        {
            try
            {
                return JsonSerializer.Deserialize<T>(versionline);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error parsing the version: " + ex.Message);
                return default;
            }
        }
    }

}
