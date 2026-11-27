using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using static osync.OllamaServer;

namespace osync;

/// <summary>
/// One GPU as a remote xOllama reports it (GET /api/xollama/devices, or /api/xollama/system with its telemetry).
/// A memory figure the server did not report is null, never 0.
/// </summary>
internal sealed record RemoteDevice(string Backend, string Id, string PciId, string Name, bool Integrated,
    ulong? TotalMemory, ulong? FreeMemory, string Engine)
{
    /// <summary>used_memory as the server reported it (system telemetry); else total − free.</summary>
    public ulong? ReportedUsed { get; init; }
    public ulong? UsedMemory => ReportedUsed ??
        (TotalMemory is { } t && FreeMemory is { } f ? (t > f ? t - f : 0) : null);
    public string Vendor { get; init; } = "";
    /// <summary>Live readings from /api/xollama/system; null for a device from /api/xollama/devices.</summary>
    public RemoteGpuTelemetry? Telemetry { get; init; }
    /// <summary>False for a GPU of this machine when the server has no device list: an empty backend then is unknown, not "not used".</summary>
    public bool BackendKnown { get; init; } = true;
}

/// <summary>A GPU's live readings (/api/xollama/system); null is a value the platform does not expose.</summary>
internal sealed record RemoteGpuTelemetry(string Source, double? UtilPct, double? MemUtilPct, double? TempC,
    double? PowerW, double? PowerLimitW, double? ClockMhz, double? ClockMaxMhz, double? FanPct,
    int? LinkGen, int? LinkWidth, int? LinkMaxGen, int? LinkMaxWidth);

/// <summary>The remote host as a whole (/api/xollama/system "host"); cpu_pct is of all cores (0–100).</summary>
internal sealed record RemoteHost(string Os, string Arch, string Hostname, long? UptimeSeconds, double? CpuPct,
    int? CpuCount, double[]? Load, ulong? MemTotal, ulong? MemUsed, ulong? SwapTotal, ulong? SwapUsed);

/// <summary>A process of the server's tree; cpu_pct is of one core (four busy cores read 400).</summary>
internal sealed record RemoteProcess(int? Pid, string Name, string Model, double? CpuPct, ulong? Rss);

/// <summary>The server's process tree (/api/xollama/system "server").</summary>
internal sealed record RemoteServerProcess(int? Pid, double? CpuPct, ulong? Rss, int? Procs, List<RemoteProcess> Children);

/// <summary>GET /api/xollama/system: one cached snapshot of the host, the server's processes and every GPU.</summary>
internal sealed record RemoteSystem(DateTime? AsOf, RemoteHost? Host, RemoteServerProcess? Server, List<RemoteDevice> Devices);

/// <summary>
/// What a remote xOllama's engine says about one loaded model; null fields were not answered. Tokens per second
/// is opencoti's decoded-token counter (polykv/tps) between two polls when it has one, else the tokens decoded
/// between two /slots reads.
/// </summary>
internal sealed record RemoteEngineStats(string Engine, bool Readable)
{
    public int? SlotsBusy { get; init; }
    public int? SlotsTotal { get; init; }
    public double? TokensPerSecond { get; init; }
    public int? Refused60s { get; init; }
    public bool PressureActive { get; init; }
    /// <summary>The engine's last props, kv and slots replies (the detail box reads them); null when not answered.</summary>
    public JsonElement? Props { get; init; }
    public JsonElement? Kv { get; init; }
    public JsonElement? Slots { get; init; }
    /// <summary>opencoti's measurement of the engine process's GPU memory (props gpu_process_memory); null when not observable.</summary>
    public GpuProcessMemory? Process => GpuProcessMemory.FromProps(Props);
}

/// <summary>
/// Everything psmonitor shows for a server: what that server answers, and for a server on this machine without
/// system telemetry, this machine's own readings in System (LocalReadings is then true).
/// </summary>
internal sealed record RemoteSnapshot
{
    public bool Connected { get; init; }
    public ServerFlavor Flavor { get; init; }
    public string Version { get; init; } = "";
    public double RoundTripMs { get; init; }
    public List<RunningModel> Models { get; init; } = new();
    public int? StoredModels { get; init; }
    public long? StoredBytes { get; init; }
    public bool? CloudDisabled { get; init; }
    public List<RemoteDevice>? Devices { get; init; }
    public DateTime? DevicesAsOf { get; init; }
    /// <summary>The server's system telemetry; null when it has none (stock Ollama, xOllama without system_telemetry_v1).</summary>
    public RemoteSystem? System { get; init; }
    /// <summary>System and Devices were read on this machine, not sent by the server.</summary>
    public bool LocalReadings { get; init; }
    /// <summary>
    /// Host RAM the KV rolling window holds, per loaded model (xOllama, kv_host_telemetry_v1); only the models whose
    /// engine reports it, null when the server reports it for none.
    /// </summary>
    public Dictionary<string, ulong>? KvHostBytes { get; init; }
    /// <summary>Where each loaded model sits (/api/xollama/system models[]); empty without system telemetry.</summary>
    public Dictionary<string, RemoteModelPlacement> Placements { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>The selected model's /api/show (model_info, drafter, think budget), once it has been read.</summary>
    public JsonElement? SelectedShow { get; init; }
    public Dictionary<string, RemoteEngineStats> Engine { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Collects <see cref="RemoteSnapshot"/>s for psmonitor against a server on another machine. Every call is cheap:
/// connections are pooled and kept alive, /api/ps is the only request made on every tick, the rest is cached
/// (version and cloud status once, stored models every minute) and xOllama's device list, which makes the server
/// rediscover its GPUs, is refreshed at most once a minute in the background so a tick never waits for it.
/// An xOllama with system telemetry (feature system_telemetry_v1) is read from /api/xollama/system instead: a
/// snapshot the server caches and refreshes on its own timer, so it is read on every tick and the device list
/// is never asked for.
/// Per-model engine reads run in parallel (capped) and an endpoint the engine does not have is not asked again
/// until the model reloads.
/// </summary>
internal sealed class RemoteServerMonitor
{
    public static readonly TimeSpan DevicesInterval = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan TagsInterval = TimeSpan.FromSeconds(60);
    public const string SystemTelemetryFeature = "system_telemetry_v1";
    /// <summary>xOllama reports the host RAM of each model's KV rolling window in /api/xollama/system (models[].kv).</summary>
    public const string KvHostFeature = "kv_host_telemetry_v1";
    private const int MaxParallelEngineReads = 4;
    /// <summary>How often a model's engine props (about 14 KB on opencoti) are read again; every refresh for the selected model.</summary>
    public static readonly TimeSpan PropsInterval = TimeSpan.FromSeconds(30);

    private static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
        MaxConnectionsPerServer = MaxParallelEngineReads + 2,
    })
    { Timeout = TimeSpan.FromSeconds(10) };

    private static readonly JsonSerializerOptions PsJson = new() { PropertyNameCaseInsensitive = true };

    private readonly string _url;
    private readonly string? _apiKey;
    private ServerFlavor _flavor = ServerFlavor.Unknown;
    private string _version = "";
    private bool? _cloudDisabled;
    private bool _identityRead;
    private bool _telemetry;
    private bool _kvHost;

    private int? _storedModels;
    private long? _storedBytes;
    private DateTime _tagsAt = DateTime.MinValue;

    private volatile List<RemoteDevice>? _devices;
    private DateTime? _devicesAsOf;
    private DateTime _devicesRequestedAt = DateTime.MinValue;
    private int _devicesInFlight;

    private string _engineListKey = "";
    private Dictionary<string, (string Engine, bool Readable)> _engineList = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _missingEndpoints = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, (List<SlotReading>? Slots, DateTime At)> _lastSlots = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, TpsReading> _lastTps = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, (JsonElement Body, DateTime At)> _lastProps = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, JsonElement> _show = new(StringComparer.OrdinalIgnoreCase);
    private volatile string? _selected;

    public RemoteServerMonitor(string url, string? apiKey)
    {
        _url = url.TrimEnd('/');
        _apiKey = apiKey;
    }

    /// <summary>The server this monitor reads.</summary>
    public string Url => _url;

    /// <summary>
    /// Unloads a model (psmonitor U): a generate request with keep_alive 0 and no prompt, as `ollama stop` does, on
    /// Ollama and xOllama alike. An embedding-only model that refuses generate is unloaded through /api/embed.
    /// Returns null on success, else the server's error.
    /// </summary>
    public async Task<string?> UnloadAsync(string model, CancellationToken cancellationToken = default)
    {
        async Task<(bool Ok, string Error)> PostAsync(string path, object body)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{_url}/{path}")
            {
                Content = new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json"),
            };
            if (_apiKey != null && _flavor == ServerFlavor.XOllama)
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
            using var response = await Http.SendAsync(request, cancellationToken);
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            return (response.IsSuccessStatusCode, response.IsSuccessStatusCode ? "" : ServerError(text, (int)response.StatusCode));
        }

        try
        {
            var (ok, error) = await PostAsync("api/generate", new { model, keep_alive = 0, stream = false });
            if (ok) return null;
            var (embedOk, _) = await PostAsync("api/embed", new { model, input = Array.Empty<string>(), keep_alive = 0 });
            return embedOk ? null : error;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>The "error" field of a server's reply, else its status code.</summary>
    internal static string ServerError(string body, int status)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String)
                return e.GetString() ?? $"HTTP {status}";
        }
        catch { }
        return $"HTTP {status}";
    }

    /// <summary>Asks for a fresh device list on the next tick (R key), still at most one request at a time.</summary>
    public void RefreshDevicesSoon() => _devicesRequestedAt = DateTime.MinValue;

    /// <summary>
    /// The model the detail box shows (null: none). Its engine props are read on every refresh, and its /api/show
    /// once; both start reading now, so the box fills before the next refresh.
    /// </summary>
    public void Select(string? model, Action? read = null)
    {
        _selected = model;
        if (model == null) return;
        _ = Task.Run(async () =>
        {
            try
            {
                var tasks = new List<Task> { ReadShowAsync(model, CancellationToken.None) };
                if (_flavor == ServerFlavor.XOllama && !_missingEndpoints.ContainsKey(model + "\nprops"))
                    tasks.Add(ReadPropsAsync(model, CancellationToken.None));
                await Task.WhenAll(tasks);
                read?.Invoke();
            }
            catch { }
        });
    }

    private async Task ReadShowAsync(string model, CancellationToken cancellationToken)
    {
        if (_show.ContainsKey(model)) return;
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{_url}/api/show")
            {
                Content = new StringContent(JsonSerializer.Serialize(new { model }), System.Text.Encoding.UTF8, "application/json"),
            };
            if (_apiKey != null && _flavor == ServerFlavor.XOllama)
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
            using var response = await Http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return;
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            _show[model] = ShowSummary(doc.RootElement);
        }
        catch { }
    }

    private async Task ReadPropsAsync(string model, CancellationToken cancellationToken)
    {
        if (await ReadEngineAsync(model, "props", cancellationToken) is { Body: { } body })
            _lastProps[model] = (body, DateTime.UtcNow);
    }

    /// <summary>The parts of /api/show the detail box reads (model_info without its token lists, drafter, think budget).</summary>
    public static JsonElement ShowSummary(JsonElement root)
    {
        var keep = new Dictionary<string, object?>();
        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in new[] { "drafter", "think_budget", "think_budget_tokens", "capabilities" })
                if (root.TryGetProperty(name, out var v)) keep[name] = v.Clone();
            if (root.TryGetProperty("model_info", out var info) && info.ValueKind == JsonValueKind.Object)
                keep["model_info"] = info.EnumerateObject()
                    .Where(p => !p.Name.StartsWith("tokenizer.", StringComparison.Ordinal) &&
                                (p.Value.ValueKind != JsonValueKind.Array || p.Value.GetArrayLength() <= 1024))
                    .ToDictionary(p => p.Name, p => p.Value.Clone());
        }
        return JsonSerializer.SerializeToElement(keep);
    }

    public async Task<RemoteSnapshot> CollectAsync(CancellationToken cancellationToken = default)
    {
        if (!_identityRead)
        {
            _flavor = GetFlavor(_url);
            _version = await ReadStringFieldAsync("api/version", "version", cancellationToken) ?? "";
            _cloudDisabled = await ReadCloudDisabledAsync(cancellationToken);
            if (_flavor == ServerFlavor.XOllama)
            {
                using var identity = await GetJsonAsync("api/xollama", cancellationToken);
                _telemetry = identity != null && HasFeature(identity.RootElement, SystemTelemetryFeature);
                _kvHost = _telemetry && HasFeature(identity!.RootElement, KvHostFeature);
            }
            _identityRead = _flavor != ServerFlavor.Unknown || _version.Length > 0;
        }

        var watch = Stopwatch.StartNew();
        List<RunningModel> models;
        try
        {
            using var response = await SendAsync(HttpMethod.Get, "api/ps", cancellationToken);
            if (!response.IsSuccessStatusCode)
                return new RemoteSnapshot { Connected = false, Flavor = _flavor, Version = _version };
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            watch.Stop();
            models = JsonSerializer.Deserialize<ProcessStatusResponse>(body, PsJson)?.Models ?? new();
        }
        catch
        {
            return new RemoteSnapshot { Connected = false, Flavor = _flavor, Version = _version };
        }

        if (DateTime.UtcNow - _tagsAt >= TagsInterval)
        {
            _tagsAt = DateTime.UtcNow;
            await ReadTagsAsync(cancellationToken);
        }

        var engine = new Dictionary<string, RemoteEngineStats>(StringComparer.OrdinalIgnoreCase);
        RemoteSystem? system = null;
        Dictionary<string, ulong>? kvHost = null;
        var placements = new Dictionary<string, RemoteModelPlacement>(StringComparer.OrdinalIgnoreCase);
        var selected = _selected;
        if (selected != null && models.Any(m => string.Equals(m.Name, selected, StringComparison.OrdinalIgnoreCase)))
            await ReadShowAsync(selected, cancellationToken);
        if (_flavor == ServerFlavor.XOllama)
        {
            if (_telemetry)
            {
                using var doc = await GetJsonAsync("api/xollama/system", cancellationToken);
                system = doc != null ? ParseSystem(doc.RootElement) : null;
                if (doc != null && _kvHost) kvHost = ParseKvHost(doc.RootElement);
                if (doc != null) placements = ParsePlacements(doc.RootElement);
                if (system != null)
                {
                    _devices = system.Devices;
                    _devicesAsOf = system.AsOf ?? DateTime.UtcNow;
                }
            }
            if (system == null)
                StartDevicesRefreshIfDue();
            if (models.Count > 0)
                engine = await ReadEngineStatsAsync(models, cancellationToken);
        }

        return new RemoteSnapshot
        {
            Connected = true,
            Flavor = _flavor,
            Version = _version,
            RoundTripMs = watch.Elapsed.TotalMilliseconds,
            Models = models,
            StoredModels = _storedModels,
            StoredBytes = _storedBytes,
            CloudDisabled = _cloudDisabled,
            Devices = _devices,
            DevicesAsOf = _devicesAsOf,
            System = system,
            KvHostBytes = kvHost,
            Placements = placements,
            SelectedShow = selected != null && _show.TryGetValue(selected, out var show) ? show : null,
            Engine = engine,
        };
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(method, $"{_url}/{path}");
        // The key is xOllama's: never hand it to a server that is not one
        if (_apiKey != null && _flavor == ServerFlavor.XOllama)
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
        return Http.SendAsync(request, cancellationToken);
    }

    private async Task<JsonDocument?> GetJsonAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await SendAsync(HttpMethod.Get, path, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        }
        catch
        {
            return null;
        }
    }

    private async Task<string?> ReadStringFieldAsync(string path, string field, CancellationToken cancellationToken)
    {
        using var doc = await GetJsonAsync(path, cancellationToken);
        return doc != null && doc.RootElement.ValueKind == JsonValueKind.Object &&
               doc.RootElement.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private async Task<bool?> ReadCloudDisabledAsync(CancellationToken cancellationToken)
    {
        using var doc = await GetJsonAsync("api/status", cancellationToken);
        return doc != null && doc.RootElement.ValueKind == JsonValueKind.Object &&
               doc.RootElement.TryGetProperty("cloud", out var cloud) && cloud.ValueKind == JsonValueKind.Object &&
               cloud.TryGetProperty("disabled", out var disabled) && disabled.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? disabled.GetBoolean()
            : null;
    }

    private async Task ReadTagsAsync(CancellationToken cancellationToken)
    {
        using var doc = await GetJsonAsync("api/tags", cancellationToken);
        if (doc == null || !doc.RootElement.TryGetProperty("models", out var list) || list.ValueKind != JsonValueKind.Array)
            return;
        _storedModels = list.GetArrayLength();
        _storedBytes = list.EnumerateArray().Sum(m => m.TryGetProperty("size", out var s) && s.TryGetInt64(out var v) ? v : 0);
    }

    private void StartDevicesRefreshIfDue()
    {
        if (DateTime.UtcNow - _devicesRequestedAt < DevicesInterval) return;
        if (Interlocked.CompareExchange(ref _devicesInFlight, 1, 0) != 0) return;
        _devicesRequestedAt = DateTime.UtcNow;
        _ = Task.Run(async () =>
        {
            try
            {
                using var doc = await GetJsonAsync("api/xollama/devices", CancellationToken.None);
                if (doc != null)
                {
                    _devices = ParseDevices(doc.RootElement);
                    _devicesAsOf = DateTime.UtcNow;
                }
            }
            finally
            {
                Interlocked.Exchange(ref _devicesInFlight, 0);
            }
        });
    }

    private async Task<Dictionary<string, RemoteEngineStats>> ReadEngineStatsAsync(List<RunningModel> models, CancellationToken cancellationToken)
    {
        // The engine list only changes when the loaded models do, or while one is still loading (not readable yet)
        var key = string.Join("|", models.Select(m => m.Name + "@" + m.Digest).OrderBy(s => s, StringComparer.Ordinal));
        if (key != _engineListKey || _engineList.Values.Any(e => !e.Readable) || _engineList.Count == 0)
        {
            if (key != _engineListKey)
            {
                _missingEndpoints.Clear();
                _lastSlots.Clear();
                _lastTps.Clear();
                _lastProps.Clear();
                _show.Clear();
            }
            using var doc = await GetJsonAsync("api/engine", cancellationToken);
            _engineList = doc != null ? ParseEngineList(doc.RootElement) : new(StringComparer.OrdinalIgnoreCase);
            _engineListKey = key;
        }

        var result = new ConcurrentDictionary<string, RemoteEngineStats>(StringComparer.OrdinalIgnoreCase);
        using var gate = new SemaphoreSlim(MaxParallelEngineReads);
        var reads = _engineList.Select(async entry =>
        {
            var (name, (engine, readable)) = (entry.Key, entry.Value);
            var stats = new RemoteEngineStats(engine, readable);
            if (readable)
            {
                await gate.WaitAsync(cancellationToken);
                try
                {
                    var opencoti = engine.Equals("opencoti", StringComparison.OrdinalIgnoreCase);
                    var slots = ReadEngineAsync(name, "slots", cancellationToken);
                    var kv = opencoti ? ReadEngineAsync(name, "kv", cancellationToken) : Task.FromResult<EngineReply?>(null);
                    var tps = opencoti ? ReadEngineAsync(name, "polykv/tps", cancellationToken, "&once=1") : Task.FromResult<EngineReply?>(null);
                    // props are mostly settings: every PropsInterval, every refresh for the model the detail box shows
                    var propsDue = !_lastProps.TryGetValue(name, out var lastProps) || DateTime.UtcNow - lastProps.At >= PropsInterval ||
                                   string.Equals(name, _selected, StringComparison.OrdinalIgnoreCase);
                    var props = propsDue ? ReadPropsAsync(name, cancellationToken) : Task.CompletedTask;
                    await Task.WhenAll(slots, kv, tps, props);
                    stats = stats with
                    {
                        Props = _lastProps.TryGetValue(name, out var p0) ? p0.Body : null,
                        Kv = kv.Result?.Body,
                        Slots = slots.Result?.Body,
                    };

                    if (slots.Result?.Body is { } slotsBody && ParseSlots(slotsBody) is { } s)
                    {
                        var now = DateTime.UtcNow;
                        _lastSlots.TryGetValue(name, out var last);
                        stats = stats with
                        {
                            SlotsBusy = s.Count(x => x.Processing),
                            SlotsTotal = s.Count,
                            TokensPerSecond = DecodeRate(last.Slots, s, (now - last.At).TotalSeconds),
                        };
                        _lastSlots[name] = (s, now);
                    }
                    if (kv.Result?.Body is { } kvBody && ParseKvPressure(kvBody) is { } p)
                        stats = stats with { Refused60s = p.Refused60s, PressureActive = p.Active };
                    if (tps.Result?.Body is { } tpsBody && ParseTpsReading(tpsBody) is { } reading)
                    {
                        // The counter between two polls; on the first poll, the speed of the slots decoding now
                        _lastTps.TryGetValue(name, out var previous);
                        stats = stats with { TokensPerSecond = CounterRate(previous, reading) ?? reading.ActiveEwma };
                        _lastTps[name] = reading;
                    }
                }
                finally
                {
                    gate.Release();
                }
            }
            result[name] = stats;
        });
        await Task.WhenAll(reads);
        return new Dictionary<string, RemoteEngineStats>(result, StringComparer.OrdinalIgnoreCase);
    }

    private sealed record EngineReply(JsonElement? Body, string? Text);

    /// <summary>
    /// One read through /api/engine. Null when it did not answer 200; an endpoint the engine does not have
    /// (404/501, or 400 from an xOllama that does not list it) is remembered and not asked again for this model.
    /// </summary>
    private async Task<EngineReply?> ReadEngineAsync(string model, string endpoint, CancellationToken cancellationToken, string extraQuery = "")
    {
        var missingKey = model + "\n" + endpoint;
        if (_missingEndpoints.ContainsKey(missingKey)) return null;
        try
        {
            var path = $"api/engine?model={Uri.EscapeDataString(model)}&endpoint={endpoint}{extraQuery}";
            using var response = await SendAsync(HttpMethod.Get, path, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.StatusCode == HttpStatusCode.BadRequest || response.StatusCode == HttpStatusCode.NotImplemented)
            {
                _missingEndpoints[missingKey] = 0;
                return null;
            }
            if (!response.IsSuccessStatusCode) return null;
            if (response.Content.Headers.ContentType?.MediaType == "text/event-stream")
                return new EngineReply(null, content);

            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            var status = root.TryGetProperty("status", out var st) && st.TryGetInt32(out var code) ? code : 200;
            if (status is 404 or 501)
            {
                _missingEndpoints[missingKey] = 0;
                return null;
            }
            if (status != 200) return null;
            JsonElement? body = root.TryGetProperty("body", out var b) && b.ValueKind != JsonValueKind.Null ? b.Clone() : null;
            var text = root.TryGetProperty("text", out var tx) && tx.ValueKind == JsonValueKind.String ? tx.GetString() : null;
            return new EngineReply(body, text);
        }
        catch
        {
            return null;
        }
    }

    // ---- parsers (pure, unit-tested) ----

    /// <summary>
    /// GET /api/xollama/devices: one entry per GPU. A GPU reachable through two backends is listed twice (CUDA
    /// and Vulkan, same PCI id): the first listing is the one xOllama serves it through, the copies are dropped.
    /// </summary>
    public static List<RemoteDevice> ParseDevices(JsonElement root)
    {
        var result = new List<RemoteDevice>();
        if (!root.TryGetProperty("devices", out var devices) || devices.ValueKind != JsonValueKind.Array) return result;
        var seenPci = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in devices.EnumerateArray())
        {
            if (d.ValueKind != JsonValueKind.Object) continue;
            var pci = Str(d, "pci_id");
            if (pci.Length > 0 && !seenPci.Add(pci)) continue;
            result.Add(new RemoteDevice(Str(d, "backend"), Str(d, "id"), pci,
                // devices: "name" is the backend's device name (CUDA0, Vulkan1) and "description" the card; system: "name" is the card
                Str(d, "description") is { Length: > 0 } desc ? desc : Str(d, "name"),
                d.TryGetProperty("integrated", out var i) && i.ValueKind == JsonValueKind.True,
                U64(d, "total_memory"), U64(d, "free_memory"), Str(d, "engine"))
            {
                ReportedUsed = U64(d, "used_memory"),
                Vendor = Str(d, "vendor"),
                Telemetry = d.TryGetProperty("source", out _) ? ParseTelemetry(d) : null,
            });
        }
        return result;
    }

    private static RemoteGpuTelemetry ParseTelemetry(JsonElement d)
    {
        var link = d.TryGetProperty("link", out var l) && l.ValueKind == JsonValueKind.Object ? l : default;
        int? LinkInt(string name) => link.ValueKind == JsonValueKind.Object && Num(link, name) is { } v ? (int)v : null;
        return new RemoteGpuTelemetry(Str(d, "source"), Num(d, "util_pct"), Num(d, "mem_util_pct"), Num(d, "temp_c"),
            Num(d, "power_w"), Num(d, "power_limit_w"), Num(d, "clock_mhz"), Num(d, "clock_max_mhz"), Num(d, "fan_pct"),
            LinkInt("gen"), LinkInt("width"), LinkInt("max_gen"), LinkInt("max_width"));
    }

    /// <summary>
    /// GET /api/xollama/system (system_telemetry_v1). Every field the platform cannot read is null and stays null
    /// here: nothing is defaulted to 0. Devices are every GPU the driver reports, one entry each; backend and engine
    /// are empty for a GPU the server does not use.
    /// </summary>
    public static RemoteSystem? ParseSystem(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;
        DateTime? asOf = root.TryGetProperty("as_of", out var a) && a.ValueKind == JsonValueKind.String &&
                         DateTimeOffset.TryParse(a.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)
            ? at.UtcDateTime
            : null;

        RemoteHost? host = null;
        if (root.TryGetProperty("host", out var h) && h.ValueKind == JsonValueKind.Object)
        {
            double[]? load = h.TryGetProperty("load", out var ld) && ld.ValueKind == JsonValueKind.Array
                ? ld.EnumerateArray().Select(x => x.TryGetDouble(out var v) ? v : double.NaN).ToArray()
                : null;
            host = new RemoteHost(Str(h, "os"), Str(h, "arch"), Str(h, "hostname"), (long?)Num(h, "uptime_s"),
                Num(h, "cpu_pct"), (int?)Num(h, "cpu_count"), load,
                U64(h, "mem_total"), U64(h, "mem_used"), U64(h, "swap_total"), U64(h, "swap_used"));
        }

        RemoteServerProcess? server = null;
        if (root.TryGetProperty("server", out var sv) && sv.ValueKind == JsonValueKind.Object)
        {
            var children = sv.TryGetProperty("children", out var ch) && ch.ValueKind == JsonValueKind.Array
                ? ch.EnumerateArray().Where(c => c.ValueKind == JsonValueKind.Object)
                    .Select(c => new RemoteProcess((int?)Num(c, "pid"), Str(c, "name"), Str(c, "model"), Num(c, "cpu_pct"), U64(c, "rss")))
                    .ToList()
                : new List<RemoteProcess>();
            server = new RemoteServerProcess((int?)Num(sv, "pid"), Num(sv, "cpu_pct"), U64(sv, "rss"), (int?)Num(sv, "procs"), children);
        }

        return new RemoteSystem(asOf, host, server, ParseDevices(root));
    }

    /// <summary>
    /// The host RAM each loaded model's KV rolling window holds (/api/xollama/system models[].kv.host_bytes,
    /// kv_host_telemetry_v1): 0 while the window is not engaged. A model the engine cannot read it for (null) is left
    /// out; null when no model reports it.
    /// </summary>
    public static Dictionary<string, ulong>? ParseKvHost(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array)
            return null;
        var result = new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in models.EnumerateArray())
        {
            if (m.ValueKind != JsonValueKind.Object || !m.TryGetProperty("kv", out var kv) || kv.ValueKind != JsonValueKind.Object) continue;
            var name = Str(m, "name");
            if (name.Length > 0 && U64(kv, "host_bytes") is { } bytes)
                result[name] = result.GetValueOrDefault(name) + bytes;
        }
        return result.Count > 0 ? result : null;
    }

    /// <summary>/api/xollama/system models[]: where each loaded model sits, its GPUs and its KV window. Empty when absent.</summary>
    public static Dictionary<string, RemoteModelPlacement> ParsePlacements(JsonElement root)
    {
        var result = new Dictionary<string, RemoteModelPlacement>(StringComparer.OrdinalIgnoreCase);
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array)
            return result;
        foreach (var m in models.EnumerateArray())
        {
            if (m.ValueKind != JsonValueKind.Object) continue;
            var name = Str(m, "name");
            if (name.Length == 0) continue;
            var gpus = m.TryGetProperty("devices", out var ds) && ds.ValueKind == JsonValueKind.Array
                ? ds.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object)
                    .Select(x => new RemoteModelGpu(Str(x, "backend"), Str(x, "id"), Str(x, "pci_id"), U64(x, "vram") ?? 0)).ToList()
                : new List<RemoteModelGpu>();
            var placement = new RemoteModelPlacement(name, (long)(U64(m, "size") ?? 0), (long)(U64(m, "size_vram") ?? 0), gpus);
            if (m.TryGetProperty("kv", out var kv) && kv.ValueKind == JsonValueKind.Object)
            {
                placement = placement with
                {
                    KvResidencyMode = Str(kv, "residency_mode") is { Length: > 0 } mode ? mode : null,
                    KvEngaged = kv.TryGetProperty("engaged", out var e) && e.ValueKind is JsonValueKind.True or JsonValueKind.False ? e.GetBoolean() : null,
                    KvArn = kv.TryGetProperty("kvarn", out var k) && k.ValueKind == JsonValueKind.True,
                    KvDeviceBytes = U64(kv, "device_bytes"),
                    KvHostBytes = U64(kv, "host_bytes"),
                };
            }
            result[name] = placement;
        }
        return result;
    }

    /// <summary>Whether GET /api/xollama lists <paramref name="feature"/> in its "features".</summary>
    public static bool HasFeature(JsonElement root, string feature) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty("features", out var f) && f.ValueKind == JsonValueKind.Array &&
        f.EnumerateArray().Any(x => x.ValueKind == JsonValueKind.String && x.GetString() == feature);

    /// <summary>GET /api/engine: loaded model → (engine actually serving it, readable).</summary>
    public static Dictionary<string, (string Engine, bool Readable)> ParseEngineList(JsonElement root)
    {
        var result = new Dictionary<string, (string, bool)>(StringComparer.OrdinalIgnoreCase);
        if (!root.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array) return result;
        foreach (var m in models.EnumerateArray())
        {
            var name = Str(m, "model");
            if (name.Length == 0) continue;
            result[name] = (Str(m, "engine"), m.TryGetProperty("readable", out var r) && r.ValueKind == JsonValueKind.True);
        }
        return result;
    }

    /// <summary>One slot of the engine's /slots: its task, whether it is generating, and the tokens decoded so far.</summary>
    public sealed record SlotReading(int Id, long Task, bool Processing, long Decoded);

    /// <summary>
    /// The engine's /slots (llama.cpp): an array of slots, busy when "is_processing" is true (older builds: a
    /// non-zero "state"), with the task's decoded tokens in next_token[0].n_decoded. Null when the body is not a
    /// slot list.
    /// </summary>
    public static List<SlotReading>? ParseSlots(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Array) return null;
        var result = new List<SlotReading>();
        foreach (var slot in body.EnumerateArray())
        {
            if (slot.ValueKind != JsonValueKind.Object) continue;
            var processing = slot.TryGetProperty("is_processing", out var p)
                ? p.ValueKind == JsonValueKind.True
                : slot.TryGetProperty("state", out var s) && s.TryGetInt32(out var state) && state != 0;
            long decoded = 0;
            if (slot.TryGetProperty("next_token", out var next))
            {
                var first = next.ValueKind == JsonValueKind.Array && next.GetArrayLength() > 0 ? next[0] : next;
                if (first.ValueKind == JsonValueKind.Object && first.TryGetProperty("n_decoded", out var d) && d.TryGetInt64(out var dv))
                    decoded = dv;
            }
            result.Add(new SlotReading(
                slot.TryGetProperty("id", out var id) && id.TryGetInt32(out var iv) ? iv : result.Count,
                slot.TryGetProperty("id_task", out var t) && t.TryGetInt64(out var tv) ? tv : -1,
                processing, decoded));
        }
        return result;
    }

    /// <summary>
    /// Generation speed between two /slots reads: tokens decoded since <paramref name="previous"/> by the slots
    /// still on the same task, over <paramref name="seconds"/>. Null when there is no earlier read to compare with.
    /// </summary>
    public static double? DecodeRate(IReadOnlyList<SlotReading>? previous, IReadOnlyList<SlotReading> current, double seconds)
    {
        if (previous == null || seconds <= 0) return null;
        long tokens = 0;
        foreach (var slot in current)
        {
            var before = previous.FirstOrDefault(p => p.Id == slot.Id);
            if (before != null && before.Task == slot.Task && slot.Decoded >= before.Decoded)
                tokens += slot.Decoded - before.Decoded;
            else if (before != null && before.Task != slot.Task && slot.Processing)
                tokens += slot.Decoded;  // a task that started after the last read
        }
        return tokens / seconds;
    }

    /// <summary>
    /// opencoti's /kv pressure block (kv_pressure_v1): bookings refused in the engine's window, and whether the
    /// last refusal is still inside it. Null when the engine does not report pressure.
    /// </summary>
    public static (int Refused60s, bool Active)? ParseKvPressure(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty("pressure", out var p) || p.ValueKind != JsonValueKind.Object)
            return null;
        var refused = p.TryGetProperty("refused_60s", out var r) && r.TryGetInt32(out var rv) ? rv : 0;
        var window = p.TryGetProperty("window_s", out var w) && w.TryGetDouble(out var wv) && wv > 0 ? wv : 60;
        var active = refused > 0 && p.TryGetProperty("last_refusal_age_s", out var a) && a.TryGetDouble(out var age) && age >= 0 && age < window;
        return (refused, active);
    }

    /// <summary>One polykv/tps reading: the engine's decoded-token counter, its clock, and the decoding sessions' speed.</summary>
    public sealed record TpsReading(long DecodedTotal, long TimestampUs, double ActiveEwma);

    /// <summary>
    /// opencoti's polykv/tps with once=1 (inside the /api/engine "body"): n_decoded_cum_total (tokens decoded over
    /// the engine's life, accepted drafts included, rejected ones not), ts_us (the engine clock) and per slot a
    /// decode-only tps_ewma. An idle slot keeps its last tps_ewma, so only the "active" ones count. Null when the
    /// body has no counter.
    /// </summary>
    public static TpsReading? ParseTpsReading(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object ||
            !body.TryGetProperty("n_decoded_cum_total", out var d) || !d.TryGetInt64(out var decoded) ||
            !body.TryGetProperty("ts_us", out var t) || !t.TryGetInt64(out var ts))
            return null;
        double active = 0;
        if (body.TryGetProperty("sessions", out var sessions) && sessions.ValueKind == JsonValueKind.Array)
        {
            foreach (var session in sessions.EnumerateArray())
            {
                if (session.ValueKind == JsonValueKind.Object &&
                    session.TryGetProperty("active", out var a) && a.ValueKind == JsonValueKind.True &&
                    session.TryGetProperty("tps_ewma", out var e) && e.TryGetDouble(out var ewma))
                    active += ewma;
            }
        }
        return new TpsReading(decoded, ts, active);
    }

    /// <summary>
    /// Tokens per second between two readings of the engine's counter: exact over the interval, 0 while idle (what
    /// the engine's own aggregate_tps does). Null without an earlier reading, or when the counter or the clock went
    /// back (the engine restarted, a slot went away): that interval is skipped.
    /// </summary>
    public static double? CounterRate(TpsReading? previous, TpsReading current)
    {
        if (previous == null) return null;
        var tokens = current.DecodedTotal - previous.DecodedTotal;
        var micros = current.TimestampUs - previous.TimestampUs;
        if (tokens < 0 || micros <= 0) return null;
        return tokens / (micros / 1_000_000.0);
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static ulong? U64(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetUInt64(out var u) ? u : null;

    private static double? Num(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) ? d : null;

    // ---- local or remote ----

    /// <summary>
    /// Whether <paramref name="url"/> is a server on this machine: localhost, a loopback or unspecified address, this
    /// machine's name, or one of its own interface addresses. psmonitor shows local system and GPU stats only then.
    /// </summary>
    public static bool IsThisMachine(string url)
    {
        if (XOllamaTweak.IsOnThisMachine(url)) return true;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        var host = uri.Host.Trim('[', ']');
        if (host.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase) ||
            host.Equals(Dns.GetHostName(), StringComparison.OrdinalIgnoreCase))
            return true;
        try
        {
            IPAddress[] addresses;
            if (IPAddress.TryParse(host, out var ip))
                addresses = new[] { ip };
            else
            {
                var lookup = Dns.GetHostAddressesAsync(host);
                if (!lookup.Wait(TimeSpan.FromSeconds(2))) return false;
                addresses = lookup.Result;
            }
            return AnyLocal(addresses, LocalAddresses());
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Whether any of <paramref name="hostAddresses"/> is loopback or one of <paramref name="localAddresses"/>.</summary>
    public static bool AnyLocal(IEnumerable<IPAddress> hostAddresses, IEnumerable<IPAddress> localAddresses)
    {
        var local = new HashSet<IPAddress>(localAddresses.Select(Normalize));
        return hostAddresses.Select(Normalize).Any(a => IPAddress.IsLoopback(a) || local.Contains(a));
    }

    private static IPAddress Normalize(IPAddress a) => a.IsIPv4MappedToIPv6 ? a.MapToIPv4() : a;

    private static IEnumerable<IPAddress> LocalAddresses()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Select(u => u.Address)
                .ToList();
        }
        catch
        {
            return Array.Empty<IPAddress>();
        }
    }
}
