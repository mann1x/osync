using System.Net;
using System.Text.Json;
using FluentAssertions;

namespace osync.Tests.UnitTests;

public class RemoteMonitorTests
{
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    // GET /api/xollama/devices of an xOllama 0.40.4 with an RTX 3090 (CUDA and its Vulkan copy), an iGPU and an RX 9070 XT
    private const string Devices = """
        {"devices":[
          {"backend":"CUDA","id":"0","pci_id":"0000:11:00.0","name":"CUDA0","description":"NVIDIA GeForce RTX 3090","total_memory":25768755200,"free_memory":24465375232,"engine":"opencoti"},
          {"backend":"Vulkan","id":"0","pci_id":"0000:7a:00.0","name":"Vulkan0","description":"AMD Radeon(TM) Graphics","integrated":true,"total_memory":33357299712,"free_memory":31689015296,"engine":"opencoti"},
          {"backend":"Vulkan","id":"2","pci_id":"0000:03:00.0","name":"Vulkan2","description":"AMD Radeon RX 9070 XT","total_memory":17095983104,"free_memory":16167993344,"engine":"opencoti"},
          {"backend":"Vulkan","id":"1","pci_id":"0000:11:00.0","name":"Vulkan1","description":"NVIDIA GeForce RTX 3090","total_memory":25503465472,"free_memory":24698159104,"engine":"opencoti"}],
         "backends":["CUDA","Vulkan","CPU"]}
        """;

    [Fact]
    public void ParseDevices_KeepsOneListingPerGpu()
    {
        var devices = RemoteServerMonitor.ParseDevices(Json(Devices));

        devices.Select(d => (d.Name, d.Backend)).Should().Equal(
            ("NVIDIA GeForce RTX 3090", "CUDA"),
            ("AMD Radeon(TM) Graphics", "Vulkan"),
            ("AMD Radeon RX 9070 XT", "Vulkan"));
        devices[0].UsedMemory.Should().Be(25768755200UL - 24465375232UL);
        devices[1].Integrated.Should().BeTrue();
        devices[0].Engine.Should().Be("opencoti");
    }

    [Fact]
    public void ParseDevices_NoGpus() =>
        RemoteServerMonitor.ParseDevices(Json("""{"devices":[],"backends":["CPU"]}""")).Should().BeEmpty();

    // GET /api/xollama/system (system_telemetry_v1): an NVIDIA GPU read through NVML with every field, an AMD one on
    // Windows (D3DKMT: memory and utilization only) and a GPU the server does not use
    private const string System = """
        {"as_of":"2026-10-19T07:36:20.4+02:00","interval_ms":2000,
         "host":{"os":"windows","arch":"amd64","hostname":"ELEVEN2GO","uptime_s":93784,"cpu_pct":null,"cpu_count":32,"load":null,
                 "mem_total":54011219968,"mem_used":9019431936,"swap_total":36829011968,"swap_used":644245094},
         "server":{"pid":1194811,"cpu_pct":103.8,"rss":4231260672,"procs":2,
                   "children":[{"pid":1194900,"name":"opencoti-llamafile","model":"qwen3:4b","cpu_pct":99.9,"rss":4200000000}]},
         "devices":[
          {"pci_id":"0000:11:00.0","vendor":"nvidia","name":"NVIDIA GeForce RTX 3090","source":"nvml","backend":"CUDA","id":"0","engine":"opencoti",
           "total_memory":25769803776,"free_memory":21242435584,"used_memory":4527368192,"util_pct":97,"mem_util_pct":61,"temp_c":64,
           "power_w":214.7,"power_limit_w":390,"clock_mhz":1890,"clock_max_mhz":2130,"fan_pct":0,
           "link":{"gen":1,"width":4,"max_gen":4,"max_width":16,"h2d_gbps":null,"d2h_gbps":null}},
          {"pci_id":"0000:03:00.0","vendor":"amd","name":"AMD Radeon RX 9070 XT","source":"d3dkmt","backend":"Vulkan","id":"2","engine":"opencoti",
           "total_memory":17095983104,"free_memory":16167993344,"used_memory":927989760,"util_pct":0,"mem_util_pct":null,"temp_c":null,
           "power_w":null,"power_limit_w":null,"clock_mhz":null,"clock_max_mhz":null,"fan_pct":null,"link":null},
          {"pci_id":"0000:7a:00.0","vendor":"intel","name":"Intel Arc A380","source":"sysfs",
           "total_memory":null,"free_memory":null,"used_memory":null,"util_pct":null,"temp_c":null,"power_w":12.5,"clock_mhz":2000,"link":null}],
         "models":[]}
        """;

    [Fact]
    public void ParseSystem_HostServerAndDevices()
    {
        var system = RemoteServerMonitor.ParseSystem(Json(System))!;

        system.AsOf.Should().Be(new DateTime(2026, 10, 19, 5, 36, 20, 400, DateTimeKind.Utc));
        system.Host!.Hostname.Should().Be("ELEVEN2GO");
        system.Host.CpuCount.Should().Be(32);
        system.Host.MemUsed.Should().Be(9019431936UL);
        system.Server!.CpuPct.Should().Be(103.8);
        system.Server.Children.Should().ContainSingle().Which.Model.Should().Be("qwen3:4b");

        var nvidia = system.Devices[0];
        nvidia.Name.Should().Be("NVIDIA GeForce RTX 3090");
        nvidia.UsedMemory.Should().Be(4527368192UL);
        nvidia.Telemetry!.Source.Should().Be("nvml");
        nvidia.Telemetry.PowerLimitW.Should().Be(390);
        nvidia.Telemetry.FanPct.Should().Be(0);
        (nvidia.Telemetry.LinkGen, nvidia.Telemetry.LinkWidth, nvidia.Telemetry.LinkMaxGen, nvidia.Telemetry.LinkMaxWidth)
            .Should().Be((1, 4, 4, 16));
    }

    [Fact]
    public void ParseSystem_WhatIsNotReadStaysNull()
    {
        var system = RemoteServerMonitor.ParseSystem(Json(System))!;

        // a rate on the first poll after idle, and Windows' missing load average, are null: never 0
        system.Host!.CpuPct.Should().BeNull();
        system.Host.Load.Should().BeNull();

        var amd = system.Devices[1].Telemetry!;
        amd.UtilPct.Should().Be(0);
        amd.TempC.Should().BeNull();
        amd.PowerW.Should().BeNull();
        amd.LinkGen.Should().BeNull();

        var unused = system.Devices[2];
        unused.Backend.Should().BeEmpty();
        unused.TotalMemory.Should().BeNull();
        unused.UsedMemory.Should().BeNull();
        unused.Telemetry!.UtilPct.Should().BeNull();
        unused.Telemetry.PowerW.Should().Be(12.5);
    }

    [Fact]
    public void ParseDevices_HasNoTelemetry() =>
        RemoteServerMonitor.ParseDevices(Json(Devices)).Should().OnlyContain(d => d.Telemetry == null);

    [Fact]
    public void HasFeature_SystemTelemetry()
    {
        RemoteServerMonitor.HasFeature(Json("""{"xollama":true,"features":["council","system_telemetry_v1"]}"""),
            RemoteServerMonitor.SystemTelemetryFeature).Should().BeTrue();
        RemoteServerMonitor.HasFeature(Json("""{"xollama":true,"features":["council"]}"""),
            RemoteServerMonitor.SystemTelemetryFeature).Should().BeFalse();
        RemoteServerMonitor.HasFeature(Json("""{"xollama":true}"""), RemoteServerMonitor.SystemTelemetryFeature).Should().BeFalse();
    }

    [Fact]
    public void ParseEngineList_EngineAndReadable()
    {
        var list = RemoteServerMonitor.ParseEngineList(Json("""
            {"models":[{"model":"qwen3:8b","engine":"opencoti","readable":true},{"model":"gemma3:4b","engine":"llamacpp","readable":false}],"endpoints":["GET props"]}
            """));

        list["qwen3:8b"].Should().Be(("opencoti", true));
        list["GEMMA3:4B"].Should().Be(("llamacpp", false));
    }

    // The body of GET /api/engine?endpoint=slots from a llama.cpp engine, trimmed to the fields read
    private static string Slots(params (int id, long task, bool processing, long decoded)[] slots) =>
        "[" + string.Join(",", slots.Select(s =>
            $$"""{"id":{{s.id}},"n_ctx":2048,"is_processing":{{(s.processing ? "true" : "false")}},"id_task":{{s.task}},"params":{"n_predict":5},"next_token":[{"has_next_token":true,"n_remain":-1,"n_decoded":{{s.decoded}}}]}""")) + "]";

    [Fact]
    public void ParseSlots_BusyAndDecoded()
    {
        var slots = RemoteServerMonitor.ParseSlots(Json(Slots((0, 4421, true, 605), (1, 12, false, 40))))!;

        slots.Should().HaveCount(2);
        slots.Count(s => s.Processing).Should().Be(1);
        slots[0].Should().Be(new RemoteServerMonitor.SlotReading(0, 4421, true, 605));
    }

    [Fact]
    public void ParseSlots_NotASlotList() =>
        RemoteServerMonitor.ParseSlots(Json("""{"error":"no slots"}""")).Should().BeNull();

    [Fact]
    public void DecodeRate_TokensOfTheSameTaskOverTime()
    {
        var before = RemoteServerMonitor.ParseSlots(Json(Slots((0, 4421, true, 605), (1, 12, false, 40))))!;
        var after = RemoteServerMonitor.ParseSlots(Json(Slots((0, 4421, true, 1043), (1, 13, true, 60))))!;

        // slot 0: 438 more on the same task; slot 1: a new task that has decoded 60 since
        RemoteServerMonitor.DecodeRate(before, after, 2).Should().Be((438 + 60) / 2.0);
        RemoteServerMonitor.DecodeRate(null, after, 2).Should().BeNull();
        RemoteServerMonitor.DecodeRate(after, after, 2).Should().Be(0);
    }

    [Fact]
    public void ParseKvPressure_RefusalsInsideTheWindow()
    {
        var kv = Json("""{"allocations":[],"pressure":{"window_s":60,"refused_60s":3,"refused_needed_max_60s":8192,"last_refusal_age_s":12.5}}""");
        RemoteServerMonitor.ParseKvPressure(kv).Should().Be((3, true));

        var old = Json("""{"pressure":{"window_s":60,"refused_60s":1,"last_refusal_age_s":75}}""");
        RemoteServerMonitor.ParseKvPressure(old).Should().Be((1, false));

        RemoteServerMonitor.ParseKvPressure(Json("""{"allocations":[]}""")).Should().BeNull();
    }

    [Fact]
    public void ParseTokensPerSecond_JsonOrEvents()
    {
        RemoteServerMonitor.ParseTokensPerSecond(Json("""{"tps":41.5}"""), null).Should().Be(41.5);
        RemoteServerMonitor.ParseTokensPerSecond(Json("""{"sessions":[{"id":"a","tps":10},{"id":"b","tps":5.5}]}"""), null).Should().Be(15.5);
        RemoteServerMonitor.ParseTokensPerSecond(null, "data: {\"tps\":41.5}\n\ndata: {\"tps\":42.0}\n\n").Should().Be(42.0);
        RemoteServerMonitor.ParseTokensPerSecond(Json("""{"error":"x"}"""), null).Should().BeNull();
        RemoteServerMonitor.ParseTokensPerSecond(null, null).Should().BeNull();
    }

    [Fact]
    public void AnyLocal_LoopbackOrOwnAddress()
    {
        var own = new[] { IPAddress.Parse("192.168.178.20"), IPAddress.Parse("fe80::1") };

        RemoteServerMonitor.AnyLocal(new[] { IPAddress.Parse("192.168.178.20") }, own).Should().BeTrue();
        RemoteServerMonitor.AnyLocal(new[] { IPAddress.Parse("::ffff:192.168.178.20") }, own).Should().BeTrue();
        RemoteServerMonitor.AnyLocal(new[] { IPAddress.Loopback }, own).Should().BeTrue();
        RemoteServerMonitor.AnyLocal(new[] { IPAddress.Parse("192.168.178.161") }, own).Should().BeFalse();
    }

    [Theory]
    [InlineData("http://localhost:11434", true)]
    [InlineData("http://127.0.0.1:22434", true)]
    [InlineData("http://192.0.2.10:11434", false)]
    public void IsThisMachine(string url, bool expected) =>
        RemoteServerMonitor.IsThisMachine(url).Should().Be(expected);
}
