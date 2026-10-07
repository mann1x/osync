using System.Text.Json;
using FluentAssertions;
using osync.Tests.Integration.Infrastructure;
using Reqnroll;
using Reqnroll.UnitTestProvider;

namespace osync.Tests.Integration.Steps;

/// <summary>Arrange and verify model state through the Ollama API. Servers: local, remote1, remote2, peer.</summary>
[Binding]
public sealed class ModelSteps
{
    private readonly ScenarioState _state;
    private readonly IUnitTestRuntimeProvider _runtime;

    public ModelSteps(ScenarioState state, IUnitTestRuntimeProvider runtime)
    {
        _state = state;
        _runtime = runtime;
    }

    [Given("a test model {string} on {word}")]
    public async Task GivenATestModelOn(string model, string server)
    {
        var asset = TestModelAsset.Instance ?? throw new InvalidOperationException(TestModelAsset.UnavailableReason);
        var name = _state.Resolve(AsPlaceholder(model));
        await ScenarioState.Api(server).CreateFromTestModelAsync(name, asset);
    }

    [Given("a test model {string} with renderer {string} on {word}")]
    public async Task GivenATestModelWithRendererOn(string model, string renderer, string server)
    {
        var asset = TestModelAsset.Instance ?? throw new InvalidOperationException(TestModelAsset.UnavailableReason);
        var name = _state.Resolve(AsPlaceholder(model));
        await ScenarioState.Api(server).CreateFromTestModelAsync(name, asset, renderer);
    }

    /// <summary>A manifest list (one build per runner) of models already on the server; skipped before 0.40.</summary>
    [Given("a model {string} with one build per runner of {string} on {word}")]
    public async Task GivenAManifestListOn(string model, string child, string server)
    {
        var api = ScenarioState.Api(server);
        var version = await api.VersionAsync();
        if (!RelayCopy.SupportsManifestLists(version))
            _runtime.TestIgnore($"{server} runs {version}: one build per runner needs 0.40 or later");
        await api.CreateManifestListAsync(_state.Resolve(AsPlaceholder(model)), _state.Resolve(AsPlaceholder(child)));
    }

    /// <summary>"alpha" -> "{alpha}", "alpha:v1" -> "{alpha}:v1"; text that already has placeholders is kept.</summary>
    private static string AsPlaceholder(string model)
    {
        if (model.Contains('{')) return model;
        var colon = model.IndexOf(':');
        return colon < 0 ? $"{{{model}}}" : $"{{{model[..colon]}}}{model[colon..]}";
    }

    [Given("the registry model {string} on {word}")]
    public async Task GivenTheRegistryModelOn(string model, string server)
    {
        var api = ScenarioState.Api(server);
        if (!await api.ExistsAsync(model))
        {
            // Only remove it again if this scenario pulled it
            _state.ExtraCleanup.Add((server, model));
            await api.PullAsync(model);
        }
    }

    /// <summary>
    /// For tests that pull a real registry model: the model must not be present yet (a developer's own copy
    /// is never deleted; the scenario is skipped instead) and is removed again after the scenario.
    /// </summary>
    [Given("the registry model {string} is not yet on {word}")]
    public async Task GivenTheRegistryModelIsNotYetOn(string model, string server)
    {
        if (await ScenarioState.Api(server).ExistsAsync(model))
            _runtime.TestIgnore($"{model} already exists on {server}; not deleting a model the tests did not create");
        _state.ExtraCleanup.Add((server, model));
    }

    [Given("the model {string} is not on {word}")]
    public async Task GivenTheModelIsNotOn(string model, string server)
    {
        var api = ScenarioState.Api(server);
        var name = _state.Resolve(model);
        if (await api.ExistsAsync(name))
        {
            if (!name.StartsWith(_state.Prefix, StringComparison.Ordinal))
                throw new InvalidOperationException($"'{name}' exists on {server}; refusing to delete a model this scenario did not create");
            await api.DeleteAsync(name);
        }
    }

    [Given("the model {string} is loaded on {word}")]
    public async Task GivenTheModelIsLoadedOn(string model, string server) =>
        await ScenarioState.Api(server).LoadAsync(_state.Resolve(model));

    [Then("the model {string} exists on {word}")]
    public async Task ThenTheModelExistsOn(string model, string server)
    {
        var name = _state.Resolve(model);
        var models = await ScenarioState.Api(server).ListAsync();
        models.Select(m => m.Name).Should().Contain(OllamaApi.WithTag(name),
            "{0} should exist on {1} after:\n{2}", name, server, _state.LastResult);
    }

    /// <summary>A setting of the model's xOllama config ("xollama" in /api/show), by JSON path ("kv.v").</summary>
    [Then("the model {string} on {word} has the xOllama setting {string} set to {string}")]
    public async Task ThenTheModelHasTheXOllamaSetting(string model, string server, string path, string value)
    {
        var show = await ScenarioState.Api(server).ShowAsync(_state.Resolve(model));
        System.Text.Json.Nodes.JsonNode? node = show["xollama"];
        foreach (var part in path.Split('.'))
            node = node?[part];
        node.Should().NotBeNull("{0} should state {1}; its xOllama config is {2} after:\n{3}",
            model, path, show["xollama"]?.ToJsonString() ?? "null", _state.LastResult);
        node!.ToString().Should().Be(value, "the xOllama config after:\n{0}", _state.LastResult);
    }

    /// <summary>Gives the model an xOllama config (JSON), the way `xollama tweak` writes one.</summary>
    [Given("the model {string} on {word} has the xOllama settings {string}")]
    public async Task GivenTheModelHasTheXOllamaSettings(string model, string server, string json) =>
        await ScenarioState.Api(server).SetXOllamaConfigAsync(_state.Resolve(AsPlaceholder(model)), json);

    [Then("the model {string} on {word} has no xOllama settings")]
    public async Task ThenTheModelHasNoXOllamaSettings(string model, string server)
    {
        var show = await ScenarioState.Api(server).ShowAsync(_state.Resolve(model));
        show["xollama"].Should().BeNull("the xOllama config after:\n{0}", _state.LastResult);
    }

    [Then("the model {string} does not exist on {word}")]
    public async Task ThenTheModelDoesNotExistOn(string model, string server)
    {
        var name = _state.Resolve(model);
        var models = await ScenarioState.Api(server).ListAsync();
        models.Select(m => m.Name).Should().NotContain(OllamaApi.WithTag(name),
            "{0} should not exist on {1} after:\n{2}", name, server, _state.LastResult);
    }

    /// <summary>The push relay uses temporary names like 10.0.0.5:45123/osync/relay-1a2b3c4d:latest.</summary>
    [Then("no relay model exists on {word}")]
    public async Task ThenNoRelayModelExistsOn(string server)
    {
        var models = await ScenarioState.Api(server).ListAsync();
        models.Select(m => m.Name).Where(n => n.Contains("/osync/relay-", StringComparison.Ordinal))
            .Should().BeEmpty("temporary relay models must be deleted after:\n{0}", _state.LastResult);
    }

    [Then("no model starting with {string} exists on {word}")]
    public async Task ThenNoModelStartingWithExistsOn(string prefix, string server)
    {
        var resolved = _state.Resolve(prefix);
        var models = await ScenarioState.Api(server).ListAsync();
        models.Select(m => m.Name).Where(n => n.StartsWith(resolved, StringComparison.Ordinal))
            .Should().BeEmpty("after:\n{0}", _state.LastResult);
    }

    /// <summary>
    /// A copy must be a complete, usable model: same size, quantization and family, and the same
    /// template and parameters as the original (manifest digests legitimately differ between copy paths).
    /// </summary>
    [Then("the model {string} on {word} is identical to {string} on {word}")]
    public async Task ThenTheModelIsIdenticalTo(string model, string server, string original, string originalServer)
    {
        var copyApi = ScenarioState.Api(server);
        var origApi = ScenarioState.Api(originalServer);
        var copyName = _state.Resolve(model);
        var origName = _state.Resolve(original);

        var copy = await copyApi.FindAsync(copyName);
        var orig = await origApi.FindAsync(origName);
        copy.Should().NotBeNull("{0} should exist on {1}", copyName, server);
        orig.Should().NotBeNull("{0} should exist on {1}", origName, originalServer);

        copy!.Size.Should().Be(orig!.Size, "the copy should contain the same layers");
        copy.QuantizationLevel.Should().Be(orig.QuantizationLevel);
        copy.Family.Should().Be(orig.Family);

        var copyShow = await copyApi.ShowAsync(copyName);
        var origShow = await origApi.ShowAsync(origName);
        foreach (var field in new[] { "template", "system", "license", "renderer", "parser", "requires" })
            copyShow[field]?.ToString().Should().Be(origShow[field]?.ToString(), "the {0} should be preserved", field);
        NormalizeParameters(copyShow["parameters"]?.ToString())
            .Should().Be(NormalizeParameters(origShow["parameters"]?.ToString()), "the parameters should be preserved");
    }

    /// <summary>
    /// Byte-for-byte: the manifests (read from the servers' models directories, see @stores) list the same config
    /// and layer digests. /api/show cannot tell, and the manifest digest itself may differ (it is re-serialized).
    /// </summary>
    [Then("the model {string} on {word} has the same layers as {string} on {word}")]
    public void ThenTheModelHasTheSameLayersAs(string model, string server, string original, string originalServer)
    {
        var copy = ManifestDigests(server, _state.Resolve(model));
        var orig = ManifestDigests(originalServer, _state.Resolve(original));
        copy.Should().Equal(orig, "the copy should have the source's config and layers, byte for byte");
    }

    /// <summary>The named manifests (read from the servers' models directories, see @stores) are the same bytes.</summary>
    [Then("the model {string} on {word} has the same manifest as {string} on {word}")]
    public void ThenTheModelHasTheSameManifestAs(string model, string server, string original, string originalServer)
    {
        ManifestBytes(server, _state.Resolve(model)).Should().Equal(ManifestBytes(originalServer, _state.Resolve(original)),
            "the copy should have the source's manifest, byte for byte, after:\n{0}", _state.LastResult);
    }

    private static byte[] ManifestBytes(string server, string model)
    {
        var store = TestEnvironment.StoreDir(server) ?? throw new InvalidOperationException($"No models directory for {server}");
        var path = ModelStore.FindManifest(store, model) ?? throw new FileNotFoundException($"No manifest for '{model}' in {store}");
        return File.ReadAllBytes(path);
    }

    private static List<string> ManifestDigests(string server, string model)
    {
        var store = TestEnvironment.StoreDir(server) ?? throw new InvalidOperationException($"No models directory for {server}");
        // manifests-v2/ on 0.40+, manifests/ on older servers; a manifest list is compared by the build a copy takes
        var path = ModelStore.FindManifest(store, model) ?? throw new FileNotFoundException($"No manifest for '{model}' in {store}");
        using var doc = JsonDocument.Parse(ModelStore.ReadModelManifest(store, path, out _));
        var root = doc.RootElement;
        return root.GetProperty("layers").EnumerateArray()
            .Select(l => $"{l.GetProperty("mediaType").GetString()} {l.GetProperty("digest").GetString()}")
            .Prepend("config " + root.GetProperty("config").GetProperty("digest").GetString())
            .ToList();
    }

    [Then("the model {string} is loaded on {word}")]
    public async Task ThenTheModelIsLoadedOn(string model, string server)
    {
        var name = OllamaApi.WithTag(_state.Resolve(model));
        var loaded = await WaitForAsync(server, l => l.Contains(name));
        loaded.Should().Contain(name, "after:\n{0}", _state.LastResult);
    }

    [Then("the model {string} is not loaded on {word}")]
    public async Task ThenTheModelIsNotLoadedOn(string model, string server)
    {
        var name = OllamaApi.WithTag(_state.Resolve(model));
        var loaded = await WaitForAsync(server, l => !l.Contains(name));
        loaded.Should().NotContain(name, "after:\n{0}", _state.LastResult);
    }

    [Then("no model is loaded on {word}")]
    public async Task ThenNoModelIsLoadedOn(string server)
    {
        var loaded = await WaitForAsync(server, l => l.Count == 0);
        loaded.Should().BeEmpty("after:\n{0}", _state.LastResult);
    }

    /// <summary>Unloading is asynchronous in Ollama; poll /api/ps for a short while.</summary>
    private static async Task<List<string>> WaitForAsync(string server, Func<List<string>, bool> condition)
    {
        var api = ScenarioState.Api(server);
        var deadline = DateTime.UtcNow.AddSeconds(15);
        List<string> loaded;
        while (!condition(loaded = await api.LoadedAsync()) && DateTime.UtcNow < deadline)
            await Task.Delay(250);
        return loaded;
    }

    private static string NormalizeParameters(string? parameters) =>
        string.Join("\n", (parameters ?? "")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => string.Join(' ', l.Split(' ', StringSplitOptions.RemoveEmptyEntries)))
            .Order(StringComparer.Ordinal));
}
