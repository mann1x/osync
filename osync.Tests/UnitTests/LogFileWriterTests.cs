using FluentAssertions;

namespace osync.Tests.UnitTests;

public class LogFileWriterTests
{
    // ── SanitizeText ────────────────────────────────────────────

    [Fact]
    public void SanitizeText_Null_ReturnsEmpty()
    {
        LogFileWriter.SanitizeText(null).Should().BeEmpty();
    }

    [Fact]
    public void SanitizeText_EmptyString_ReturnsEmpty()
    {
        LogFileWriter.SanitizeText("").Should().BeEmpty();
    }

    [Fact]
    public void SanitizeText_PlainText_Unchanged()
    {
        LogFileWriter.SanitizeText("hello world").Should().Be("hello world");
    }

    [Fact]
    public void SanitizeText_SpectreMarkup_Stripped()
    {
        LogFileWriter.SanitizeText("[red]text[/]").Should().Be("text");
    }

    [Fact]
    public void SanitizeText_SpectreMarkupWithAttributes_Stripped()
    {
        LogFileWriter.SanitizeText("[cyan bold]hello[/]").Should().Be("hello");
    }

    [Fact]
    public void SanitizeText_SpectreClosingTag_Stripped()
    {
        LogFileWriter.SanitizeText("[red]a[/red]b[/]").Should().Be("ab");
    }

    [Fact]
    public void SanitizeText_AnsiEscapeCodes_Stripped()
    {
        LogFileWriter.SanitizeText("\x1B[31mred\x1B[0m").Should().Be("red");
    }

    [Fact]
    public void SanitizeText_SpectreEscapedBrackets_Converted()
    {
        // [[ and ]] alone (not wrapping a word) → [ and ]
        // Note: [[word]] first has [word] stripped as markup, leaving [[]], then [[ → [ and ]] → ]
        LogFileWriter.SanitizeText("a [[ b ]] c").Should().Be("a [ b ] c");
    }

    [Fact]
    public void SanitizeText_ArrayIndex_Preserved()
    {
        // [0] should NOT be stripped — regex requires at least one letter/#
        LogFileWriter.SanitizeText("array[0]").Should().Be("array[0]");
    }

    [Fact]
    public void SanitizeText_ControlChars_Stripped()
    {
        LogFileWriter.SanitizeText("text\x01\x02more").Should().Be("textmore");
    }

    [Fact]
    public void SanitizeText_NewlinesAndTabs_Preserved()
    {
        LogFileWriter.SanitizeText("line1\nline2\ttab").Should().Be("line1\nline2\ttab");
    }

    [Fact]
    public void SanitizeText_AnsiAndMarkupCombined_BothStripped()
    {
        var input = "[red]\x1B[31mhello[/]\x1B[0m";
        LogFileWriter.SanitizeText(input).Should().Be("hello");
    }

    [Fact]
    public void SanitizeText_HexColorMarkup_Stripped()
    {
        LogFileWriter.SanitizeText("[#ff0000]red text[/]").Should().Be("red text");
    }

    [Fact]
    public void SanitizeText_DimMarkup_Stripped()
    {
        LogFileWriter.SanitizeText("[dim]faded[/]").Should().Be("faded");
    }
}
