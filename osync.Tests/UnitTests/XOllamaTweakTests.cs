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

    [Fact]
    public void Scopes_OfferTheEnginePolicies()
    {
        XOllamaTweak.Scopes.Select(s => s.Flags).Should().Contain(
            "--kv-residency --kv-rolling-window --fit --vram-target --mtp-policy");
        XOllamaTweak.Arguments(XOllamaTweak.Scopes[8], "qwen3:8b", null)
            .Should().Be("tweak model qwen3:8b --kv-residency --kv-rolling-window --fit --vram-target --mtp-policy");
    }

    [Fact]
    public void ShowModelScope_RunsTweakShowModel()
    {
        var show = XOllamaTweak.Scopes.Single(s => s.Command == "show model");
        XOllamaTweak.Arguments(show, "qwen3:8b", null).Should().Be("tweak show model qwen3:8b");
    }

    [Theory]
    [InlineData(0, null, "tweak server")]
    [InlineData(0, "--slots", "tweak server --slots")]
    [InlineData(1, "0000:01:00.0 --priority 10", "tweak server gpu 0000:01:00.0 --priority 10")]
    [InlineData(2, "--unset OLLAMA_KV_CACHE_TYPE", "tweak envs --unset OLLAMA_KV_CACHE_TYPE")]
    [InlineData(3, null, "tweak show server")]
    public void ServerScopes_RunOnce_WithoutAModel(int index, string? extra, string expected)
    {
        var scope = XOllamaTweak.ServerScopes[index];
        scope.PerModel.Should().BeFalse();
        XOllamaTweak.Arguments(scope, "qwen3:8b", extra).Should().Be(expected);
    }

    [Theory]
    [InlineData("http://localhost:22434", true)]
    [InlineData("http://127.0.0.1:22434", true)]
    [InlineData("http://[::1]:22434", true)]
    [InlineData("http://0.0.0.0:22434", true)]
    [InlineData("http://192.168.1.10:22434", false)]
    [InlineData("http://gpu.example.com:22434", false)]
    [InlineData("not a url", false)]
    public void IsOnThisMachine_OnlyLoopback(string url, bool expected) =>
        XOllamaTweak.IsOnThisMachine(url).Should().Be(expected);

    [Fact]
    public void ServerDefaults_FlattensTheDefaultsOfASettingsAnswer()
    {
        using var doc = JsonDocument.Parse("""{"path":"/root/.ollama/xollama-settings.json","envs":[],"defaults":{"version":6,"kv":{"k":"q8_0","v":"q8_0","rolling_window":"on"},"fit":{"vram_target_mib":20000}}}""");
        XOllamaTweak.ServerDefaults(doc.RootElement).Should().Equal(
            ("kv.k", "q8_0"), ("kv.v", "q8_0"), ("kv.rolling_window", "on"), ("fit.vram_target_mib", "20000"));
    }

    [Fact]
    public void ServerDefaults_NoneStated_GivesNoRows()
    {
        using var doc = JsonDocument.Parse("""{"path":"x","envs":[]}""");
        XOllamaTweak.ServerDefaults(doc.RootElement).Should().BeEmpty();
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
