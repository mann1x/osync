using FluentAssertions;

namespace osync.Tests.UnitTests;

public class LocalServerResolutionTests
{
    private static OsyncSettings Configured(bool ignoreEnvironment)
    {
        var settings = new OsyncSettings();
        settings.Server.Flavor = "ollama";
        settings.Server.Host = "nas";
        settings.Server.IgnoreEnvironment = ignoreEnvironment ? true : null;
        return settings;
    }

    private static Func<string, string?> Env(string? xollamaHost = null, string? ollamaHost = null) => name => name switch
    {
        "XOLLAMA_HOST" => xollamaHost,
        "OLLAMA_HOST" => ollamaHost,
        _ => null
    };

    [Fact]
    public void Environment_TakesPrecedence_ByDefault()
    {
        OllamaServer.ResolveLocalUrl(Env(xollamaHost: "127.0.0.1:22434"), Configured(false), _ => false)
            .Should().Be("http://127.0.0.1:22434");
        OllamaServer.OverridingEnvironmentVariable(Env(xollamaHost: "127.0.0.1:22434"), Configured(false))
            .Should().Be("XOLLAMA_HOST");
    }

    [Fact]
    public void Settings_TakePrecedence_WhenTheEnvironmentIsIgnored()
    {
        OllamaServer.ResolveLocalUrl(Env(xollamaHost: "127.0.0.1:22434", ollamaHost: "10.0.0.1"), Configured(true), _ => false)
            .Should().Be("http://nas:11434");
        OllamaServer.OverridingEnvironmentVariable(Env(xollamaHost: "127.0.0.1:22434"), Configured(true))
            .Should().BeNull();
    }

    [Fact]
    public void IgnoringTheEnvironment_NeedsAConfiguredServer()
    {
        var settings = new OsyncSettings();
        settings.Server.IgnoreEnvironment = true;

        OllamaServer.ResolveLocalUrl(Env(ollamaHost: "10.0.0.1:11434"), settings, _ => false).Should().Be("http://10.0.0.1:11434");
    }

    [Fact]
    public void WithoutEnvironmentOrSettings_TheDefaultPortsAreProbed()
    {
        OllamaServer.ResolveLocalUrl(Env(), new OsyncSettings(), url => url.EndsWith(":22434")).Should().Be("http://localhost:22434");
        OllamaServer.ResolveLocalUrl(Env(), new OsyncSettings(), _ => false).Should().Be("http://localhost:11434");
    }

    // ---- servers of manage (Ctrl+Left / Ctrl+Right) ---------------------------------------------------------

    [Fact]
    public void ManageServers_LocalFirst_ThenChosenAliases()
    {
        var settings = new OsyncSettings();
        settings.Aliases["gpu"] = "http://gpu:11434";
        settings.Aliases["nas"] = "http://nas:11434";
        settings.Aliases["same"] = "http://localhost:11434/";
        settings.Manage.Servers = new() { "nas", "missing", "same", "NAS" };

        var targets = ManageServers.Targets(settings, localUrl: "http://localhost:11434");

        targets.Select(t => t.Name).Should().Equal("local", "nas");
        targets[0].Url.Should().BeNull();
        targets[1].Url.Should().Be("http://nas:11434");
    }

    [Fact]
    public void ManageServers_IncludeTheOtherServer_WhenBothRunSideBySide()
    {
        var settings = new OsyncSettings();
        settings.Server.Flavor = "ollama";
        settings.Server.Both = true;
        settings.Aliases["ollama"] = "http://localhost:11434";
        settings.Aliases["xollama"] = "http://localhost:22434";
        settings.Aliases["gpu"] = "http://gpu:11434";
        settings.Manage.Servers = new() { "gpu" };

        ManageServers.Targets(settings, localUrl: "http://localhost:11434").Select(t => t.Name)
            .Should().Equal("local", "xollama", "gpu");
    }
}
