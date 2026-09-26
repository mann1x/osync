using FluentAssertions;

namespace osync.Tests.UnitTests;

public class JudgeArgumentParserTests
{
    // ── Local model parsing ─────────────────────────────────────

    [Fact]
    public void Parse_LocalModel_ReturnsLocalhostUrl()
    {
        var result = JudgeArgumentParser.Parse("llama3");

        result.Success.Should().BeTrue();
        result.IsCloud.Should().BeFalse();
        // The local server as osync resolves it (OLLAMA_HOST, settings file, default ports)
        result.BaseUrl.Should().Be(OllamaServer.LocalUrl);
        result.ModelName.Should().Be("llama3:latest");
    }

    [Fact]
    public void Parse_LocalModelWithTag_PreservesTag()
    {
        var result = JudgeArgumentParser.Parse("llama3:8b");

        result.Success.Should().BeTrue();
        result.ModelName.Should().Be("llama3:8b");
    }

    [Fact]
    public void Parse_LocalModelWithLatest_NoDoubleTag()
    {
        var result = JudgeArgumentParser.Parse("llama3:latest");

        result.Success.Should().BeTrue();
        result.ModelName.Should().Be("llama3:latest");
    }

    // ── Full URL parsing ────────────────────────────────────────

    [Fact]
    public void Parse_FullHttpUrl_ExtractsBaseUrlAndModel()
    {
        var result = JudgeArgumentParser.Parse("http://192.168.1.1:11434/mymodel");

        result.Success.Should().BeTrue();
        result.IsCloud.Should().BeFalse();
        result.BaseUrl.Should().Be("http://192.168.1.1:11434");
        result.ModelName.Should().Be("mymodel:latest");
    }

    [Fact]
    public void Parse_FullHttpsUrl_ExtractsCorrectly()
    {
        var result = JudgeArgumentParser.Parse("https://server.com:443/model:tag");

        result.Success.Should().BeTrue();
        result.BaseUrl.Should().Be("https://server.com:443");
        result.ModelName.Should().Be("model:tag");
    }

    // ── Cloud provider — missing API key ────────────────────────

    [Fact]
    public void Parse_CloudProviderNoKey_ReturnsFailureWithKeyError()
    {
        // Ensure env var is not set
        var original = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", null);
            var result = JudgeArgumentParser.Parse("@claude/claude-3-opus");

            result.Success.Should().BeFalse();
            result.Error.Should().Contain("API key");
        }
        finally
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", original);
        }
    }

    [Fact]
    public void Parse_CloudProviderNoKey_OpenAI()
    {
        var original = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", null);
            var result = JudgeArgumentParser.Parse("@openai/gpt-4");

            result.Success.Should().BeFalse();
            result.Error.Should().Contain("API key");
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", original);
        }
    }

    // ── Cloud provider — unknown ────────────────────────────────

    [Fact]
    public void Parse_UnknownCloudProvider_ReturnsFailure()
    {
        var result = JudgeArgumentParser.Parse("@unknownprovider/model");

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Invalid cloud judge format");
    }

    // ── Cloud provider — no model ───────────────────────────────

    [Fact]
    public void Parse_CloudProviderNoModel_ReturnsFailure()
    {
        var result = JudgeArgumentParser.Parse("@claude");

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Invalid cloud judge format");
    }

    // ── Cloud provider with explicit key ────────────────────────

    [Fact]
    public void Parse_CloudProviderWithKey_Succeeds()
    {
        var result = JudgeArgumentParser.Parse("@claude:sk-test123/claude-3-opus", timeout: 30);

        result.Success.Should().BeTrue();
        result.IsCloud.Should().BeTrue();
        result.ModelName.Should().Be("claude-3-opus");
        result.KeySource.Should().Be("cmd");
    }

    // ── Cloud provider with env key ─────────────────────────────

    [Fact]
    public void Parse_CloudProviderWithEnvKey_Succeeds()
    {
        var original = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", "test-key-123");
            var result = JudgeArgumentParser.Parse("@claude/claude-3-opus", timeout: 30);

            result.Success.Should().BeTrue();
            result.IsCloud.Should().BeTrue();
            result.KeySource.Should().Be("env");
        }
        finally
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", original);
        }
    }
}
