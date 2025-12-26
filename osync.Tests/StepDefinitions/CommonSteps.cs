using TechTalk.SpecFlow;
using FluentAssertions;
using osync.Tests.Infrastructure;
using osync.Tests.Support;

namespace osync.Tests.StepDefinitions;

[Binding]
public class CommonSteps
{
    private readonly TestContext _context;
    private readonly OsyncRunner _runner;
    private readonly TestConfiguration _config;

    public CommonSteps(
        TestContext context,
        OsyncRunner runner,
        TestConfiguration config)
    {
        _context = context;
        _runner = runner;
        _config = config;
    }

    [When(@"I run osync with arguments ""(.*)""")]
    public async Task WhenIRunOsyncWithArguments(string args)
    {
        var resolvedArgs = _context.ResolveVariables(args);
        Console.WriteLine($"Executing: osync {resolvedArgs}");
        _context.LastResult = await _runner.RunAsync(resolvedArgs);
    }

    [Then(@"the command should succeed")]
    public void ThenTheCommandShouldSucceed()
    {
        _context.LastResult.Should().NotBeNull("result should not be null");

        if (!_context.LastResult!.IsSuccess)
        {
            Console.WriteLine($"Command failed with exit code {_context.LastResult.ExitCode}");
            Console.WriteLine($"Output: {_context.LastResult.Output}");
            Console.WriteLine($"Error: {_context.LastResult.Error}");
        }

        _context.LastResult.ExitCode.Should().Be(0,
            $"command should succeed but got exit code {_context.LastResult.ExitCode}");
    }

    [Then(@"the command should fail")]
    public void ThenTheCommandShouldFail()
    {
        _context.LastResult.Should().NotBeNull("result should not be null");
        _context.LastResult!.ExitCode.Should().NotBe(0,
            "command should fail but succeeded");
    }

    [Then(@"the output should contain ""(.*)""")]
    public void ThenTheOutputShouldContain(string expectedText)
    {
        var resolved = _context.ResolveVariables(expectedText);
        _context.LastResult.Should().NotBeNull("result should not be null");

        var combinedOutput = _context.LastResult!.Output + _context.LastResult.Error;
        combinedOutput.Should().Contain(resolved,
            $"output should contain '{resolved}'");
    }

    [Then(@"the output should not contain ""(.*)""")]
    public void ThenTheOutputShouldNotContain(string text)
    {
        var resolved = _context.ResolveVariables(text);
        _context.LastResult.Should().NotBeNull("result should not be null");

        var combinedOutput = _context.LastResult!.Output + _context.LastResult.Error;
        combinedOutput.Should().NotContain(resolved,
            $"output should not contain '{resolved}'");
    }

    [Then(@"the execution time should be less than (.*) seconds")]
    public void ThenTheExecutionTimeShouldBeLessThan(int seconds)
    {
        _context.LastResult.Should().NotBeNull("result should not be null");
        _context.LastResult!.Duration.TotalSeconds.Should().BeLessThan(seconds,
            $"execution should complete within {seconds} seconds");
    }
}
