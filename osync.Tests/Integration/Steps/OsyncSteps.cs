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
        var result = await OsyncCli.RunAsync(_state.Resolve(arguments));
        _state.LastResult = result;
        _output.WriteLine(result.ToString());
    }

    [When("I run osync {string} without host settings")]
    public async Task WhenIRunOsyncWithoutHostSettings(string arguments)
    {
        var result = await OsyncCli.RunAsync(_state.Resolve(arguments), withHostSettings: false);
        _state.LastResult = result;
        _output.WriteLine(result.ToString());
    }

    [When("I run osync {string} with {word} set to {string}")]
    public async Task WhenIRunOsyncWithEnvironment(string arguments, string variable, string value)
    {
        var result = await OsyncCli.RunAsync(_state.Resolve(arguments),
            environment: new Dictionary<string, string> { [variable] = _state.Resolve(value) });
        _state.LastResult = result;
        _output.WriteLine(result.ToString());
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
