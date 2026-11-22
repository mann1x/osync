using System.Text.Json;
using FluentAssertions;
using osync;
using Xunit;

namespace osync.Tests.UnitTests;

/// <summary>psmonitor's VRAM/RAM split and the detail box of the selected model.</summary>
public class ModelDetailTests
{
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();
    private const long GiB = 1L << 30;

    // Read from xOllama 0.40.16-xollama.2 / opencoti-0.10.5-c14 on Windows (RTX 3090), 2026-11-21, shortened:
    // qwen36 27B Q4_K_M with its MTP head, 128k context, q8_0 KV, one slot prefilling a 26k prompt
    private const string Props = """
        {"default_generation_settings":{"n_ctx":131072},"total_slots":1,
         "opencoti":{
           "kv":{"kv_pool_cells":131072,"cells_used":25603,"cells_free":8512,
                 "effective":{"type_k":"q8_0","type_v":"q8_0","type_k_tail":"q8_0","type_v_tail":"q8_0","n_cells":131072,"n_cells_resident":131072,"n_layers_spilling":0,"fully_resident":true}},
           "dca":{"enabled":false,"chunk_size":0,"yarn_factor":1.0},
           "sparse_attn":{"enabled":false},
           "speculative":{"types":["none","draft-mtp"],"draft_model":"","n_max":4,"drafter":{"state":"attached","kind":"nextn"}},
           "kv_reuse":{"n_parallel":1,"cache_ram_mib":2048,"ctx_checkpoints":8},
           "sampling":{"effective_placement":"cpu"},
           "polykv":{"max_pools":0,"pools_enabled":false},
           "gpu_process_memory":{"observable":true,"shared_mib":1118,"dedicated_mib":22394,"pinned_mib":1028,"on_device_mib":22073,"off_card_mib":0,"spilled":false},
           "elastic_slots":{"enabled":true,"slots_live":1,"slots_max":4,"reason":"saturated, hold"}}}
        """;

    private const string Kv = """
        {"rs":{"elastic":true,"committed_bytes":564133888},
         "rolling_kv":{"residency_mode":"auto","engaged":false,"kvarn":false,"layers":16,"layers_spilling":0,"type_k":"q8_0","type_v":"q8_0"},
         "cells_total":131072,"cells_used":26556,"cells_free":8512,
         "pressure":{"window_s":60,"refused_60s":0}}
        """;

    private const string Slots = """
        [{"id":0,"n_ctx":131072,"is_processing":true,"opencoti":{"draft_n_total":4516,"draft_n_accepted":3391,"state":"processing_prompt","prefill_progress":{"total":26560,"processed":26048}}},
         {"id":1,"n_ctx":131072,"is_processing":false,"opencoti":{"draft_n_total":0,"draft_n_accepted":0,"state":"idle"}}]
        """;

    private const string Show = """
        {"model_info":{"general.architecture":"qwen35","qwen35.block_count":64,"qwen35.attention.head_count":24,"qwen35.attention.head_count_kv":4,
                       "qwen35.attention.key_length":256,"qwen35.attention.value_length":256,"qwen35.embedding_length":5120,"qwen35.context_length":262144},
         "think_budget":"medium","think_budget_tokens":32768,
         "drafter":{"source":"built-in","spec_type":"draft-mtp","tokens":4,"loaded":{"state":"attached","kind":"nextn"}}}
        """;

    private static RunningModel Qwen() => new()
    {
        Name = "qwen36-base-mtp_tb:27b-q4km-128k",
        Size = 22441707437,
        SizeVram = 22441707437,
        ContextLength = 131072,
        Runner = "llamacpp",
        Details = new ModelDetails { Family = "qwen35", Format = "gguf", ParameterSize = "27.3B", QuantizationLevel = "Q4_K_M" },
    };

    [Fact]
    public void MemorySplit_PartialOffload_RamIsTheRest()
    {
        var split = MemorySplit.From(10 * GiB, 8 * GiB, null);
        split.Vram.Should().Be(8 * GiB);
        split.Ram.Should().Be(2 * GiB);
        split.RamPct.Should().BeApproximately(20, 0.01);
    }

    [Fact]
    public void MemorySplit_FullOffload_AddsTheKvTailInHostMemory()
    {
        // xOllama reports size = size_vram when every layer is on the GPU: the window's host tail is in neither
        var split = MemorySplit.From(8 * GiB, 8 * GiB, (ulong)(2 * GiB));
        split.Ram.Should().Be(2 * GiB);
        // partial offload: host buffers, the tail with them, are already in size
        MemorySplit.From(10 * GiB, 8 * GiB, (ulong)GiB).Ram.Should().Be(2 * GiB);
    }

    [Fact]
    public void MemorySplit_FromProcess_IsTheOsMeasurement()
    {
        var gpm = GpuProcessMemory.FromProps(Json(Props));
        gpm.Should().NotBeNull();
        gpm!.IsSpilled.Should().BeFalse();
        var split = MemorySplit.FromProcess(gpm)!.Value;
        split.Vram.Should().Be(22394L << 20);
        split.Ram.Should().Be(1118L << 20);
        split.RamPct.Should().BeApproximately(4.76, 0.01);
    }

    [Fact]
    public void GpuProcessMemory_NotObservableOrSpilled()
    {
        GpuProcessMemory.FromProps(Json("""{"opencoti":{"gpu_process_memory":{"observable":false,"dedicated_mib":1}}}""")).Should().BeNull();
        GpuProcessMemory.FromProps(Json("""{"total_slots":1}""")).Should().BeNull();
        GpuProcessMemory.FromProps(null).Should().BeNull();
        // a Vulkan card that ran out of VRAM: the driver placed 4.5 GB in system RAM
        var spilled = GpuProcessMemory.FromProps(Json("""{"opencoti":{"gpu_process_memory":{"observable":true,"dedicated_mib":15800,"shared_mib":4700,"off_card_mib":4600,"spilled":true}}}"""));
        spilled!.IsSpilled.Should().BeTrue();
        MemorySplit.FromProcess(spilled)!.Value.RamPct.Should().BeApproximately(22.9, 0.1);
    }

    [Fact]
    public void ParsePlacements_ReadsGpusAndKvWindow()
    {
        var placements = RemoteServerMonitor.ParsePlacements(Json("""
            {"models":[{"name":"qwen36-base-mtp_tb:27b-q4km-128k","pid":20596,"size":22441707437,"size_vram":22441707437,
              "devices":[{"backend":"CUDA","id":"0","pci_id":"0000:11:00.0","vram":22441707437}],"context_length":131072,
              "kv":{"residency_mode":"auto","engaged":false,"kvarn":false,"cells":131072,"device_bytes":null,"host_bytes":0}},
             {"name":"loading:latest","size":1,"size_vram":1,"devices":[],"kv":null}]}
            """));

        placements.Should().HaveCount(2);
        var qwen = placements["qwen36-base-mtp_tb:27b-q4km-128k"];
        qwen.Gpus.Should().ContainSingle().Which.Should().Be(new RemoteModelGpu("CUDA", "0", "0000:11:00.0", 22441707437));
        qwen.KvResidencyMode.Should().Be("auto");
        qwen.KvEngaged.Should().BeFalse();
        qwen.KvHostBytes.Should().Be(0UL);
        qwen.KvDeviceBytes.Should().BeNull();
        placements["loading:latest"].KvHostBytes.Should().BeNull();
        RemoteServerMonitor.ParsePlacements(Json("""{"host":{}}""")).Should().BeEmpty();
    }

    [Fact]
    public void Build_OpencotiModel_ReadsEveryPart()
    {
        var d = ModelDetail.Build(Qwen(), "opencoti", null, Json(Props), Json(Kv), Json(Slots), Json(Show));

        d.EngineRead.Should().BeTrue();
        d.Memory.Vram.Should().Be(22394L << 20);  // the OS's figure wins over size_vram
        d.ServerMemory.Vram.Should().Be(22441707437);
        d.TypeK.Should().Be("q8_0");
        d.TypeV.Should().Be("q8_0");
        d.Cells.Should().Be(131072);
        d.CellsUsed.Should().Be(26556);  // /kv is newer than props
        d.KvLayers.Should().Be(16);
        d.FullyResident.Should().BeTrue();
        d.RsBytes.Should().Be(564133888UL);
        d.SlotsBusy.Should().Be(1);
        d.SlotsLive.Should().Be(1);
        d.SlotsMax.Should().Be(4);
        d.PrefillDone.Should().Be(26048);
        d.SpecTypes.Should().Be("draft-mtp");
        d.DraftNMax.Should().Be(4);
        d.DrafterKind.Should().Be("nextn");
        d.DraftAcceptance.Should().BeApproximately(0.7509, 0.0001);
        d.Dca.Should().BeFalse();
        d.PoolsEnabled.Should().BeFalse();
        d.CacheRamMib.Should().Be(2048);
        d.ContextTrained.Should().Be(262144);
        d.ThinkBudget.Should().Be("medium");
        // 16 attention layers × 4 KV heads × (256 + 256) × q8_0 (34/32 bytes) × 131072 cells
        d.KvBytes.Should().Be((ulong)(16 * 4 * 512 * 34.0 / 32 * 131072));
    }

    [Fact]
    public void Lines_OpencotiModel_FiveCompactLines()
    {
        var lines = ModelDetail.Build(Qwen(), "opencoti", null, Json(Props), Json(Kv), Json(Slots), Json(Show)).Lines();

        lines.Should().HaveCount(5);
        lines.Select(l => l.Label).Should().Equal("Memory", "KV", "Slots", "MTP", "opencoti");
        lines[0].Text.Should().StartWith("VRAM 21.9 GB (95%) · RAM 1.1 GB (5%)").And.Contain("not spilled").And.Contain("engine buffers 20.9 GB");
        lines[1].Text.Should().Be("q8_0/q8_0 · ~4.2 GB · state 538 MB · 131072 cells, 26556 used, 8512 free · all in VRAM · window auto");
        lines[2].Text.Should().Be("1 busy of 1 (max 4) · elastic: saturated, hold · prefill 26048/26560 (98%) · context 131072 of 262144");
        lines[3].Text.Should().Be("draft-mtp (nextn, attached) · up to 4 tokens · 75% accepted");
        lines[4].Text.Should().Be("DCA off · PolyKV pools off · prompt cache 2.0 GB, 8 checkpoints · sampling on cpu · think medium (32768 tokens)");
    }

    [Fact]
    public void Lines_StockOllama_ServerFiguresOnly()
    {
        var model = Qwen();
        model.SizeVram = model.Size * 3 / 4;
        var lines = ModelDetail.Build(model, "", null, null, null, null, null).Lines();

        lines.Select(l => l.Label).Should().Equal("Memory", "Model", "Context");
        lines[0].Text.Should().Be("VRAM 15.7 GB (75%) · RAM 5.2 GB (25%)");
        lines[1].Text.Should().Be("27.3B · qwen35 · Q4_K_M · gguf · runner llamacpp");
        lines[2].Text.Should().Be("131072");
    }

    [Fact]
    public void Lines_SpilledShowsIt()
    {
        var props = Json("""{"opencoti":{"gpu_process_memory":{"observable":true,"dedicated_mib":15800,"shared_mib":4700,"pinned_mib":100,"off_card_mib":4608,"spilled":true}}}""");
        var lines = ModelDetail.Build(Qwen(), "opencoti", null, props, null, null, null).Lines();
        lines[0].Text.Should().Contain("SPILLED 4.5 GB to RAM");
    }

    [Theory]
    [InlineData("f16", 2.0)]
    [InlineData("q8_0", 34.0 / 32)]
    [InlineData("q4_0", 18.0 / 32)]
    public void BytesPerElement_KnownTypes(string type, double expected) =>
        ModelDetail.BytesPerElement(type).Should().Be(expected);

    [Fact]
    public void EstimateKvBytes_UnknownWidthOrShape_IsNull()
    {
        var info = new Dictionary<string, JsonElement> { ["general.architecture"] = Json("\"llama\""), ["llama.block_count"] = Json("32") };
        ModelDetail.EstimateKvBytes(info, 4096, null, "kvarn3", "kvarn3").Should().BeNull();
        ModelDetail.EstimateKvBytes(info, 4096, null, "f16", "f16").Should().BeNull();  // no heads
        info["llama.attention.head_count"] = Json("32");
        info["llama.attention.head_count_kv"] = Json("[8,8,8]");
        info["llama.embedding_length"] = Json("4096");
        // 32 layers × 8 KV heads × (128 + 128) × 2 bytes × 4096 cells = 512 MiB
        ModelDetail.EstimateKvBytes(info, 4096, null, "f16", "f16").Should().Be(512UL << 20);
    }

    [Theory]
    [InlineData(null, 1, 0)]
    [InlineData(null, -1, 2)]
    [InlineData("a", 1, 1)]
    [InlineData("a", -1, 0)]
    [InlineData("c", 1, 2)]
    [InlineData("gone", 1, 0)]
    public void NextSelection_MovesAndStopsAtTheEnds(string? selected, int step, int expected) =>
        PsMonitorUI.NextSelection(new[] { "a", "b", "c" }, selected, step).Should().Be(expected);

    [Fact]
    public void MemoryCells_ShowsVramAndRamShare()
    {
        PsMonitorUI.MemoryCells(new MemorySplit(8 * GiB, 0), false).Should().Be(("[green]8.0 GB[/]", "[dim]0%[/]"));
        PsMonitorUI.MemoryCells(new MemorySplit(8 * GiB, 2 * GiB), false).Should().Be(("[green]8.0 GB[/]", "[yellow]2.0 GB 20%[/]"));
        PsMonitorUI.MemoryCells(new MemorySplit(8 * GiB, 2 * GiB), true).Ram.Should().Be("[red]2.0 GB 20%[/]");
        PsMonitorUI.MemoryCells(new MemorySplit(1000 * GiB, GiB / 2), false).Ram.Should().Be("[dim]512 MB <1%[/]");
    }

    // solidpc, xOllama 0.40.16-xollama.2 on Linux, 2026-11-21: an embedding model on opencoti. The OS's GPU memory is
    // not observable there, the engine reports no KV types and nothing drafts
    [Fact]
    public void Lines_LinuxEmbeddingModel_ServerFiguresAndNoGuesses()
    {
        var model = new RunningModel
        {
            Name = "snowflake-arctic-embed2:latest", Size = 664000265, SizeVram = 664000265, ContextLength = 8192, Runner = "llamacpp",
            Details = new ModelDetails { Family = "bert", Format = "gguf", ParameterSize = "566.70M", QuantizationLevel = "F16" },
        };
        var props = Json("""
            {"default_generation_settings":{"n_ctx":8192},"total_slots":1,
             "opencoti":{"kv":{"kv_pool_cells":8192,"cells_used":61,"cells_free":8192},
               "dca":{"enabled":false,"chunk_size":0},"sparse_attn":{"enabled":false},
               "speculative":{"types":["none"],"draft_model":"","n_max":3,"cache_type_k":"f16","cache_type_v":"f16","drafter":{"state":"none","kind":null}},
               "kv_reuse":{"cache_ram_mib":8192,"ctx_checkpoints":32},"sampling":{"effective_placement":"idle"},
               "polykv":{"max_pools":0,"pools_enabled":false},"gpu_process_memory":{"observable":false},
               "elastic_slots":{"enabled":false,"slots_live":1,"slots_max":1,"reason":"disabled"}}}
            """);
        var kv = Json("""{"rs":null,"rolling_kv":{"residency_mode":"auto","available":false},"cells_total":8192,"cells_used":61,"cells_free":8192,"pressure":{"refused_60s":0}}""");
        var slots = Json("""[{"id":0,"n_ctx":8192,"is_processing":false,"opencoti":{"draft_n_total":0,"draft_n_accepted":0,"state":"idle"}}]""");
        var show = Json("""{"model_info":{"general.architecture":"bert","bert.block_count":24,"bert.attention.head_count":16,"bert.embedding_length":1024,"bert.context_length":8192}}""");

        var d = ModelDetail.Build(model, "opencoti", null, props, kv, slots, show);
        d.Process.Should().BeNull();
        d.KvBytes.Should().BeNull();
        var lines = d.Lines();
        lines[0].Text.Should().Be("VRAM 633 MB (100%) · RAM 0 MB (0%)");
        lines[1].Text.Should().Be("8192 cells, 61 used, 8192 free · window auto");
        lines[2].Text.Should().Be("0 busy of 1 · context 8192");
        lines[3].Text.Should().Be("off");
        lines[4].Text.Should().Be("DCA off · PolyKV pools off · prompt cache 8.0 GB, 32 checkpoints");
    }

    // ELEVEN2GO, 2026-11-21: gemma4 26B A4B with an attached assistant drafter on the 3090 (interleaved sliding
    // window: 25 SWA layers, 5 full-attention layers), after one 64-token chat
    [Fact]
    public void Lines_Gemma4WithAssistantDrafter()
    {
        var model = new RunningModel
        {
            Name = "mannix/gemma4-26b-agentic:mtp-vision-Q4_K_M", Size = 18348570048, SizeVram = 18348570048, ContextLength = 32768, Runner = "llamacpp",
            Details = new ModelDetails { Family = "gemma4", Format = "gguf", ParameterSize = "25.2B", QuantizationLevel = "Q4_K_M" },
        };
        var placement = new RemoteModelPlacement(model.Name, model.Size, model.SizeVram, new List<RemoteModelGpu> { new("CUDA", "0", "0000:11:00.0", 18348570048) })
            { KvResidencyMode = "auto", KvEngaged = false, KvHostBytes = 0 };
        var props = Json("""
            {"default_generation_settings":{"n_ctx":32768},"total_slots":1,
             "opencoti":{"kv":{"kv_pool_cells":32768,"cells_used":85,"cells_free":32768,
                 "effective":{"type_k":"q8_0","type_v":"q8_0","type_k_tail":"q8_0","type_v_tail":"q8_0","n_cells":32768,"n_layers_spilling":0,"fully_resident":true,"is_iswa":true}},
               "dca":{"enabled":false},"sparse_attn":{"enabled":false},
               "speculative":{"types":["none","draft-assistant"],"draft_model":"/C/Users/ManniX/.ollama/models/blobs/sha256-5963a2edfb516fbdff499b1d4b7f8158a7a811af7196eaae00be2119188e422e","n_max":2,
                              "drafter":{"state":"attached","kind":"assistant","head_mib":432}},
               "kv_reuse":{"ctx_checkpoints":8,"cache_ram_mib":2048},"sampling":{"effective_placement":"idle"},
               "polykv":{"max_pools":0,"pools_enabled":false},
               "gpu_process_memory":{"observable":true,"shared_mib":988,"dedicated_mib":18319,"pinned_mib":902,"on_device_mib":18048,"off_card_mib":0,"spilled":false},
               "elastic_slots":{"enabled":true,"slots_live":1,"slots_max":4,"reason":"idle slot available"}}}
            """);
        var kv = Json("""
            {"rs":null,"rolling_kv":{"residency_mode":"auto","engaged":false,"kvarn":false,"layers":5,"layers_spilling":0,"type_k":"q8_0","type_v":"q8_0"},
             "cells_total":32768,"cells_used":85,"cells_free":32768,"swa":{"cells_total":4096,"window":1024,"cells_phys_total":4608},"pressure":{"refused_60s":0}}
            """);
        var slots = Json("""
            [{"id":0,"n_ctx":32768,"is_processing":false,"opencoti":{"draft_n_total":59,"draft_n_accepted":32,"tps_ewma":79.5,"state":"idle"}},
             {"id":1,"n_ctx":32768,"is_processing":false,"opencoti":{"draft_n_total":0,"draft_n_accepted":0,"tps_ewma":0.0,"state":"idle"}}]
            """);
        var show = Json("""
            {"model_info":{"general.architecture":"gemma4","gemma4.attention.head_count":16,"gemma4.attention.head_count_kv":[8,8,8,8,8,2,8,8,8,8,8,2,8,8,8,8,8,2,8,8,8,8,8,2,8,8,8,8,8,2],
               "gemma4.attention.key_length":512,"gemma4.attention.key_length_swa":256,"gemma4.attention.value_length":512,"gemma4.attention.value_length_swa":256,
               "gemma4.attention.sliding_window":1024,"gemma4.attention.sliding_window_pattern":[true,true,true,true,true,false,true,true,true,true,true,false,true,true,true,true,true,false,true,true,true,true,true,false,true,true,true,true,true,false],"gemma4.block_count":30,"gemma4.context_length":262144},
             "drafter":{"source":"attached","spec_type":"draft-assistant","architecture":"gemma4-assistant","parameter_size":"419.71M","quantization_level":"Q8_0",
               "tokens":2,"loaded":{"state":"attached","kind":"assistant","head_mib":432}}}
            """);

        var d = ModelDetail.Build(model, "opencoti", placement, props, kv, slots, show);
        // 25 SWA layers × 8 heads × (256 + 256) over 4608 cells, 5 full layers × 2 heads × (512 + 512) over 32768, q8_0
        d.KvBytes.Should().Be((ulong)(25 * 8 * 512 * 34.0 / 32 * 4608 + 5 * 2 * 1024 * 34.0 / 32 * 32768));
        var lines = d.Lines();
        lines[0].Text.Should().Be("VRAM 17.9 GB (95%) · RAM 988 MB (5%) · on CUDA0 · 902 MB pinned · not spilled · engine buffers 17.1 GB");
        lines[1].Text.Should().Be("q8_0/q8_0 · ~818 MB · 32768 cells, 85 used, 32768 free · all in VRAM · window auto");
        lines[2].Text.Should().Be("0 busy of 1 (max 4) · elastic: idle slot available · last 79.5 tok/s · context 32768 of 262144");
        lines[3].Text.Should().Be("draft-assistant (assistant, attached) · gemma4-assistant 419.71M Q8_0 · up to 2 tokens · 54% accepted");
    }

    [Theory]
    [InlineData("/C/Users/ManniX/.ollama/models/blobs/sha256-5963a2edfb516fbdff499b1d4b7f8158a7a811af7196eaae00be2119188e422e", "sha256-5963a2edfb51")]
    [InlineData(@"D:\models\draft.Q8_0.gguf", "draft.Q8_0.gguf")]
    public void ShortModelPath_FileNameAndShortDigest(string path, string expected) =>
        ModelDetail.ShortModelPath(path).Should().Be(expected);

    // ELEVEN2GO, 2026-11-21: the same gemma pinned to the RX 9070 XT (16 GB). The engine put the token embedding and
    // what did not fit on the CPU: 2.8 GiB of ordinary process memory the GPU counters never see (shared 85 MiB),
    // which the server reports as size − size_vram
    [Fact]
    public void MemorySplit_FromProcess_CountsWeightsTheEngineKeptOnTheCpu()
    {
        var props = Json("""
            {"opencoti":{"speculative":{"types":["none","draft-assistant"],"token_embd":{"device":"CPU","reason":"model 16.1 GiB vs 15.1 GiB free on vulkan:amd exceeds 70% fit bound"}},
              "gpu_process_memory":{"observable":true,"shared_mib":85,"dedicated_mib":15073,"pinned_mib":84,"on_device_mib":14929,"off_card_mib":0,"spilled":false}}}
            """);
        var split = MemorySplit.FromProcess(GpuProcessMemory.FromProps(props), 18131821298, 15110965818)!.Value;
        split.Vram.Should().Be(15073L << 20);
        split.Ram.Should().Be(18131821298 - 15110965818);
        split.RamPct.Should().BeApproximately(16.0, 0.1);

        var model = new RunningModel { Name = "g", Size = 18131821298, SizeVram = 15110965818, ContextLength = 32768 };
        var lines = ModelDetail.Build(model, "opencoti", null, props, null, null, null).Lines();
        lines[0].Text.Should().Be("VRAM 14.7 GB (84%) · RAM 2.8 GB (16%) · 84 MB pinned · not spilled · engine buffers 16.9 GB · token embedding on CPU");
    }
}
