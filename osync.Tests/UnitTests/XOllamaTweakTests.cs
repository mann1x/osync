using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;

namespace osync.Tests.UnitTests;

public class XOllamaTweakTests
{
    private static List<(string Path, string Value)> Flatten(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return XOllamaTweak.Flatten(doc.RootElement);
    }

    [Fact]
    public void Flatten_ListsNestedSettingsByPath_WithoutTheVersion()
    {
        Flatten("""{"version":4,"kv":{"k":"f16","v":"q8_0"},"council":{"enabled":true,"critic":{"count":3}},"slots":{"tps_floor":12.5}}""")
            .Should().Equal(("kv.k", "f16"), ("kv.v", "q8_0"), ("council.enabled", "on"), ("council.critic.count", "3"),
                ("slots.tps_floor", "12.5"));
    }

    [Fact]
    public void Flatten_WritesSwitchesAsOnOff_AndListsAsCommaSeparated()
    {
        Flatten("""{"dca":{"enabled":false},"devices":{"backend":"CUDA","ids":["0000:01:00.0","0000:02:00.0"]}}""")
            .Should().Equal(("dca.enabled", "off"), ("devices.backend", "CUDA"), ("devices.ids", "0000:01:00.0,0000:02:00.0"));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("""{"version":1}""")]
    [InlineData("""{"kv":null,"devices":{"ids":[]}}""")]
    public void Flatten_NoSettings_GivesNoRows(string json) =>
        Flatten(json).Should().BeEmpty();

    [Fact]
    public void Describe_AlignsValues_AndShortensLongOnes()
    {
        var lines = XOllamaTweak.Describe(new List<(string, string)>
        {
            ("kv.k", "f16"),
            ("council.charter", "You are a council.\nResearch the question " + new string('x', 80))
        }, maxValue: 30).ToList();

        lines[0].Should().Be("kv.k             f16");
        lines[1].Should().StartWith("council.charter  You are a council. Research");
        lines[1].Should().EndWith("...");
        lines[1].Length.Should().Be("council.charter  ".Length + 30);
    }

    [Theory]
    [InlineData("", null, "tweak model qwen3:8b")]
    [InlineData("--council", null, "tweak model qwen3:8b --council")]
    [InlineData("", " --kv-k=q8_0 --kv-v=q8_0 ", "tweak model qwen3:8b --kv-k=q8_0 --kv-v=q8_0")]
    [InlineData("--dca", "--dca-chunk=32768", "tweak model qwen3:8b --dca --dca-chunk=32768")]
    public void Arguments_ScopeThenTypedFlags(string scope, string? extra, string expected) =>
        XOllamaTweak.Arguments("qwen3:8b", scope, extra).Should().Be(expected);

    [Fact]
    public void Scopes_EndWithClear()
    {
        XOllamaTweak.Scopes[0].Flags.Should().BeEmpty("the first option walks every setting");
        XOllamaTweak.Scopes[XOllamaTweak.ClearScope].Flags.Should().Be("--clear");
    }

    [Theory]
    [InlineData("http://192.168.1.10:22434", "192.168.1.10:22434")]
    [InlineData("http://localhost:11434/", "localhost:11434")]
    [InlineData("https://gpu.example.com:443", "https://gpu.example.com")]
    public void CliEnvironment_PointsBothVariablesAtTheServer(string url, string expected)
    {
        var startInfo = new ProcessStartInfo("xollama");
        OllamaServer.ApplyCliEnvironment(startInfo, url);
        startInfo.Environment["XOLLAMA_HOST"].Should().Be(expected);
        startInfo.Environment["OLLAMA_HOST"].Should().Be(expected);
    }
}
