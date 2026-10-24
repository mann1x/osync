using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;

namespace osync;

/// <summary>
/// Spectre.Console-based monitoring UI for osync
/// Inspired by nvitop
/// </summary>
public partial class PsMonitorUI
{
    // Configuration
    private readonly string? _destination;
    private int _refreshIntervalSeconds;
    private readonly int _initialRefreshInterval;
    private int _historyMinutes;
    private readonly int _maxHistoryMinutes = 60;
    private readonly int _minHistoryMinutes = 1;

    // State
    private volatile bool _isPaused;
    private volatile bool _isRunning;
    private volatile bool _needsRedraw = true;
    private int _lastWidth;
    private int _lastHeight;

    // Collectors
    // The server's API is read the same way for every server (RemoteServerMonitor). A server on this machine that
    // sends no system telemetry gets its host, process and GPU readings from the local collectors instead.
    private readonly RemoteServerMonitor _remoteMonitor;
    private GpuMonitor? _gpuMonitor;  // created on first use: an xOllama with system telemetry never needs it
    private readonly SystemMetricsCollector? _systemMetrics;

    // Data
    private List<RunningModel>? _loadedModels;
    private string _ollamaHost = "http://localhost:11434";
    private string _ollamaVersion = "";
    private bool _ollamaConnected;

    // History for graphs using BrailleGraph
    private readonly Dictionary<string, BrailleGraph> _graphs = new();
    private const int DefaultGraphHeightChars = 5;  // 5 chars = 20 pixels tall (braille is 4 pixels per char)
    private const int MinGraphHeightChars = 1;      // Absolute minimum graph height (allows fitting in small consoles)
    private int _currentGraphHeight = DefaultGraphHeightChars;  // Dynamically adjusted based on console height
    private const int MaxStoredDataPoints = 3600;  // Store up to 1 hour at 1s refresh (max history)


    public PsMonitorUI(string? destination, int refreshIntervalSeconds, int historyMinutes)
    {
        _destination = destination;
        _refreshIntervalSeconds = Math.Max(1, Math.Min(300, refreshIntervalSeconds));
        _initialRefreshInterval = _refreshIntervalSeconds;
        _historyMinutes = Math.Clamp(historyMinutes, _minHistoryMinutes, _maxHistoryMinutes);

        // Resolve the server - same logic as Ps command
        _ollamaHost = OllamaServer.ResolveHost(destination);

        // OSYNC_PSMONITOR_REMOTE=1 shows a server on this machine as a remote one (only what its API reports)
        _remote = Environment.GetEnvironmentVariable("OSYNC_PSMONITOR_REMOTE") == "1" ||
                  !RemoteServerMonitor.IsThisMachine(_ollamaHost);
        _remoteMonitor = new RemoteServerMonitor(_ollamaHost, XOllamaTweak.ApiKey());
        if (!_remote)
        {
            _systemMetrics = new SystemMetricsCollector();
            _remoteGraphMode = RemoteGraphMode.GpuUtil;  // as before on this machine; the VRAM graph without a GPU
        }
        InitRemoteGraphs();
    }

    public void Run()
    {
        _isRunning = true;

        // Hide cursor using both Console API and ANSI escape sequence
        Console.CursorVisible = false;
        Console.Write("\x1b[?25l");  // ANSI: hide cursor

        // Enable raw input mode for better keyboard handling on Windows
        Console.TreatControlCAsInput = true;

        // Try to disable console scrolling by setting buffer = window size (Windows only)
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Console.SetBufferSize(Console.WindowWidth, Console.WindowHeight);
            }
        }
        catch { }

        // Initialize size tracking
        _lastWidth = Console.WindowWidth;
        _lastHeight = Console.WindowHeight;

        // Clear screen fully
        FullScreenClear();

        // Do initial data collection synchronously so we have data to show
        CollectDataAsync().Wait();

        // Start data collection in separate thread
        var dataThread = new Thread(DataCollectionThread)
        {
            IsBackground = true
        };
        dataThread.Start();

        try
        {
            var lastRender = DateTime.MinValue;
            var renderInterval = TimeSpan.FromMilliseconds(200); // 5fps render

            // Main loop handles both keyboard and rendering
            while (_isRunning)
            {
                // Check keyboard with non-blocking approach
                while (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true);
                    HandleKey(key);
                }

                // Check for resize - compare current size with last known size
                int currentWidth = Console.WindowWidth;
                int currentHeight = Console.WindowHeight;
                if (currentWidth != _lastWidth || currentHeight != _lastHeight)
                {
                    _lastWidth = currentWidth;
                    _lastHeight = currentHeight;

                    // Disable scrolling for new size (Windows only)
                    try
                    {
                        if (OperatingSystem.IsWindows())
                        {
                            Console.SetBufferSize(currentWidth, currentHeight);
                        }
                    }
                    catch { }

                    // Force full redraw
                    FullScreenClear();
                    _needsRedraw = true;
                }

                // Render if needed (throttled)
                var now = DateTime.UtcNow;
                if (_needsRedraw && (now - lastRender) > renderInterval)
                {
                    RenderDisplay();
                    _needsRedraw = false;
                    lastRender = now;
                }

                // Short sleep to prevent CPU spinning while staying responsive
                Thread.Sleep(16); // ~60fps check rate for keyboard
            }
        }
        finally
        {
            _isRunning = false;
            Console.Write("\x1b[?25h");  // ANSI: show cursor
            Console.CursorVisible = true;
            Console.TreatControlCAsInput = false;
            Console.Clear();
            _systemMetrics?.Dispose();
        }
    }

    private void FullScreenClear()
    {
        try
        {
            Console.Clear();
            Console.SetCursorPosition(0, 0);
        }
        catch { }
    }

    private void HandleKey(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.Q:
            case ConsoleKey.Escape:
                _isRunning = false;
                break;

            case ConsoleKey.C when key.Modifiers.HasFlag(ConsoleModifiers.Control):
                _isRunning = false;
                break;

            case ConsoleKey.P:
                _isPaused = !_isPaused;
                _needsRedraw = true;
                break;

            case ConsoleKey.H:
                ShowHelp();
                _needsRedraw = true;
                break;

            case ConsoleKey.LeftArrow:
                if (_historyMinutes > _minHistoryMinutes)
                {
                    _historyMinutes--;
                    _needsRedraw = true;
                }
                break;

            case ConsoleKey.RightArrow:
                if (_historyMinutes < _maxHistoryMinutes)
                {
                    _historyMinutes++;
                    _needsRedraw = true;
                }
                break;

            case ConsoleKey.UpArrow:
                _refreshIntervalSeconds = Math.Min(300, _refreshIntervalSeconds + 1);
                _needsRedraw = true;
                break;

            case ConsoleKey.DownArrow:
                _refreshIntervalSeconds = Math.Max(1, _refreshIntervalSeconds - 1);
                _needsRedraw = true;
                break;

            case ConsoleKey.O:
                CycleSecondGraph();
                _needsRedraw = true;
                break;

            case ConsoleKey.I:
                CycleRemoteGraph();
                _needsRedraw = true;
                break;

            case ConsoleKey.R:
                _remoteMonitor.RefreshDevicesSoon();
                break;

            case ConsoleKey.G:
                CycleRemoteGpu();
                _needsRedraw = true;
                break;
        }
    }

    private void DataCollectionThread()
    {
        var lastCollection = DateTime.UtcNow;
        while (_isRunning)
        {
            var now = DateTime.UtcNow;
            var elapsed = (now - lastCollection).TotalSeconds;

            if (!_isPaused && elapsed >= _refreshIntervalSeconds)
            {
                CollectDataAsync().Wait();
                _needsRedraw = true;
                lastCollection = now;
            }

            Thread.Sleep(100); // Check every 100ms for pause/interval changes
        }
    }

    private void ShowHelp()
    {
        Console.Clear();
        AnsiConsole.Write(new Panel(
            new Markup(
                "[bold cyan]osync Monitor - Keyboard Shortcuts[/]\n\n" +
                "[yellow]Q/Esc[/]        Quit monitor\n" +
                "[yellow]P[/]            Pause/Resume updates\n" +
                "[yellow]H[/]            Show this help\n" +
                "[yellow]Left/Right[/]   Adjust graph history (+/- 1 min)\n" +
                "[yellow]Up/Down[/]      Adjust refresh interval\n" +
                "[yellow]I[/]            Cycle the graph (VRAM, RAM, tok/s, busy slots, KV window in host RAM;\n" +
                "             GPU, host and server graphs when they are known)\n" +
                "[yellow]G[/]            Pick the GPU the GPU graphs show\n" +
                "[yellow]O[/]            Show or hide the second graph (server CPU and memory)\n" +
                "[yellow]R[/]            Re-read the server's GPU list (xOllama without telemetry)\n\n" +
                (_remote
                    ? "[dim]Remote server: only what its API reports is shown, nothing from this machine.[/]\n\n"
                    : "[dim]Server on this machine: its API, plus this machine's readings where the server sends none.[/]\n\n") +
                "[dim]Press any key to continue...[/]"
            ))
            .Header("[bold]Help[/]")
            .BorderColor(Color.Cyan1));

        Console.ReadKey(true);
        Console.Clear();
    }

    private Task CollectDataAsync() => CollectServerDataAsync();

    /// <summary>
    /// Calculates expected data points for the full history based on current settings.
    /// </summary>
    private int GetExpectedDataPoints()
    {
        // Expected data points = history duration / refresh interval
        return (_historyMinutes * 60) / _refreshIntervalSeconds;
    }

    private void RenderDisplay()
    {
        // Up to the last column but one: a full line would wrap on some consoles
        RenderFrame(Console.WindowWidth - 1, Console.WindowHeight);
    }

    private void ShowError(string message)
    {
        try
        {
            Console.SetCursorPosition(0, Console.WindowHeight - 3);
            Console.Write($"Render error: {message}".PadRight(Console.WindowWidth - 1));
        }
        catch { }
    }

    private string RenderTimeAxisString(int width)
    {
        // Format: "5m----|----4m|----3m|----2m|----1m|----"
        // 5 segments for history duration
        // Labels at start of each segment, pipe at end (except last segment)
        var sb = new StringBuilder();
        sb.Append("[dim]");

        var segments = 5;  // 5 intervals for history
        var segmentWidth = width / segments;
        var remainder = width % segments;

        // Calculate total history in seconds
        var totalSeconds = _historyMinutes * 60;
        var secondsPerSegment = totalSeconds / segments;

        // Determine if we need to use seconds (when minute labels would repeat)
        // If history < 5 minutes, some minute labels will repeat
        var useSeconds = _historyMinutes < 5;

        var currentPos = 0;
        for (int seg = 0; seg < segments; seg++)
        {
            // Distribute remainder chars across first segments
            var thisSegmentWidth = segmentWidth + (seg < remainder ? 1 : 0);

            // Calculate time for this segment
            var secondsAgo = totalSeconds - (secondsPerSegment * seg);
            string label;

            if (useSeconds)
            {
                // Use seconds format for short history
                if (secondsAgo >= 60)
                {
                    var mins = secondsAgo / 60;
                    var secs = secondsAgo % 60;
                    if (secs == 0)
                        label = $"{mins}m";
                    else
                        label = $"{mins}m{secs}s";
                }
                else
                {
                    label = $"{secondsAgo}s";
                }
            }
            else
            {
                // Use minutes format for longer history
                var minutesAgo = secondsAgo / 60;
                label = $"{minutesAgo}m";
            }

            if (seg == segments - 1)
            {
                // Last segment: "1m----" (label, dashes, no pipe at end)
                sb.Append(label);
                currentPos += label.Length;
                var dashCount = thisSegmentWidth - label.Length;
                sb.Append(new string('-', Math.Max(0, dashCount)));
                currentPos += Math.Max(0, dashCount);
            }
            else
            {
                // Other segments: "Xm----|" (label, dashes, pipe)
                sb.Append(label);
                currentPos += label.Length;
                var dashCount = thisSegmentWidth - label.Length - 1;
                sb.Append(new string('-', Math.Max(0, dashCount)));
                currentPos += Math.Max(0, dashCount);
                sb.Append('|');
                currentPos++;
            }
        }

        // Pad to exact width if needed
        while (currentPos < width)
        {
            sb.Append('-');
            currentPos++;
        }

        sb.Append("[/]");
        return sb.ToString();
    }

    private void RenderStatusBar(int width)
    {
        try
        {
            var statusY = Console.WindowHeight - 1;
            if (statusY > 0)
            {
                Console.SetCursorPosition(0, statusY);

                // Build left part (plain text for length calculation)
                var pauseText = _isPaused ? "PAUSED" : "RUNNING";
                var leftText = $" {pauseText} | Refresh: {_refreshIntervalSeconds}s | History: {_historyMinutes}m | Q=Quit P=Pause H=Help";

                // Right side: date/time (use fixed format to avoid locale issues)
                var now = DateTime.Now;
                var dateStr = now.ToString("ddd MMM dd HH:mm:ss yyyy", System.Globalization.CultureInfo.InvariantCulture);

                // Calculate available space
                var leftLen = leftText.Length;
                var rightLen = dateStr.Length;
                var totalNeeded = leftLen + 2 + rightLen; // 2 for minimum spacing

                // If not enough space, truncate left part
                if (totalNeeded > width)
                {
                    // Use shorter left text
                    leftText = $" {pauseText} | {_refreshIntervalSeconds}s | {_historyMinutes}m";
                    leftLen = leftText.Length;
                    totalNeeded = leftLen + 2 + rightLen;
                }

                // Calculate padding (ensure we don't overflow)
                var padding = Math.Max(1, width - leftLen - rightLen);

                // Write using ANSI codes directly to avoid markup calculation issues
                var pauseColor = _isPaused ? "\x1b[33m" : "\x1b[32m"; // Yellow or Green
                var reset = "\x1b[0m";
                var dim = "\x1b[2m";
                var white = "\x1b[37m";

                // Build and write the line, ensuring it fits
                var sb = new StringBuilder();
                sb.Append($" {pauseColor}{pauseText}{reset}");
                sb.Append($" {dim}|{reset} Refresh: {white}{_refreshIntervalSeconds}s{reset}");
                sb.Append($" {dim}|{reset} History: {white}{_historyMinutes}m{reset}");
                sb.Append($" {dim}|{reset} {dim}Q{reset}=Quit {dim}P{reset}=Pause {dim}H{reset}=Help");

                // Pad and add date - ensure total doesn't exceed width
                var currentLen = leftText.Length;
                var spaceForDate = width - currentLen - 1; // -1 for safety margin

                if (spaceForDate >= rightLen)
                {
                    var spacer = new string(' ', spaceForDate - rightLen);
                    sb.Append(spacer);
                    sb.Append($"{dim}{dateStr}{reset}");
                }

                Console.Write(sb.ToString());
            }
        }
        catch { }
    }

    private string CreateProgressBarString(double percent, int width, string color)
    {
        percent = Math.Clamp(percent, 0, 100);
        var filled = (int)Math.Round(percent / 100.0 * width);
        var empty = width - filled;

        var filledStr = new string('█', filled);
        var emptyStr = new string('░', empty);

        return $"[{color}]{filledStr}[/][dim]{emptyStr}[/]";
    }

    // Helper methods
    private static string TruncateString(string str, int maxLength)
    {
        if (string.IsNullOrEmpty(str) || str.Length <= maxLength)
            return str ?? "";
        // Truncate from beginning, keep end (model name is more relevant)
        return ".." + str.Substring(str.Length - maxLength + 2);
    }

    private static string GetShortDigest(string? digest)
    {
        if (string.IsNullOrEmpty(digest)) return "N/A";
        var cleanDigest = digest.Replace("sha256:", "");
        return cleanDigest.Length > 12 ? cleanDigest[..12] : cleanDigest;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "N/A";
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:F1} {sizes[order]}";
    }

    private static string FormatUntil(DateTime? expiresAt)
    {
        if (!expiresAt.HasValue) return "N/A";

        // Ollama returns local time with timezone, so compare with local time
        var diff = expiresAt.Value - DateTime.Now;
        if (diff.TotalSeconds < 0) return "expired";
        if (diff.TotalMinutes < 1) return $"{diff.Seconds}s";
        if (diff.TotalHours < 1) return $"{diff.Minutes}m {diff.Seconds}s";
        return $"{(int)diff.TotalHours}h {diff.Minutes}m";
    }
}
