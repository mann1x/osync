using FluentAssertions;

namespace osync.Tests.UnitTests;

public class HuggingFaceAuthTests
{
    [Theory]
    [InlineData("https://huggingface.co/api/models/bartowski/x", true)]
    [InlineData("https://hf.co/v2/bartowski/x/manifests/Q4_K_M", true)]
    [InlineData("https://cdn-lfs.huggingface.co/repos/x", true)]
    [InlineData("http://huggingface.co/api/models/x", false)]  // never over plain HTTP
    [InlineData("https://huggingface.co.evil.example/api", false)]
    [InlineData("http://localhost:11434/api/tags", false)]
    [InlineData("https://registry.ollama.ai/v2/library/llama3/manifests/latest", false)]
    public void Get_SendsTheTokenOnlyToHuggingFace(string url, bool authorized)
    {
        using var request = HuggingFaceAuth.Get(url, token: "hf_test");

        if (authorized)
            request.Headers.Authorization!.ToString().Should().Be("Bearer hf_test");
        else
            request.Headers.Authorization.Should().BeNull();
    }
}
