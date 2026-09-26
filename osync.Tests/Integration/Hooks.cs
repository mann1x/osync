using osync.Tests.Integration.Infrastructure;
using Reqnroll;
using Reqnroll.UnitTestProvider;

namespace osync.Tests.Integration;

/// <summary>
/// Scenario tags declare what a scenario needs; scenarios whose requirements are not met are skipped:
///   @local     local Ollama server reachable, ollama CLI on PATH, test model available
///   @remote1   OSYNC_TEST_REMOTE1 configured and reachable (+ test model)
///   @remote2   OSYNC_TEST_REMOTE2 configured and reachable (+ test model)
///   @registry  OSYNC_TEST_REGISTRY=1 (downloads from registry.ollama.ai / huggingface.co)
///   @cli       needs nothing but the osync binary (runs in the unit-test tier)
///   @exclusive OSYNC_TEST_EXCLUSIVE=1 (servers are dedicated to tests; e.g. "unload all")
///   @knownbug  documents a confirmed osync bug; excluded from the required CI job until fixed
/// Scenarios without any of these tags need nothing but the osync binary.
/// </summary>
[Binding]
public sealed class Hooks
{
    private readonly ScenarioContext _scenario;
    private readonly ScenarioState _state;
    private readonly IUnitTestRuntimeProvider _runtime;
    private readonly IReqnrollOutputHelper _output;

    public Hooks(ScenarioContext scenario, ScenarioState state, IUnitTestRuntimeProvider runtime, IReqnrollOutputHelper output)
    {
        _scenario = scenario;
        _state = state;
        _runtime = runtime;
        _output = output;
    }

    [BeforeTestRun]
    public static async Task RemoveStaleTestModels()
    {
        // Leftovers of interrupted runs; only names in the test namespace are touched
        foreach (var url in new[] { TestEnvironment.LocalUrl, TestEnvironment.Remote1Url, TestEnvironment.Remote2Url })
        {
            if (!TestEnvironment.IsReachable(url)) continue;
            var api = new OllamaApi(url!);
            foreach (var model in await api.ListAsync())
                if (model.Name.StartsWith(ScenarioState.TestModelPrefix, StringComparison.Ordinal))
                    await api.DeleteAsync(model.Name);
        }
    }

    [BeforeScenario(Order = 0)]
    public void CheckRequirements()
    {
        var tags = _scenario.ScenarioInfo.CombinedTags;
        var missing = new List<string>();

        bool needsModel = false;
        if (tags.Contains("local"))
        {
            needsModel = true;
            if (!TestEnvironment.IsReachable(TestEnvironment.LocalUrl)) missing.Add($"local Ollama at {TestEnvironment.LocalUrl}");
            if (!TestEnvironment.OllamaCliAvailable) missing.Add("ollama CLI on PATH");
        }
        foreach (var remote in new[] { "remote1", "remote2" })
        {
            if (!tags.Contains(remote)) continue;
            needsModel = true;
            var url = TestEnvironment.ServerUrl(remote);
            if (url == null) missing.Add($"OSYNC_TEST_{remote.ToUpperInvariant()}");
            else if (!TestEnvironment.IsReachable(url)) missing.Add($"{remote} Ollama at {url}");
        }
        if (tags.Contains("registry") && !TestEnvironment.RegistryEnabled) missing.Add("OSYNC_TEST_REGISTRY=1");
        if (tags.Contains("exclusive") && !TestEnvironment.ExclusiveServers) missing.Add("OSYNC_TEST_EXCLUSIVE=1");
        if (needsModel && TestModelAsset.Instance == null) missing.Add(TestModelAsset.UnavailableReason ?? "test model");

        if (missing.Count > 0)
            _runtime.TestIgnore("Requires: " + string.Join(", ", missing));
    }

    [AfterScenario]
    public async Task Cleanup()
    {
        foreach (var server in new[] { "local", "remote1", "remote2" })
        {
            var url = TestEnvironment.ServerUrl(server);
            if (!TestEnvironment.IsReachable(url)) continue;
            var api = new OllamaApi(url!);
            try
            {
                foreach (var model in await api.ListAsync())
                {
                    if (model.Name.StartsWith(_state.Prefix, StringComparison.Ordinal))
                    {
                        await api.UnloadAsync(model.Name);
                        await api.DeleteAsync(model.Name);
                    }
                }
                foreach (var (s, model) in _state.ExtraCleanup.Where(e => e.Server == server))
                    await api.DeleteAsync(model);
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Cleanup on {server} failed: {ex.Message}");
            }
        }
    }
}
