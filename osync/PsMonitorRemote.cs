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
    Slots      // busy slots (xOllama)
}

/// <summary>
/// psmonitor for a server on another machine: everything shown comes from that server's API (see
/// <see cref="RemoteServerMonitor"/>); nothing is read from this machine, so there is no local GPU, CPU or memory panel.
/// </summary>
public partial class PsMonitorUI
{
    private readonly bool _remote;
    private readonly RemoteServerMonitor? _remoteMonitor;
    private RemoteSnapshot? _remoteData;
    private RemoteGraphMode _remoteGraphMode = RemoteGraphMode.Vram;

    private void InitRemoteGraphs()
    {
        foreach (var key in new[] { "r_vram", "r_ram", "r_tps", "r_slots" })
            _graphs[key] = new BrailleGraph(MaxStoredDataPoints / 2, _currentGraphHeight, 0, 100, autoScale: true);
    }

    private bool RemoteIsXOllama => _remoteData?.Flavor == ServerFlavor.XOllama;

    private void CycleRemoteGraph()
    {
        _remoteGraphMode = _remoteGraphMode switch
        {
            RemoteGraphMode.Vram => RemoteGraphMode.Ram,
            RemoteGraphMode.Ram => RemoteIsXOllama ? RemoteGraphMode.Tokens : RemoteGraphMode.Vram,
            RemoteGraphMode.Tokens => RemoteGraphMode.Slots,
            _ => RemoteGraphMode.Vram
        };
    }

    private async Task CollectRemoteDataAsync()
    {
        try
        {
            var snapshot = await _remoteMonitor!.CollectAsync();
            _remoteData = snapshot;
            _ollamaConnected = snapshot.Connected;
            _ollamaVersion = snapshot.Version;
            _loadedModels = snapshot.Connected ? snapshot.Models : null;
            if (!snapshot.Connected) return;

            const double GiB = 1024.0 * 1024 * 1024;
            _graphs["r_vram"].AddDataPoint(snapshot.Models.Sum(m => m.SizeVram) / GiB, "green");
            _graphs["r_ram"].AddDataPoint(snapshot.Models.Sum(m => Math.Max(0, m.Size - m.SizeVram)) / GiB, "yellow");
            _graphs["r_tps"].AddDataPoint(snapshot.Engine.Values.Sum(e => e.TokensPerSecond ?? 0), "aqua");
            _graphs["r_slots"].AddDataPoint(snapshot.Engine.Values.Sum(e => e.SlotsBusy ?? 0), "orange1");
        }
        catch { }
    }

    private void RenderRemoteDisplay(int width, int height)
    {
        try
        {
            var devices = _remoteData?.Devices;
            var deviceRows = devices is { Count: > 0 } ? devices.Count : 0;
            var modelRows = Math.Max(1, _loadedModels?.Count ?? 1);
            // Devices table (4 + rows, when any), graph panel (graph + 3), models table (2 title lines + 4 + rows), status bar
            var fixedHeight = (deviceRows > 0 ? 4 + deviceRows : 0) + 3 + (2 + 4 + modelRows) + 1;
            _currentGraphHeight = Math.Clamp(height - fixedHeight, MinGraphHeightChars, DefaultGraphHeightChars);

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

            if (deviceRows > 0) RenderRemoteDevices(console, devices!);
            RenderRemoteGraph(console, width);
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
            ShowError($"Remote: {ex.Message}");
        }
    }

    private static readonly System.Text.RegularExpressions.Regex AnsiCodes = new(@"\x1b\[[0-9;?]*[A-Za-z]|\x1b\][^\x07]*\x07", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static int VisibleLength(string line) => AnsiCodes.Replace(line, "").Length;

    /// <summary>The server's GPUs as it reports them (xOllama), refreshed at most once a minute.</summary>
    private void RenderRemoteDevices(IAnsiConsole console, List<RemoteDevice> devices)
    {
        var age = _remoteData?.DevicesAsOf is { } asOf ? (int)(DateTime.UtcNow - asOf).TotalSeconds : 0;
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Cyan1)
            .Expand()
            .AddColumn(new TableColumn("[bold]GPU[/]").Centered())
            .AddColumn(new TableColumn("[bold]Name[/]").NoWrap())
            .AddColumn(new TableColumn("[bold]Backend[/]").Centered())
            .AddColumn(new TableColumn("[bold]Engine[/]").Centered())
            .AddColumn(new TableColumn($"[bold]VRAM used[/] [dim](server, {age}s ago, R=refresh)[/]").NoWrap())
            .AddColumn(new TableColumn("[bold]Free[/]").RightAligned());

        var index = 0;
        foreach (var d in devices)
        {
            var pct = d.TotalMemory > 0 ? d.UsedMemory * 100.0 / d.TotalMemory : 0;
            var color = pct < 50 ? "green" : pct < 80 ? "yellow" : "red";
            var name = Markup.Escape(d.Name) + (d.Integrated ? " [dim](integrated)[/]" : "");
            table.AddRow(
                $"{index++}",
                name,
                Markup.Escape(d.Backend),
                Markup.Escape(d.Engine.Length > 0 ? d.Engine : "-"),
                $"{CreateProgressBarString(pct, 20, color)} [{color}]{d.UsedMemory / 1073741824.0:F1}[/]/{d.TotalMemory / 1073741824.0:F1}G",
                $"{d.FreeMemory / 1073741824.0:F1}G");
        }
        console.Write(table);
    }

    private void RenderRemoteGraph(IAnsiConsole console, int width)
    {
        var graphWidthChars = Math.Max(40, width - 4);
        var expectedDataPoints = GetExpectedDataPoints();
        var models = _loadedModels ?? new List<RunningModel>();
        var engine = _remoteData?.Engine ?? new Dictionary<string, RemoteEngineStats>();

        string key, label, value;
        switch (_remoteGraphMode)
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
            default:
                key = "r_vram";
                label = "Loaded models, VRAM";
                var vram = models.Sum(m => m.SizeVram);
                var total = _remoteData?.Devices?.Where(d => !d.Integrated).Sum(d => (long)d.TotalMemory) ?? 0;
                value = total > 0 ? $"{FormatGiB(vram)}/{FormatGiB(total)}" : FormatGiB(vram);
                break;
        }

        var content = new StringBuilder();
        foreach (var line in _graphs[key].Render(graphWidthChars, expectedDataPoints, _currentGraphHeight))
            content.AppendLine(line);
        content.Append(RenderTimeAxisString(graphWidthChars));

        console.Write(new Panel(new Markup(content.ToString()))
            .Header($"[green][[{value}]][/]──[bold yellow]{label}[/]──[dim](I=Toggle)[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Yellow)
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
            title = $"[bold magenta]Remote {flavor}[/] ── {ver}[green]●[/] [white]{Markup.Escape(_ollamaHost)}[/] [dim]RTT {data.RoundTripMs:F0} ms[/] ── {osyncVer}";
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
            title = $"[bold magenta]Remote {flavor}[/] ── [red]●[/] Not connected [dim]({Markup.Escape(_ollamaHost)})[/] ── {osyncVer}\n ";
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
