using FluentAssertions;

namespace osync.Tests.UnitTests;

public class OsyncSettingsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("osync-settings-tests-").FullName;
    private string SettingsPath => Path.Combine(_dir, "settings.json");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Load_MissingFile_GivesDefaults()
    {
        var settings = OsyncSettings.Load(SettingsPath);

        settings.Server.Flavor.Should().Be("auto");
        settings.ColorMode.Should().Be("auto");
        settings.ConfiguredServerUrl.Should().BeNull();
        settings.ConfiguredFlavor.Should().BeNull();
    }

    [Fact]
    public void SaveAndLoad_RoundTrips()
    {
        var settings = new OsyncSettings { ColorMode = "256" };
        settings.Server.Flavor = "xollama";
        settings.Server.Host = "gpu-box";
        settings.Manage.Theme = "Dracula";
        settings.Save(SettingsPath);

        var loaded = OsyncSettings.Load(SettingsPath);

        loaded.ColorMode.Should().Be("256");
        loaded.Server.Flavor.Should().Be("xollama");
        loaded.Server.Host.Should().Be("gpu-box");
        loaded.Manage.Theme.Should().Be("Dracula");
        var json = File.ReadAllText(SettingsPath);
        json.Should().Contain("\"flavor\": \"xollama\"");
        json.Should().NotContain("configured", "computed properties must not be written");
    }

    [Fact]
    public void Load_InvalidJson_GivesDefaultsAndKeepsTheFile()
    {
        File.WriteAllText(SettingsPath, "{ not json");

        var settings = OsyncSettings.Load(SettingsPath);

        settings.Server.Flavor.Should().Be("auto");
        File.ReadAllText(SettingsPath).Should().Be("{ not json");
    }

    [Fact]
    public void Load_AcceptsCommentsAndTrailingCommas()
    {
        File.WriteAllText(SettingsPath, "{\n // my server\n \"server\": { \"host\": \"box\", },\n}");

        OsyncSettings.Load(SettingsPath).Server.Host.Should().Be("box");
    }

    [Theory]
    [InlineData("ollama", null, null, "http://localhost:11434")]
    [InlineData("xollama", null, null, "http://localhost:22434")]
    [InlineData("auto", "gpu-box", null, "http://gpu-box:11434")]
    [InlineData("xollama", "10.0.0.5", null, "http://10.0.0.5:22434")]
    [InlineData("ollama", "gpu-box", 12345, "http://gpu-box:12345")]
    [InlineData("auto", null, null, null)]
    public void ConfiguredServerUrl_UsesTheFlavorDefaultPort(string flavor, string? host, int? port, string? expected)
    {
        var settings = new OsyncSettings();
        settings.Server.Flavor = flavor;
        settings.Server.Host = host;
        settings.Server.Port = port;

        settings.ConfiguredServerUrl.Should().Be(expected);
    }

    [Fact]
    public void ConfigDirectory_HonorsOverride()
    {
        var previous = Environment.GetEnvironmentVariable("OSYNC_CONFIG_DIR");
        try
        {
            Environment.SetEnvironmentVariable("OSYNC_CONFIG_DIR", _dir);
            OsyncSettings.FilePath.Should().Be(SettingsPath);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OSYNC_CONFIG_DIR", previous);
        }
    }
}
