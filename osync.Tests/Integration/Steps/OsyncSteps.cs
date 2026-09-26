using FluentAssertions;
using osync.Tests.Integration.Infrastructure;
using Reqnroll;

namespace osync.Tests.Integration.Steps;

[Binding]
public sealed class OsyncSteps
{
    private readonly ScenarioState _state;
    private readonly IReqnrollOutputHelper _output;

    public OsyncSteps(ScenarioState state, IReqnrollOutputHelper output)
    {
        _state = state;
        _output = output;
    }

    private OsyncResult Last => _state.LastResult ?? throw new InvalidOperationException("No osync command has been run in this scenario");

    [When("I run osync {string}")]
    public async Task WhenIRunOsync(string arguments)
    {
        var result = await OsyncCli.RunAsync(_state.Resolve(arguments), environment: Env());
        _state.LastResult = result;
        _output.WriteLine(result.ToString());
    }

    [When("I run osync {string} without host settings")]
    public async Task WhenIRunOsyncWithoutHostSettings(string arguments)
    {
        var result = await OsyncCli.RunAsync(_state.Resolve(arguments), withHostSettings: false, environment: Env());
        _state.LastResult = result;
        _output.WriteLine(result.ToString());
    }

    [When("I run osync {string} with {word} set to {string}")]
    public async Task WhenIRunOsyncWithEnvironment(string arguments, string variable, string value)
    {
        var environment = Env();
        environment[variable] = _state.Resolve(value);
        var result = await OsyncCli.RunAsync(_state.Resolve(arguments), environment: environment);
        _state.LastResult = result;
        _output.WriteLine(result.ToString());
    }

    [When("I run osync {string} without access to the local models directory")]
    public async Task WhenIRunOsyncWithoutModelsDirectory(string arguments)
    {
        var environment = Env();
        var missing = Path.Combine(_state.ConfigDir, "no-such-models-dir");
        environment["OLLAMA_MODELS"] = missing;
        environment["XOLLAMA_MODELS"] = missing;
        var result = await OsyncCli.RunAsync(_state.Resolve(arguments), environment: environment);
        _state.LastResult = result;
        _output.WriteLine(result.ToString());
    }

    [When("I open manage in a terminal and press {string}")]
    public Task WhenIOpenManageAndPress(string keys) => OpenManage("manage", keys, withHostSettings: true);

    [When("I open manage without host settings in a terminal and press {string}")]
    public Task WhenIOpenManageWithoutHostSettingsAndPress(string keys) => OpenManage("manage", keys, withHostSettings: false);

    private async Task OpenManage(string arguments, string keys, bool withHostSettings)
    {
        var result = await OsyncCli.RunInTerminalAsync(arguments, TerminalKeys.Parse(_state.Resolve(keys)),
            readyText: "osync manage v", withHostSettings: withHostSettings, environment: Env());
        _state.LastResult = result;
        _output.WriteLine(result.ToString());
    }

    [Then("the settings file has {string} set to {string}")]
    public void ThenTheSettingsFileHas(string path, string expected)
    {
        var file = Path.Combine(_state.ConfigDir, "settings.json");
        File.Exists(file).Should().BeTrue($"{file} should have been written");
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(file));
        var element = doc.RootElement;
        foreach (var name in path.Split('.'))
        {
            element.TryGetProperty(name, out element).Should().BeTrue($"settings.json should contain {path}");
        }
        element.ToString().Should().Be(_state.Resolve(expected));
    }

    [Then("the settings file does not exist")]
    public void ThenTheSettingsFileDoesNotExist() =>
        File.Exists(Path.Combine(_state.ConfigDir, "settings.json")).Should().BeFalse();

    private Dictionary<string, string> Env() => new() { ["OSYNC_CONFIG_DIR"] = _state.ConfigDir };

    /// <summary>Writes a settings.json whose server entry points at one of the test servers.</summary>
    [Given("the settings file configures the {word} server")]
    public void GivenTheSettingsFileConfiguresTheServer(string server)
    {
        var url = new Uri(TestEnvironment.ServerUrl(server)!);
        var flavor = TestEnvironment.IsXOllama(url.ToString().TrimEnd('/')) ? "xollama" : "ollama";
        File.WriteAllText(Path.Combine(_state.ConfigDir, "settings.json"),
            $$"""{ "server": { "flavor": "{{flavor}}", "host": "{{url.Host}}", "port": {{url.Port}} } }""");
    }

    [Then("the output names the server flavor of {word}")]
    public void ThenTheOutputNamesTheServerFlavorOf(string server)
    {
        var url = TestEnvironment.ServerUrl(server)!;
        var flavor = TestEnvironment.IsXOllama(url) ? "xOllama" : "Ollama";
        Last.AllOutput.Should().Contain($"Server: {flavor} at ", "output of:\n{0}", Last);
    }

    [Then("the command succeeds")]
    public void ThenTheCommandSucceeds() =>
        Last.ExitCode.Should().Be(0, "the command should succeed:\n{0}", Last);

    [Then("the command fails")]
    public void ThenTheCommandFails() =>
        Last.ExitCode.Should().NotBe(0, "the command should fail:\n{0}", Last);

    [Then("the output contains {string}")]
    public void ThenTheOutputContains(string text) =>
        Last.AllOutput.Should().Contain(_state.Resolve(text), "output of:\n{0}", Last);

    [Then("the output does not contain {string}")]
    public void ThenTheOutputDoesNotContain(string text) =>
        Last.AllOutput.Should().NotContain(_state.Resolve(text), "output of:\n{0}", Last);
}
