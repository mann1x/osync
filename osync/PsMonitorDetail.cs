using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace osync;

/// <summary>A loaded model's share of one GPU, as /api/xollama/system places it (models[].devices[]).</summary>
internal sealed record RemoteModelGpu(string Backend, string Id, string PciId, ulong Vram);

/// <summary>
/// A loaded model as /api/xollama/system places it: its size, its VRAM, the GPUs it is on and its KV rolling window
/// (opencoti, kv_host_telemetry_v1). Null fields were not reported.
/// </summary>
internal sealed record RemoteModelPlacement(string Name, long Size, long SizeVram, List<RemoteModelGpu> Gpus)
{
    public string? KvResidencyMode { get; init; }
    public bool? KvEngaged { get; init; }
    public bool KvArn { get; init; }
    public ulong? KvDeviceBytes { get; init; }
    /// <summary>The window's tail in pinned host memory: 0 while not engaged, null when the engine reports no size.</summary>
    public ulong? KvHostBytes { get; init; }
}

/// <summary>Where a loaded model's memory is: in VRAM, and outside it (system RAM).</summary>
internal readonly record struct MemorySplit(long Vram, long Ram)
{
    public long Total => Vram + Ram;
    /// <summary>The share in system RAM, 0–100.</summary>
    public double RamPct => Total > 0 ? Ram * 100.0 / Total : 0;
    public double VramPct => Total > 0 ? Vram * 100.0 / Total : 0;

    /// <summary>
    /// /api/ps size and size_vram, plus the KV window's tail in host memory. On a llama.cpp engine both figures are
    /// the engine's own buffers (weights, KV cache, compute) as it logs them at load: size_vram the ones on a GPU,
    /// size all of them. When every layer is on the GPU xOllama reports size = size_vram, and the window tail opencoti
    /// keeps in pinned host memory is not in either figure: it is added here as RAM. While size is larger than
    /// size_vram the host buffers are already counted in size, the tail with them.
    /// </summary>
    public static MemorySplit From(long size, long sizeVram, ulong? kvHostBytes)
    {
        var vram = Math.Max(0, sizeVram);
        var ram = Math.Max(0, size - vram);
        if (ram == 0 && kvHostBytes is > 0 and var tail && tail <= long.MaxValue)
            ram = (long)tail;
        return new MemorySplit(vram, ram);
    }

    /// <summary>
    /// What the OS charges the engine process (opencoti props gpu_process_memory, Windows): dedicated is the memory
    /// on the cards, shared the system RAM the GPU maps for it (pinned host buffers, and allocations the driver moved
    /// off a full card). This is a measurement, where size/size_vram are the engine's own buffer sizes: a Vulkan card
    /// that runs out of VRAM keeps the allocation and places it in shared memory, and only this shows it.
    /// Weights the engine itself placed on the CPU (the token embedding, layers that did not fit) are ordinary process
    /// memory the GPU counters never see: the engine reports them as size − size_vram, so RAM is the larger of the two.
    /// </summary>
    public static MemorySplit? FromProcess(GpuProcessMemory? gpm, long size = 0, long sizeVram = 0) =>
        gpm is { DedicatedMib: { } ded, SharedMib: { } shared }
            ? new MemorySplit(MiB(ded), Math.Max(MiB(shared), Math.Max(0, size - Math.Max(0, sizeVram))))
            : null;

    private static long MiB(double mib) => (long)(Math.Max(0, mib) * 1024 * 1024);
}

/// <summary>opencoti's props gpu_process_memory: the engine process's GPU memory as the OS accounts it (Windows); null when not observable.</summary>
internal sealed record GpuProcessMemory(double? DedicatedMib, double? SharedMib, double? PinnedMib, double? OffCardMib, bool? Spilled)
{
    public static GpuProcessMemory? FromProps(JsonElement? props)
    {
        if (props is not { ValueKind: JsonValueKind.Object } p || !p.TryGetProperty("opencoti", out var oc) || oc.ValueKind != JsonValueKind.Object ||
            !oc.TryGetProperty("gpu_process_memory", out var g) || g.ValueKind != JsonValueKind.Object ||
            !(g.TryGetProperty("observable", out var o) && o.ValueKind == JsonValueKind.True))
            return null;
        double? D(string n) => g.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
        bool? B(string n) => g.TryGetProperty(n, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;
        return new GpuProcessMemory(D("dedicated_mib"), D("shared_mib"), D("pinned_mib"), D("off_card_mib"), B("spilled"));
    }

    /// <summary>The driver placed part of the model's device memory in system RAM.</summary>
    public bool IsSpilled => Spilled == true || OffCardMib is > 0;
}

/// <summary>
/// Everything the detail box shows for the selected model, read from /api/ps, /api/xollama/system, /api/show and the
/// engine (/api/engine props, kv, slots). A null field was not reported: it is shown as such, never as 0.
/// </summary>
internal sealed record ModelDetail
{
    public string Name { get; init; } = "";
    public string Engine { get; init; } = "";
    public MemorySplit Memory { get; init; }
    public List<RemoteModelGpu> Gpus { get; init; } = new();
    public ulong? KvHostBytes { get; init; }
    /// <summary>The server's figures (size, size_vram), kept for the line when Memory is the OS's measurement.</summary>
    public MemorySplit ServerMemory { get; init; }
    /// <summary>GPU memory of the engine process as the OS sees it (opencoti props gpu_process_memory, Windows).</summary>
    public GpuProcessMemory? Process { get; init; }

    // Model
    public string Family { get; init; } = "";
    public string Format { get; init; } = "";
    public string Quant { get; init; } = "";
    public string Params { get; init; } = "";
    public int Context { get; init; }
    public long? ContextTrained { get; init; }
    public string? ThinkBudget { get; init; }
    public long? ThinkBudgetTokens { get; init; }

    // KV cache
    public string? TypeK { get; init; }
    public string? TypeV { get; init; }
    public string? TypeKTail { get; init; }
    public string? TypeVTail { get; init; }
    public bool KvArn { get; init; }
    public long? Cells { get; init; }
    public long? CellsUsed { get; init; }
    public long? CellsFree { get; init; }
    public int? KvLayers { get; init; }
    public int? LayersSpilling { get; init; }
    public bool? FullyResident { get; init; }
    public string? ResidencyMode { get; init; }
    public bool? WindowEngaged { get; init; }
    /// <summary>The KV cache's size, worked out from its type, cells and the model's attention shape; null when unknown (KVarN).</summary>
    public ulong? KvBytes { get; init; }
    /// <summary>Recurrent state (hybrid models: Mamba, gated delta net) the engine holds next to the KV cache.</summary>
    public ulong? RsBytes { get; init; }
    public int? Refused60s { get; init; }

    // Slots
    public int? SlotsBusy { get; init; }
    public int? SlotsLive { get; init; }
    public int? SlotsMax { get; init; }
    public string? ElasticReason { get; init; }
    public string? SlotState { get; init; }
    public long? PrefillDone { get; init; }
    public long? PrefillTotal { get; init; }

    // Speculative decoding (MTP / drafter)
    public string? SpecTypes { get; init; }
    public string? DrafterState { get; init; }
    public string? DrafterKind { get; init; }
    public string? DraftModel { get; init; }
    /// <summary>Where the engine put the token embedding (opencoti speculative.token_embd.device), when not on a GPU.</summary>
    public string? TokenEmbdDevice { get; init; }
    public int? DraftNMax { get; init; }
    public double? DraftAcceptance { get; init; }
    /// <summary>The last generation speed of the slots (opencoti tps_ewma; an idle slot keeps its last value).</summary>
    public double? LastTps { get; init; }
    /// <summary>Sliding-window cells (interleaved SWA models: kv.swa), for the KV size.</summary>
    public long? SwaCells { get; init; }

    // Engine features
    public bool? Dca { get; init; }
    public int? DcaChunk { get; init; }
    public bool? SparseAttn { get; init; }
    public bool? PoolsEnabled { get; init; }
    public int? PoolsMax { get; init; }
    public double? CacheRamMib { get; init; }
    public int? CtxCheckpoints { get; init; }
    public string? Sampling { get; init; }

    /// <summary>Whether the engine answered props (opencoti or llama.cpp): without it only the server's figures are known.</summary>
    public bool EngineRead { get; init; }

    // ---- building (pure, unit-tested) ----

    public static ModelDetail Build(RunningModel model, string engine, RemoteModelPlacement? placement,
        JsonElement? props, JsonElement? kv, JsonElement? slots, JsonElement? show)
    {
        var d = new ModelDetail
        {
            Name = model.Name ?? "",
            Engine = engine.Length > 0 ? engine : model.Runner ?? "",
            Memory = MemorySplit.From(model.Size, model.SizeVram, placement?.KvHostBytes),
            ServerMemory = MemorySplit.From(model.Size, model.SizeVram, placement?.KvHostBytes),
            Gpus = placement?.Gpus ?? new List<RemoteModelGpu>(),
            KvHostBytes = placement?.KvHostBytes,
            Family = model.Details?.Family ?? "",
            Format = model.Details?.Format ?? "",
            Quant = model.Details?.QuantizationLevel ?? "",
            Params = model.Details?.ParameterSize ?? "",
            Context = model.ContextLength,
            ResidencyMode = placement?.KvResidencyMode,
            WindowEngaged = placement?.KvEngaged,
            KvArn = placement?.KvArn ?? false,
        };

        Dictionary<string, JsonElement>? info = null;
        if (show is { ValueKind: JsonValueKind.Object } s)
        {
            info = s.TryGetProperty("model_info", out var mi) && mi.ValueKind == JsonValueKind.Object
                ? mi.EnumerateObject().ToDictionary(p => p.Name, p => p.Value)
                : null;
            d = d with
            {
                ThinkBudget = Str(s, "think_budget"),
                ThinkBudgetTokens = Int(s, "think_budget_tokens"),
                ContextTrained = info != null ? ArchLong(info, "context_length") : null,
            };
            // xOllama's drafter (built-in MTP head or a separate draft model), as configured
            if (s.TryGetProperty("drafter", out var dr) && dr.ValueKind == JsonValueKind.Object)
            {
                d = d with
                {
                    SpecTypes = Str(dr, "spec_type"),
                    DraftNMax = (int?)Int(dr, "tokens"),
                    DrafterState = Obj(dr, "loaded") is { } loaded ? Str(loaded, "state") : null,
                    DrafterKind = Obj(dr, "loaded") is { } loaded2 ? Str(loaded2, "kind") : null,
                    // an attached draft model: its architecture, size and quantization rather than its blob path
                    DraftModel = string.Join(" ", new[] { Str(dr, "architecture"), Str(dr, "parameter_size"), Str(dr, "quantization_level") }
                        .Where(x => x is { Length: > 0 })) is { Length: > 0 } named ? named : null,
                };
            }
        }

        if (props is { ValueKind: JsonValueKind.Object } p)
        {
            d = d with { EngineRead = true };
            if (GpuProcessMemory.FromProps(p) is { } gpm)
                d = d with { Process = gpm, Memory = MemorySplit.FromProcess(gpm, model.Size, model.SizeVram) ?? d.Memory };
            if (Obj(p, "default_generation_settings") is { } gen && Int(gen, "n_ctx") is { } nctx && d.Context == 0)
                d = d with { Context = (int)nctx };
            // stock llama.cpp: its slot count
            if (Int(p, "total_slots") is { } total) d = d with { SlotsLive = (int)total };

            if (Obj(p, "opencoti") is { } oc)
            {
                if (Obj(oc, "kv") is { } okv)
                {
                    var eff = Obj(okv, "effective");
                    d = d with
                    {
                        Cells = Int(okv, "kv_pool_cells") ?? (eff is { } e0 ? Int(e0, "n_cells") : null),
                        CellsUsed = Int(okv, "cells_used"),
                        CellsFree = Int(okv, "cells_free"),
                    };
                    if (eff is { } e)
                    {
                        d = d with
                        {
                            TypeK = Str(e, "type_k"), TypeV = Str(e, "type_v"),
                            TypeKTail = Str(e, "type_k_tail"), TypeVTail = Str(e, "type_v_tail"),
                            LayersSpilling = (int?)Int(e, "n_layers_spilling"),
                            FullyResident = Bool(e, "fully_resident"),
                        };
                    }
                }
                if (Obj(oc, "dca") is { } dca)
                    d = d with { Dca = Bool(dca, "enabled"), DcaChunk = (int?)Int(dca, "chunk_size") };
                if (Obj(oc, "sparse_attn") is { } sparse)
                    d = d with { SparseAttn = Bool(sparse, "enabled") };
                if (Obj(oc, "polykv") is { } poly)
                    d = d with { PoolsEnabled = Bool(poly, "pools_enabled"), PoolsMax = (int?)Int(poly, "max_pools") };
                if (Obj(oc, "kv_reuse") is { } reuse)
                    d = d with { CacheRamMib = Dbl(reuse, "cache_ram_mib"), CtxCheckpoints = (int?)Int(reuse, "ctx_checkpoints") };
                if (Obj(oc, "sampling") is { } sampling)
                    d = d with { Sampling = Str(sampling, "effective_placement") };
                if (Obj(oc, "elastic_slots") is { } elastic && Bool(elastic, "enabled") == true)
                    d = d with { SlotsLive = (int?)Int(elastic, "slots_live"), SlotsMax = (int?)Int(elastic, "slots_max"), ElasticReason = Str(elastic, "reason") };
                if (Obj(oc, "speculative") is { } spec)
                {
                    var types = spec.TryGetProperty("types", out var t) && t.ValueKind == JsonValueKind.Array
                        ? string.Join(",", t.EnumerateArray().Select(x => x.GetString()).Where(x => x is { Length: > 0 } && x != "none"))
                        : "";
                    var drafter = Obj(spec, "drafter");
                    if (Obj(spec, "token_embd") is { } te && Str(te, "device") is { } dev && dev.Equals("CPU", StringComparison.OrdinalIgnoreCase))
                        d = d with { TokenEmbdDevice = dev };
                    d = d with
                    {
                        SpecTypes = types.Length > 0 ? types : d.SpecTypes ?? "none",
                        DraftNMax = (int?)Int(spec, "n_max") ?? d.DraftNMax,
                        DraftModel = d.DraftModel ?? (Str(spec, "draft_model") is { Length: > 0 } dm ? ShortModelPath(dm) : null),
                        DrafterState = drafter is { } dr1 ? Str(dr1, "state") ?? d.DrafterState : d.DrafterState,
                        DrafterKind = drafter is { } dr2 ? Str(dr2, "kind") ?? d.DrafterKind : d.DrafterKind,
                    };
                }
            }
        }

        if (kv is { ValueKind: JsonValueKind.Object } k)
        {
            d = d with
            {
                Cells = Int(k, "cells_total") ?? d.Cells,
                CellsUsed = Int(k, "cells_used") ?? d.CellsUsed,
                CellsFree = Int(k, "cells_free") ?? d.CellsFree,
            };
            if (Obj(k, "rolling_kv") is { } roll)
            {
                d = d with
                {
                    ResidencyMode = Str(roll, "residency_mode") ?? d.ResidencyMode,
                    WindowEngaged = Bool(roll, "engaged") ?? d.WindowEngaged,
                    KvArn = Bool(roll, "kvarn") ?? d.KvArn,
                    KvLayers = (int?)Int(roll, "layers"),
                    LayersSpilling = (int?)Int(roll, "layers_spilling") ?? d.LayersSpilling,
                    TypeK = d.TypeK ?? Str(roll, "type_k"), TypeV = d.TypeV ?? Str(roll, "type_v"),
                    TypeKTail = d.TypeKTail ?? Str(roll, "type_k_tail"), TypeVTail = d.TypeVTail ?? Str(roll, "type_v_tail"),
                };
            }
            if (Obj(k, "rs") is { } rs && Int(rs, "committed_bytes") is > 0 and var rsBytes)
                d = d with { RsBytes = (ulong)rsBytes };
            if (Obj(k, "swa") is { } swa)
                d = d with { SwaCells = Int(swa, "cells_phys_total") ?? Int(swa, "cells_total") };
            if (Obj(k, "pressure") is { } pressure)
                d = d with { Refused60s = (int?)Int(pressure, "refused_60s") };
        }

        if (slots is { ValueKind: JsonValueKind.Array } sl)
        {
            int busy = 0;
            long drafted = 0, accepted = 0;
            double? lastTps = null;
            foreach (var slot in sl.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object))
            {
                var processing = Bool(slot, "is_processing") == true;
                if (processing) busy++;
                if (Obj(slot, "opencoti") is not { } so) continue;
                drafted += Int(so, "draft_n_total") ?? 0;
                accepted += Int(so, "draft_n_accepted") ?? 0;
                if (Dbl(so, "tps_ewma") is > 0 and var tps) lastTps = (lastTps ?? 0) + tps;
                if (processing && d.SlotState == null)
                {
                    d = d with { SlotState = Str(so, "state") };
                    if (Obj(so, "prefill_progress") is { } pp && Str(so, "state") == "processing_prompt")
                        d = d with { PrefillDone = Int(pp, "processed"), PrefillTotal = Int(pp, "total") };
                }
            }
            d = d with { SlotsBusy = busy, DraftAcceptance = drafted > 0 ? (double)accepted / drafted : null, LastTps = lastTps };
            if (d.SlotsLive == null) d = d with { SlotsLive = sl.GetArrayLength() };
        }

        if (info != null)
            // only from the types the engine says it used: an embedding model, or an engine that does not report them, gets no guess
            d = d with { KvBytes = d.TypeK != null && d.TypeV != null ? EstimateKvBytes(info, d.Cells ?? d.Context, d.KvLayers, d.TypeK, d.TypeV, d.SwaCells) : null };
        return d;
    }

    /// <summary>Bytes per element of a llama.cpp cache type; null for one whose size is unknown (KVarN).</summary>
    public static double? BytesPerElement(string type) => type.ToLowerInvariant() switch
    {
        "f32" => 4,
        "f16" or "bf16" => 2,
        "q8_0" => 34.0 / 32,
        "q6_0" => 26.0 / 32,
        "q5_1" => 24.0 / 32,
        "q5_0" => 22.0 / 32,
        "q4_1" => 20.0 / 32,
        "q4_0" or "iq4_nl" => 18.0 / 32,
        _ => null,
    };

    /// <summary>
    /// The KV cache's size: cells × attention layers × KV heads × (key + value head size) at the cache types' width.
    /// The layers are the engine's own count when it reports one (a hybrid model keeps KV on its attention layers
    /// only), else the model's block count. Null when the model's shape or a type's width is not known.
    /// </summary>
    public static ulong? EstimateKvBytes(Dictionary<string, JsonElement> info, long cells, int? kvLayers, string typeK, string typeV, long? swaCells = null)
    {
        if (cells <= 0 || BytesPerElement(typeK) is not { } bk || BytesPerElement(typeV) is not { } bv) return null;
        // Interleaved sliding-window models (gemma): per layer, a full-attention layer holds every cell at its own head
        // count and head size, a sliding-window layer only the window's cells at the SWA head size
        if (ArchArray(info, "attention.sliding_window_pattern") is { Count: > 0 } pattern)
        {
            var kvHeadsPerLayer = ArchArray(info, "attention.head_count_kv");
            var kvHeadsAll = ArchLong(info, "attention.head_count_kv");
            var key = ArchLong(info, "attention.key_length");
            var value = ArchLong(info, "attention.value_length") ?? key;
            var keySwa = ArchLong(info, "attention.key_length_swa") ?? key;
            var valueSwa = ArchLong(info, "attention.value_length_swa") ?? keySwa;
            var window = ArchLong(info, "attention.sliding_window");
            var swa = swaCells ?? (window is > 0 ? Math.Min(cells, window.Value) : cells);
            if (key is not > 0 || value is not > 0) return null;
            double total = 0;
            for (var i = 0; i < pattern.Count; i++)
            {
                var layerHeads = kvHeadsPerLayer is { } per && i < per.Count ? per[i] : kvHeadsAll;
                if (layerHeads is not > 0) return null;
                total += pattern[i] != 0
                    ? layerHeads.Value * (keySwa!.Value * bk + valueSwa!.Value * bv) * swa
                    : layerHeads.Value * (key.Value * bk + value.Value * bv) * cells;
            }
            return (ulong)total;
        }
        var layers = kvLayers ?? ArchLong(info, "block_count");
        var heads = ArchLong(info, "attention.head_count");
        var kvHeads = ArchLong(info, "attention.head_count_kv") ?? heads;
        var embedding = ArchLong(info, "embedding_length");
        long? fallbackDim = heads is > 0 && embedding is > 0 ? embedding / heads : null;
        var keyDim = ArchLong(info, "attention.key_length") ?? fallbackDim;
        var valueDim = ArchLong(info, "attention.value_length") ?? keyDim;
        if (layers is not > 0 || kvHeads is not > 0 || keyDim is not > 0 || valueDim is not > 0) return null;
        var perCell = layers.Value * kvHeads.Value * (keyDim.Value * bk + valueDim.Value * bv);
        return (ulong)(perCell * cells);
    }

    /// <summary>A per-layer model_info array (numbers, or booleans as 1/0); null when absent or not an array.</summary>
    private static List<long>? ArchArray(Dictionary<string, JsonElement> info, string key)
    {
        var arch = info.TryGetValue("general.architecture", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() : null;
        if (arch == null || !info.TryGetValue($"{arch}.{key}", out var value) || value.ValueKind != JsonValueKind.Array) return null;
        return value.EnumerateArray().Select(x => x.ValueKind switch
        {
            JsonValueKind.True => 1L,
            JsonValueKind.False => 0L,
            JsonValueKind.Number when x.TryGetInt64(out var n) => n,
            _ => -1L,
        }).ToList();
    }

    /// <summary>A model file path as a short name: the file, and a blob's digest cut to 12 characters.</summary>
    public static string ShortModelPath(string path)
    {
        var name = path.Replace('\\', '/').Split('/').Last();
        return name.StartsWith("sha256-", StringComparison.Ordinal) && name.Length > 19 ? name[..19] : name;
    }

    /// <summary>A model_info number under the model's architecture ("qwen35.block_count"); for a per-layer array, its largest value.</summary>
    private static long? ArchLong(Dictionary<string, JsonElement> info, string key)
    {
        var arch = info.TryGetValue("general.architecture", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() : null;
        JsonElement value;
        if (arch != null && info.TryGetValue($"{arch}.{key}", out value)) { }
        else
        {
            var match = info.FirstOrDefault(kv => kv.Key.EndsWith("." + key, StringComparison.Ordinal) && kv.Key.Count(c => c == '.') == key.Count(c => c == '.') + 1);
            if (match.Key == null) return null;
            value = match.Value;
        }
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var n)) return n;
        if (value.ValueKind == JsonValueKind.Array)
        {
            var numbers = value.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Number && x.TryGetInt64(out _)).Select(x => x.GetInt64()).ToList();
            return numbers.Count > 0 ? numbers.Max() : null;
        }
        return null;
    }

    // ---- formatting (pure, unit-tested) ----

    /// <summary>The box's lines, at most five, each as (label, text): plain text, escaped and colored by the renderer.</summary>
    public List<(string Label, string Text)> Lines()
    {
        var lines = new List<(string, string)>();

        // Memory
        var mem = new List<string>
        {
            $"VRAM {Gb(Memory.Vram)} ({Memory.VramPct:F0}%)",
            $"RAM {Gb(Memory.Ram)} ({Memory.RamPct:F0}%)",
        };
        // per GPU, the server's figures; with the OS's measurement only where (they would not add up to it)
        if (Gpus.Count > 0)
            mem.Add(Process != null
                ? "on " + string.Join(", ", Gpus.Select(g => $"{g.Backend}{g.Id}"))
                : string.Join(", ", Gpus.Select(g => $"{g.Backend}{g.Id} {Gb((long)g.Vram)}")));
        if (KvHostBytes is > 0)
            mem.Add($"KV tail in RAM {Gb((long)KvHostBytes.Value)}");
        if (Process is { } proc)
        {
            if (proc.PinnedMib is > 0 and var pin) mem.Add($"{Gb(Mib(pin))} pinned");
            mem.Add(proc.OffCardMib is > 0 and var off ? $"SPILLED {Gb(Mib(off))} to RAM" : proc.Spilled == true ? "SPILLED to RAM" : "not spilled");
            mem.Add($"engine buffers {Gb(ServerMemory.Total)}");
        }
        if (TokenEmbdDevice != null)
            mem.Add("token embedding on CPU");
        lines.Add(("Memory", string.Join(" · ", mem)));

        if (!EngineRead)
        {
            // No engine to ask (stock Ollama, a model still loading): what the server says about the model
            var model = new List<string>();
            if (Params.Length > 0) model.Add(Params);
            if (Family.Length > 0) model.Add(Family);
            if (Quant.Length > 0) model.Add(Quant);
            if (Format.Length > 0) model.Add(Format);
            if (Engine.Length > 0) model.Add($"runner {Engine}");
            lines.Add(("Model", string.Join(" · ", model)));
            lines.Add(("Context", Context > 0
                ? $"{Context}" + (ContextTrained is > 0 && ContextTrained != Context ? $" of {ContextTrained} trained" : "")
                : "-"));
            if (SpecTypes is { Length: > 0 } st && st != "none")
                lines.Add(("MTP", Spec()));
            if (ThinkBudget != null)
                lines.Add(("Think", ThinkText()));
            return lines.Take(5).ToList();
        }

        // KV cache
        var kv = new List<string>();
        if (TypeK != null || TypeV != null)
        {
            var types = $"{TypeK ?? "?"}/{TypeV ?? "?"}";
            if ((TypeKTail != null && TypeKTail != TypeK) || (TypeVTail != null && TypeVTail != TypeV))
                types += $" (tail {TypeKTail ?? "?"}/{TypeVTail ?? "?"})";
            kv.Add(types);
        }
        else if (KvArn) kv.Add("KVarN");
        if (KvBytes is { } kb) kv.Add($"~{Gb((long)kb)}");
        if (RsBytes is { } rs) kv.Add($"state {Gb((long)rs)}");
        var cells = Cells ?? (Context > 0 ? Context : null);
        if (cells != null)
            kv.Add($"{cells} cells" + (CellsUsed != null ? $", {CellsUsed} used" : "") + (CellsFree != null ? $", {CellsFree} free" : ""));
        if (FullyResident == true && (LayersSpilling ?? 0) == 0)
            kv.Add("all in VRAM");
        else if (LayersSpilling is > 0)
            kv.Add(KvLayers is > 0 ? $"{LayersSpilling}/{KvLayers} layers spill" : $"{LayersSpilling} layers spill");
        if (ResidencyMode != null)
            kv.Add($"window {ResidencyMode}" + (WindowEngaged == true ? " (engaged)" : ""));
        if (Refused60s is > 0) kv.Add($"{Refused60s} refused in 60 s");
        lines.Add(("KV", kv.Count > 0 ? string.Join(" · ", kv) : "-"));

        // Slots
        var slots = new List<string>();
        if (SlotsLive != null)
            slots.Add($"{SlotsBusy ?? 0} busy of {SlotsLive}" + (SlotsMax != null && SlotsMax != SlotsLive ? $" (max {SlotsMax})" : ""));
        if (ElasticReason is { Length: > 0 } reason) slots.Add($"elastic: {reason}");
        if (PrefillDone != null && PrefillTotal is > 0)
            slots.Add($"prefill {PrefillDone}/{PrefillTotal} ({PrefillDone * 100.0 / PrefillTotal:F0}%)");
        else if (SlotState is { Length: > 0 } state && state != "idle")
            slots.Add(state.Replace('_', ' '));
        if (LastTps is { } last) slots.Add($"last {last:F1} tok/s");
        slots.Add($"context {Context}" + (ContextTrained is > 0 && ContextTrained != Context ? $" of {ContextTrained}" : ""));
        lines.Add(("Slots", string.Join(" · ", slots)));

        lines.Add(("MTP", Spec()));

        var eng = new List<string>();
        if (Dca != null) eng.Add(Dca == true ? $"DCA on (chunk {DcaChunk})" : "DCA off");
        if (PoolsEnabled != null) eng.Add(PoolsEnabled == true ? $"PolyKV pools on (max {PoolsMax})" : "PolyKV pools off");
        if (SparseAttn == true) eng.Add("sparse attention");
        if (CacheRamMib is > 0) eng.Add($"prompt cache {Gb(Mib(CacheRamMib.Value))}" + (CtxCheckpoints is > 0 ? $", {CtxCheckpoints} checkpoints" : ""));
        if (Sampling is { Length: > 0 } smp && smp != "idle") eng.Add($"sampling on {smp}");
        if (ThinkBudget != null) eng.Add("think " + ThinkText());
        lines.Add((Engine.Length > 0 ? Engine : "Engine", eng.Count > 0 ? string.Join(" · ", eng) : "-"));
        return lines.Take(5).ToList();
    }

    private string Spec()
    {
        if (SpecTypes is null or "" or "none") return "off";
        var parts = new List<string> { SpecTypes };
        var drafter = string.Join(", ", new[] { DrafterKind, DrafterState }.Where(x => x is { Length: > 0 }));
        if (drafter.Length > 0) parts[0] += $" ({drafter})";
        if (DraftModel != null) parts.Add(DraftModel);
        if (DraftNMax is > 0) parts.Add($"up to {DraftNMax} tokens");
        if (DraftAcceptance is { } acc) parts.Add($"{acc * 100:F0}% accepted");
        return string.Join(" · ", parts);
    }

    private string ThinkText() => ThinkBudget + (ThinkBudgetTokens is > 0 ? $" ({ThinkBudgetTokens} tokens)" : "");

    private static long Mib(double mib) => (long)(mib * 1024 * 1024);

    public static string Gb(long bytes) => bytes >= 1L << 30
        ? (bytes / (double)(1L << 30)).ToString("F1", CultureInfo.InvariantCulture) + " GB"
        : (bytes / (double)(1L << 20)).ToString("F0", CultureInfo.InvariantCulture) + " MB";

    // ---- JSON helpers ----

    private static JsonElement? Obj(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Object ? v : null;

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static long? Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? (v.TryGetInt64(out var n) ? n : (long)v.GetDouble())
            : null;

    private static double? Dbl(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    private static bool? Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;
}
