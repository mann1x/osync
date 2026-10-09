using FluentAssertions;

namespace osync.Tests.UnitTests;

public class GraphGlyphsTests
{
    private static GraphStyle Detect(bool windows, string? font, params (string Name, string Value)[] env)
    {
        var vars = env.ToDictionary(e => e.Name, e => e.Value);
        return GraphGlyphs.Detect(n => vars.TryGetValue(n, out var v) ? v : null, windows, font).Style;
    }

    [Fact]
    public void NotWindows_UsesBraille()
    {
        Detect(false, null).Should().Be(GraphStyle.Braille);
    }

    [Theory]
    [InlineData("Consolas")]
    [InlineData("Lucida Console")]
    [InlineData("Terminal")]
    [InlineData(null)]
    public void ClassicWindowsConsole_UsesBlocks(string? font)
    {
        Detect(true, font).Should().Be(GraphStyle.Blocks);
    }

    [Theory]
    [InlineData("Cascadia Mono")]
    [InlineData("Cascadia Code PL")]
    [InlineData("DejaVu Sans Mono")]
    public void ClassicWindowsConsole_WithBrailleFont_UsesBraille(string font)
    {
        Detect(true, font).Should().Be(GraphStyle.Braille);
    }

    [Theory]
    [InlineData("WT_SESSION", "b2f1c7a0-1111-2222-3333-444455556666")]
    [InlineData("TERM_PROGRAM", "vscode")]
    [InlineData("ConEmuANSI", "ON")]
    [InlineData("WEZTERM_EXECUTABLE", "C:\\wezterm\\wezterm-gui.exe")]
    public void WindowsTerminalsWithFontFallback_UseBraille(string name, string value)
    {
        Detect(true, "Consolas", (name, value)).Should().Be(GraphStyle.Braille);
    }

    [Theory]
    [InlineData("blocks", true, "Cascadia Mono", GraphStyle.Blocks)]
    [InlineData("block", false, null, GraphStyle.Blocks)]
    [InlineData("braille", true, "Consolas", GraphStyle.Braille)]
    public void EnvironmentOverride_Wins(string value, bool windows, string? font, GraphStyle expected)
    {
        Detect(windows, font, ("OSYNC_GRAPH", value), ("WT_SESSION", "x")).Should().Be(expected);
    }

    [Fact]
    public void UnknownOverride_IsIgnored()
    {
        Detect(true, "Consolas", ("OSYNC_GRAPH", "fancy")).Should().Be(GraphStyle.Blocks);
    }

    [Fact]
    public void BlockGraph_UsesOnlyConsoleSafeGlyphs()
    {
        var graph = new BrailleGraph(10, 2);
        foreach (var v in new[] { 0.0, 10, 30, 50, 70, 90, 100, 60, 40, 20 })
            graph.AddDataPoint(v);

        var lines = graph.Render(10, 0, 2, GraphStyle.Blocks);
        var text = string.Concat(lines);
        text.Should().NotContainAny(Enumerable.Range(0x2800, 256).Select(c => ((char)c).ToString()));
        text.Should().Contain("█").And.Contain("▄");
    }

    [Fact]
    public void BlockGraph_FullValue_FillsColumn()
    {
        var graph = new BrailleGraph(1, 2);
        graph.AddDataPoint(100, "red");
        graph.AddDataPoint(100, "red");

        var lines = graph.Render(1, 0, 2, GraphStyle.Blocks);
        lines.Should().Equal("[red]█[/]", "[red]█[/]");
    }

    [Fact]
    public void BrailleGraph_KeepsBrailleGlyphs()
    {
        var graph = new BrailleGraph(1, 1);
        graph.AddDataPoint(100, "red");
        graph.AddDataPoint(100, "red");

        graph.Render(1, 0, 1, GraphStyle.Braille).Should().Equal("[red]\u28FF[/]");
    }

    [Fact]
    public void EmptyGraph_BlockStyle_IsBlank()
    {
        new BrailleGraph(4, 1).Render(4, 0, 1, GraphStyle.Blocks).Should().Equal("[dim]    [/]");
    }
}
