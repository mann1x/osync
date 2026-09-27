using FluentAssertions;

namespace osync.Tests.UnitTests;

public class ThemesTests
{
    public static TheoryData<string> ThemeNames()
    {
        var data = new TheoryData<string>();
        foreach (var theme in Themes.All) data.Add(theme.Name);
        return data;
    }

    /// <summary>Foreground/background pairs of a theme and the contrast each needs (WCAG: 4.5 text, 3 large/secondary).</summary>
    private static IEnumerable<(string Pair, Rgb Fg, Rgb Bg, double Minimum)> Pairs(OsyncTheme t)
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
            yield return ($"success on {name}", t.Success, bg, 3);
            yield return ($"warning on {name}", t.Warning, bg, 3);
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
        var theme = Themes.Find(name);
        foreach (var (pair, fg, bg, minimum) in Pairs(theme))
            Rgb.Contrast(fg, bg).Should().BeGreaterThanOrEqualTo(minimum, $"{name}: {pair} ({fg} on {bg})");
    }

    [Theory]
    [MemberData(nameof(ThemeNames))]
    public void TrueColorTheme_SelectedRowStandsOut(string name)
    {
        var theme = Themes.Find(name);
        theme.SelectionBackground.Should().NotBe(theme.Background);
        theme.SelectionBackground.Should().NotBe(theme.AltBackground);
    }

    [Theory]
    [MemberData(nameof(ThemeNames))]
    public void SixteenColorTheme_UsesOnlyStandardColorsAndIsReadable(string name)
    {
        var adapted = Themes.Adapt(Themes.Find(name), ColorDepth.Standard16);
        var anchors = Themes.Ansi16.Select(a => a.Anchor).ToHashSet();

        // Back to the colors a terminal renders, to check what the user actually sees
        var rendered = adapted.Map(c =>
        {
            anchors.Should().Contain(c, $"{name}: {c} is not one of the 16 standard colors");
            return Themes.Ansi16.First(a => a.Anchor == c).Rendered;
        });

        rendered.SelectionBackground.Should().NotBe(rendered.Background, $"{name}: the selected row must stand out");
        foreach (var (pair, fg, bg, minimum) in Pairs(rendered))
            Rgb.Contrast(fg, bg).Should().BeGreaterThanOrEqualTo(minimum, $"{name} (16 colors): {pair} ({fg} on {bg})");
    }

    [Theory]
    [MemberData(nameof(ThemeNames))]
    public void Theme_KeepsSeveralColumnColors(string name)
    {
        // Not a monochrome list: the data columns use at least 4 different colors (3 with the 16 standard colors,
        // 2 for the single-hue retro themes)
        foreach (var depth in new[] { ColorDepth.TrueColor, ColorDepth.Colors256, ColorDepth.Standard16 })
        {
            var t = Themes.Adapt(Themes.Find(name), depth);
            new[] { t.Text, t.Size, t.Params, t.Quant, t.Family, t.Muted, t.Id }.Distinct().Count()
                .Should().BeGreaterThanOrEqualTo(depth != ColorDepth.Standard16 ? 4 : name is "Matrix" or "Amber" ? 2 : 3, $"{name} at {depth}");
        }
    }

    [Fact]
    public void ThemeNames_AreUnique()
    {
        Themes.All.Select(t => t.Name.ToLowerInvariant()).Should().OnlyHaveUniqueItems();
        Themes.All.Count.Should().BeGreaterThanOrEqualTo(30);
        Themes.All.Count(t => t.IsLight).Should().BeGreaterThanOrEqualTo(5, "light terminals need light themes");
    }

    [Theory]
    [InlineData("nord", "Nord")]
    [InlineData("  Tokyo Night ", "Tokyo Night")]
    [InlineData("no such theme", "Default")]
    [InlineData(null, "Default")]
    public void Find_IsCaseInsensitiveWithDefault(string? name, string expected)
    {
        Themes.Find(name).Name.Should().Be(expected);
    }

    [Fact]
    public void NoColors_UsesMonochrome()
    {
        Themes.Adapt(Themes.Find("Dracula"), ColorDepth.None).Should().Be(Themes.Monochrome);
        foreach (var (pair, fg, bg, minimum) in Pairs(Themes.Monochrome))
            Rgb.Contrast(fg, bg).Should().BeGreaterThanOrEqualTo(minimum, pair);
    }

    [Fact]
    public void TrueColor_KeepsTheTheme()
    {
        var theme = Themes.Find("Nord");
        Themes.Adapt(theme, ColorDepth.TrueColor).Should().Be(theme);
    }

    [Theory]
    [InlineData("#ff0000", "#ff0000")]
    [InlineData("#000000", "#000000")]
    [InlineData("#808080", "#808080")]   // gray ramp: 8 + 12 * 10
    [InlineData("#1e1e2e", "#262626")]   // dark background -> gray ramp
    [InlineData("#61afef", "#5fafff")]   // cube: 95, 175, 255
    public void Xterm256_SnapsToThePalette(string input, string expected)
    {
        Themes.ToXterm256(Rgb.Hex(input)).Should().Be(Rgb.Hex(expected));
    }

    [Theory]
    [MemberData(nameof(ThemeNames))]
    public void Xterm256_ColorsAreInThePalette(string name)
    {
        byte[] cube = { 0, 95, 135, 175, 215, 255 };
        var adapted = Themes.Adapt(Themes.Find(name), ColorDepth.Colors256);
        adapted.Map(c =>
        {
            var isCube = cube.Contains(c.R) && cube.Contains(c.G) && cube.Contains(c.B);
            var isGray = c.R == c.G && c.G == c.B && c.R >= 8 && c.R <= 238 && (c.R - 8) % 10 == 0;
            (isCube || isGray).Should().BeTrue($"{name}: {c} is not an xterm-256 color");
            Themes.ToXterm256(c).Should().Be(c);
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
        Themes.UseSixteenColors(Enum.Parse<ColorDepth>(depth), termProgram).Should().Be(expected);
    }

    [Theory]
    [InlineData("#ffffff", "#000000", 21.0)]
    [InlineData("#777777", "#ffffff", 4.48)]
    public void Contrast_FollowsWcag(string a, string b, double expected)
    {
        Rgb.Contrast(Rgb.Hex(a), Rgb.Hex(b)).Should().BeApproximately(expected, 0.01);
    }

    // ---- shell output --------------------------------------------------------------------------------------

    public static TheoryData<string, string> ThemeNamesAndDepths()
    {
        var data = new TheoryData<string, string>();
        foreach (var theme in Themes.All)
            foreach (var depth in new[] { "TrueColor", "Colors256", "Standard16" })
                data.Add(theme.Name, depth);
        return data;
    }

    [Theory]
    [MemberData(nameof(ThemeNamesAndDepths))]
    public void ShellTheme_IsReadableOnTheTerminalBackground(string name, string depthName)
    {
        var theme = Themes.Find(name);
        var depth = Enum.Parse<ColorDepth>(depthName);
        var p = Themes.ForShell(theme, depth)!;
        var roles = new (string Role, ShellColor Color, double Minimum)[]
        {
            ("text", p.Text, 4.5), ("heading", p.Heading, 3), ("muted", p.Muted, 3), ("size", p.Size, 3),
            ("params", p.Params, 3), ("quant", p.Quant, 3), ("family", p.Family, 3), ("id", p.Id, 3),
            ("loaded", p.Loaded, 3), ("success", p.Success, 3), ("warning", p.Warning, 3), ("error", p.Error, 3)
        };
        // 16 colors: the terminal renders the standard colors itself, the check uses typical renderings
        var minimumFactor = depth == ColorDepth.Standard16 ? 0.9 : 1.0;
        foreach (var bg in Themes.TerminalBackgrounds(theme.IsLight))
            foreach (var (role, color, minimum) in roles)
                Rgb.Contrast(color.Rgb, bg).Should().BeGreaterThanOrEqualTo(minimum * minimumFactor,
                    $"{name} at {depthName}: {role} {color.Rgb} on {bg}");
    }

    [Fact]
    public void ShellTheme_EncodesTheDepth()
    {
        var theme = Themes.Find("Dracula");
        Themes.ForShell(theme, ColorDepth.TrueColor)!.Error.Sgr.Should().Be("38;2;255;85;85");
        Themes.ForShell(theme, ColorDepth.Colors256)!.Error.Sgr.Should().MatchRegex(@"^38;5;\d+$");
        Themes.ForShell(theme, ColorDepth.Standard16)!.Error.Sgr.Should().MatchRegex(@"^(3[0-7]|9[0-7])$");
        Themes.ForShell(theme, ColorDepth.None).Should().BeNull();
    }

    [Theory]
    [InlineData("#ff0000", 196)]
    [InlineData("#000000", 16)]
    [InlineData("#ffffff", 231)]
    [InlineData("#808080", 244)]
    [InlineData("#5fafff", 75)]
    public void Xterm256Index_IsThePaletteIndex(string color, int index)
    {
        Themes.Xterm256Index(Rgb.Hex(color)).Should().Be(index);
    }

    [Theory]
    [InlineData("tokyo night", "Tokyo Night")]
    [InlineData("tokyo-night", "Tokyo Night")]
    [InlineData("CATPPUCCIN_MOCHA", "Catppuccin Mocha")]
    [InlineData("2", "Dracula")]
    public void FindExact_AcceptsLooseNamesAndNumbers(string name, string expected)
    {
        Themes.FindExact(name)!.Name.Should().Be(expected);
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("0")]
    [InlineData("999")]
    [InlineData("")]
    public void FindExact_UnknownIsNull(string name)
    {
        Themes.FindExact(name).Should().BeNull();
    }

    [Theory]
    [InlineData(null, "Default")]
    [InlineData("15;0", "Default")]
    [InlineData("0;15", "Light")]
    [InlineData("0;default;7", "Light")]
    public void DefaultShellTheme_FollowsColorFgBg(string? colorFgBg, string expected)
    {
        Themes.DefaultForShell(n => n == "COLORFGBG" ? colorFgBg : null).Name.Should().Be(expected);
    }

    [Fact]
    public void Paint_WritesSgrOnlyWithAPalette()
    {
        var palette = Themes.ForShell(Themes.Find("Nord"), ColorDepth.TrueColor);
        Out.Paint(palette, "x", p => p.Error, bold: true).Should().Be("\u001b[1;38;2;227;128;138mx\u001b[0m");
        Out.Paint(null, "x", p => p.Error).Should().Be("x");
        Out.PaletteFor("plain", ColorDepth.TrueColor).Should().BeNull();
    }

    [Fact]
    public void Status_HighlightsModelsAndServers()
    {
        var palette = Themes.ForShell(Themes.Find("Nord"), ColorDepth.TrueColor)!;
        var text = Out.Status(palette, "Copying 'qwen3:8b' from http://gpu:11434 to 'copy' on http://nas:11434...");

        text.Should().Contain($"'\u001b[1;{palette.Text.Sgr}mqwen3:8b\u001b[0m'");
        text.Should().Contain($"\u001b[{palette.Family.Sgr}mhttp://gpu:11434\u001b[0m to");
        text.Should().EndWith($"\u001b[{palette.Family.Sgr}mhttp://nas:11434\u001b[0m...");
        System.Text.RegularExpressions.Regex.Replace(text, "\u001b\\[[0-9;]*m", "")
            .Should().Be("Copying 'qwen3:8b' from http://gpu:11434 to 'copy' on http://nas:11434...", "only colors are added");
        Out.Status(null, "Copying 'x'").Should().Be("Copying 'x'");
    }
}
