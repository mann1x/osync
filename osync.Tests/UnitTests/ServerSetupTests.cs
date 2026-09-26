using FluentAssertions;

namespace osync.Tests.UnitTests;

public class ServerSetupTests
{
    private static ServerSetup.Probe Down(string _) => new(false, ServerFlavor.Unknown, null);

    private static (bool Ok, OsyncSettings Settings, string Output) Run(string answers, Func<string, ServerSetup.Probe>? probe = null, OsyncSettings? settings = null)
    {
        settings ??= new OsyncSettings();
        var output = new StringWriter();
        var ok = ServerSetup.Configure(settings, new StringReader(answers), output, probe ?? Down);
        return (ok, settings, output.ToString());
    }

    [Fact]
    public void Defaults_WhenNothingRuns_AreLocalOllama()
    {
        var (ok, settings, output) = Run("\n\n\n");

        ok.Should().BeTrue();
        settings.Server.Flavor.Should().Be("ollama");
        settings.Server.Host.Should().Be("localhost");
        settings.Server.Port.Should().BeNull("the default port is not stored");
        output.Should().Contain("no server answers at http://localhost:11434");
    }

    [Fact]
    public void DetectedXOllama_IsTheDefault_WithItsPort()
    {
        ServerSetup.Probe Probe(string url) => url.EndsWith(":22434")
            ? new(true, ServerFlavor.XOllama, "0.34.2-xollama.1")
            : new(false, ServerFlavor.Unknown, null);

        var (ok, settings, output) = Run("\n\n\n", Probe);

        ok.Should().BeTrue();
        settings.Server.Flavor.Should().Be("xollama");
        settings.ConfiguredServerUrl.Should().Be("http://localhost:22434");
        output.Should().Contain("Found xOllama 0.34.2-xollama.1 on localhost:22434");
        output.Should().Contain("✓ Connected to xOllama");
    }

    [Fact]
    public void ExplicitAnswers_AreStored()
    {
        var (ok, settings, _) = Run("2\ngpu-box\n30000\n");

        ok.Should().BeTrue();
        settings.Server.Flavor.Should().Be("xollama");
        settings.Server.Host.Should().Be("gpu-box");
        settings.Server.Port.Should().Be(30000);
        settings.ConfiguredServerUrl.Should().Be("http://gpu-box:30000");
    }

    [Fact]
    public void InvalidAnswers_AreAskedAgain()
    {
        var (ok, settings, output) = Run("3\nxollama\nnot a host!\n10.0.0.7\n99999\n\n");

        ok.Should().BeTrue();
        settings.Server.Flavor.Should().Be("xollama");
        settings.Server.Host.Should().Be("10.0.0.7");
        settings.Server.Port.Should().BeNull();
        output.Should().Contain("Invalid answer");
    }

    [Fact]
    public void EndOfInput_CancelsWithoutChanges()
    {
        var (ok, settings, _) = Run("2\n");

        ok.Should().BeFalse();
        settings.Server.Flavor.Should().Be("auto");
    }

    [Fact]
    public void FlavorMismatch_IsReported()
    {
        ServerSetup.Probe Probe(string url) => new(true, ServerFlavor.Ollama, "0.34.4");

        var (_, _, output) = Run("2\n\n\n", Probe);

        output.Should().Contain("is Ollama 0.34.4, not xOllama");
    }

    [Fact]
    public void SavedSettings_AreTheDefaults()
    {
        var saved = new OsyncSettings();
        saved.Server.Flavor = "ollama";
        saved.Server.Host = "nas";
        saved.Server.Port = 11500;

        var (ok, settings, _) = Run("\n\n\n", settings: saved);

        ok.Should().BeTrue();
        settings.ConfiguredServerUrl.Should().Be("http://nas:11500");
    }
}
