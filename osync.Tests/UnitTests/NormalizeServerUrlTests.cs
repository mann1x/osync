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

    // A copy source is remote when it names a server, with or without http:// (192.168.1.5:22434/model:tag)
    [Theory]
    [InlineData("192.168.178.161:22434/mannix/gemma-4-20B-v9-agentic:mtp-a_ref-Q4_K_M", true)]
    [InlineData("server.invalid:22434/qwen3:4b", true)]
    [InlineData("http://server.invalid/qwen3:4b", true)]
    [InlineData("mannix/gemma-4-20B-v9-agentic:mtp-a_ref-Q4_K_M", false)]
    [InlineData("qwen3:4b", false)]
    [InlineData("hf.co/org/model:Q4_K_M", false)]
    public void LooksLikeRemoteServer_RecognizesServersWithoutScheme(string input, bool expected)
    {
        OsyncProgram.LooksLikeRemoteServer(input).Should().Be(expected);
    }
}
