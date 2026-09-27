namespace osync
{
    /// <summary>24-bit color, independent of the UI library (so themes can be unit-tested and downgraded).</summary>
    internal readonly record struct Rgb(byte R, byte G, byte B)
    {
        public static Rgb Hex(string hex)
        {
            var value = Convert.ToInt32(hex.TrimStart('#'), 16);
            return new Rgb((byte)(value >> 16), (byte)(value >> 8), (byte)value);
        }

        /// <summary>WCAG relative luminance (0 = black, 1 = white).</summary>
        public double Luminance
        {
            get
            {
                static double Channel(byte c)
                {
                    var s = c / 255.0;
                    return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
                }
                return 0.2126 * Channel(R) + 0.7152 * Channel(G) + 0.0722 * Channel(B);
            }
        }

        /// <summary>WCAG contrast ratio between two colors (1 to 21).</summary>
        public static double Contrast(Rgb a, Rgb b)
        {
            var (hi, lo) = a.Luminance >= b.Luminance ? (a.Luminance, b.Luminance) : (b.Luminance, a.Luminance);
            return (hi + 0.05) / (lo + 0.05);
        }

        public override string ToString() => $"#{R:x2}{G:x2}{B:x2}";
    }

    /// <summary>
    /// Color palette of the `manage` view. Every column has its own color; the whole palette is 24-bit and is
    /// downgraded to the terminal's color depth by <see cref="ManageThemes.Adapt"/>.
    /// </summary>
    internal sealed record ManageTheme
    {
        public required string Name { get; init; }

        // Model list
        public required Rgb Background { get; init; }
        public required Rgb AltBackground { get; init; }     // odd rows
        public required Rgb Text { get; init; }              // model name
        public required Rgb Muted { get; init; }             // age, column header
        public required Rgb Size { get; init; }
        public required Rgb Params { get; init; }
        public required Rgb Quant { get; init; }
        public required Rgb Family { get; init; }
        public required Rgb Id { get; init; }
        public required Rgb Loaded { get; init; }            // marker of models loaded in memory
        public required Rgb Checked { get; init; }           // [X] of multi-selected models
        public required Rgb SelectionBackground { get; init; }
        public required Rgb SelectionText { get; init; }

        // Top and bottom bars
        public required Rgb BarBackground { get; init; }
        public required Rgb BarText { get; init; }
        public required Rgb BarAccent { get; init; }         // values, shortcut keys

        // Dialogs and message boxes
        public required Rgb DialogBackground { get; init; }
        public required Rgb DialogText { get; init; }
        public required Rgb FieldBackground { get; init; }   // text fields
        public required Rgb FieldText { get; init; }
        public required Rgb Error { get; init; }

        /// <summary>Applies <paramref name="map"/> to every color of the palette.</summary>
        public ManageTheme Map(Func<Rgb, Rgb> map) => this with
        {
            Background = map(Background), AltBackground = map(AltBackground), Text = map(Text), Muted = map(Muted),
            Size = map(Size), Params = map(Params), Quant = map(Quant), Family = map(Family), Id = map(Id),
            Loaded = map(Loaded), Checked = map(Checked),
            SelectionBackground = map(SelectionBackground), SelectionText = map(SelectionText),
            BarBackground = map(BarBackground), BarText = map(BarText), BarAccent = map(BarAccent),
            DialogBackground = map(DialogBackground), DialogText = map(DialogText),
            FieldBackground = map(FieldBackground), FieldText = map(FieldText), Error = map(Error)
        };
    }

    /// <summary>Built-in themes of `manage` and their adaptation to the terminal's color depth.</summary>
    internal static class ManageThemes
    {
        private static Rgb H(string hex) => Rgb.Hex(hex);

        public static readonly IReadOnlyList<ManageTheme> All = new[]
        {
            new ManageTheme
            {
                Name = "Default",
                Background = H("#181a21"), AltBackground = H("#1f222b"), Text = H("#dcdfe4"), Muted = H("#8a93a5"),
                Size = H("#61afef"), Params = H("#c678dd"), Quant = H("#e5c07b"), Family = H("#56b6c2"), Id = H("#9da5b4"),
                Loaded = H("#98c379"), Checked = H("#e06c75"),
                SelectionBackground = H("#3d5a9e"), SelectionText = H("#ffffff"),
                BarBackground = H("#2c313c"), BarText = H("#abb2bf"), BarAccent = H("#61afef"),
                DialogBackground = H("#2c313c"), DialogText = H("#dcdfe4"), FieldBackground = H("#181a21"), FieldText = H("#ffffff"),
                Error = H("#e06c75")
            },
            new ManageTheme
            {
                Name = "Dracula",
                Background = H("#282a36"), AltBackground = H("#2f3240"), Text = H("#f8f8f2"), Muted = H("#8b95c9"),
                Size = H("#8be9fd"), Params = H("#bd93f9"), Quant = H("#f1fa8c"), Family = H("#50fa7b"), Id = H("#a4acd4"),
                Loaded = H("#50fa7b"), Checked = H("#ff79c6"),
                SelectionBackground = H("#6272a4"), SelectionText = H("#ffffff"),
                BarBackground = H("#21222c"), BarText = H("#f8f8f2"), BarAccent = H("#ff79c6"),
                DialogBackground = H("#343746"), DialogText = H("#f8f8f2"), FieldBackground = H("#21222c"), FieldText = H("#f8f8f2"),
                Error = H("#ff5555")
            },
            new ManageTheme
            {
                Name = "Nord",
                Background = H("#2e3440"), AltBackground = H("#343b49"), Text = H("#eceff4"), Muted = H("#9aa5ba"),
                Size = H("#88c0d0"), Params = H("#b48ead"), Quant = H("#ebcb8b"), Family = H("#a3be8c"), Id = H("#a3b3cc"),
                Loaded = H("#a3be8c"), Checked = H("#d08770"),
                SelectionBackground = H("#4c6a94"), SelectionText = H("#ffffff"),
                BarBackground = H("#3b4252"), BarText = H("#d8dee9"), BarAccent = H("#88c0d0"),
                DialogBackground = H("#3b4252"), DialogText = H("#eceff4"), FieldBackground = H("#2e3440"), FieldText = H("#eceff4"),
                Error = H("#e3808a")
            },
            new ManageTheme
            {
                Name = "Tokyo Night",
                Background = H("#1a1b26"), AltBackground = H("#1f2233"), Text = H("#c0caf5"), Muted = H("#8189b5"),
                Size = H("#7aa2f7"), Params = H("#bb9af7"), Quant = H("#e0af68"), Family = H("#7dcfff"), Id = H("#9aa5ce"),
                Loaded = H("#9ece6a"), Checked = H("#ff9e64"),
                SelectionBackground = H("#3d59a1"), SelectionText = H("#ffffff"),
                BarBackground = H("#16161e"), BarText = H("#a9b1d6"), BarAccent = H("#7aa2f7"),
                DialogBackground = H("#24283b"), DialogText = H("#c0caf5"), FieldBackground = H("#1a1b26"), FieldText = H("#c0caf5"),
                Error = H("#f7768e")
            },
            new ManageTheme
            {
                Name = "Catppuccin Mocha",
                Background = H("#1e1e2e"), AltBackground = H("#252536"), Text = H("#cdd6f4"), Muted = H("#9399b2"),
                Size = H("#89b4fa"), Params = H("#cba6f7"), Quant = H("#f9e2af"), Family = H("#94e2d5"), Id = H("#a6adc8"),
                Loaded = H("#a6e3a1"), Checked = H("#f38ba8"),
                SelectionBackground = H("#585b70"), SelectionText = H("#ffffff"),
                BarBackground = H("#181825"), BarText = H("#bac2de"), BarAccent = H("#f5c2e7"),
                DialogBackground = H("#313244"), DialogText = H("#cdd6f4"), FieldBackground = H("#1e1e2e"), FieldText = H("#cdd6f4"),
                Error = H("#f38ba8")
            },
            new ManageTheme
            {
                Name = "Gruvbox Dark",
                Background = H("#282828"), AltBackground = H("#302e2d"), Text = H("#ebdbb2"), Muted = H("#a89984"),
                Size = H("#83a598"), Params = H("#d3869b"), Quant = H("#fabd2f"), Family = H("#8ec07c"), Id = H("#bdae93"),
                Loaded = H("#b8bb26"), Checked = H("#fe8019"),
                SelectionBackground = H("#665c54"), SelectionText = H("#fbf1c7"),
                BarBackground = H("#3c3836"), BarText = H("#d5c4a1"), BarAccent = H("#fabd2f"),
                DialogBackground = H("#3c3836"), DialogText = H("#ebdbb2"), FieldBackground = H("#282828"), FieldText = H("#fbf1c7"),
                Error = H("#fb4934")
            },
            new ManageTheme
            {
                Name = "Monokai",
                Background = H("#272822"), AltBackground = H("#2e2f29"), Text = H("#f8f8f2"), Muted = H("#a59f85"),
                Size = H("#66d9ef"), Params = H("#ae81ff"), Quant = H("#e6db74"), Family = H("#a6e22e"), Id = H("#b0ada0"),
                Loaded = H("#a6e22e"), Checked = H("#f92672"),
                SelectionBackground = H("#5b5a4e"), SelectionText = H("#ffffff"),
                BarBackground = H("#1e1f1c"), BarText = H("#f8f8f2"), BarAccent = H("#fd971f"),
                DialogBackground = H("#34352d"), DialogText = H("#f8f8f2"), FieldBackground = H("#272822"), FieldText = H("#f8f8f2"),
                Error = H("#f92672")
            },
            new ManageTheme
            {
                Name = "Solarized Dark",
                Background = H("#002b36"), AltBackground = H("#05323e"), Text = H("#eee8d5"), Muted = H("#839496"),
                Size = H("#268bd2"), Params = H("#8d91e0"), Quant = H("#b58900"), Family = H("#2aa198"), Id = H("#93a1a1"),
                Loaded = H("#859900"), Checked = H("#d9541c"),
                SelectionBackground = H("#1f6fa8"), SelectionText = H("#fdf6e3"),
                BarBackground = H("#073642"), BarText = H("#93a1a1"), BarAccent = H("#b58900"),
                DialogBackground = H("#073642"), DialogText = H("#eee8d5"), FieldBackground = H("#002b36"), FieldText = H("#fdf6e3"),
                Error = H("#f0605a")
            },
            new ManageTheme
            {
                Name = "Ocean",
                Background = H("#0b1e3f"), AltBackground = H("#102650"), Text = H("#e6f1ff"), Muted = H("#8fa9d6"),
                Size = H("#64d2ff"), Params = H("#c3a6ff"), Quant = H("#ffd866"), Family = H("#5eead4"), Id = H("#a9bfe3"),
                Loaded = H("#7ee787"), Checked = H("#ff9e64"),
                SelectionBackground = H("#1f6feb"), SelectionText = H("#ffffff"),
                BarBackground = H("#081630"), BarText = H("#cfe3ff"), BarAccent = H("#64d2ff"),
                DialogBackground = H("#102a54"), DialogText = H("#e6f1ff"), FieldBackground = H("#0b1e3f"), FieldText = H("#ffffff"),
                Error = H("#ff6b6b")
            },
            new ManageTheme
            {
                Name = "Matrix",
                Background = H("#000000"), AltBackground = H("#07120a"), Text = H("#4dff7c"), Muted = H("#2fae52"),
                Size = H("#9dffb5"), Params = H("#00d448"), Quant = H("#d2ffd9"), Family = H("#35e36a"), Id = H("#3fbf61"),
                Loaded = H("#ffffff"), Checked = H("#ffe066"),
                SelectionBackground = H("#1f9e45"), SelectionText = H("#000000"),
                BarBackground = H("#002a0e"), BarText = H("#4dff7c"), BarAccent = H("#d2ffd9"),
                DialogBackground = H("#001f0a"), DialogText = H("#4dff7c"), FieldBackground = H("#000000"), FieldText = H("#b6ffc8"),
                Error = H("#ff5555")
            },
            new ManageTheme
            {
                Name = "Solarized Light",
                Background = H("#fdf6e3"), AltBackground = H("#f5eed8"), Text = H("#073642"), Muted = H("#5f7278"),
                Size = H("#1f6fa8"), Params = H("#5a5fb8"), Quant = H("#8a6800"), Family = H("#16776f"), Id = H("#586e75"),
                Loaded = H("#5c6b00"), Checked = H("#b8420f"),
                SelectionBackground = H("#1f6fa8"), SelectionText = H("#fdf6e3"),
                BarBackground = H("#eee8d5"), BarText = H("#3d5259"), BarAccent = H("#b8420f"),
                DialogBackground = H("#eee8d5"), DialogText = H("#073642"), FieldBackground = H("#fdf6e3"), FieldText = H("#073642"),
                Error = H("#c42b28")
            },
            new ManageTheme
            {
                Name = "Light",
                Background = H("#ffffff"), AltBackground = H("#f3f5f8"), Text = H("#1f2328"), Muted = H("#5f6770"),
                Size = H("#0969da"), Params = H("#8250df"), Quant = H("#8a5c00"), Family = H("#1a7f37"), Id = H("#57606a"),
                Loaded = H("#1a7f37"), Checked = H("#cf222e"),
                SelectionBackground = H("#0969da"), SelectionText = H("#ffffff"),
                BarBackground = H("#e6eaef"), BarText = H("#24292f"), BarAccent = H("#0550ae"),
                DialogBackground = H("#f3f5f8"), DialogText = H("#1f2328"), FieldBackground = H("#ffffff"), FieldText = H("#1f2328"),
                Error = H("#cf222e")
            },
            new ManageTheme
            {
                Name = "High Contrast",
                Background = H("#000000"), AltBackground = H("#121212"), Text = H("#ffffff"), Muted = H("#c8c8c8"),
                Size = H("#00ffff"), Params = H("#ff8cff"), Quant = H("#ffff00"), Family = H("#00ff00"), Id = H("#d0d0d0"),
                Loaded = H("#00ff00"), Checked = H("#ffff00"),
                SelectionBackground = H("#ffff00"), SelectionText = H("#000000"),
                BarBackground = H("#000000"), BarText = H("#ffffff"), BarAccent = H("#ffff00"),
                DialogBackground = H("#000000"), DialogText = H("#ffffff"), FieldBackground = H("#262626"), FieldText = H("#ffffff"),
                Error = H("#ff5050")
            },
            new ManageTheme
            {
                Name = "Classic",
                // The look of osync before 1.3.1: the 16 standard terminal colors
                Background = H("#000000"), AltBackground = H("#000000"), Text = H("#ffffff"), Muted = H("#c0c0c0"),
                Size = H("#00cdcd"), Params = H("#cd00cd"), Quant = H("#cdcd00"), Family = H("#00cd00"), Id = H("#c0c0c0"),
                Loaded = H("#00ff00"), Checked = H("#ffff00"),
                SelectionBackground = H("#ffffff"), SelectionText = H("#000000"),
                BarBackground = H("#000000"), BarText = H("#ffffff"), BarAccent = H("#00ffff"),
                DialogBackground = H("#0000ee"), DialogText = H("#ffffff"), FieldBackground = H("#000000"), FieldText = H("#ffffff"),
                Error = H("#ffff55")
            }
        };

        /// <summary>Theme used without colors (NO_COLOR, TERM=dumb, colorMode none): black and white only.</summary>
        public static readonly ManageTheme Monochrome = new()
        {
            Name = "Monochrome",
            Background = H("#000000"), AltBackground = H("#000000"), Text = H("#ffffff"), Muted = H("#ffffff"),
            Size = H("#ffffff"), Params = H("#ffffff"), Quant = H("#ffffff"), Family = H("#ffffff"), Id = H("#ffffff"),
            Loaded = H("#ffffff"), Checked = H("#ffffff"),
            SelectionBackground = H("#ffffff"), SelectionText = H("#000000"),
            BarBackground = H("#ffffff"), BarText = H("#000000"), BarAccent = H("#000000"),
            DialogBackground = H("#000000"), DialogText = H("#ffffff"), FieldBackground = H("#ffffff"), FieldText = H("#000000"),
            Error = H("#ffffff")
        };

        public static ManageTheme Default => All[0];

        /// <summary>Theme by name (case-insensitive); null or unknown names give the default theme.</summary>
        public static ManageTheme Find(string? name) =>
            All.FirstOrDefault(t => string.Equals(t.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? Default;

        public static int IndexOf(ManageTheme theme)
        {
            for (int i = 0; i < All.Count; i++)
                if (string.Equals(All[i].Name, theme.Name, StringComparison.OrdinalIgnoreCase)) return i;
            return 0;
        }

        /// <summary>
        /// The theme as it is drawn at <paramref name="depth"/>:
        ///   TrueColor   unchanged
        ///   Colors256   every color snapped to the nearest xterm-256 color (Terminal.Gui always writes 24-bit
        ///               sequences; terminals and tmux that map them to their 256-color palette then show exactly
        ///               the intended color)
        ///   Standard16  the nearest of the 16 standard colors, with low-contrast text fixed (see ToAnsi16)
        ///   None        the monochrome theme
        /// </summary>
        public static ManageTheme Adapt(ManageTheme theme, ColorDepth depth) => depth switch
        {
            ColorDepth.None => Monochrome,
            ColorDepth.Colors256 => theme.Map(ToXterm256),
            ColorDepth.Standard16 => ToAnsi16(theme),
            _ => theme
        };

        /// <summary>
        /// The 16 standard colors: how terminals typically render them (Windows Terminal "Campbell", close to
        /// xterm and VGA), and the value Terminal.Gui maps exactly to that color when it writes 16 colors.
        /// </summary>
        internal static readonly (Rgb Rendered, Rgb Anchor)[] Ansi16 =
        {
            (new Rgb(12, 12, 12), new Rgb(0, 0, 0)),             // black
            (new Rgb(197, 15, 31), new Rgb(255, 0, 0)),          // red
            (new Rgb(19, 161, 14), new Rgb(0, 128, 0)),          // green
            (new Rgb(193, 156, 0), new Rgb(255, 255, 0)),        // yellow
            (new Rgb(0, 55, 218), new Rgb(0, 0, 255)),           // blue
            (new Rgb(136, 23, 152), new Rgb(255, 0, 255)),       // magenta
            (new Rgb(58, 150, 221), new Rgb(0, 255, 255)),       // cyan
            (new Rgb(204, 204, 204), new Rgb(128, 128, 128)),    // white (light gray)
            (new Rgb(118, 118, 118), new Rgb(118, 118, 118)),    // bright black (dark gray)
            (new Rgb(231, 72, 86), new Rgb(231, 72, 86)),        // bright red
            (new Rgb(22, 198, 12), new Rgb(22, 198, 12)),        // bright green
            (new Rgb(249, 241, 165), new Rgb(249, 241, 165)),    // bright yellow
            (new Rgb(59, 120, 255), new Rgb(59, 120, 255)),      // bright blue
            (new Rgb(180, 0, 158), new Rgb(180, 0, 158)),        // bright magenta
            (new Rgb(97, 214, 214), new Rgb(97, 214, 214)),      // bright cyan
            (new Rgb(242, 242, 242), new Rgb(255, 255, 255))     // bright white
        };

        private static readonly Rgb Ansi16Black = Ansi16[0].Rendered;
        private static readonly Rgb Ansi16BrightWhite = Ansi16[15].Rendered;

        /// <summary>
        /// The theme in the 16 standard colors: every color goes to the nearest standard color, then text that
        /// would become hard to read on its background (same color, low contrast) is switched to black or white.
        /// The result uses the values Terminal.Gui maps exactly (see <see cref="Ansi16"/>).
        /// </summary>
        public static ManageTheme ToAnsi16(ManageTheme theme)
        {
            var o = theme;
            var background = Nearest16(o.Background, _ => true);
            var altBackground = Nearest16(o.AltBackground, _ => true);

            // Backgrounds that carry text must allow readable text; the selected row must stand out from the list
            static bool CarriesText(Rgb bg) => Math.Max(Rgb.Contrast(Ansi16BrightWhite, bg), Rgb.Contrast(Ansi16Black, bg)) >= 4.5;
            var selection = Nearest16(o.SelectionBackground, c => c != background && c != altBackground && CarriesText(c));
            var bar = Nearest16(o.BarBackground, CarriesText);
            var dialog = Nearest16(o.DialogBackground, CarriesText);
            var field = Nearest16(o.FieldBackground, CarriesText);

            // Foregrounds: the nearest standard color that is readable on the background they are drawn on
            Rgb OnList(Rgb fg, double minimum) =>
                Nearest16(fg, c => c != background && c != altBackground &&
                                   Rgb.Contrast(c, background) >= minimum && Rgb.Contrast(c, altBackground) >= minimum);
            Rgb On(Rgb fg, Rgb bg, double minimum) => Nearest16(fg, c => c != bg && Rgb.Contrast(c, bg) >= minimum);

            var t = theme with
            {
                Background = background,
                AltBackground = altBackground,
                Text = OnList(o.Text, 4.5),
                Muted = OnList(o.Muted, 3),
                Size = OnList(o.Size, 3),
                Params = OnList(o.Params, 3),
                Quant = OnList(o.Quant, 3),
                Family = OnList(o.Family, 3),
                Id = OnList(o.Id, 3),
                Loaded = OnList(o.Loaded, 3),
                Checked = OnList(o.Checked, 3),
                SelectionBackground = selection,
                SelectionText = On(o.SelectionText, selection, 4.5),
                BarBackground = bar,
                BarText = On(o.BarText, bar, 4.5),
                BarAccent = On(o.BarAccent, bar, 3),
                DialogBackground = dialog,
                DialogText = On(o.DialogText, dialog, 4.5),
                FieldBackground = field,
                FieldText = On(o.FieldText, field, 4.5),
                Error = On(o.Error, dialog, 3)
            };

            return t.Map(c => Ansi16.First(a => a.Rendered == c).Anchor);
        }

        /// <summary>
        /// The standard color (as rendered) nearest to <paramref name="color"/> among those accepted by
        /// <paramref name="allowed"/>; black or white when none is.
        /// </summary>
        private static Rgb Nearest16(Rgb color, Func<Rgb, bool> allowed)
        {
            Rgb? best = null;
            foreach (var (rendered, _) in Ansi16)
            {
                if (!allowed(rendered)) continue;
                if (best == null || Distance(color, rendered) < Distance(color, best.Value)) best = rendered;
            }
            return best ?? (color.Luminance > 0.5 ? Ansi16BrightWhite : Ansi16Black);
        }

        /// <summary>Index in <see cref="Ansi16"/> of the standard color nearest to <paramref name="color"/>.</summary>
        internal static int NearestAnsi16(Rgb color)
        {
            int best = 0;
            for (int i = 1; i < Ansi16.Length; i++)
                if (Distance(color, Ansi16[i].Rendered) < Distance(color, Ansi16[best].Rendered)) best = i;
            return best;
        }


        /// <summary>
        /// Whether Terminal.Gui must write only the 16 standard colors. Terminal.Gui has no 256-color output: it
        /// writes 24-bit color unless forced to 16 colors. 256-color terminals get 24-bit sequences (most of them,
        /// and tmux, map those to their palette), except Apple Terminal, which misreads them.
        /// </summary>
        public static bool UseSixteenColors(ColorDepth depth, string? termProgram) => depth switch
        {
            ColorDepth.None or ColorDepth.Standard16 => true,
            ColorDepth.Colors256 => string.Equals(termProgram?.Trim(), "Apple_Terminal", StringComparison.OrdinalIgnoreCase),
            _ => false
        };

        private static readonly byte[] CubeLevels = { 0, 95, 135, 175, 215, 255 };

        /// <summary>Nearest color of the xterm-256 palette's 6x6x6 cube and gray ramp (16-255).</summary>
        public static Rgb ToXterm256(Rgb color)
        {
            static int NearestLevel(byte c)
            {
                int best = 0;
                for (int i = 1; i < CubeLevels.Length; i++)
                    if (Math.Abs(CubeLevels[i] - c) < Math.Abs(CubeLevels[best] - c)) best = i;
                return best;
            }

            var cube = new Rgb(CubeLevels[NearestLevel(color.R)], CubeLevels[NearestLevel(color.G)], CubeLevels[NearestLevel(color.B)]);

            // Gray ramp: 8, 18, ..., 238
            int average = (color.R + color.G + color.B) / 3;
            int grayIndex = Math.Clamp((int)Math.Round((average - 8) / 10.0), 0, 23);
            byte grayLevel = (byte)(8 + grayIndex * 10);
            var gray = new Rgb(grayLevel, grayLevel, grayLevel);

            return Distance(color, gray) < Distance(color, cube) ? gray : cube;
        }

        private static int Distance(Rgb a, Rgb b)
        {
            int dr = a.R - b.R, dg = a.G - b.G, db = a.B - b.B;
            // Weighted for perceived brightness (green counts most)
            return 2 * dr * dr + 4 * dg * dg + 3 * db * db;
        }
    }
}
