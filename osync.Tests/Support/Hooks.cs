using TechTalk.SpecFlow;
using osync.Tests.Infrastructure;

namespace osync.Tests.Support;

[Binding]
public class Hooks
{
    private static TestConfiguration? _config;
    private static OsyncRunner? _runner;

    [BeforeTestRun]
    public static void BeforeTestRun()
    {
        _config = TestConfiguration.Load();
        _runner = new OsyncRunner(_config);

        Console.WriteLine("=== osync Test Suite Starting ===");
        Console.WriteLine($"Test Model: {_config.RegistryModel}");
        Console.WriteLine($"Remote 1: {_config.RemoteDestination1}");
        Console.WriteLine($"Remote 2: {_config.RemoteDestination2}");
        Console.WriteLine($"Test Timeout: {_config.TestTimeout}ms");
        Console.WriteLine("================================");
    }

    [BeforeScenario]
    public void BeforeScenario(ScenarioContext scenarioContext)
    {
        if (_config == null || _runner == null)
        {
            throw new InvalidOperationException("Test configuration not initialized");
        }

        // Register instances for dependency injection
        scenarioContext.ScenarioContainer.RegisterInstanceAs(_config);
        scenarioContext.ScenarioContainer.RegisterInstanceAs(_runner);
        scenarioContext.ScenarioContainer.RegisterInstanceAs(new TestContext(_config));

        var scenarioTitle = scenarioContext.ScenarioInfo.Title;
        Console.WriteLine($"\n--- Scenario: {scenarioTitle} ---");
    }

    [AfterScenario]
    public void AfterScenario(ScenarioContext scenarioContext, TestContext testContext)
    {
        if (_config?.CleanupAfterTests == true && testContext.CreatedModels.Any())
        {
            Console.WriteLine($"Cleaning up {testContext.CreatedModels.Count} test model(s)...");
            // Cleanup logic would go here
        }

        var status = scenarioContext.TestError == null ? "✓ PASSED" : "✗ FAILED";
        Console.WriteLine($"{status}: {scenarioContext.ScenarioInfo.Title}");

        if (scenarioContext.TestError != null)
        {
            Console.WriteLine($"Error: {scenarioContext.TestError.Message}");
        }
    }

    [AfterTestRun]
    public static void AfterTestRun()
    {
        Console.WriteLine("\n=== osync Test Suite Completed ===");
    }
}
