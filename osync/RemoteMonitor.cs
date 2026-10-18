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

/// <summary>One GPU as a remote xOllama reports it (GET /api/xollama/devices).</summary>
internal sealed record RemoteDevice(string Backend, string Id, string PciId, string Name, bool Integrated,
    ulong TotalMemory, ulong FreeMemory, string Engine)
{
    public ulong UsedMemory => TotalMemory > FreeMemory ? TotalMemory - FreeMemory : 0;
}

/// <summary>
/// What a remote xOllama's engine says about one loaded model; null fields were not answered. Tokens per second
/// is opencoti's own reading (polykv/tps) when it has one, else the tokens decoded between two /slots reads.
/// </summary>
internal sealed record RemoteEngineStats(string Engine, bool Readable)
{
    public int? SlotsBusy { get; init; }
    public int? SlotsTotal { get; init; }
    public double? TokensPerSecond { get; init; }
    public int? Refused60s { get; init; }
    public bool PressureActive { get; init; }
}

/// <summary>Everything psmonitor shows for a server on another machine: only what that server answers.</summary>
internal sealed class RemoteSnapshot
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
    public Dictionary<string, RemoteEngineStats> Engine { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Collects <see cref="RemoteSnapshot"/>s for psmonitor against a server on another machine. Every call is cheap:
/// connections are pooled and kept alive, /api/ps is the only request made on every tick, the rest is cached
/// (version and cloud status once, stored models every minute) and xOllama's device list, which makes the server
/// rediscover its GPUs, is refreshed at most once a minute in the background so a tick never waits for it.
/// Per-model engine reads run in parallel (capped) and an endpoint the engine does not have is not asked again
/// until the model reloads.
/// </summary>
internal sealed class RemoteServerMonitor
{
    public static readonly TimeSpan DevicesInterval = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan TagsInterval = TimeSpan.FromSeconds(60);
    private const int MaxParallelEngineReads = 4;

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

    public RemoteServerMonitor(string url, string? apiKey)
    {
        _url = url.TrimEnd('/');
        _apiKey = apiKey;
    }

    /// <summary>Asks for a fresh device list on the next tick (R key), still at most one request at a time.</summary>
    public void RefreshDevicesSoon() => _devicesRequestedAt = DateTime.MinValue;

    public async Task<RemoteSnapshot> CollectAsync(CancellationToken cancellationToken = default)
    {
        if (!_identityRead)
        {
            _flavor = GetFlavor(_url);
            _version = await ReadStringFieldAsync("api/version", "version", cancellationToken) ?? "";
            _cloudDisabled = await ReadCloudDisabledAsync(cancellationToken);
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
        if (_flavor == ServerFlavor.XOllama)
        {
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
                    await Task.WhenAll(slots, kv, tps);

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
                    if (tps.Result is { } tpsReply && ParseTokensPerSecond(tpsReply.Body, tpsReply.Text) is { } t)
                        stats = stats with { TokensPerSecond = t };
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
            var pci = Str(d, "pci_id");
            if (pci.Length > 0 && !seenPci.Add(pci)) continue;
            result.Add(new RemoteDevice(Str(d, "backend"), Str(d, "id"), pci,
                // "name" is the backend's device name (CUDA0, Vulkan1); "description" is the card
                Str(d, "description") is { Length: > 0 } desc ? desc : Str(d, "name"),
                d.TryGetProperty("integrated", out var i) && i.ValueKind == JsonValueKind.True,
                U64(d, "total_memory"), U64(d, "free_memory"), Str(d, "engine")));
        }
        return result;
    }

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

    /// <summary>
    /// opencoti's /polykv/tps reading: the generation speed summed over the sessions. Accepts the JSON body
    /// ({"tps":x}, or a list/"sessions" of entries with "tps") or server-sent events, where the last event wins.
    /// </summary>
    public static double? ParseTokensPerSecond(JsonElement? body, string? text)
    {
        if (body is { } b) return SumTps(b);
        if (string.IsNullOrEmpty(text)) return null;
        double? last = null;
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            var json = trimmed.StartsWith("data:", StringComparison.Ordinal) ? trimmed[5..].Trim() : trimmed;
            if (json.Length == 0 || (json[0] != '{' && json[0] != '[')) continue;
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (SumTps(doc.RootElement) is { } v) last = v;
            }
            catch (JsonException) { }
        }
        return last;
    }

    private static double? SumTps(JsonElement e)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var name in new[] { "total_tps", "tps" })
                    if (e.TryGetProperty(name, out var v) && v.TryGetDouble(out var d)) return d;
                foreach (var name in new[] { "sessions", "slots", "seqs" })
                    if (e.TryGetProperty(name, out var list)) return SumTps(list);
                return null;
            case JsonValueKind.Array:
                double sum = 0;
                var any = false;
                foreach (var item in e.EnumerateArray())
                    if (SumTps(item) is { } x) { sum += x; any = true; }
                return any ? sum : null;
            default:
                return null;
        }
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static ulong U64(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.TryGetUInt64(out var u) ? u : 0;

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
