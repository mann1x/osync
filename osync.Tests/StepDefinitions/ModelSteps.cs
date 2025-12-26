using TechTalk.SpecFlow;
using FluentAssertions;
using osync.Tests.Infrastructure;
using osync.Tests.Support;

namespace osync.Tests.StepDefinitions;

[Binding]
public class ModelSteps
{
    private readonly TestContext _context;
    private readonly OsyncRunner _runner;
    private readonly TestConfiguration _config;

    public ModelSteps(
        TestContext context,
        OsyncRunner runner,
        TestConfiguration config)
    {
        _context = context;
        _runner = runner;
        _config = config;
    }

    [Given(@"the model ""(.*)"" exists locally")]
    public async Task GivenTheModelExistsLocally(string modelName)
    {
        var resolved = _context.ResolveVariables(modelName);

        // First check if model exists
        var checkResult = await _runner.RunAsync($"ls");

        if (!checkResult.Output.Contains(resolved.Split(':')[0]))
        {
            // Model doesn't exist, pull it
            Console.WriteLine($"Model {resolved} not found locally, pulling...");
            var pullResult = await _runner.RunAsync($"pull {resolved}");
            pullResult.IsSuccess.Should().BeTrue($"failed to pull model {resolved}");

            // Add to created models for cleanup
            _context.AddCreatedModel(resolved);
        }

        // Verify model now exists
        var verifyResult = await _runner.RunAsync($"ls");
        verifyResult.IsSuccess.Should().BeTrue();
        verifyResult.Output.Should().Contain(resolved.Split(':')[0],
            $"model {resolved} should exist locally after pull");
    }

    [Given(@"the model ""(.*)"" exists on ""(.*)""")]
    public async Task GivenTheModelExistsOnRemote(string modelName, string remoteUrl)
    {
        var resolvedModel = _context.ResolveVariables(modelName);
        var resolvedRemote = _context.ResolveVariables(remoteUrl);

        // Check if model exists on remote
        var checkResult = await _runner.RunAsync($"ls -d {resolvedRemote}");

        if (!checkResult.Output.Contains(resolvedModel.Split(':')[0]))
        {
            // Model doesn't exist on remote, upload it
            Console.WriteLine($"Model {resolvedModel} not found on {resolvedRemote}, uploading...");

            // First ensure it exists locally
            await GivenTheModelExistsLocally(_config.RegistryModel);

            // Then upload to remote
            var uploadResult = await _runner.RunAsync($"copy {_config.RegistryModel} {resolvedRemote}/{resolvedModel}");
            uploadResult.IsSuccess.Should().BeTrue($"failed to upload model {resolvedModel} to {resolvedRemote}");

            // Add to created models for cleanup
            _context.AddCreatedModel($"{resolvedRemote}/{resolvedModel}");
        }

        // Verify model exists on remote
        var verifyResult = await _runner.RunAsync($"ls -d {resolvedRemote}");
        verifyResult.IsSuccess.Should().BeTrue();
        verifyResult.Output.Should().Contain(resolvedModel.Split(':')[0],
            $"model {resolvedModel} should exist on {resolvedRemote}");
    }

    [Then(@"the model ""(.*)"" should exist locally")]
    public async Task ThenTheModelShouldExistLocally(string modelName)
    {
        var resolved = _context.ResolveVariables(modelName);
        var result = await _runner.RunAsync($"ls");

        result.IsSuccess.Should().BeTrue();
        result.Output.Should().Contain(resolved.Split(':')[0],
            $"model {resolved} should exist locally");
    }

    [Then(@"the model ""(.*)"" should not exist locally")]
    public async Task ThenTheModelShouldNotExistLocally(string modelName)
    {
        var resolved = _context.ResolveVariables(modelName);
        var result = await _runner.RunAsync($"ls");

        result.IsSuccess.Should().BeTrue();
        result.Output.Should().NotContain(resolved.Split(':')[0],
            $"model {resolved} should not exist locally");
    }

    [Then(@"the model ""(.*)"" should exist on ""(.*)""")]
    public async Task ThenTheModelShouldExistOnRemote(string modelName, string remoteUrl)
    {
        var resolvedModel = _context.ResolveVariables(modelName);
        var resolvedRemote = _context.ResolveVariables(remoteUrl);

        var result = await _runner.RunAsync($"ls -d {resolvedRemote}");

        result.IsSuccess.Should().BeTrue($"failed to list models on {resolvedRemote}");
        result.Output.Should().Contain(resolvedModel.Split(':')[0],
            $"model {resolvedModel} should exist on {resolvedRemote}");
    }

    [Then(@"the model ""(.*)"" should not exist on ""(.*)""")]
    public async Task ThenTheModelShouldNotExistOnRemote(string modelName, string remoteUrl)
    {
        var resolvedModel = _context.ResolveVariables(modelName);
        var resolvedRemote = _context.ResolveVariables(remoteUrl);

        var result = await _runner.RunAsync($"ls -d {resolvedRemote}");

        result.IsSuccess.Should().BeTrue();
        result.Output.Should().NotContain(resolvedModel.Split(':')[0],
            $"model {resolvedModel} should not exist on {resolvedRemote}");
    }

    [Then(@"the output should be properly formatted")]
    public void ThenTheOutputShouldBeProperlyFormatted()
    {
        _context.LastResult.Should().NotBeNull();
        _context.LastResult!.Output.Should().NotBeNullOrEmpty();

        // Check that output doesn't have obvious formatting issues
        _context.LastResult.Output.Should().NotContain("null",
            "output should not contain null values");
        _context.LastResult.Output.Should().NotContain("undefined",
            "output should not contain undefined values");
    }

    [Then(@"the output should contain valid JSON or structured text")]
    public void ThenTheOutputShouldContainValidJsonOrStructuredText()
    {
        _context.LastResult.Should().NotBeNull();
        _context.LastResult!.Output.Should().NotBeNullOrEmpty();

        // Check for structured output indicators
        var hasStructure = _context.LastResult.Output.Contains("{") ||
                          _context.LastResult.Output.Contains("architecture") ||
                          _context.LastResult.Output.Contains("parameters") ||
                          _context.LastResult.Output.Contains(":");

        hasStructure.Should().BeTrue("output should contain structured information");
    }
}
