using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Spectre.Console;

namespace osync;

/// <summary>Graph shown by psmonitor for a remote server (I cycles).</summary>
public enum RemoteGraphMode
{
    Vram,      // memory the loaded models hold in VRAM
    Ram,       // memory the loaded models hold outside VRAM
    Tokens,    // generation speed (xOllama)
    Slots,     // busy slots (xOllama)
    GpuUtil,   // the selected GPU's utilization (xOllama system telemetry, as are the modes below)
    GpuPower,
    GpuTemp,
    GpuVram,   // the selected GPU's memory in use
    GpuClock,
    GpuFan,
    HostCpu,   // the whole host's CPU
    HostMem,
    ServerCpu, // the server's process tree, of one core
    ServerMem, // the server's process tree, resident memory
    KvHost     // host RAM held by the KV rolling window of the loaded models (xOllama, kv_host_telemetry_v1)
}

/// <summary>
/// psmonitor's view, the same for every server: what the server's API reports (see <see cref="RemoteServerMonitor"/>).
/// For a server on another machine that is all there is: nothing is read from this machine. For a server on this
/// machine that sends no system telemetry (stock Ollama, an older xOllama), the System panel and the GPUs are read
/// here instead (<see cref="LocalReadings"/>).
/// </summary>
public partial class PsMonitorUI
{
    private readonly bool _remote;
    private RemoteSnapshot? _remoteData;
    private RemoteGraphMode? _secondGraphMode = RemoteGraphMode.ServerCpu;  // the graph under the main one (O cycles), when there is room
    private string _gpuDriverInfo = "";
    private RemoteGraphMode _remoteGraphMode = RemoteGraphMode.Vram;
    private int _remoteGpu;  // index into the telemetry devices, for the GPU graphs (G cycles)

    private void InitRemoteGraphs()
    {
        foreach (var key in new[] { "r_vram", "r_ram", "r_tps", "r_slots", "r_scpu", "r_smem", "r_kvhost" })
            _graphs[key] = new BrailleGraph(MaxStoredDataPoints / 2, _currentGraphHeight, 0, 100, autoScale: true);
        foreach (var key in new[] { "r_hcpu", "r_hmem" })
            _graphs[key] = new BrailleGraph(MaxStoredDataPoints / 2, _currentGraphHeight, 0, 100);
    }

    private static string GpuGraphKey(RemoteDevice device, RemoteGraphMode mode) => $"r_gpu_{mode}_{device.PciId}_{device.Name}";

    private BrailleGraph GpuGraph(RemoteDevice device, RemoteGraphMode mode)
    {
        var key = GpuGraphKey(device, mode);
        if (!_graphs.TryGetValue(key, out var graph))
            _graphs[key] = graph = mode is RemoteGraphMode.GpuUtil or RemoteGraphMode.GpuVram or RemoteGraphMode.GpuFan
                ? new BrailleGraph(MaxStoredDataPoints / 2, _currentGraphHeight, 0, 100)
                : new BrailleGraph(MaxStoredDataPoints / 2, _currentGraphHeight, 0, 100, autoScale: true);
        return graph;
    }

    /// <summary>The GPUs the server sends live readings for (system telemetry), in its order.</summary>
    private List<RemoteDevice> TelemetryDevices =>
        _remoteData?.System?.Devices.Where(d => d.Telemetry != null).ToList() ?? new List<RemoteDevice>();

    private RemoteDevice? SelectedRemoteGpu
    {
        get
        {
            var devices = TelemetryDevices;
            return devices.Count == 0 ? null : devices[Math.Clamp(_remoteGpu, 0, devices.Count - 1)];
        }
    }

    private void CycleRemoteGpu()
    {
        var count = TelemetryDevices.Count;
        _remoteGpu = count == 0 ? 0 : (_remoteGpu + 1) % count;
    }

    private bool RemoteIsXOllama => _remoteData?.Flavor == ServerFlavor.XOllama;

    /// <summary>The graphs this server has data for: GPU, host and server graphs only with system telemetry.</summary>
    private List<RemoteGraphMode> RemoteGraphModes()
    {
        var modes = new List<RemoteGraphMode> { RemoteGraphMode.Vram, RemoteGraphMode.Ram };
        if (RemoteIsXOllama)
            modes.AddRange(new[] { RemoteGraphMode.Tokens, RemoteGraphMode.Slots });
        if (_remoteData?.KvHostBytes is { Count: > 0 })
            modes.Add(RemoteGraphMode.KvHost);
        if (TelemetryDevices.Count > 0)
        {
            modes.AddRange(new[] { RemoteGraphMode.GpuUtil, RemoteGraphMode.GpuPower, RemoteGraphMode.GpuTemp, RemoteGraphMode.GpuVram });
            // only the readings some GPU has
            if (TelemetryDevices.Any(d => d.Telemetry!.ClockMhz != null)) modes.Add(RemoteGraphMode.GpuClock);
            if (TelemetryDevices.Any(d => d.Telemetry!.FanPct != null)) modes.Add(RemoteGraphMode.GpuFan);
        }
        if (_remoteData?.System?.Host != null)
            modes.AddRange(new[] { RemoteGraphMode.HostCpu, RemoteGraphMode.HostMem });
        if (_remoteData?.System?.Server != null)
            modes.AddRange(new[] { RemoteGraphMode.ServerCpu, RemoteGraphMode.ServerMem });
        return modes;
    }

    private void CycleRemoteGraph()
    {
        var modes = RemoteGraphModes();
        var at = modes.IndexOf(_remoteGraphMode);
        _remoteGraphMode = modes[(at + 1) % modes.Count];
    }

    /// <summary>O: the second graph shows the server's CPU, then its memory, then nothing.</summary>
    private void CycleSecondGraph() => _secondGraphMode = _secondGraphMode switch
    {
        RemoteGraphMode.ServerCpu => RemoteGraphMode.ServerMem,
        RemoteGraphMode.ServerMem => null,
        _ => RemoteGraphMode.ServerCpu
    };

    private async Task CollectServerDataAsync()
    {
        try
        {
            var snapshot = await _remoteMonitor.CollectAsync();
            // A server on this machine without system telemetry: this machine's readings take its place
            if (!_remote && snapshot.System == null)
                snapshot = await WithLocalReadingsAsync(snapshot);
            _remoteData = snapshot;
            _ollamaConnected = snapshot.Connected;
            _ollamaVersion = snapshot.Version;
            _loadedModels = snapshot.Connected ? snapshot.Models : null;

            if (snapshot.Connected)
            {
                const double GiB = 1024.0 * 1024 * 1024;
                _graphs["r_vram"].AddDataPoint(snapshot.Models.Sum(m => m.SizeVram) / GiB, "green");
                _graphs["r_ram"].AddDataPoint(snapshot.Models.Sum(m => Math.Max(0, m.Size - m.SizeVram)) / GiB, "yellow");
                _graphs["r_tps"].AddDataPoint(snapshot.Engine.Values.Sum(e => e.TokensPerSecond ?? 0), "aqua");
                _graphs["r_slots"].AddDataPoint(snapshot.Engine.Values.Sum(e => e.SlotsBusy ?? 0), "orange1");
                // Only the models whose engine reports its window: an unknown is never drawn as 0
                if (snapshot.KvHostBytes is { Count: > 0 } kv)
                    _graphs["r_kvhost"].AddDataPoint(kv.Values.Sum(b => (double)b) / GiB, "fuchsia");
            }

            // Telemetry: a reading the server did not have (null) adds no point, it is never drawn as 0
            if (snapshot.System is { } system)
            {
                foreach (var d in system.Devices.Where(d => d.Telemetry != null))
                {
                    var t = d.Telemetry!;
                    if (t.UtilPct is { } util) GpuGraph(d, RemoteGraphMode.GpuUtil).AddDataPoint(util);
                    if (t.PowerW is { } power) GpuGraph(d, RemoteGraphMode.GpuPower).AddDataPoint(power, "orange1");
                    if (t.TempC is { } temp) GpuGraph(d, RemoteGraphMode.GpuTemp).AddDataPoint(temp, temp < 70 ? "green" : temp < 85 ? "yellow" : "red");
                    if (d.TotalMemory is > 0 && d.UsedMemory is { } used) GpuGraph(d, RemoteGraphMode.GpuVram).AddDataPoint(used * 100.0 / d.TotalMemory.Value);
                    if (t.ClockMhz is { } clock) GpuGraph(d, RemoteGraphMode.GpuClock).AddDataPoint(clock, "aqua");
                    if (t.FanPct is { } fan) GpuGraph(d, RemoteGraphMode.GpuFan).AddDataPoint(fan);
                }
                if (system.Host?.CpuPct is { } cpu) _graphs["r_hcpu"].AddDataPoint(cpu);
                if (system.Host is { MemTotal: > 0 } host && host.MemUsed is { } memUsed)
                    _graphs["r_hmem"].AddDataPoint(memUsed * 100.0 / host.MemTotal.Value);
                if (system.Server?.CpuPct is { } scpu) _graphs["r_scpu"].AddDataPoint(scpu, "aqua");
                if (system.Server?.Rss is { } rss) _graphs["r_smem"].AddDataPoint(rss / GiBf, "dodgerblue1");
            }
        }
        catch { }
    }

    /// <summary>The snapshot with this machine's CPU, memory, server processes and GPUs in place of the server's telemetry.</summary>
    private async Task<RemoteSnapshot> WithLocalReadingsAsync(RemoteSnapshot snapshot)
    {
        _gpuMonitor ??= new GpuMonitor();
        var gpus = await _gpuMonitor.CollectMetricsAsync();
        var system = _systemMetrics != null ? await _systemMetrics.GetSystemMetricsAsync() : null;
        var server = _systemMetrics?.GetOllamaMetrics();
        if (_gpuMonitor.HasGpu && _gpuDriverInfo.Length == 0 && _gpuMonitor.DriverVersion.Length > 0)
        {
            _gpuDriverInfo = $"{_gpuMonitor.GetSmiToolName()} driver {_gpuMonitor.DriverVersion}" +
                             (_gpuMonitor.SdkVersion.Length > 0 ? $" · {_gpuMonitor.GetSdkName()} {_gpuMonitor.SdkVersion}" : "");
        }

        var local = LocalReadings.Build(system, server, gpus, snapshot.Devices, Environment.ProcessorCount,
            Environment.MachineName, Environment.TickCount64 / 1000);
        return snapshot with { System = local, Devices = local.Devices, DevicesAsOf = local.AsOf, LocalReadings = true };
    }

    private void RenderFrame(int width, int height)
    {
        try
        {
            var devices = _remoteData?.Devices;
            var deviceRows = devices is { Count: > 0 } ? devices.Count : 0;
            var modelRows = Math.Max(1, _loadedModels?.Count ?? 1);
            var system = _remoteData?.System;
            var systemRows = system == null ? 0 : (system.Host != null ? 1 : 0) + (system.Server != null ? 1 : 0);
            // System panel (2 + rows), devices table (4 + rows, when any), graph panels (graph + 3 each), models table
            // (2 title lines + 4 + rows), status bar
            var fixedHeight = (systemRows > 0 ? 2 + systemRows : 0) + (deviceRows > 0 ? 4 + deviceRows : 0) + (2 + 4 + modelRows) + 1;
            var room = height - fixedHeight;
            // The second graph only when both keep at least 3 rows, and only with a reading to draw
            var second = _secondGraphMode is { } sm && sm != _remoteGraphMode && RemoteGraphModes().Contains(sm) && room >= 2 * (3 + 3)
                ? _secondGraphMode
                : null;
            _currentGraphHeight = second != null
                ? Math.Clamp(room / 2 - 3, MinGraphHeightChars, DefaultGraphHeightChars)
                : Math.Clamp(room - 3, MinGraphHeightChars, DefaultGraphHeightChars);

            // The frame is drawn off-screen and written line by line over the previous one: no cursor position is
            // read back from the terminal, and nothing flickers
            var frame = new StringWriter();
            var console = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Ansi = AnsiConsole.Profile.Capabilities.Ansi ? AnsiSupport.Yes : AnsiSupport.No,
                ColorSystem = (ColorSystemSupport)AnsiConsole.Profile.Capabilities.ColorSystem,
                Interactive = InteractionSupport.No,
                Out = new AnsiConsoleOutput(frame),
            });
            console.Profile.Width = width;
            console.Profile.Height = height;
            console.Profile.Capabilities.Unicode = AnsiConsole.Profile.Capabilities.Unicode;

            if (systemRows > 0) RenderRemoteSystem(console, system!, width);
            if (deviceRows > 0) RenderRemoteDevices(console, devices!);
            RenderRemoteGraph(console, width, _remoteGraphMode, main: true);
            if (second is { } secondMode) RenderRemoteGraph(console, width, secondMode, main: false);
            RenderRemoteModels(console, width);

            var lines = frame.ToString().Replace("\r", "").Split('\n');
            var output = new StringBuilder();
            for (var row = 0; row < height - 1; row++)
            {
                var line = row < lines.Length ? lines[row] : "";
                output.Append(line);
                // Up to the last column but one: a full line would wrap on some consoles
                output.Append(' ', Math.Max(0, width - VisibleLength(line)));
                if (row < height - 2) output.Append("\r\n");
            }
            // Home by escape code where the terminal takes them: .NET's SetCursorPosition writes nothing without terminfo
            if (AnsiConsole.Profile.Capabilities.Ansi)
                output.Insert(0, "\x1b[H");
            else
                Console.SetCursorPosition(0, 0);
            Console.Write(output.ToString());
            RenderStatusBar(width);
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private static readonly System.Text.RegularExpressions.Regex AnsiCodes = new(@"\x1b\[[0-9;?]*[A-Za-z]|\x1b\][^\x07]*\x07", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static int VisibleLength(string line) => AnsiCodes.Replace(line, "").Length;

    private const double GiBf = 1073741824.0;

    /// <summary>A reading, or a dim "-" when the server has none (null is never shown as 0).</summary>
    private static string Reading(double? value, string format, string unit = "", string color = "white") =>
        value is { } v ? $"[{color}]{v.ToString(format, System.Globalization.CultureInfo.InvariantCulture)}{unit}[/]" : "[dim]-[/]";

    private static string Uptime(long seconds) =>
        seconds >= 86400 ? $"{seconds / 86400}d {seconds % 86400 / 3600}h" : seconds >= 3600 ? $"{seconds / 3600}h {seconds % 3600 / 60}m" : $"{seconds / 60}m";

    /// <summary>A used/total bar with the figures, or "-" when the server reports no total.</summary>
    private string UsageBar(ulong? used, ulong? total, int barWidth)
    {
        if (total == 0) return "[dim]none[/]";
        if (total is not > 0 || used is not { } u) return "[dim]-[/]";
        var pct = u * 100.0 / total.Value;
        var color = pct < 50 ? "green" : pct < 80 ? "yellow" : "red";
        return $"{CreateProgressBarString(pct, barWidth, color)} [{color}]{u / GiBf:F1}[/]/{total.Value / GiBf:F1}G";
    }

    /// <summary>The remote host and the server's processes (xOllama system telemetry), as the server reads them.</summary>
    private void RenderRemoteSystem(IAnsiConsole console, RemoteSystem system, int width)
    {
        var lines = new List<string>();
        var header = "[bold]System[/]";
        if (system.Host is { } host)
        {
            var about = new List<string>();
            if (host.Hostname.Length > 0) about.Add(Markup.Escape(host.Hostname));
            if (host.Os.Length > 0) about.Add(Markup.Escape(host.Arch.Length > 0 ? $"{host.Os}/{host.Arch}" : host.Os));
            if (host.UptimeSeconds is { } up) about.Add($"up {Uptime(up)}");
            if (about.Count > 0) header += $" ── [white]{string.Join(" · ", about)}[/]";

            var cpuColor = host.CpuPct is { } c ? (c < 50 ? "green" : c < 80 ? "yellow" : "red") : "white";
            var cpu = host.CpuPct is { } pct
                ? $"{CreateProgressBarString(pct, 10, cpuColor)} {Reading(pct, "F0", "%", cpuColor)}"
                : "[dim]-[/]";
            if (host.CpuCount is { } cores) cpu += $" [dim]of {cores}[/]";
            var parts = new List<string>
            {
                $"[bold]CPU[/] {cpu}",
                $"[bold]MEM[/] {UsageBar(host.MemUsed, host.MemTotal, 10)}",
                $"[bold]SWP[/] {UsageBar(host.SwapUsed, host.SwapTotal, 10)}",
            };
            if (host.Load is { Length: > 0 } load && load.All(double.IsFinite))
                parts.Add($"[bold]Load[/] [white]{string.Join(" ", load.Select(l => l.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)))}[/]");
            lines.Add(FitParts(parts, width - 4));
        }
        if (system.Server is { } server)
        {
            var parts = new List<string>
            {
                $"[bold]Server[/] {Reading(server.CpuPct, "F0", "% CPU", "aqua")} · {Reading(server.Rss / GiBf, "F1", "G RSS")}" +
                (server.Procs is { } procs ? $" · [white]{procs}[/] procs" : ""),
            };
            // The engines and runners serving models, busiest first
            foreach (var child in server.Children.Where(c => c.Model.Length > 0).OrderByDescending(c => c.CpuPct ?? -1))
                parts.Add($"[dim]{Markup.Escape(child.Model)}[/] {Reading(child.CpuPct, "F0", "%")} {Reading(child.Rss / GiBf, "F1", "G")}");
            lines.Add(FitParts(parts, width - 4));
        }
        string source;
        if (_remoteData?.LocalReadings == true)
        {
            source = "this machine" + (_gpuDriverInfo.Length > 0 ? $" · {Markup.Escape(_gpuDriverInfo)}" : "");
        }
        else
        {
            var age = system.AsOf is { } asOf ? Math.Max(0, (int)(DateTime.UtcNow - asOf).TotalSeconds) : 0;
            source = $"from the server, {age}s ago";
        }
        console.Write(new Panel(new Markup(string.Join("\n", lines)))
            .Header($"{header} ── [dim]{source}[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Blue)
            .Expand());
    }

    /// <summary>Joins markup parts with " │ " up to the width the line has; parts that would not fit are left out.</summary>
    private static string FitParts(List<string> parts, int width)
    {
        var line = new StringBuilder();
        var used = 0;
        foreach (var part in parts)
        {
            var length = Markup.Remove(part).Length + (used > 0 ? 3 : 0);
            if (used > 0 && used + length > width) break;
            if (used > 0) line.Append(" [dim]│[/] ");
            line.Append(part);
            used += length;
        }
        return line.ToString();
    }

    /// <summary>
    /// The server's GPUs as it reports them (xOllama): live readings with system telemetry, else the device list
    /// refreshed at most once a minute.
    /// </summary>
    private void RenderRemoteDevices(IAnsiConsole console, List<RemoteDevice> devices)
    {
        var age = _remoteData?.DevicesAsOf is { } asOf ? Math.Max(0, (int)(DateTime.UtcNow - asOf).TotalSeconds) : 0;
        var live = devices.Any(d => d.Telemetry != null);
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Cyan1)
            .Expand()
            .AddColumn(new TableColumn("[bold]GPU[/]").Centered().NoWrap())
            .AddColumn(new TableColumn("[bold]Name[/]").NoWrap())
            .AddColumn(new TableColumn("[bold]Backend[/]").Centered().NoWrap())
            .AddColumn(new TableColumn("[bold]Engine[/]").Centered().NoWrap());
        if (live)
        {
            // One line per GPU: a wrapped cell would push the frame past the screen
            table.AddColumn(new TableColumn("[bold]VRAM used[/]").NoWrap())
                .AddColumn(new TableColumn("[bold]Util[/]").RightAligned().NoWrap())
                .AddColumn(new TableColumn("[bold]Temp[/]").RightAligned().NoWrap())
                .AddColumn(new TableColumn("[bold]Power[/]").RightAligned().NoWrap())
                .AddColumn(new TableColumn("[bold]MHz[/]").RightAligned().NoWrap())
                .AddColumn(new TableColumn("[bold]Fan[/]").RightAligned().NoWrap())
                .AddColumn(new TableColumn("[bold]PCIe[/]").Centered().NoWrap());
        }
        else
        {
            table.AddColumn(new TableColumn($"[bold]VRAM used[/] [dim](server, {age}s ago, R=refresh)[/]").NoWrap())
                .AddColumn(new TableColumn("[bold]Free[/]").RightAligned());
        }

        var index = 0;
        foreach (var d in devices)
        {
            // a GPU the server does not use has no backend; without the server's device list that is unknown
            var used = d.Backend.Length > 0 || !d.BackendKnown;
            var name = Markup.Escape(d.Name) + (d.Integrated ? " [dim](integrated)[/]" : "") + (used ? "" : " [dim](not used)[/]");
            var row = new List<string>
            {
                $"{index++}",
                name,
                Markup.Escape(d.Backend.Length > 0 ? d.Backend : "-"),
                Markup.Escape(d.Engine.Length > 0 ? d.Engine : "-"),
                UsageBar(d.UsedMemory, d.TotalMemory, live ? 10 : 20),
            };
            if (live)
            {
                var t = d.Telemetry;
                var util = t?.UtilPct;
                row.Add(Reading(util, "F0", "%", util is { } u ? (u < 50 ? "green" : u < 80 ? "yellow" : "red") : "white"));
                row.Add(Reading(t?.TempC, "F0", "°C", t?.TempC is { } c ? (c < 70 ? "green" : c < 85 ? "yellow" : "red") : "white"));
                row.Add(t?.PowerW is { } w
                    ? Reading(w, "F0", "") + (t.PowerLimitW is { } lim ? $"[dim]/{lim:F0}[/]" : "") + "W"
                    : "[dim]-[/]");
                row.Add(Reading(t?.ClockMhz, "F0"));
                row.Add(Reading(t?.FanPct, "F0", "%"));
                row.Add(t?.LinkGen is { } gen
                    // What the bus runs at now (an idle card often drops to a lower generation), then its maximum
                    ? $"[white]{gen}x{t.LinkWidth?.ToString() ?? "?"}[/]" +
                      (t.LinkMaxGen is { } mg && (mg != gen || t.LinkMaxWidth != t.LinkWidth) ? $"[dim]/{mg}x{t.LinkMaxWidth?.ToString() ?? "?"}[/]" : "")
                    : "[dim]-[/]");
            }
            else
            {
                row.Add(d.FreeMemory is { } free ? $"{free / GiBf:F1}G" : "[dim]-[/]");
            }
            table.AddRow(row.ToArray());
        }
        console.Write(table);
    }

    private void RenderRemoteGraph(IAnsiConsole console, int width, RemoteGraphMode mode, bool main)
    {
        var graphWidthChars = Math.Max(40, width - 4);
        var expectedDataPoints = GetExpectedDataPoints();
        var models = _loadedModels ?? new List<RunningModel>();
        var engine = _remoteData?.Engine ?? new Dictionary<string, RemoteEngineStats>();

        string key, label, value;
        switch (mode)
        {
            case RemoteGraphMode.Ram:
                key = "r_ram";
                label = "Loaded models, memory outside VRAM";
                value = FormatGiB(models.Sum(m => Math.Max(0, m.Size - m.SizeVram)));
                break;
            case RemoteGraphMode.Tokens:
                key = "r_tps";
                label = "Generation speed";
                var tps = engine.Values.Where(e => e.TokensPerSecond.HasValue).ToList();
                value = tps.Count > 0 ? $"{tps.Sum(e => e.TokensPerSecond!.Value):F1} tok/s" : "n/a";
                break;
            case RemoteGraphMode.Slots:
                key = "r_slots";
                label = "Busy slots";
                var slots = engine.Values.Where(e => e.SlotsTotal.HasValue).ToList();
                value = slots.Count > 0 ? $"{slots.Sum(e => e.SlotsBusy ?? 0)}/{slots.Sum(e => e.SlotsTotal!.Value)}" : "n/a";
                break;
            case RemoteGraphMode.GpuUtil or RemoteGraphMode.GpuPower or RemoteGraphMode.GpuTemp or RemoteGraphMode.GpuVram
                or RemoteGraphMode.GpuClock or RemoteGraphMode.GpuFan when SelectedRemoteGpu is { } gpu:
                var gpuIndex = _remoteData!.Devices!.IndexOf(gpu);
                var t = gpu.Telemetry!;
                (label, value) = mode switch
                {
                    RemoteGraphMode.GpuUtil => ("Utilization", t.UtilPct is { } u ? $"{u:F0}%" : "n/a"),
                    RemoteGraphMode.GpuPower => ("Power", t.PowerW is { } w ? $"{w:F0} W" : "n/a"),
                    RemoteGraphMode.GpuVram => ("VRAM used", gpu.UsedMemory is { } vu && gpu.TotalMemory is > 0
                        ? $"{vu / GiBf:F1}/{gpu.TotalMemory.Value / GiBf:F1} GB" : "n/a"),
                    RemoteGraphMode.GpuClock => ("Clock", t.ClockMhz is { } mhz ? $"{mhz:F0} MHz" : "n/a"),
                    RemoteGraphMode.GpuFan => ("Fan", t.FanPct is { } fan ? $"{fan:F0}%" : "n/a"),
                    _ => ("Temperature", t.TempC is { } c ? $"{c:F0} °C" : "n/a"),
                };
                label = $"GPU {gpuIndex} {Markup.Escape(gpu.Name)} {label}";
                GpuGraph(gpu, mode);
                key = GpuGraphKey(gpu, mode);
                break;
            case RemoteGraphMode.HostCpu:
                key = "r_hcpu";
                label = "Host CPU (all cores)";
                value = _remoteData?.System?.Host?.CpuPct is { } hc ? $"{hc:F0}%" : "n/a";
                break;
            case RemoteGraphMode.HostMem:
                key = "r_hmem";
                label = "Host memory";
                value = _remoteData?.System?.Host is { MemTotal: > 0, MemUsed: { } mu } h ? $"{mu / GiBf:F1}/{h.MemTotal!.Value / GiBf:F1} GB" : "n/a";
                break;
            case RemoteGraphMode.ServerCpu:
                key = "r_scpu";
                label = "Server processes CPU (100% = one core)";
                value = _remoteData?.System?.Server?.CpuPct is { } sc ? $"{sc:F0}%" : "n/a";
                break;
            case RemoteGraphMode.ServerMem:
                key = "r_smem";
                label = "Server processes memory (RSS)";
                value = _remoteData?.System?.Server?.Rss is { } sr ? $"{sr / GiBf:F1} GB" : "n/a";
                break;
            case RemoteGraphMode.KvHost:
                key = "r_kvhost";
                label = "KV rolling window, host RAM";
                value = _remoteData?.KvHostBytes is { Count: > 0 } kvHost ? FormatGiB((long)kvHost.Values.Sum(b => (double)b)) : "n/a";
                break;
            default:
                key = "r_vram";
                label = "Loaded models, VRAM";
                var vram = models.Sum(m => m.SizeVram);
                var total = _remoteData?.Devices?.Where(d => !d.Integrated && d.Backend.Length > 0).Sum(d => (long)(d.TotalMemory ?? 0)) ?? 0;
                value = total > 0 ? $"{FormatGiB(vram)}/{FormatGiB(total)}" : FormatGiB(vram);
                break;
        }

        var content = new StringBuilder();
        foreach (var line in _graphs[key].Render(graphWidthChars, expectedDataPoints, _currentGraphHeight))
            content.AppendLine(line);
        content.Append(RenderTimeAxisString(graphWidthChars));

        var keys = main ? $"I=Toggle{(TelemetryDevices.Count > 1 ? ", G=GPU" : "")}" : "O=Toggle";
        console.Write(new Panel(new Markup(content.ToString()))
            .Header($"[green][[{value}]][/]──[bold {(main ? "yellow" : "dodgerblue1")}]{label}[/]──[dim]({keys})[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(main ? Color.Yellow : Color.DodgerBlue1)
            .Expand());
    }

    private void RenderRemoteModels(IAnsiConsole console, int width)
    {
        var data = _remoteData;
        var xollama = RemoteIsXOllama;
        var osyncVer = $"[cyan]osync {OsyncProgram.GetFullVersion()}[/]";
        var flavor = data != null && data.Flavor != ServerFlavor.Unknown ? OllamaServer.DisplayName(data.Flavor) : "Ollama";

        string title;
        if (_ollamaConnected && data != null)
        {
            var ver = data.Version.Length > 0 ? $"[magenta]v{Markup.Escape(data.Version)}[/] " : "";
            title = _remote
                ? $"[bold magenta]Remote {flavor}[/] ── {ver}[green]●[/] [white]{Markup.Escape(_ollamaHost)}[/] [dim]RTT {data.RoundTripMs:F0} ms[/] ── {osyncVer}"
                : $"[bold magenta]{flavor}[/] ── {ver}[green]●[/] Connected to [white]{Markup.Escape(_ollamaHost)}[/] ── {osyncVer}";
            var info = new List<string>();
            if (data.StoredModels is { } stored)
                info.Add($"{stored} models stored ({FormatGiB(data.StoredBytes ?? 0)})");
            if (data.CloudDisabled is { } disabled)
                info.Add(disabled ? "cloud off" : "cloud on");
            if (xollama && data.Devices is { Count: 0 })
                info.Add("no GPU reported");
            title += "\n[dim]" + (info.Count > 0 ? string.Join(" · ", info) : " ") + "[/]";
        }
        else
        {
            title = $"[bold magenta]{(_remote ? "Remote " : "")}{flavor}[/] ── [red]●[/] Not connected [dim]({Markup.Escape(_ollamaHost)})[/] ── {osyncVer}\n ";
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Magenta1)
            .Title(new TableTitle(title))
            .Expand();

        // Column widths are content widths: each column adds 2 of padding and 1 of border, plus the last border
        var fixedColumns = new[] { 12, 8, 6, 8, 14, 7 }.Sum() + 9 + (xollama ? 5 + 7 : 0);
        var columnCount = 1 + 7 + (xollama ? 2 : 0);
        var nameWidth = width - fixedColumns - 3 * columnCount - 1;
        // The model name matters more than its ID: the ID goes first when the name would not fit
        var showId = nameWidth >= 30;
        if (!showId) nameWidth += 12 + 3;
        nameWidth = Math.Max(16, nameWidth);
        table.AddColumn(new TableColumn("[bold]Model[/]").Width(nameWidth).NoWrap());
        if (showId) table.AddColumn(new TableColumn("[bold]ID[/]").Centered().Width(12));
        table.AddColumn(new TableColumn("[bold]Param[/]").Centered().Width(8))
            .AddColumn(new TableColumn("[bold]Quant[/]").Centered().Width(6))
            .AddColumn(new TableColumn(xollama ? "[bold]Engine[/]" : "[bold]Runner[/]").Centered().Width(8))
            .AddColumn(new TableColumn("[bold]Size (VRAM)[/]").Centered().Width(14).NoWrap())
            .AddColumn(new TableColumn("[bold]Context[/]").Centered().Width(7));
        if (xollama)
        {
            table.AddColumn(new TableColumn("[bold]Slots[/]").Centered().Width(5))
                .AddColumn(new TableColumn("[bold]tok/s[/]").Centered().Width(7));
        }
        table.AddColumn(new TableColumn("[bold]Expires[/]").Centered().Width(9));

        if (_ollamaConnected && _loadedModels is { Count: > 0 })
        {
            foreach (var model in _loadedModels)
            {
                var name = model.Name ?? "";
                if (name.Length > nameWidth - 2) name = TruncateString(name, nameWidth - 2);
                RemoteEngineStats? stats = null;
                data?.Engine.TryGetValue(model.Name ?? "", out stats);

                string size;
                if (model.Size > 0 && model.SizeVram < model.Size)
                {
                    var vramPct = model.SizeVram * 100.0 / model.Size;
                    var vramColor = vramPct < 50 ? "yellow" : vramPct < 80 ? "lime" : "green";
                    size = $"[{vramColor}]{FormatBytes(model.Size)}[/] [dim]({vramPct:F0}%)[/]";
                }
                else
                {
                    size = $"[green]{FormatBytes(model.Size)}[/]";
                }

                var runner = xollama ? stats?.Engine ?? "" : model.Runner ?? "";
                var ctx = model.ContextLength > 0 ? model.ContextLength.ToString() : model.Details?.Format == "media" ? "media" : "N/A";
                var row = new List<string>
                {
                    $"[white]{Markup.Escape(name)}[/]",
                    $"[white]{Markup.Escape(model.Details?.ParameterSize is { Length: > 0 } p ? p : "N/A")}[/]",
                    $"[white]{Markup.Escape(model.Details?.QuantizationLevel is { Length: > 0 } q ? q : "-")}[/]",
                    $"[white]{Markup.Escape(runner.Length > 0 ? runner : "-")}[/]",
                    size,
                    $"[white]{ctx}[/]",
                };
                if (xollama)
                {
                    var slots = stats?.SlotsTotal is { } total ? $"{stats.SlotsBusy ?? 0}/{total}" : "-";
                    if (stats?.PressureActive == true)
                        slots = $"[red]{slots}![/]";  // the engine is refusing bookings right now
                    row.Add(stats?.SlotsBusy > 0 ? $"[orange1]{slots}[/]" : $"[white]{slots}[/]");
                    row.Add(stats?.TokensPerSecond is { } tps ? $"[aqua]{tps:F1}[/]" : "[dim]-[/]");
                }
                row.Add($"[yellow]{FormatUntil(model.ExpiresAt)}[/]");
                if (showId) row.Insert(1, $"[dim]{GetShortDigest(model.Digest)}[/]");
                table.AddRow(row.ToArray());
            }
        }
        else
        {
            table.AddRow(Enumerable.Range(0, columnCount - (showId ? 0 : 1)).Select(i => i == 0 ? "[dim]No models loaded[/]" : "[dim]-[/]").ToArray());
        }

        console.Write(table);
    }

    private static string FormatGiB(long bytes) => $"{bytes / 1073741824.0:F1} GB";
}
