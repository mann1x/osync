using FluentAssertions;

namespace osync.Tests.UnitTests;

public class ServerSetupTests
{
    private static ServerSetup.Probe Down(string _) => new(false, ServerFlavor.Unknown, null);

    private static (bool Ok, OsyncSettings Settings, string Output) Run(string answers, Func<string, ServerSetup.Probe>? probe = null, OsyncSettings? settings = null,
        Func<string, string?>? env = null)
    {
        settings ??= new OsyncSettings();
        var output = new StringWriter();
        var ok = ServerSetup.Configure(settings, new StringReader(answers), output, probe ?? Down, env ?? (_ => null));
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
        var (ok, settings, output) = Run("4\nxollama\nnot a host!\n10.0.0.7\n99999\n\n");

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

    // ---- both servers side by side --------------------------------------------------------------------------

    [Fact]
    public void Both_StoresTheDefaultServerAndAliasesForEach()
    {
        var (ok, settings, output) = Run("3\n\n\n\n\n");

        ok.Should().BeTrue();
        settings.Server.Both.Should().BeTrue();
        settings.Server.Flavor.Should().Be("ollama");
        settings.ConfiguredServerUrl.Should().Be("http://localhost:11434");
        settings.Aliases["ollama"].Should().Be("http://localhost:11434");
        settings.Aliases["xollama"].Should().Be("http://localhost:22434");
        output.Should().Contain("Default server");
    }

    [Fact]
    public void Both_WithXOllamaAsDefault_AndCustomPorts()
    {
        var (ok, settings, _) = Run("both\nbox\n11500\n22500\n2\n");

        ok.Should().BeTrue();
        settings.Server.Flavor.Should().Be("xollama");
        settings.ConfiguredServerUrl.Should().Be("http://box:22500");
        settings.Aliases["ollama"].Should().Be("http://box:11500");
        settings.Aliases["xollama"].Should().Be("http://box:22500");
    }

    [Fact]
    public void BothFound_IsTheDefaultAnswer()
    {
        ServerSetup.Probe Probe(string url) => url.EndsWith(":22434")
            ? new(true, ServerFlavor.XOllama, "0.34.2-xollama.1")
            : new(true, ServerFlavor.Ollama, "0.34.4");

        var (ok, settings, output) = Run("\n\n\n\n\n", Probe);

        ok.Should().BeTrue();
        settings.Server.Both.Should().BeTrue();
        output.Should().Contain("3) Both (side by side) [3]");
    }

    [Fact]
    public void SwitchingFromBothToOne_RemovesTheirAliases()
    {
        var settings = new OsyncSettings();
        Run("3\n\n\n\n\n", settings: settings);
        settings.Aliases["gpu"] = "http://gpu:11434";

        var (ok, _, output) = Run("1\n\n\n", settings: settings);

        ok.Should().BeTrue();
        settings.Server.Both.Should().BeNull();
        settings.Aliases.Should().ContainKey("gpu").And.NotContainKey("ollama").And.NotContainKey("xollama");
        output.Should().Contain("Removed the aliases");
    }

    // ---- install: ask only when needed ----------------------------------------------------------------------

    private static (bool Changed, OsyncSettings Settings, string Output) Auto(Func<string, ServerSetup.Probe> probe,
        string answers = "", bool interactive = true, OsyncSettings? settings = null, Dictionary<string, string>? env = null)
    {
        settings ??= new OsyncSettings();
        var output = new StringWriter();
        var changed = ServerSetup.AutoConfigure(settings, new StringReader(answers), output, probe,
            n => env != null && env.TryGetValue(n, out var v) ? v : null, interactive);
        return (changed, settings, output.ToString());
    }

    private static ServerSetup.Probe OnlyXOllama(string url) => url.EndsWith(":22434")
        ? new(true, ServerFlavor.XOllama, "0.34.2-xollama.1")
        : new(false, ServerFlavor.Unknown, null);

    private static ServerSetup.Probe BothUp(string url) => url.EndsWith(":22434")
        ? new(true, ServerFlavor.XOllama, "0.34.2-xollama.1")
        : new(true, ServerFlavor.Ollama, "0.34.4");

    [Fact]
    public void Install_OneServerFound_IsUsedWithoutQuestions()
    {
        var (changed, settings, output) = Auto(OnlyXOllama, answers: "");

        changed.Should().BeTrue();
        settings.Server.Flavor.Should().Be("xollama");
        settings.ConfiguredServerUrl.Should().Be("http://localhost:22434");
        output.Should().Contain("Found xOllama 0.34.2-xollama.1 on localhost:22434: using it");
        output.Should().NotContain("Server type");
    }

    [Fact]
    public void Install_BothFound_Asks()
    {
        var (changed, settings, output) = Auto(BothUp, answers: "\n\n\n\n\n");

        changed.Should().BeTrue();
        output.Should().Contain("Server type");
        settings.Server.Both.Should().BeTrue();
    }

    [Fact]
    public void Install_NoneFound_Asks()
    {
        var (changed, _, output) = Auto(Down, answers: "1\n\n\n");

        changed.Should().BeTrue();
        output.Should().Contain("Server type");
    }

    [Fact]
    public void Install_WithoutATerminal_DoesNotAsk()
    {
        var (changed, _, output) = Auto(BothUp, interactive: false);

        changed.Should().BeFalse();
        output.Should().Contain("osync setup server");
        output.Should().NotContain("Server type");
    }

    [Fact]
    public void Install_ConfiguredServer_IsKept()
    {
        var saved = new OsyncSettings();
        saved.Server.Flavor = "ollama";
        saved.Server.Host = "nas";

        var (changed, settings, output) = Auto(BothUp, settings: saved);

        changed.Should().BeFalse();
        settings.Server.Host.Should().Be("nas");
        output.Should().Contain("Ollama @ nas:11434");
    }

    [Fact]
    public void Install_EnvironmentVariable_TakesPrecedence()
    {
        var (changed, _, output) = Auto(BothUp, env: new() { ["OLLAMA_HOST"] = "10.0.0.2:11434" });

        changed.Should().BeFalse();
        output.Should().Contain("OLLAMA_HOST=10.0.0.2:11434");
    }

    // ---- environment variables vs settings ------------------------------------------------------------------

    [Fact]
    public void EnvironmentVariable_AsksWhichServerWins()
    {
        var env = (Func<string, string?>)(n => n == "XOLLAMA_HOST" ? "127.0.0.1:22434" : null);

        var (ok, settings, output) = Run("1\nnas\n\n\n", env: env);

        ok.Should().BeTrue();
        settings.Server.IgnoreEnvironment.Should().BeTrue("Enter keeps the server configured here");
        output.Should().Contain("XOLLAMA_HOST=127.0.0.1:22434 is set");

        var (_, again, _) = Run("1\nnas\n\n2\n", env: env, settings: settings);
        again.Server.IgnoreEnvironment.Should().BeNull("the environment variable was chosen");
    }
}
