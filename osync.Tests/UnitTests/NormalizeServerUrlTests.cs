using FluentAssertions;

namespace osync.Tests.UnitTests;

public class NormalizeServerUrlTests
{
    // Hosts under .invalid never resolve (RFC 2606), so the default-port probe fails fast and keeps 11434
    [Theory]
    [InlineData("http://eleven2go.invalid/qwen3:4b", "http://eleven2go.invalid:11434/qwen3:4b")]
    [InlineData("eleven2go.invalid/qwen3:4b", "http://eleven2go.invalid:11434/qwen3:4b")]
    [InlineData("http://server.invalid/hf.co/org/model:Q4_K_M", "http://server.invalid:11434/hf.co/org/model:Q4_K_M")]
    [InlineData("server.invalid", "http://server.invalid:11434")]
    [InlineData("server.invalid/", "http://server.invalid:11434")]
    [InlineData("server.invalid:22434/qwen3:4b", "http://server.invalid:22434/qwen3:4b")]
    [InlineData("http://server.invalid:8080", "http://server.invalid:8080")]
    [InlineData("https://server.invalid/model:tag", "https://server.invalid:11434/model:tag")]
    public void NormalizeServerUrl_OnlyTreatsAuthorityPortsAsPorts(string input, string expected)
    {
        OsyncProgram.NormalizeServerUrl(input).Should().Be(expected);
    }
}
