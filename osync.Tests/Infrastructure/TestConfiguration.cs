using Microsoft.Extensions.Configuration;

namespace osync.Tests.Infrastructure;

public class TestConfiguration
{
    public string RegistryModel { get; set; } = "llama3.2:1b";
    public string RemoteDestination1 { get; set; } = string.Empty;
    public string RemoteDestination2 { get; set; } = string.Empty;
    public int TestTimeout { get; set; } = 300000;
    public bool CleanupAfterTests { get; set; } = true;
    public string? OsyncExecutablePath { get; set; }
    public bool VerboseOutput { get; set; }

    public TestCategories TestCategories { get; set; } = new();

    public static TestConfiguration Load(string[]? commandLineArgs = null)
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("test-config.json", optional: false)
            .AddEnvironmentVariables("OSYNC_TEST_")
            .AddCommandLine(commandLineArgs ?? Array.Empty<string>())
            .Build();

        var testConfig = new TestConfiguration();
        config.GetSection("TestConfiguration").Bind(testConfig);
        return testConfig;
    }
}

public class TestCategories
{
    public bool RunBasic { get; set; } = true;
    public bool RunRemote { get; set; } = false;
    public bool RunInteractive { get; set; } = false;
    public bool RunDestructive { get; set; } = false;
}
