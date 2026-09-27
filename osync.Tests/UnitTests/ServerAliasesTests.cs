using FluentAssertions;

namespace osync.Tests.UnitTests;

public class ServerAliasesTests
{
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["gpu"] = "http://192.168.1.10:11434",
        ["xollama"] = "http://localhost:22434/"
    };

    [Theory]
    [InlineData("gpu", "http://192.168.1.10:11434")]
    [InlineData("GPU", "http://192.168.1.10:11434")]
    [InlineData("gpu/", "http://192.168.1.10:11434")]
    [InlineData("gpu/qwen3:8b", "http://192.168.1.10:11434/qwen3:8b")]
    [InlineData("gpu/library/qwen3:8b", "http://192.168.1.10:11434/library/qwen3:8b")]
    [InlineData("xollama/model", "http://localhost:22434/model")]
    public void Alias_ExpandsToItsServer(string input, string expected)
    {
        ServerAliases.TryExpand(input, Aliases, out var expanded).Should().BeTrue();
        expanded.Should().Be(expected);
    }

    [Theory]
    [InlineData("qwen3:8b")]
    [InlineData("gpu2/model")]
    [InlineData("mygpu")]
    [InlineData("hf.co/user/repo")]
    [InlineData("http://gpu:11434/model")]
    [InlineData("")]
    public void OtherNames_AreNotAliases(string input)
    {
        ServerAliases.TryExpand(input, Aliases, out var expanded).Should().BeFalse();
        expanded.Should().Be(input);
    }

    [Theory]
    [InlineData("gpu", true)]
    [InlineData("box-2", true)]
    [InlineData("a_b", true)]
    [InlineData("2box", false)]
    [InlineData("my.server", false)]
    [InlineData("host:11434", false)]
    [InlineData("localhost", false)]
    [InlineData("", false)]
    public void AliasNames_AreValidated(string name, bool valid)
    {
        (ServerAliases.ValidateName(name) == null).Should().Be(valid);
    }
}
