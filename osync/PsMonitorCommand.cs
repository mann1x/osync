using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;

namespace osync;

/// <summary>
/// GPU display modes for the utilization graph panel
/// </summary>
public enum GpuDisplayMode
{
    Utilization,  // Default: GPU compute utilization
    Power,        // Power draw in watts
    Temperature,  // Temperature in Celsius
    Vram,         // VRAM utilization percentage
    Clock,        // Clock speed percentage of max
    Fan           // Fan speed percentage
}

/// <summary>
/// Spectre.Console-based monitoring UI for osync
/// Inspired by nvitop
/// </summary>
public class PsMonitorUI
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
    private volatile bool _showOllamaMemGraph;  // Toggle: false=CPU graph, true=MEM graph
    private GpuDisplayMode _gpuDisplayMode = GpuDisplayMode.Utilization;  // GPU graph display mode
    private int _lastWidth;
    private int _lastHeight;

    // Section height tracking for clean redraws
    private int _lastGpuSectionEndLine;
    private int _lastSystemSectionEndLine;
    private int _lastOllamaSectionEndLine;

    // Collectors
    private readonly GpuMonitor _gpuMonitor = new();
    private readonly SystemMetricsCollector _systemMetrics = new();
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(10) };

    // Data
    private List<GpuMetrics> _gpuData = new();
    private SystemMetricsData? _systemData;
    private OllamaProcessMetrics? _ollamaData;
    private List<RunningModel>? _loadedModels;
    private string _ollamaHost = "http://localhost:11434";
    private string _ollamaVersion = "";
    private bool _ollamaConnected;

    // History for graphs using BrailleGraph
    private readonly Dictionary<string, BrailleGraph> _graphs = new();
    private const int DefaultGraphHeightChars = 5;  // 5 chars = 20 pixels tall (braille is 4 pixels per char)
    private const int MinGraphHeightChars = 1;      // Absolute minimum graph height (allows fitting in small consoles)
    private int _currentGraphHeight = DefaultGraphHeightChars;  // Dynamically adjusted based on console height
    private const int GraphWidthChars = 60;  // Initial width - actual render width is dynamic
    private const int MaxStoredDataPoints = 3600;  // Store up to 1 hour at 1s refresh (max history)


    public PsMonitorUI(string? destination, int refreshIntervalSeconds, int historyMinutes)
    {
        _destination = destination;
        _refreshIntervalSeconds = Math.Max(1, Math.Min(300, refreshIntervalSeconds));
        _initialRefreshInterval = _refreshIntervalSeconds;
        _historyMinutes = Math.Clamp(historyMinutes, _minHistoryMinutes, _maxHistoryMinutes);

        // Resolve Ollama host - same logic as Ps command
        _ollamaHost = destination
            ?? Environment.GetEnvironmentVariable("OLLAMA_HOST")
            ?? "http://localhost:11434";

        // If OLLAMA_HOST is 0.0.0.0 (bind address), replace with localhost
        if (_ollamaHost == "0.0.0.0" || _ollamaHost == "0.0.0.0:11434" ||
            _ollamaHost.Contains("://0.0.0.0"))
        {
            _ollamaHost = "http://localhost:11434";
        }

        // Normalize URL
        if (!_ollamaHost.StartsWith("http://") && !_ollamaHost.StartsWith("https://"))
            _ollamaHost = "http://" + _ollamaHost;

        // Ensure port is present
        try
        {
            var uri = new Uri(_ollamaHost);
            if (uri.Port == -1 || uri.Port == 80)
            {
                _ollamaHost = $"{uri.Scheme}://{uri.Host}:11434";
            }
        }
        catch { }

        // Initialize graphs with enough capacity for max history
        // Using MaxStoredDataPoints as width to set internal buffer size
        _graphs["cpu"] = new BrailleGraph(MaxStoredDataPoints / 2, _currentGraphHeight, 0, 100);
        _graphs["mem"] = new BrailleGraph(MaxStoredDataPoints / 2, _currentGraphHeight, 0, 100);
        _graphs["ollama_mem"] = new BrailleGraph(MaxStoredDataPoints / 2, _currentGraphHeight, 0, 100);
    }

    public void Run()
    {
        _isRunning = true;
        Console.OutputEncoding = System.Text.Encoding.UTF8;

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
            _systemMetrics.Dispose();
        }
    }

    private void FullScreenClear()
    {
        try
        {
            Console.Clear();
            Console.SetCursorPosition(0, 0);
            // Reset section tracking since we cleared everything
            _lastGpuSectionEndLine = 0;
            _lastSystemSectionEndLine = 0;
            _lastOllamaSectionEndLine = 0;
        }
        catch { }
    }

    /// <summary>
    /// Clears the current line from current cursor position to end of line
    /// </summary>
    private void ClearToEndOfLine(int width)
    {
        try
        {
            var curX = Console.CursorLeft;
            if (curX < width)
            {
                Console.Write(new string(' ', width - curX));
            }
        }
        catch { }
    }

    /// <summary>
    /// Clears lines from startLine to endLine (exclusive), full width
    /// </summary>
    private void ClearLines(int startLine, int endLine, int width)
    {
        try
        {
            var clearLine = new string(' ', width);
            for (int i = startLine; i < endLine; i++)
            {
                Console.SetCursorPosition(0, i);
                Console.Write(clearLine);
            }
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
                _showOllamaMemGraph = !_showOllamaMemGraph;
                _needsRedraw = true;
                break;

            case ConsoleKey.I:
                // Cycle through GPU display modes
                _gpuDisplayMode = _gpuDisplayMode switch
                {
                    GpuDisplayMode.Utilization => GpuDisplayMode.Power,
                    GpuDisplayMode.Power => GpuDisplayMode.Temperature,
                    GpuDisplayMode.Temperature => GpuDisplayMode.Vram,
                    GpuDisplayMode.Vram => GpuDisplayMode.Clock,
                    GpuDisplayMode.Clock => GpuDisplayMode.Fan,
                    GpuDisplayMode.Fan => GpuDisplayMode.Utilization,
                    _ => GpuDisplayMode.Utilization
                };
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
                "[yellow]Up/Down[/]      Adjust refresh interval\n\n" +
                "[dim]Press any key to continue...[/]"
            ))
            .Header("[bold]Help[/]")
            .BorderColor(Color.Cyan1));

        Console.ReadKey(true);
        Console.Clear();
    }

    private async Task CollectDataAsync()
    {
        try
        {
            // Collect GPU metrics
            _gpuData = await _gpuMonitor.CollectMetricsAsync();

            // Collect system metrics
            _systemData = await _systemMetrics.GetSystemMetricsAsync();
            _ollamaData = _systemMetrics.GetOllamaMetrics();

            // Fetch Ollama info (models)
            await FetchOllamaInfoAsync();

            // Use Ollama API as the canonical source for VRAM usage - it's reliable and cross-platform.
            // D3DKMT/nvidia-smi are unreliable (return partial data or model-only memory).
            if (_gpuData.Count > 0 && _loadedModels != null && _loadedModels.Count > 0)
            {
                var totalOllamaVramFromApi = _loadedModels.Sum(m => m.SizeVram) / (1024.0 * 1024.0); // Convert to MB
                var hasPerProcessGpuUtil = _gpuData.Any(g => g.HasPerProcessGpuUtil);

                // Always use Ollama API for VRAM - it's the canonical source
                if (totalOllamaVramFromApi > 0)
                {
                    // Distribute to first GPU (most common case) or split evenly
                    if (_gpuData.Count == 1)
                    {
                        _gpuData[0].OllamaVramMB = totalOllamaVramFromApi;
                    }
                    else
                    {
                        // For multi-GPU, distribute evenly (not perfect but better than 0)
                        var perGpu = totalOllamaVramFromApi / _gpuData.Count;
                        foreach (var gpu in _gpuData)
                        {
                            gpu.OllamaVramMB = perGpu;
                        }
                    }
                }

                // If GPU provider couldn't get per-process GPU utilization,
                // fall back to total GPU utilization as a rough approximation
                if (!hasPerProcessGpuUtil)
                {
                    foreach (var gpu in _gpuData)
                    {
                        // Use total GPU utilization as fallback (shows system-wide, not Ollama-specific)
                        gpu.OllamaGpuUtilization = gpu.ComputeUtilization;
                        gpu.HasPerProcessGpuUtil = false;
                    }
                }
            }

            // Update GPU graphs for all metrics
            foreach (var gpu in _gpuData)
            {
                // Utilization graph (0-100%)
                var utlKey = $"gpu{gpu.Index}_utl";
                if (!_graphs.ContainsKey(utlKey))
                    _graphs[utlKey] = new BrailleGraph(MaxStoredDataPoints / 2, _currentGraphHeight, 0, 100);
                _graphs[utlKey].AddDataPoint(gpu.OllamaGpuUtilization);

                // Power graph (0-100% of power limit)
                var pwrKey = $"gpu{gpu.Index}_pwr";
                if (!_graphs.ContainsKey(pwrKey))
                    _graphs[pwrKey] = new BrailleGraph(MaxStoredDataPoints / 2, _currentGraphHeight, 0, 100);
                var powerPct = gpu.PowerLimitW > 0 ? (gpu.PowerDrawW / gpu.PowerLimitW) * 100 : 0;
                _graphs[pwrKey].AddDataPoint(powerPct);

                // Temperature graph (0-100°C range, clamped)
                var tmpKey = $"gpu{gpu.Index}_tmp";
                if (!_graphs.ContainsKey(tmpKey))
                    _graphs[tmpKey] = new BrailleGraph(MaxStoredDataPoints / 2, _currentGraphHeight, 0, 100);
                var tempPct = Math.Min(100, gpu.Temperature);  // Temperature as direct value (0-100°C)
                _graphs[tmpKey].AddDataPoint(tempPct);

                // VRAM graph (0-100%)
                var vrmKey = $"gpu{gpu.Index}_vrm";
                if (!_graphs.ContainsKey(vrmKey))
                    _graphs[vrmKey] = new BrailleGraph(MaxStoredDataPoints / 2, _currentGraphHeight, 0, 100);
                _graphs[vrmKey].AddDataPoint(gpu.VramUtilization);

                // Clock graph (0-100% of max clock)
                var clkKey = $"gpu{gpu.Index}_clk";
                if (!_graphs.ContainsKey(clkKey))
                    _graphs[clkKey] = new BrailleGraph(MaxStoredDataPoints / 2, _currentGraphHeight, 0, 100);
                var clockPct = gpu.ClockSpeedMaxMHz > 0 ? (gpu.ClockSpeedMHz * 100.0 / gpu.ClockSpeedMaxMHz) : 0;
                _graphs[clkKey].AddDataPoint(clockPct);

                // Fan graph (0-100%)
                var fanKey = $"gpu{gpu.Index}_fan";
                if (!_graphs.ContainsKey(fanKey))
                    _graphs[fanKey] = new BrailleGraph(MaxStoredDataPoints / 2, _currentGraphHeight, 0, 100);
                _graphs[fanKey].AddDataPoint(gpu.FanSpeedPercent);
            }

            // Update CPU graph - use Ollama CPU if Ollama processes exist, system CPU as fallback
            if (_ollamaData != null && _ollamaData.ProcessCount > 0)
            {
                _graphs["cpu"].AddDataPoint(_ollamaData.CpuUsagePercent);
            }
            else if (_systemData != null)
            {
                // Fallback to system-wide CPU when no Ollama processes found
                _graphs["cpu"].AddDataPoint(_systemData.CpuUsagePercent);
            }

            // Update system memory graph
            if (_systemData != null)
            {
                _graphs["mem"].AddDataPoint(_systemData.RamUsagePercent);
            }

            // Update Ollama memory graph (Ollama's RAM as percentage of total system RAM)
            if (_ollamaData != null && _ollamaData.ProcessCount > 0 && _systemData != null && _systemData.RamTotalBytes > 0)
            {
                var ollamaMemPct = (_ollamaData.PrivateBytes * 100.0) / _systemData.RamTotalBytes;
                _graphs["ollama_mem"].AddDataPoint(ollamaMemPct);
            }
            else
            {
                // No Ollama processes, add 0
                _graphs["ollama_mem"].AddDataPoint(0);
            }
        }
        catch { }
    }

    private BrailleGraph GetOrCreateGraph(string key)
    {
        if (!_graphs.ContainsKey(key))
            _graphs[key] = new BrailleGraph(MaxStoredDataPoints / 2, _currentGraphHeight, 0, 100);
        return _graphs[key];
    }

    /// <summary>
    /// Calculates expected data points for the full history based on current settings.
    /// </summary>
    private int GetExpectedDataPoints()
    {
        // Expected data points = history duration / refresh interval
        return (_historyMinutes * 60) / _refreshIntervalSeconds;
    }

    private async Task FetchOllamaInfoAsync()
    {
        try
        {
            // Get Ollama version
            if (string.IsNullOrEmpty(_ollamaVersion))
            {
                try
                {
                    var versionResponse = await _httpClient.GetStringAsync($"{_ollamaHost}/api/version");
                    using var doc = JsonDocument.Parse(versionResponse);
                    if (doc.RootElement.TryGetProperty("version", out var ver))
                    {
                        _ollamaVersion = ver.GetString() ?? "";
                    }
                }
                catch { }
            }

            // Get loaded models
            var response = await _httpClient.GetStringAsync($"{_ollamaHost}/api/ps");
            var psResponse = JsonSerializer.Deserialize<ProcessStatusResponse>(response, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            _loadedModels = psResponse?.Models;
            _ollamaConnected = true;
        }
        catch
        {
            _loadedModels = null;
            _ollamaConnected = false;
        }
    }

    private void RenderDisplay()
    {
        try
        {
            // Use actual window width
            var width = Console.WindowWidth - 1;  // -1 to prevent line wrapping
            var height = Console.WindowHeight;

            // Calculate optimal graph height based on available space
            _currentGraphHeight = CalculateOptimalGraphHeight(height);

            // Move cursor to top-left without clearing (reduces flicker)
            Console.SetCursorPosition(0, 0);

            // GPU Section - clear previous area if section might have shrunk
            var gpuStartLine = Console.CursorTop;
            if (_lastGpuSectionEndLine > gpuStartLine)
            {
                ClearLines(gpuStartLine, _lastGpuSectionEndLine, width + 1);
                Console.SetCursorPosition(0, gpuStartLine);
            }
            try { RenderGpuSection(width); }
            catch (Exception ex) { ShowError($"GPU: {ex.Message}"); return; }
            _lastGpuSectionEndLine = Console.CursorTop;

            // System Section - clear previous area if section might have shrunk
            var systemStartLine = Console.CursorTop;
            if (_lastSystemSectionEndLine > systemStartLine)
            {
                ClearLines(systemStartLine, _lastSystemSectionEndLine, width + 1);
                Console.SetCursorPosition(0, systemStartLine);
            }
            try { RenderSystemSection(width); }
            catch (Exception ex) { ShowError($"System: {ex.Message}"); return; }
            _lastSystemSectionEndLine = Console.CursorTop;

            // Ollama Section - clear previous area if section might have shrunk
            var ollamaStartLine = Console.CursorTop;
            if (_lastOllamaSectionEndLine > ollamaStartLine)
            {
                ClearLines(ollamaStartLine, _lastOllamaSectionEndLine, width + 1);
                Console.SetCursorPosition(0, ollamaStartLine);
            }
            try { RenderOllamaSection(width); }
            catch (Exception ex) { ShowError($"Ollama: {ex.Message}"); return; }
            _lastOllamaSectionEndLine = Console.CursorTop;

            // Get current line after rendering content
            var currentLine = Console.CursorTop;

            // Clear all lines between content and status bar (height - 1)
            // This ensures a clean gap and status bar always at bottom
            var clearLine = new string(' ', width + 1);
            for (int i = currentLine; i < height - 1; i++)
            {
                Console.SetCursorPosition(0, i);
                Console.Write(clearLine);
            }

            // Status bar at bottom
            RenderStatusBar(width);
        }
        catch (Exception ex)
        {
            ShowError($"Main: {ex.Message}");
        }
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

    /// <summary>
    /// Calculate optimal graph height based on console height and number of GPUs
    /// Shrinks from 5 → 4 → 3 → 2 → 1 rows as needed to fit
    /// </summary>
    private int CalculateOptimalGraphHeight(int consoleHeight)
    {
        var gpuCount = Math.Max(1, _gpuData.Count);
        var modelCount = Math.Max(1, _loadedModels?.Count ?? 1);

        // Fixed height elements (carefully counted with Spectre.Console Rounded borders):
        // - GPU Status panel: 3 lines (top border+header, content line, bottom border)
        // - GPU table: 4 + gpuCount lines (top border, header row, separator, gpuCount data rows, bottom border)
        // - System bar panel: 5 lines (top border+header, CPU bar, MEM bar, SWP bar, bottom border)
        // - Ollama table with title: 2 (title lines) + 4 + modelCount (table: top border, header, separator, rows, bottom)
        // - Status bar: 1 line
        var gpuStatusHeight = 3;
        var gpuTableHeight = 4 + gpuCount;
        var systemBarPanelHeight = 5;
        var ollamaTableHeight = 2 + 4 + modelCount;  // 2 title lines + table structure
        var statusBarHeight = 1;

        var fixedHeight = gpuStatusHeight + gpuTableHeight + systemBarPanelHeight +
                          ollamaTableHeight + statusBarHeight;

        // Variable height elements:
        // - Each graph panel: graphHeight + 3 (top border+header, content rows, time axis line, bottom border)
        // - GPU util graphs: 1 per GPU
        // - System CPU/MEM graph: 1
        var graphOverhead = 3;  // borders (2) + time axis line (1)
        var totalGraphs = gpuCount + 1;  // GPU util per GPU + 1 system CPU graph

        // Calculate available height for all graphs
        var availableForGraphs = consoleHeight - fixedHeight;

        // Height per graph panel (including overhead)
        var heightPerGraphPanel = totalGraphs > 0 ? availableForGraphs / totalGraphs : availableForGraphs;

        // Graph content height = panel height - overhead
        var graphContentHeight = heightPerGraphPanel - graphOverhead;

        // Clamp between min (1) and default (5) - allow very small graphs rather than overflow
        if (graphContentHeight >= DefaultGraphHeightChars)
            return DefaultGraphHeightChars;
        else if (graphContentHeight >= 4)
            return 4;
        else if (graphContentHeight >= 3)
            return 3;
        else if (graphContentHeight >= 2)
            return 2;
        else if (graphContentHeight >= 1)
            return 1;
        else
            return 1;  // Minimum 1-row graphs when space is very tight
    }

    private void RenderGpuSection(int width)
    {
        // GPU Header with driver info in a panel
        var smi = _gpuMonitor.HasGpu ? _gpuMonitor.GetSmiToolName() : "";
        var driver = _gpuMonitor.DriverVersion;
        var sdk = _gpuMonitor.GetSdkName();
        var sdkVer = _gpuMonitor.SdkVersion;

        var headerContent = _gpuMonitor.HasGpu
            ? $"[bold cyan]{smi}[/] [dim]Driver:[/] [white]{driver}[/]    [dim]{sdk}:[/] [white]{sdkVer}[/]"
            : "[dim]No GPU detected[/]";

        var gpuPanel = new Panel(new Markup(headerContent))
            .Header("[bold cyan]GPU Status[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Cyan1)
            .Padding(1, 0);

        AnsiConsole.Write(gpuPanel);

        if (_gpuData.Count == 0)
        {
            if (_gpuMonitor.HasGpu)
            {
                var err = (_gpuMonitor.ActiveProvider as NvidiaGpuProvider)?.LastError ?? "";
                AnsiConsole.MarkupLine($"[yellow]No GPU data available[/] [dim]{err}[/]");
            }
            // Still show empty graph space
            RenderEmptyGraphSpace(width);
            AnsiConsole.WriteLine();
            return;
        }

        // Calculate column widths
        // Fixed columns widths: GPU(4) + Fan(5) + Temp(6) + Power(10) + Clock(14) + VRAM(14) + Util(5) = 58
        // Table borders: 2 chars per column border + outer = ~20 chars
        var fixedColumnsTotal = 58 + 20;

        // Find longest GPU name
        var longestName = _gpuData.Max(g => g.Name?.Length ?? 0);

        // Available space for name + VRAM bar
        var availableSpace = width - fixedColumnsTotal;

        // Split available space: prioritize name, give rest to VRAM bar
        var minVramBarWidth = 10;
        var maxNameWidth = Math.Max(15, longestName);
        var nameWidth = Math.Min(maxNameWidth, Math.Max(15, availableSpace - minVramBarWidth));

        // VRAM bar gets remaining space (no extra, just what fits)
        var vramBarWidth = Math.Max(minVramBarWidth, availableSpace - nameWidth);

        // Build dynamic VRAM Usage header with Ollama totals
        // Use Ollama API as canonical source for model VRAM allocation
        // Use 1024-based units to match the VRAM column (GiB)
        var totalOllamaVram = _gpuData.Sum(g => g.OllamaVramMB);
        var totalGpuVram = _gpuData.Sum(g => g.VramTotalMB);  // Dedicated VRAM only
        var usesSharedMemory = totalOllamaVram > totalGpuVram;

        string vramUsageHeader;
        if (usesSharedMemory)
        {
            // Show that Ollama is using shared memory (exceeds dedicated VRAM)
            var sharedUsed = totalOllamaVram - totalGpuVram;
            vramUsageHeader = $"[bold]Ollama VRAM[/] [fuchsia]{totalGpuVram / 1024:F1}G[/][dim]+[/][olive]{sharedUsed / 1024:F1}G shared[/]";
        }
        else
        {
            vramUsageHeader = $"[bold]Ollama VRAM ({totalOllamaVram / 1024:F1}/{totalGpuVram / 1024:F1}G)[/]";
        }

        // GPU Table - don't use Expand() to have predictable column widths
        var gpuTable = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Cyan1)
            .AddColumn(new TableColumn("[bold]GPU[/]").Centered().Width(4))
            .AddColumn(new TableColumn("[bold]Name[/]").Width(nameWidth).NoWrap())
            .AddColumn(new TableColumn("[bold]Fan[/]").Centered().Width(5))
            .AddColumn(new TableColumn("[bold]Temp[/]").Centered().Width(6))
            .AddColumn(new TableColumn("[bold]Power[/]").Centered().Width(10))
            .AddColumn(new TableColumn("[bold]Clock[/]").Centered().Width(14))
            .AddColumn(new TableColumn("[bold]VRAM[/]").Centered().Width(14))
            .AddColumn(new TableColumn("[bold]Util[/]").Centered().Width(5))
            .AddColumn(new TableColumn(vramUsageHeader).Width(vramBarWidth).NoWrap());

        foreach (var gpu in _gpuData)
        {
            // Temperature coloring (based on absolute values)
            var tempColor = gpu.Temperature < 60 ? "green" : gpu.Temperature < 75 ? "yellow" : "red";

            // Utilization coloring (based on percentage)
            var utilColor = gpu.ComputeUtilization < 50 ? "green" : gpu.ComputeUtilization < 80 ? "yellow" : "red";

            // VRAM coloring (based on percentage of limit)
            var vramPct = gpu.VramUtilization;
            var vramColor = vramPct < 50 ? "green" : vramPct < 80 ? "yellow" : "red";

            // Ollama VRAM percentage (for the bar) - based on dedicated VRAM only
            var ollamaVramPct = gpu.VramTotalMB > 0 ? (gpu.OllamaVramMB / gpu.VramTotalMB) * 100 : 0;
            var gpuUsesSharedMemory = ollamaVramPct > 100;  // Ollama is spilling to shared GPU memory
            var ollamaVramPctCapped = Math.Min(ollamaVramPct, 100);  // Cap at 100% for bar display

            // Color: green < 50%, yellow < 80%, red < 100%, magenta if using shared memory
            string ollamaVramColor;
            if (gpuUsesSharedMemory)
                ollamaVramColor = "fuchsia";  // Magenta/pink to indicate shared memory usage
            else if (ollamaVramPct >= 80)
                ollamaVramColor = "red";
            else if (ollamaVramPct >= 50)
                ollamaVramColor = "yellow";
            else
                ollamaVramColor = "green";

            // Power coloring (based on percentage of limit)
            var powerPct = gpu.PowerLimitW > 0 ? (gpu.PowerDrawW / gpu.PowerLimitW) * 100 : 0;
            var powerColor = powerPct < 50 ? "green" : powerPct < 80 ? "yellow" : "red";

            // Clock coloring (based on percentage of max clock)
            var clockPct = gpu.ClockSpeedMaxMHz > 0 ? (gpu.ClockSpeedMHz / (double)gpu.ClockSpeedMaxMHz) * 100 : 0;
            var clockColor = clockPct < 50 ? "green" : clockPct < 80 ? "yellow" : "lime";

            // Create Ollama VRAM bar using Ollama API value (capped at 100% for display)
            var vramBar = CreateProgressBarString(ollamaVramPctCapped, vramBarWidth, ollamaVramColor);

            var clockStr = gpu.ClockSpeedMHz > 0
                ? (gpu.ClockSpeedMaxMHz > 0 ? $"[{clockColor}]{gpu.ClockSpeedMHz}[/][dim]/{gpu.ClockSpeedMaxMHz}[/]" : $"[{clockColor}]{gpu.ClockSpeedMHz}MHz[/]")
                : "[dim]N/A[/]";

            var powerStr = gpu.PowerLimitW > 0
                ? $"[{powerColor}]{gpu.PowerDrawW:F0}[/][dim]/{gpu.PowerLimitW:F0}W[/]"
                : $"[white]{gpu.PowerDrawW:F0}W[/]";

            var vramStr = $"[{vramColor}]{gpu.VramUsedMB / 1024:F1}[/][dim]/{gpu.VramTotalMB / 1024:F1}G[/]";

            // Only truncate name if absolutely necessary
            var displayName = gpu.Name?.Length > nameWidth ? TruncateString(gpu.Name, nameWidth) : gpu.Name;

            gpuTable.AddRow(
                $"[cyan]{gpu.Index}[/]",
                $"[white]{displayName}[/]",
                gpu.FanSpeedPercent > 0 ? $"[white]{gpu.FanSpeedPercent}%[/]" : "[dim]N/A[/]",
                $"[{tempColor}]{gpu.Temperature}°C[/]",
                powerStr,
                clockStr,
                vramStr,
                $"[{utilColor}]{gpu.ComputeUtilization:F0}%[/]",
                vramBar
            );
        }
        AnsiConsole.Write(gpuTable);

        // GPU Graphs in rounded table - dynamic width
        // Panel borders (2) + padding (2) = 4 chars overhead
        var graphWidthChars = Math.Max(40, width - 4);
        var expectedDataPoints = GetExpectedDataPoints();
        foreach (var gpu in _gpuData)
        {
            // Determine which graph key, value, label, and unit to use based on display mode
            string graphKey;
            double currentValue;
            string metricLabel;
            string valueFormat;
            string valueUnit;

            switch (_gpuDisplayMode)
            {
                case GpuDisplayMode.Power:
                    graphKey = $"gpu{gpu.Index}_pwr";
                    currentValue = gpu.PowerLimitW > 0 ? (gpu.PowerDrawW / gpu.PowerLimitW) * 100 : 0;
                    metricLabel = "Power";
                    valueFormat = $"{gpu.PowerDrawW:F0}";
                    valueUnit = gpu.PowerLimitW > 0 ? $"/{gpu.PowerLimitW:F0}W" : "W";
                    break;
                case GpuDisplayMode.Temperature:
                    graphKey = $"gpu{gpu.Index}_tmp";
                    currentValue = Math.Min(100, gpu.Temperature);
                    metricLabel = "Temperature";
                    valueFormat = $"{gpu.Temperature}";
                    valueUnit = "°C";
                    break;
                case GpuDisplayMode.Vram:
                    graphKey = $"gpu{gpu.Index}_vrm";
                    currentValue = gpu.VramUtilization;
                    metricLabel = "VRAM";
                    valueFormat = $"{gpu.VramUsedMB / 1024:F1}";
                    valueUnit = $"/{gpu.VramTotalMB / 1024:F1}G";
                    break;
                case GpuDisplayMode.Clock:
                    graphKey = $"gpu{gpu.Index}_clk";
                    currentValue = gpu.ClockSpeedMaxMHz > 0 ? (gpu.ClockSpeedMHz * 100.0 / gpu.ClockSpeedMaxMHz) : 0;
                    metricLabel = "Clock";
                    valueFormat = $"{gpu.ClockSpeedMHz}";
                    valueUnit = gpu.ClockSpeedMaxMHz > 0 ? $"/{gpu.ClockSpeedMaxMHz}MHz" : "MHz";
                    break;
                case GpuDisplayMode.Fan:
                    graphKey = $"gpu{gpu.Index}_fan";
                    currentValue = gpu.FanSpeedPercent;
                    metricLabel = "Fan";
                    valueFormat = $"{gpu.FanSpeedPercent}";
                    valueUnit = "%";
                    break;
                default: // Utilization
                    graphKey = $"gpu{gpu.Index}_utl";
                    currentValue = gpu.OllamaGpuUtilization;
                    metricLabel = (gpu.HasPerProcessGpuUtil ? "Ollama" : "System") + " GPU";
                    valueFormat = $"{currentValue:F1}";
                    valueUnit = "%";
                    break;
            }

            // Color based on current value (all values are 0-100 range)
            var color = currentValue < 50 ? "green" : currentValue < 80 ? "yellow" : "red";

            // Build graph content
            var graphContent = new StringBuilder();

            if (_graphs.TryGetValue(graphKey, out var graph))
            {
                var lines = graph.Render(graphWidthChars, expectedDataPoints, _currentGraphHeight);
                foreach (var line in lines)
                {
                    graphContent.AppendLine(line);
                }
            }
            else
            {
                var emptyLines = RenderEmptyBrailleLines(graphWidthChars, _currentGraphHeight);
                foreach (var line in emptyLines)
                {
                    graphContent.AppendLine(line);
                }
            }
            graphContent.Append(RenderTimeAxisString(graphWidthChars));

            // Format value for header with dot padding
            var headerValue = _gpuDisplayMode == GpuDisplayMode.Utilization
                ? $"{currentValue:F1}%".PadLeft(6, '·')
                : $"{valueFormat}{valueUnit}".PadLeft(10, '·');

            // Border color based on mode
            var borderColor = _gpuDisplayMode == GpuDisplayMode.Utilization
                ? (gpu.HasPerProcessGpuUtil ? Color.Orange1 : Color.Yellow)
                : Color.Yellow;
            var labelColorName = _gpuDisplayMode == GpuDisplayMode.Utilization
                ? (gpu.HasPerProcessGpuUtil ? "orange1" : "yellow")
                : "yellow";

            var graphPanel = new Panel(new Markup(graphContent.ToString()))
                .Header($"[{color}][[{headerValue}]][/]──[bold {labelColorName}]{metricLabel} {gpu.Index} Utilization[/]──[dim](I=Toggle)[/]")
                .Border(BoxBorder.Rounded)
                .BorderColor(borderColor)
                .Expand();

            AnsiConsole.Write(graphPanel);
        }
    }

    private string[] RenderBrailleGraphLines(BrailleGraph graph, int targetWidthChars, string color)
    {
        // Graph renders at target width with embedded colors
        // Data is right-aligned to rightmost position
        return graph.Render(targetWidthChars);
    }

    private string[] RenderEmptyBrailleLines(int targetWidthChars, int heightChars)
    {
        var result = new string[heightChars];
        var emptyLine = new string('\u2800', targetWidthChars);
        for (int i = 0; i < heightChars; i++)
        {
            result[i] = $"[dim]{emptyLine}[/]";
        }
        return result;
    }

    private void RenderEmptyGraphSpace(int width)
    {
        var graphWidthChars = Math.Max(40, width - 4);
        var emptyLines = RenderEmptyBrailleLines(graphWidthChars, _currentGraphHeight);
        foreach (var line in emptyLines)
        {
            AnsiConsole.MarkupLine(line);
        }
        AnsiConsole.MarkupLine(RenderTimeAxisString(graphWidthChars));
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

    private void RenderSystemSection(int width)
    {
        // Calculate widths - same as GPU section
        // Panel borders (2) + padding (2) = 4 chars overhead
        var graphWidthChars = Math.Max(40, width - 4);
        var barWidth = Math.Max(30, width - 45);

        if (_systemData == null)
        {
            var loadingPanel = new Panel(new Markup("[dim]Collecting system metrics...[/]"))
                .Header("[bold green]System[/]")
                .Border(BoxBorder.Rounded)
                .BorderColor(Color.Green)
                .Expand();
            AnsiConsole.Write(loadingPanel);
            return;
        }

        // Get Ollama CPU and MEM usage (fallback to 0 if not available)
        var ollamaCpuPct = _ollamaData?.CpuUsagePercent ?? 0;
        var ollamaMemPct = (_ollamaData != null && _ollamaData.ProcessCount > 0 && _systemData.RamTotalBytes > 0)
            ? (_ollamaData.PrivateBytes * 100.0 / _systemData.RamTotalBytes)
            : 0;

        var sysCpuColor = _systemData.CpuUsagePercent < 50 ? "green" : _systemData.CpuUsagePercent < 80 ? "yellow" : "red";
        var memColor = _systemData.RamUsagePercent < 50 ? "green" : _systemData.RamUsagePercent < 80 ? "yellow" : "red";
        var swpColor = _systemData.SwapUsagePercent < 50 ? "green" : _systemData.SwapUsagePercent < 80 ? "yellow" : "red";

        // Build Ollama CPU or MEM graph content based on toggle
        var graphContent = new StringBuilder();
        var expectedDataPoints = GetExpectedDataPoints();
        var hasOllamaData = _ollamaData != null && _ollamaData.ProcessCount > 0;

        // Determine which graph to show
        var graphKey = _showOllamaMemGraph ? "ollama_mem" : "cpu";
        var utilizationType = _showOllamaMemGraph ? "MEM" : "CPU";
        var pctToShow = _showOllamaMemGraph ? ollamaMemPct : (hasOllamaData ? ollamaCpuPct : _systemData.CpuUsagePercent);
        var pctColor = pctToShow < 50 ? "green" : pctToShow < 80 ? "yellow" : "red";

        if (_graphs.TryGetValue(graphKey, out var graph))
        {
            var lines = graph.Render(graphWidthChars, expectedDataPoints, _currentGraphHeight);
            foreach (var line in lines)
            {
                graphContent.AppendLine(line);
            }
        }
        else
        {
            var emptyLines = RenderEmptyBrailleLines(graphWidthChars, _currentGraphHeight);
            foreach (var line in emptyLines)
            {
                graphContent.AppendLine(line);
            }
        }
        graphContent.Append(RenderTimeAxisString(graphWidthChars));

        // Format percentage for header with dot padding: [·24.7%]
        // Show "Ollama" if Ollama processes found, "System" otherwise (only for CPU)
        var label = hasOllamaData ? "Ollama" : (_showOllamaMemGraph ? "Ollama" : "System");
        var borderColor = hasOllamaData ? Color.DodgerBlue1 : Color.SteelBlue;
        var labelColorName = hasOllamaData ? "dodgerblue1" : "steelblue";

        var pctValue = $"{pctToShow:F1}%";
        var pctStr = pctValue.PadLeft(6, '·');
        var ollamaUtilPanel = new Panel(new Markup(graphContent.ToString()))
            .Header($"[{pctColor}][[{pctStr}]][/]──[bold {labelColorName}]{label} {utilizationType} Utilization[/]──[dim](O=Toggle)[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(borderColor)
            .Expand();

        AnsiConsole.Write(ollamaUtilPanel);

        // System panel with CPU, MEM, SWP bars
        var sysCpuBar = CreateProgressBarString(_systemData.CpuUsagePercent, barWidth, sysCpuColor);
        var memBar = CreateProgressBarString(_systemData.RamUsagePercent, barWidth, memColor);
        var swpBar = CreateProgressBarString(_systemData.SwapUsagePercent, barWidth, swpColor);

        var sysContent = new StringBuilder();
        sysContent.AppendLine($"[bold]CPU:[/] {sysCpuBar} [{sysCpuColor}]{_systemData.CpuUsagePercent,5:F1}%[/]");
        sysContent.AppendLine($"[bold]MEM:[/] {memBar} [{memColor}]{_systemData.RamUsagePercent,5:F1}%[/] [dim]({_systemData.RamUsedGB:F1}/{_systemData.RamTotalGB:F1} GB)[/]");
        sysContent.Append($"[bold]SWP:[/] {swpBar} [{swpColor}]{_systemData.SwapUsagePercent,5:F1}%[/] [dim]({_systemData.SwapUsedGB:F1}/{_systemData.SwapTotalGB:F1} GB)[/]");

        var sysPanel = new Panel(new Markup(sysContent.ToString()))
            .Header("[bold lime]System[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Lime)
            .Expand();

        AnsiConsole.Write(sysPanel);
    }

    private void RenderOllamaSection(int width)
    {
        // Build title line 1: connection info
        var osyncVer = $"[cyan]osync {OsyncProgram.GetFullVersion()}[/]";
        string titleLine1;

        if (_ollamaConnected)
        {
            var verStr = !string.IsNullOrEmpty(_ollamaVersion) ? $"[magenta]v{_ollamaVersion}[/]" : "";
            titleLine1 = $"[bold magenta]Ollama[/] ── {verStr} [green]●[/] Connected to [white]{_ollamaHost}[/] ── {osyncVer}";
        }
        else
        {
            titleLine1 = $"[bold magenta]Ollama[/] ── [red]●[/] Not connected [dim]({_ollamaHost})[/] ── {osyncVer}";
        }

        // Build title line 2: MEM/CPU bar
        var titleLine2 = "";
        if (_ollamaData != null && _ollamaData.ProcessCount > 0 && _systemData != null)
        {
            var barWidth = Math.Max(20, width - 55);

            if (_showOllamaMemGraph)
            {
                // Graph shows MEM, so bar shows CPU
                var ollamaCpuPct = _ollamaData.CpuUsagePercent;
                var ollamaCpuColor = ollamaCpuPct < 50 ? "green" : ollamaCpuPct < 80 ? "yellow" : "red";
                var cpuBar = CreateProgressBarString(ollamaCpuPct, barWidth, ollamaCpuColor);
                var procCountStr = _ollamaData.ProcessCount > 1
                    ? $"[dim]({_ollamaData.ProcessCount} procs)[/]"
                    : "";
                titleLine2 = $"[bold]CPU:[/] {cpuBar} [{ollamaCpuColor}]{ollamaCpuPct,5:F1}%[/] {procCountStr}";
            }
            else
            {
                // Graph shows CPU, so bar shows MEM
                var ollamaMemPct = _systemData.RamTotalBytes > 0
                    ? (_ollamaData.PrivateBytes * 100.0 / _systemData.RamTotalBytes)
                    : 0;
                var ollamaMemColor = ollamaMemPct < 50 ? "green" : ollamaMemPct < 80 ? "yellow" : "red";
                var memBar = CreateProgressBarString(ollamaMemPct, barWidth, ollamaMemColor);
                var memoryGB = _ollamaData.PrivateBytes / (1024.0 * 1024.0 * 1024.0);
                var procCountStr = _ollamaData.ProcessCount > 1
                    ? $"[dim]({_ollamaData.ProcessCount} procs, {memoryGB:F2} GB)[/]"
                    : $"[dim]({memoryGB:F2} GB)[/]";
                titleLine2 = $"[bold]MEM:[/] {memBar} [{ollamaMemColor}]{ollamaMemPct,5:F1}%[/] {procCountStr}";
            }
        }

        // Calculate dynamic model name width based on available space
        // Fixed columns: ID(14) + Param(10) + Size(VRAM)(16) + Context(8) + Expires(12) + borders(~15) = ~75
        var fixedColumnsWidth = 75;
        var modelNameWidth = Math.Max(30, width - fixedColumnsWidth);

        // Build combined title (connection info + MEM bar)
        var tableTitle = string.IsNullOrEmpty(titleLine2)
            ? titleLine1
            : $"{titleLine1}\n{titleLine2}";

        // Single table with title containing Ollama info and MEM bar
        var modelTable = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Magenta1)
            .Title(new TableTitle(tableTitle))
            .Expand()
            .AddColumn(new TableColumn("[bold]Model[/]").Width(modelNameWidth).NoWrap())
            .AddColumn(new TableColumn("[bold]ID[/]").Centered().Width(14))
            .AddColumn(new TableColumn("[bold]Param[/]").Centered().Width(10))
            .AddColumn(new TableColumn("[bold]Size (VRAM)[/]").Centered().Width(16))
            .AddColumn(new TableColumn("[bold]Context[/]").Centered().Width(8))
            .AddColumn(new TableColumn("[bold]Expires[/]").Centered().Width(12));

        if (_ollamaConnected && _loadedModels != null && _loadedModels.Count > 0)
        {
            foreach (var model in _loadedModels)
            {
                var name = model.Name ?? "";
                // Only truncate if really necessary
                if (name.Length > modelNameWidth - 2)
                    name = TruncateString(name, modelNameWidth - 2);

                var id = GetShortDigest(model.Digest);

                // Param column: show parameter count (e.g., "8B") from Details.ParameterSize
                var paramStr = !string.IsNullOrEmpty(model.Details?.ParameterSize)
                    ? model.Details.ParameterSize
                    : "N/A";

                // Size (VRAM) column: show total allocation size with VRAM percentage when partially offloaded
                // Use GiB (1024-based) for consistency with VRAM column
                string sizeStr;
                if (model.Size > 0 && model.SizeVram < model.Size)
                {
                    // Model partially offloaded - show total size and VRAM percentage
                    var vramPct = (model.SizeVram * 100.0) / model.Size;
                    var vramColor = vramPct < 50 ? "yellow" : vramPct < 80 ? "lime" : "green";
                    sizeStr = $"[{vramColor}]{FormatBytes(model.Size)}[/] [dim]({vramPct:F0}%)[/]";
                }
                else
                {
                    // Model fully in VRAM
                    sizeStr = $"[green]{FormatBytes(model.Size)}[/]";
                }

                var ctx = model.ContextLength > 0 ? model.ContextLength.ToString() : "N/A";
                var until = FormatUntil(model.ExpiresAt);

                modelTable.AddRow(
                    $"[white]{name}[/]",
                    $"[dim]{id}[/]",
                    $"[white]{paramStr}[/]",
                    sizeStr,
                    $"[white]{ctx}[/]",
                    $"[yellow]{until}[/]"
                );
            }
        }
        else
        {
            // Show empty state row
            modelTable.AddRow(
                "[dim]No models loaded[/]",
                "[dim]-[/]",
                "[dim]-[/]",
                "[dim]-[/]",
                "[dim]-[/]",
                "[dim]-[/]"
            );
        }

        AnsiConsole.Write(modelTable);
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

    /// <summary>
    /// Creates a two-tone progress bar for dedicated (vivid) + shared (pale) GPU memory
    /// </summary>
    private string CreateTwoToneVramBar(double dedicatedMB, double sharedMB, double totalMB, int width)
    {
        if (totalMB <= 0)
            return $"[dim]{new string('░', width)}[/]";

        var dedicatedPct = (dedicatedMB / totalMB) * 100;
        var sharedPct = (sharedMB / totalMB) * 100;
        var totalPct = dedicatedPct + sharedPct;

        // Clamp to 100% total
        if (totalPct > 100)
        {
            var scale = 100 / totalPct;
            dedicatedPct *= scale;
            sharedPct *= scale;
            totalPct = 100;
        }

        var dedicatedChars = (int)Math.Round(dedicatedPct / 100.0 * width);
        var sharedChars = (int)Math.Round(sharedPct / 100.0 * width);
        var emptyChars = width - dedicatedChars - sharedChars;

        // Ensure we don't overflow
        if (dedicatedChars + sharedChars > width)
        {
            if (sharedChars > 0)
                sharedChars = width - dedicatedChars;
            else
                dedicatedChars = width;
        }
        emptyChars = Math.Max(0, width - dedicatedChars - sharedChars);

        // Color based on total usage
        var dedicatedColor = totalPct < 50 ? "green" : totalPct < 80 ? "yellow" : "red";
        // Shared uses dimmed/paler version
        var sharedColor = totalPct < 50 ? "darkgreen" : totalPct < 80 ? "olive" : "maroon";

        var dedicatedStr = new string('█', dedicatedChars);
        var sharedStr = new string('▓', sharedChars);  // Use lighter block for shared
        var emptyStr = new string('░', emptyChars);

        return $"[{dedicatedColor}]{dedicatedStr}[/][{sharedColor}]{sharedStr}[/][dim]{emptyStr}[/]";
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

    /// <summary>
    /// Formats bytes using SI units (1000-based) to match ollama ps output
    /// </summary>
    private static string FormatBytesGB(long bytes)
    {
        if (bytes <= 0) return "N/A";
        // Use 1000-based (SI units) to match ollama ps output
        double gb = bytes / 1_000_000_000.0;
        if (gb >= 1.0)
            return $"{gb:F0} GB";
        double mb = bytes / 1_000_000.0;
        return $"{mb:F0} MB";
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
