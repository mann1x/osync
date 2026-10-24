using System.Text.Json;
using FluentAssertions;

namespace osync.Tests.UnitTests;

public class LocalReadingsTests
{
    private static GpuMetrics Gpu(int index, string name, double usedMb = 4096, double totalMb = 16303) => new()
    {
        Index = index,
        Name = name,
        Vendor = GpuVendor.Nvidia,
        ComputeUtilization = 64,
        VramUsedMB = usedMb,
        VramTotalMB = totalMb,
        Temperature = 60,
        PowerDrawW = 235,
        PowerLimitW = 360,
        ClockSpeedMHz = 2692,
        ClockSpeedMaxMHz = 3090,
        FanSpeedPercent = 51,
    };

    private static List<RemoteDevice> XOllamaDevices() => RemoteServerMonitor.ParseDevices(JsonDocument.Parse("""
        {"devices":[
          {"backend":"CUDA","id":"0","pci_id":"0000:01:00.0","name":"CUDA0","description":"NVIDIA GeForce RTX 5080","total_memory":17094475776,"free_memory":3000000000,"engine":"opencoti"},
          {"backend":"Vulkan","id":"1","pci_id":"0000:7a:00.0","name":"Vulkan1","description":"AMD Radeon(TM) Graphics","integrated":true,"total_memory":536870912,"free_memory":436870912,"engine":"opencoti"}]}
        """).RootElement);

    [Fact]
    public void MergeDevices_TakesBackendAndEngineFromTheServersListAndKeepsLocalReadings()
    {
        var devices = LocalReadings.MergeDevices(new[] { Gpu(0, "NVIDIA GeForce RTX 5080") }, XOllamaDevices());

        devices.Should().HaveCount(2);
        var gpu = devices[0];
        (gpu.Backend, gpu.Engine, gpu.PciId).Should().Be(("CUDA", "opencoti", "0000:01:00.0"));
        gpu.UsedMemory.Should().Be((ulong)(4096 * 1024.0 * 1024.0));
        gpu.Telemetry!.UtilPct.Should().Be(64);
        gpu.Telemetry.PowerLimitW.Should().Be(360);
        gpu.Telemetry.LinkGen.Should().BeNull();
        // a card only xOllama lists comes after, without live readings
        devices[1].Name.Should().Be("AMD Radeon(TM) Graphics");
        devices[1].Telemetry.Should().BeNull();
    }

    [Fact]
    public void MergeDevices_WithoutAServerList_LeavesTheBackendUnknown()
    {
        var devices = LocalReadings.MergeDevices(new[] { Gpu(0, "NVIDIA GeForce RTX 5080") }, null);

        devices.Should().ContainSingle();
        devices[0].Backend.Should().BeEmpty();
        devices[0].BackendKnown.Should().BeFalse();
    }

    [Fact]
    public void MergeDevices_ReadingsTheToolsDoNotHaveAreNull()
    {
        var gpu = Gpu(0, "Some GPU");
        gpu.FanSpeedPercent = 0;
        gpu.PowerLimitW = 0;
        gpu.ClockSpeedMaxMHz = 0;

        var t = LocalReadings.MergeDevices(new[] { gpu }, null)[0].Telemetry!;

        t.FanPct.Should().BeNull();
        t.PowerLimitW.Should().BeNull();
        t.ClockMaxMhz.Should().BeNull();
        t.ClockMhz.Should().Be(2692);
    }

    [Fact]
    public void Build_ServerCpuIsOfOneCore()
    {
        var system = new SystemMetricsData
        {
            CpuUsagePercent = 10, RamUsedBytes = 28L << 30, RamTotalBytes = 32L << 30, SwapUsedBytes = 0, SwapTotalBytes = 0,
        };
        var server = new OllamaProcessMetrics { ProcessCount = 4, CpuUsagePercent = 12.5, WorkingSetBytes = 8L << 30 };

        var result = LocalReadings.Build(system, server, Array.Empty<GpuMetrics>(), null, 16, "pc", 3600);

        result.Server!.CpuPct.Should().Be(200);  // 12.5% of 16 cores = two cores busy
        result.Server.Procs.Should().Be(4);
        result.Host!.MemTotal.Should().Be(32UL << 30);
        result.Host.SwapTotal.Should().Be(0UL);  // no swap: "none", not unknown
        result.Host.Load.Should().BeNull();
        result.Devices.Should().BeEmpty();
    }

    [Fact]
    public void Build_NoServerProcess_NoServerLine()
    {
        var result = LocalReadings.Build(null, null, Array.Empty<GpuMetrics>(), null, 8, "pc", 1);

        result.Host.Should().BeNull();
        result.Server.Should().BeNull();
    }

    [Theory]
    [InlineData("NVIDIA GeForce RTX 5080", "NVIDIA GeForce RTX 5080", true)]
    [InlineData("GeForce RTX 5080", "NVIDIA GeForce RTX 5080", true)]
    [InlineData("AMD Radeon RX 9070 XT", "AMD Radeon(TM) Graphics", false)]
    [InlineData("", "NVIDIA GeForce RTX 5080", false)]
    public void SameCard(string a, string b, bool expected) => LocalReadings.SameCard(a, b).Should().Be(expected);
}
