using FluentAssertions;

namespace osync.Tests.UnitTests;

public class ColorSupportTests
{
    private static ColorDepth Detect(string? settingsMode = "auto", string? term = "xterm", bool windows = false, params (string Name, string Value)[] env)
    {
        var vars = env.ToDictionary(e => e.Name, e => e.Value);
        return ColorSupport.Detect(n => vars.TryGetValue(n, out var v) ? v : null, settingsMode, term, windows).Depth;
    }

    [Theory]
    [InlineData("truecolor", "TrueColor")]
    [InlineData("24bit", "TrueColor")]
    [InlineData("256", "Colors256")]
    [InlineData("16", "Standard16")]
    [InlineData("none", "None")]
    public void EnvironmentOverride_Wins(string mode, string expected)
    {
        Detect("16", "xterm-256color", false, ("OSYNC_COLOR_MODE", mode), ("COLORTERM", "truecolor"))
            .ToString().Should().Be(expected);
    }

    [Fact]
    public void NoColor_DisablesColors()
    {
        Detect("truecolor", "xterm-256color", false, ("NO_COLOR", "1")).Should().Be(ColorDepth.None);
    }

    [Fact]
    public void SettingsMode_OverridesDetection()
    {
        Detect("truecolor", "xterm-256color").Should().Be(ColorDepth.TrueColor);
        Detect("16", "xterm-256color", false, ("COLORTERM", "truecolor")).Should().Be(ColorDepth.Standard16);
    }

    [Theory]
    [InlineData("xterm-256color", "Colors256")]
    [InlineData("tmux-256color", "Colors256")]
    [InlineData("screen-256color", "Colors256")]
    [InlineData("xterm-direct", "TrueColor")]
    [InlineData("xterm", "Standard16")]
    [InlineData("linux", "Standard16")]
    [InlineData("dumb", "None")]
    [InlineData("", "None")]
    public void Term_DecidesWithoutColorterm(string term, string expected)
    {
        Detect("auto", term).ToString().Should().Be(expected);
    }

    [Fact]
    public void Colorterm_MeansTrueColor_EvenWithA256ColorTerm()
    {
        Detect("auto", "xterm-256color", false, ("COLORTERM", "truecolor")).Should().Be(ColorDepth.TrueColor);
    }

    [Fact]
    public void KnownTrueColorTerminals_AreDetected()
    {
        Detect("auto", "xterm-256color", false, ("TERM_PROGRAM", "iTerm.app")).Should().Be(ColorDepth.TrueColor);
        Detect("auto", "xterm-256color", false, ("TERM_PROGRAM", "Apple_Terminal")).Should().Be(ColorDepth.Colors256);
    }

    [Fact]
    public void WindowsTerminal_IsTrueColor()
    {
        Detect("auto", null, true, ("WT_SESSION", "{guid}")).Should().Be(ColorDepth.TrueColor);
    }

    [Theory]
    [InlineData("TrueColor", "TrueColor")]
    [InlineData(" rgb ", "TrueColor")]
    [InlineData("8bit", "Colors256")]
    [InlineData("auto", "")]
    [InlineData("nonsense", "")]
    public void Parse_AcceptsAliases(string value, string expected)
    {
        (ColorSupport.Parse(value)?.ToString() ?? "").Should().Be(expected);
    }
}
