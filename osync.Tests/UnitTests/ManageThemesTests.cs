using FluentAssertions;

namespace osync.Tests.UnitTests;

public class ManageThemesTests
{
    public static TheoryData<string> ThemeNames()
    {
        var data = new TheoryData<string>();
        foreach (var theme in ManageThemes.All) data.Add(theme.Name);
        return data;
    }

    /// <summary>Foreground/background pairs of a theme and the contrast each needs (WCAG: 4.5 text, 3 large/secondary).</summary>
    private static IEnumerable<(string Pair, Rgb Fg, Rgb Bg, double Minimum)> Pairs(ManageTheme t)
    {
        foreach (var (name, bg) in new[] { ("background", t.Background), ("alt background", t.AltBackground) })
        {
            yield return ($"text on {name}", t.Text, bg, 4.5);
            yield return ($"muted on {name}", t.Muted, bg, 3);
            yield return ($"size on {name}", t.Size, bg, 3);
            yield return ($"params on {name}", t.Params, bg, 3);
            yield return ($"quant on {name}", t.Quant, bg, 3);
            yield return ($"family on {name}", t.Family, bg, 3);
            yield return ($"id on {name}", t.Id, bg, 3);
            yield return ($"loaded on {name}", t.Loaded, bg, 3);
            yield return ($"checked on {name}", t.Checked, bg, 3);
        }
        yield return ("selection", t.SelectionText, t.SelectionBackground, 4.5);
        yield return ("bar text", t.BarText, t.BarBackground, 4.5);
        yield return ("bar accent", t.BarAccent, t.BarBackground, 3);
        yield return ("dialog text", t.DialogText, t.DialogBackground, 4.5);
        yield return ("field text", t.FieldText, t.FieldBackground, 4.5);
        yield return ("error", t.Error, t.DialogBackground, 3);
    }

    [Theory]
    [MemberData(nameof(ThemeNames))]
    public void TrueColorTheme_IsReadable(string name)
    {
        var theme = ManageThemes.Find(name);
        foreach (var (pair, fg, bg, minimum) in Pairs(theme))
            Rgb.Contrast(fg, bg).Should().BeGreaterThanOrEqualTo(minimum, $"{name}: {pair} ({fg} on {bg})");
    }

    [Theory]
    [MemberData(nameof(ThemeNames))]
    public void TrueColorTheme_SelectedRowStandsOut(string name)
    {
        var theme = ManageThemes.Find(name);
        theme.SelectionBackground.Should().NotBe(theme.Background);
        theme.SelectionBackground.Should().NotBe(theme.AltBackground);
    }

    [Theory]
    [MemberData(nameof(ThemeNames))]
    public void SixteenColorTheme_UsesOnlyStandardColorsAndIsReadable(string name)
    {
        var adapted = ManageThemes.Adapt(ManageThemes.Find(name), ColorDepth.Standard16);
        var anchors = ManageThemes.Ansi16.Select(a => a.Anchor).ToHashSet();

        // Back to the colors a terminal renders, to check what the user actually sees
        var rendered = adapted.Map(c =>
        {
            anchors.Should().Contain(c, $"{name}: {c} is not one of the 16 standard colors");
            return ManageThemes.Ansi16.First(a => a.Anchor == c).Rendered;
        });

        rendered.SelectionBackground.Should().NotBe(rendered.Background, $"{name}: the selected row must stand out");
        foreach (var (pair, fg, bg, minimum) in Pairs(rendered))
            Rgb.Contrast(fg, bg).Should().BeGreaterThanOrEqualTo(minimum, $"{name} (16 colors): {pair} ({fg} on {bg})");
    }

    [Theory]
    [MemberData(nameof(ThemeNames))]
    public void Theme_KeepsSeveralColumnColors(string name)
    {
        // Not a monochrome list: the data columns use at least 4 different colors, also with 16 colors
        foreach (var depth in new[] { ColorDepth.TrueColor, ColorDepth.Colors256, ColorDepth.Standard16 })
        {
            var t = ManageThemes.Adapt(ManageThemes.Find(name), depth);
            new[] { t.Text, t.Size, t.Params, t.Quant, t.Family, t.Muted, t.Id }.Distinct().Count()
                .Should().BeGreaterThanOrEqualTo(name is "High Contrast" or "Classic" or "Matrix" && depth == ColorDepth.Standard16 ? 3 : 4, $"{name} at {depth}");
        }
    }

    [Fact]
    public void ThemeNames_AreUnique()
    {
        ManageThemes.All.Select(t => t.Name.ToLowerInvariant()).Should().OnlyHaveUniqueItems();
        ManageThemes.All.Count.Should().BeGreaterThanOrEqualTo(10);
    }

    [Theory]
    [InlineData("nord", "Nord")]
    [InlineData("  Tokyo Night ", "Tokyo Night")]
    [InlineData("no such theme", "Default")]
    [InlineData(null, "Default")]
    public void Find_IsCaseInsensitiveWithDefault(string? name, string expected)
    {
        ManageThemes.Find(name).Name.Should().Be(expected);
    }

    [Fact]
    public void NoColors_UsesMonochrome()
    {
        ManageThemes.Adapt(ManageThemes.Find("Dracula"), ColorDepth.None).Should().Be(ManageThemes.Monochrome);
        foreach (var (pair, fg, bg, minimum) in Pairs(ManageThemes.Monochrome))
            Rgb.Contrast(fg, bg).Should().BeGreaterThanOrEqualTo(minimum, pair);
    }

    [Fact]
    public void TrueColor_KeepsTheTheme()
    {
        var theme = ManageThemes.Find("Nord");
        ManageThemes.Adapt(theme, ColorDepth.TrueColor).Should().Be(theme);
    }

    [Theory]
    [InlineData("#ff0000", "#ff0000")]
    [InlineData("#000000", "#000000")]
    [InlineData("#808080", "#808080")]   // gray ramp: 8 + 12 * 10
    [InlineData("#1e1e2e", "#262626")]   // dark background -> gray ramp
    [InlineData("#61afef", "#5fafff")]   // cube: 95, 175, 255
    public void Xterm256_SnapsToThePalette(string input, string expected)
    {
        ManageThemes.ToXterm256(Rgb.Hex(input)).Should().Be(Rgb.Hex(expected));
    }

    [Theory]
    [MemberData(nameof(ThemeNames))]
    public void Xterm256_ColorsAreInThePalette(string name)
    {
        byte[] cube = { 0, 95, 135, 175, 215, 255 };
        var adapted = ManageThemes.Adapt(ManageThemes.Find(name), ColorDepth.Colors256);
        adapted.Map(c =>
        {
            var isCube = cube.Contains(c.R) && cube.Contains(c.G) && cube.Contains(c.B);
            var isGray = c.R == c.G && c.G == c.B && c.R >= 8 && c.R <= 238 && (c.R - 8) % 10 == 0;
            (isCube || isGray).Should().BeTrue($"{name}: {c} is not an xterm-256 color");
            ManageThemes.ToXterm256(c).Should().Be(c);
            return c;
        });
    }

    [Theory]
    [InlineData("TrueColor", null, false)]
    [InlineData("Colors256", null, false)]
    [InlineData("Colors256", "Apple_Terminal", true)]
    [InlineData("TrueColor", "Apple_Terminal", false)]
    [InlineData("Standard16", null, true)]
    [InlineData("None", null, true)]
    public void SixteenColorOutput_OnlyWhereNeeded(string depth, string? termProgram, bool expected)
    {
        ManageThemes.UseSixteenColors(Enum.Parse<ColorDepth>(depth), termProgram).Should().Be(expected);
    }

    [Theory]
    [InlineData("#ffffff", "#000000", 21.0)]
    [InlineData("#777777", "#ffffff", 4.48)]
    public void Contrast_FollowsWcag(string a, string b, double expected)
    {
        Rgb.Contrast(Rgb.Hex(a), Rgb.Hex(b)).Should().BeApproximately(expected, 0.01);
    }
}
