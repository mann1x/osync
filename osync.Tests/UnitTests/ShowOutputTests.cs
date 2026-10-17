using FluentAssertions;
using Xunit;

namespace osync.Tests.UnitTests;

public class ShowOutputTests
{
    [Theory]
    [InlineData("show", "m", "--modelfile")]
    [InlineData("show", "m", "--modelfile", "-d", "http://gpu:22434")]
    [InlineData("show", "m", "--template")]
    [InlineData("show", "m", "--license")]
    [InlineData("show", "m", "--parameters")]
    [InlineData("show", "m", "--system")]
    [InlineData("SHOW", "m", "--Modelfile")]
    [InlineData("showversion")]
    [InlineData("-v")]
    [InlineData("version")]
    public void Raw_output_has_no_banner(params string[] args) =>
        OsyncProgram.ShowsStartupBanner(args).Should().BeFalse();

    [Theory]
    [InlineData]
    [InlineData("show", "m")]
    [InlineData("show", "m", "-v")]
    [InlineData("ls")]
    [InlineData("cp", "a", "b")]
    [InlineData("ls", "--modelfile")]
    public void Other_commands_keep_the_banner(params string[] args) =>
        OsyncProgram.ShowsStartupBanner(args).Should().BeTrue();

    [Theory]
    [InlineData(0x0007u, 0x0007u)]  // processed output, wrap, VT: unchanged
    [InlineData(0x000Fu, 0x0007u)]  // Spectre.Console's mode: LF returns the carriage again
    [InlineData(0x0008u, 0x0000u)]
    [InlineData(0x001Fu, 0x0017u)]  // other flags kept
    public void Newline_auto_return_is_restored(uint mode, uint expected) =>
        WindowsConsoleMode.WithNewlineAutoReturn(mode).Should().Be(expected);
}
