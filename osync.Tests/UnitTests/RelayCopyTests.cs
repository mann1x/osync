using FluentAssertions;

namespace osync.Tests.UnitTests;

public class RelayCopyTests
{
    [Fact]
    public void ParseParameters_TypesValuesAndCollectsStops()
    {
        var text = "num_ctx                        1024\n" +
                   "temperature                    0.7\n" +
                   "stop                           \"<|im_start|>\"\n" +
                   "stop                           \"<|im_end|>\"\n" +
                   "penalize_newline               true\n";

        var result = RelayCopy.ParseParameters(text);

        result["num_ctx"].Should().Be(1024L);
        result["temperature"].Should().Be(0.7);
        result["penalize_newline"].Should().Be(true);
        result["stop"].Should().BeEquivalentTo(new List<object> { "<|im_start|>", "<|im_end|>" });
    }

    [Fact]
    public void ParseParameters_UnescapesQuotedStrings()
    {
        var result = RelayCopy.ParseParameters("stop \"\\nUser:\"");

        result["stop"].Should().BeEquivalentTo(new List<object> { "\nUser:" });
    }

    [Fact]
    public void ParseParameters_IgnoresEmptyAndMalformedLines()
    {
        RelayCopy.ParseParameters("\n\n  \nlonely\n").Should().BeEmpty();
    }
}

public class OllamaServerTests
{
    [Theory]
    [InlineData("localhost", 11434, "http://localhost:11434")]
    [InlineData("0.0.0.0", 11434, "http://localhost:11434")]
    [InlineData("0.0.0.0:22434", 11434, "http://localhost:22434")]
    [InlineData("127.0.0.1:22434", 11434, "http://127.0.0.1:22434")]
    [InlineData("myserver", 22434, "http://myserver:22434")]
    [InlineData("http://myserver/", 11434, "http://myserver:11434")]
    [InlineData("https://myserver", 11434, "https://myserver:11434")]
    [InlineData("http://myserver:80", 11434, "http://myserver")]
    [InlineData("[::]:11434", 11434, "http://localhost:11434")]
    public void ToClientUrl_NormalizesHostSettings(string host, int defaultPort, string expected)
    {
        OllamaServer.ToClientUrl(host, defaultPort).Should().Be(expected);
    }

    [Fact]
    public void ResolveHost_UsesDestinationWhenGiven()
    {
        OllamaServer.ResolveHost("192.168.1.10").Should().Be("http://192.168.1.10:11434");
    }
}
