using FluentAssertions;

namespace osync.Tests.UnitTests;

public class CloudJudgeProviderFactoryTests
{
    // ── IsCloudProvider ─────────────────────────────────────────

    [Theory]
    [InlineData("@claude/model", true)]
    [InlineData("@openai/gpt-4", true)]
    [InlineData("@hf/model", true)]
    [InlineData("localmodel", false)]
    [InlineData("http://server/model", false)]
    [InlineData("", false)]
    public void IsCloudProvider_ReturnsCorrectResult(string argument, bool expected)
    {
        CloudJudgeProviderFactory.IsCloudProvider(argument).Should().Be(expected);
    }

    // ── ParseArgument ───────────────────────────────────────────

    [Fact]
    public void ParseArgument_ClaudeAlias_ReturnsAnthropic()
    {
        var result = CloudJudgeProviderFactory.ParseArgument("@claude/claude-3-opus");

        result.Should().NotBeNull();
        result!.ProviderName.Should().Be("anthropic");
        result.ModelName.Should().Be("claude-3-opus");
    }

    [Fact]
    public void ParseArgument_GptAlias_ReturnsOpenai()
    {
        var result = CloudJudgeProviderFactory.ParseArgument("@gpt/gpt-4");

        result.Should().NotBeNull();
        result!.ProviderName.Should().Be("openai");
    }

    [Fact]
    public void ParseArgument_HfAlias_ReturnsHuggingface()
    {
        var result = CloudJudgeProviderFactory.ParseArgument("@hf/my-model");

        result.Should().NotBeNull();
        result!.ProviderName.Should().Be("huggingface");
        result.ModelName.Should().Be("my-model");
    }

    [Fact]
    public void ParseArgument_WithExplicitToken_ExtractsToken()
    {
        var result = CloudJudgeProviderFactory.ParseArgument("@claude:sk-abc123/model");

        result.Should().NotBeNull();
        result!.ApiKey.Should().Be("sk-abc123");
        result.ApiKeyFromEnv.Should().BeFalse();
    }

    [Fact]
    public void ParseArgument_WithoutToken_ApiKeyFromEnv()
    {
        var original = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", "env-key");
            var result = CloudJudgeProviderFactory.ParseArgument("@claude/model");

            result.Should().NotBeNull();
            result!.ApiKey.Should().Be("env-key");
            result.ApiKeyFromEnv.Should().BeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", original);
        }
    }

    [Fact]
    public void ParseArgument_AzureWithEndpoint_ParsesKeyAndEndpoint()
    {
        var result = CloudJudgeProviderFactory.ParseArgument("@azure:mykey@myendpoint.openai.azure.com/my-deployment");

        result.Should().NotBeNull();
        result!.ProviderName.Should().Be("azure");
        result.ApiKey.Should().Be("mykey");
        result.Endpoint.Should().Be("https://myendpoint.openai.azure.com");
        result.ModelName.Should().Be("my-deployment");
    }

    [Fact]
    public void ParseArgument_UnknownProvider_ReturnsNull()
    {
        var result = CloudJudgeProviderFactory.ParseArgument("@unknownprovider/model");
        result.Should().BeNull();
    }

    [Fact]
    public void ParseArgument_NoSlash_ReturnsNull()
    {
        var result = CloudJudgeProviderFactory.ParseArgument("@claude");
        result.Should().BeNull();
    }

    [Fact]
    public void ParseArgument_NonCloudString_ReturnsNull()
    {
        var result = CloudJudgeProviderFactory.ParseArgument("localmodel:latest");
        result.Should().BeNull();
    }

    [Fact]
    public void ParseArgument_AllProviders_Recognized()
    {
        var providers = new[]
        {
            ("@claude/m", "anthropic"),
            ("@anthropic/m", "anthropic"),
            ("@openai/m", "openai"),
            ("@gpt/m", "openai"),
            ("@gemini/m", "gemini"),
            ("@google/m", "gemini"),
            ("@huggingface/m", "huggingface"),
            ("@hf/m", "huggingface"),
            ("@azure/m", "azure"),
            ("@azureopenai/m", "azure"),
            ("@cohere/m", "cohere"),
            ("@mistral/m", "mistral"),
            ("@together/m", "together"),
            ("@togetherai/m", "together"),
            ("@replicate/m", "replicate")
        };

        foreach (var (input, expectedProvider) in providers)
        {
            var result = CloudJudgeProviderFactory.ParseArgument(input);
            result.Should().NotBeNull($"provider '{input}' should be recognized");
            result!.ProviderName.Should().Be(expectedProvider, $"'{input}' should map to '{expectedProvider}'");
        }
    }

    // ── GetEnvVarsForProvider ────────────────────────────────────

    [Fact]
    public void GetEnvVarsForProvider_Anthropic_ReturnsCorrectVars()
    {
        var vars = CloudJudgeProviderFactory.GetEnvVarsForProvider("anthropic");
        vars.Should().Contain("ANTHROPIC_API_KEY");
    }

    [Fact]
    public void GetEnvVarsForProvider_Gemini_ReturnsMultipleVars()
    {
        var vars = CloudJudgeProviderFactory.GetEnvVarsForProvider("gemini");
        vars.Should().Contain("GEMINI_API_KEY");
        vars.Should().Contain("GOOGLE_API_KEY");
    }

    [Fact]
    public void GetEnvVarsForProvider_Unknown_ReturnsEmpty()
    {
        var vars = CloudJudgeProviderFactory.GetEnvVarsForProvider("unknown");
        vars.Should().BeEmpty();
    }

    // ── GetSupportedProviders ───────────────────────────────────

    [Fact]
    public void GetSupportedProviders_ReturnsAll9Providers()
    {
        var providers = CloudJudgeProviderFactory.GetSupportedProviders().ToList();

        providers.Should().HaveCount(9);
        providers.Should().Contain(new[] { "anthropic", "openai", "gemini", "huggingface", "azure", "cohere", "mistral", "together", "replicate" });
    }
}
