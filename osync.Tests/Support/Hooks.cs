using TechTalk.SpecFlow;
using osync.Tests.Infrastructure;

namespace osync.Tests.Support;

[Binding]
public class Hooks
{
    private static TestConfiguration? _config;
    private static OsyncRunner? _runner;

    [BeforeTestRun]
    public static async Task BeforeTestRun()
    {
        _config = TestConfiguration.Load();
        _runner = new OsyncRunner(_config);

        Console.WriteLine("=== osync Test Suite Starting ===");
        Console.WriteLine($"Test Model: {_config.RegistryModel}");
        Console.WriteLine($"Remote 1: {_config.RemoteDestination1}");
        Console.WriteLine($"Remote 2: {_config.RemoteDestination2}");
        Console.WriteLine($"Test Timeout: {_config.TestTimeout}ms");
        Console.WriteLine("================================");

        // Ensure the base test model is available
        await EnsureBaseModelExists();
    }

    private static async Task EnsureBaseModelExists()
    {
        if (_config == null || _runner == null) return;

        var model = _config.RegistryModel;
        Console.WriteLine($"Checking if base model '{model}' exists locally...");

        try
        {
            var checkResult = await _runner.RunAsync("ls");
            if (checkResult.IsSuccess && checkResult.Output.Contains(model.Split(':')[0]))
            {
                Console.WriteLine($"Base model '{model}' is already available.");
                return;
            }

            Console.WriteLine($"Base model '{model}' not found, pulling...");
            var pullResult = await _runner.RunAsync($"pull {model}", timeoutMs: 600000);
            if (pullResult.IsSuccess)
            {
                Console.WriteLine($"Successfully pulled base model '{model}'.");
            }
            else
            {
                Console.WriteLine($"WARNING: Failed to pull base model '{model}': {pullResult.Error}");
                Console.WriteLine("Some tests may fail without the base model.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WARNING: Error ensuring base model: {ex.Message}");
        }
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
    public async Task AfterScenario(ScenarioContext scenarioContext, TestContext testContext)
    {
        if (_config?.CleanupAfterTests == true && testContext.CreatedModels.Any())
        {
            Console.WriteLine($"Cleaning up {testContext.CreatedModels.Count} test model(s)...");
            foreach (var model in testContext.CreatedModels)
            {
                try
                {
                    Console.WriteLine($"  Removing test model: {model}");
                    var result = await _runner!.RunAsync($"rm {model}");
                    if (!result.IsSuccess)
                        Console.WriteLine($"  Warning: Failed to remove {model}: {result.Error}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  Warning: Error removing {model}: {ex.Message}");
                }
            }
        }

        var status = scenarioContext.TestError == null ? "PASSED" : "FAILED";
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
