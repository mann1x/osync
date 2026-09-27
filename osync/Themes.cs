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
    /// A color theme of osync: the palette of the `manage` view (every column has its own color) and of the
    /// command output in the shell (<see cref="Themes.ForShell"/>). The palette is 24-bit and is downgraded to the
    /// terminal's color depth (<see cref="Themes.Adapt"/>, <see cref="Themes.ForShell"/>).
    /// </summary>
    internal sealed record OsyncTheme
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

        // Messages
        public required Rgb Success { get; init; }
        public required Rgb Warning { get; init; }

        /// <summary>A theme for light backgrounds (and light terminals in the shell).</summary>
        public bool IsLight => Background.Luminance > 0.5;

        /// <summary>Applies <paramref name="map"/> to every color of the palette.</summary>
        public OsyncTheme Map(Func<Rgb, Rgb> map) => this with
        {
            Background = map(Background), AltBackground = map(AltBackground), Text = map(Text), Muted = map(Muted),
            Size = map(Size), Params = map(Params), Quant = map(Quant), Family = map(Family), Id = map(Id),
            Loaded = map(Loaded), Checked = map(Checked),
            SelectionBackground = map(SelectionBackground), SelectionText = map(SelectionText),
            BarBackground = map(BarBackground), BarText = map(BarText), BarAccent = map(BarAccent),
            DialogBackground = map(DialogBackground), DialogText = map(DialogText),
            FieldBackground = map(FieldBackground), FieldText = map(FieldText), Error = map(Error),
            Success = map(Success), Warning = map(Warning)
        };
    }

    /// <summary>Built-in themes (dark first, then light) and their adaptation to the terminal's color depth.</summary>
    internal static class Themes
    {
        private static Rgb H(string hex) => Rgb.Hex(hex);

        public static readonly IReadOnlyList<OsyncTheme> All = new[]
        {
            new OsyncTheme
            {
                Name = "Default",
                Background = H("#181a21"), AltBackground = H("#1f222b"), Text = H("#dcdfe4"), Muted = H("#8a93a5"),
                Size = H("#61afef"), Params = H("#c678dd"), Quant = H("#e5c07b"), Family = H("#56b6c2"), Id = H("#9da5b4"),
                Loaded = H("#98c379"), Checked = H("#e06c75"), SelectionBackground = H("#3d5a9e"), SelectionText = H("#ffffff"),
                BarBackground = H("#2c313c"), BarText = H("#abb2bf"), BarAccent = H("#61afef"),
                DialogBackground = H("#2c313c"), DialogText = H("#dcdfe4"), FieldBackground = H("#181a21"), FieldText = H("#ffffff"),
                Error = H("#e06c75"), Success = H("#98c379"), Warning = H("#e5c07b")
            },
            new OsyncTheme
            {
                Name = "Dracula",
                Background = H("#282a36"), AltBackground = H("#2f3240"), Text = H("#f8f8f2"), Muted = H("#8b95c9"),
                Size = H("#8be9fd"), Params = H("#bd93f9"), Quant = H("#f1fa8c"), Family = H("#50fa7b"), Id = H("#a4acd4"),
                Loaded = H("#50fa7b"), Checked = H("#ff79c6"), SelectionBackground = H("#6272a4"), SelectionText = H("#ffffff"),
                BarBackground = H("#21222c"), BarText = H("#f8f8f2"), BarAccent = H("#ff79c6"),
                DialogBackground = H("#343746"), DialogText = H("#f8f8f2"), FieldBackground = H("#21222c"), FieldText = H("#f8f8f2"),
                Error = H("#ff5555"), Success = H("#50fa7b"), Warning = H("#f1fa8c")
            },
            new OsyncTheme
            {
                Name = "Nord",
                Background = H("#2e3440"), AltBackground = H("#343b49"), Text = H("#eceff4"), Muted = H("#9aa5ba"),
                Size = H("#88c0d0"), Params = H("#b48ead"), Quant = H("#ebcb8b"), Family = H("#a3be8c"), Id = H("#a3b3cc"),
                Loaded = H("#a3be8c"), Checked = H("#d08770"), SelectionBackground = H("#4c6a94"), SelectionText = H("#ffffff"),
                BarBackground = H("#3b4252"), BarText = H("#d8dee9"), BarAccent = H("#88c0d0"),
                DialogBackground = H("#3b4252"), DialogText = H("#eceff4"), FieldBackground = H("#2e3440"), FieldText = H("#eceff4"),
                Error = H("#e3808a"), Success = H("#a3be8c"), Warning = H("#ebcb8b")
            },
            new OsyncTheme
            {
                Name = "Tokyo Night",
                Background = H("#1a1b26"), AltBackground = H("#1f2233"), Text = H("#c0caf5"), Muted = H("#8189b5"),
                Size = H("#7aa2f7"), Params = H("#bb9af7"), Quant = H("#e0af68"), Family = H("#7dcfff"), Id = H("#9aa5ce"),
                Loaded = H("#9ece6a"), Checked = H("#ff9e64"), SelectionBackground = H("#3d59a1"), SelectionText = H("#ffffff"),
                BarBackground = H("#16161e"), BarText = H("#a9b1d6"), BarAccent = H("#7aa2f7"),
                DialogBackground = H("#24283b"), DialogText = H("#c0caf5"), FieldBackground = H("#1a1b26"), FieldText = H("#c0caf5"),
                Error = H("#f7768e"), Success = H("#9ece6a"), Warning = H("#e0af68")
            },
            new OsyncTheme
            {
                Name = "Catppuccin Mocha",
                Background = H("#1e1e2e"), AltBackground = H("#252536"), Text = H("#cdd6f4"), Muted = H("#9399b2"),
                Size = H("#89b4fa"), Params = H("#cba6f7"), Quant = H("#f9e2af"), Family = H("#94e2d5"), Id = H("#a6adc8"),
                Loaded = H("#a6e3a1"), Checked = H("#f38ba8"), SelectionBackground = H("#585b70"), SelectionText = H("#ffffff"),
                BarBackground = H("#181825"), BarText = H("#bac2de"), BarAccent = H("#f5c2e7"),
                DialogBackground = H("#313244"), DialogText = H("#cdd6f4"), FieldBackground = H("#1e1e2e"), FieldText = H("#cdd6f4"),
                Error = H("#f38ba8"), Success = H("#a6e3a1"), Warning = H("#f9e2af")
            },
            new OsyncTheme
            {
                Name = "Gruvbox Dark",
                Background = H("#282828"), AltBackground = H("#302e2d"), Text = H("#ebdbb2"), Muted = H("#a89984"),
                Size = H("#83a598"), Params = H("#d3869b"), Quant = H("#fabd2f"), Family = H("#8ec07c"), Id = H("#bdae93"),
                Loaded = H("#b8bb26"), Checked = H("#fe8019"), SelectionBackground = H("#665c54"), SelectionText = H("#fbf1c7"),
                BarBackground = H("#3c3836"), BarText = H("#d5c4a1"), BarAccent = H("#fabd2f"),
                DialogBackground = H("#3c3836"), DialogText = H("#ebdbb2"), FieldBackground = H("#282828"), FieldText = H("#fbf1c7"),
                Error = H("#fb4934"), Success = H("#b8bb26"), Warning = H("#fabd2f")
            },
            new OsyncTheme
            {
                Name = "Monokai",
                Background = H("#272822"), AltBackground = H("#2e2f29"), Text = H("#f8f8f2"), Muted = H("#a59f85"),
                Size = H("#66d9ef"), Params = H("#ae81ff"), Quant = H("#e6db74"), Family = H("#a6e22e"), Id = H("#b0ada0"),
                Loaded = H("#a6e22e"), Checked = H("#f92672"), SelectionBackground = H("#5b5a4e"), SelectionText = H("#ffffff"),
                BarBackground = H("#1e1f1c"), BarText = H("#f8f8f2"), BarAccent = H("#fd971f"),
                DialogBackground = H("#34352d"), DialogText = H("#f8f8f2"), FieldBackground = H("#272822"), FieldText = H("#f8f8f2"),
                Error = H("#f92672"), Success = H("#a6e22e"), Warning = H("#e6db74")
            },
            new OsyncTheme
            {
                Name = "Solarized Dark",
                Background = H("#002b36"), AltBackground = H("#05323e"), Text = H("#eee8d5"), Muted = H("#839496"),
                Size = H("#268bd2"), Params = H("#8d91e0"), Quant = H("#b58900"), Family = H("#2aa198"), Id = H("#93a1a1"),
                Loaded = H("#859900"), Checked = H("#d9541c"), SelectionBackground = H("#1f6fa8"), SelectionText = H("#fdf6e3"),
                BarBackground = H("#073642"), BarText = H("#93a1a1"), BarAccent = H("#b58900"),
                DialogBackground = H("#073642"), DialogText = H("#eee8d5"), FieldBackground = H("#002b36"), FieldText = H("#fdf6e3"),
                Error = H("#f0605a"), Success = H("#859900"), Warning = H("#b58900")
            },
            new OsyncTheme
            {
                Name = "Ocean",
                Background = H("#0b1e3f"), AltBackground = H("#102650"), Text = H("#e6f1ff"), Muted = H("#8fa9d6"),
                Size = H("#64d2ff"), Params = H("#c3a6ff"), Quant = H("#ffd866"), Family = H("#5eead4"), Id = H("#a9bfe3"),
                Loaded = H("#7ee787"), Checked = H("#ff9e64"), SelectionBackground = H("#1f6feb"), SelectionText = H("#ffffff"),
                BarBackground = H("#081630"), BarText = H("#cfe3ff"), BarAccent = H("#64d2ff"),
                DialogBackground = H("#102a54"), DialogText = H("#e6f1ff"), FieldBackground = H("#0b1e3f"), FieldText = H("#ffffff"),
                Error = H("#ff6b6b"), Success = H("#7ee787"), Warning = H("#ffd866")
            },
            new OsyncTheme
            {
                Name = "Matrix",
                Background = H("#000000"), AltBackground = H("#07120a"), Text = H("#4dff7c"), Muted = H("#2fae52"),
                Size = H("#9dffb5"), Params = H("#00d448"), Quant = H("#d2ffd9"), Family = H("#35e36a"), Id = H("#3fbf61"),
                Loaded = H("#ffffff"), Checked = H("#ffe066"), SelectionBackground = H("#1f9e45"), SelectionText = H("#000000"),
                BarBackground = H("#002a0e"), BarText = H("#4dff7c"), BarAccent = H("#d2ffd9"),
                DialogBackground = H("#001f0a"), DialogText = H("#4dff7c"), FieldBackground = H("#000000"), FieldText = H("#b6ffc8"),
                Error = H("#ff5555"), Success = H("#9dffb5"), Warning = H("#ffe066")
            },
            new OsyncTheme
            {
                Name = "High Contrast",
                Background = H("#000000"), AltBackground = H("#121212"), Text = H("#ffffff"), Muted = H("#c8c8c8"),
                Size = H("#00ffff"), Params = H("#ff8cff"), Quant = H("#ffff00"), Family = H("#00ff00"), Id = H("#d0d0d0"),
                Loaded = H("#00ff00"), Checked = H("#ffff00"), SelectionBackground = H("#ffff00"), SelectionText = H("#000000"),
                BarBackground = H("#000000"), BarText = H("#ffffff"), BarAccent = H("#ffff00"),
                DialogBackground = H("#000000"), DialogText = H("#ffffff"), FieldBackground = H("#262626"), FieldText = H("#ffffff"),
                Error = H("#ff5050"), Success = H("#00ff00"), Warning = H("#ffff00")
            },
            new OsyncTheme
            {
                Name = "Classic",
                Background = H("#000000"), AltBackground = H("#000000"), Text = H("#ffffff"), Muted = H("#c0c0c0"),
                Size = H("#00cdcd"), Params = H("#cd00cd"), Quant = H("#cdcd00"), Family = H("#00cd00"), Id = H("#c0c0c0"),
                Loaded = H("#00ff00"), Checked = H("#ffff00"), SelectionBackground = H("#ffffff"), SelectionText = H("#000000"),
                BarBackground = H("#000000"), BarText = H("#ffffff"), BarAccent = H("#00ffff"),
                DialogBackground = H("#0000ee"), DialogText = H("#ffffff"), FieldBackground = H("#000000"), FieldText = H("#ffffff"),
                Error = H("#ffff55"), Success = H("#00ff00"), Warning = H("#ffff00")
            },
            new OsyncTheme
            {
                Name = "One Dark",
                Background = H("#282c34"), AltBackground = H("#2e323b"), Text = H("#dcdfe4"), Muted = H("#8b929e"),
                Size = H("#61afef"), Params = H("#c678dd"), Quant = H("#e5c07b"), Family = H("#56b6c2"), Id = H("#a0a7b4"),
                Loaded = H("#98c379"), Checked = H("#e06c75"), SelectionBackground = H("#3e5f99"), SelectionText = H("#ffffff"),
                BarBackground = H("#21252b"), BarText = H("#abb2bf"), BarAccent = H("#61afef"),
                DialogBackground = H("#323842"), DialogText = H("#dcdfe4"), FieldBackground = H("#1e2227"), FieldText = H("#ffffff"),
                Error = H("#ef7d86"), Success = H("#98c379"), Warning = H("#e5c07b")
            },
            new OsyncTheme
            {
                Name = "GitHub Dark",
                Background = H("#0d1117"), AltBackground = H("#161b22"), Text = H("#e6edf3"), Muted = H("#8d96a0"),
                Size = H("#79c0ff"), Params = H("#d2a8ff"), Quant = H("#e3b341"), Family = H("#56d4dd"), Id = H("#9da7b3"),
                Loaded = H("#56d364"), Checked = H("#ffa657"), SelectionBackground = H("#1f6feb"), SelectionText = H("#ffffff"),
                BarBackground = H("#161b22"), BarText = H("#c9d1d9"), BarAccent = H("#58a6ff"),
                DialogBackground = H("#161b22"), DialogText = H("#e6edf3"), FieldBackground = H("#0d1117"), FieldText = H("#ffffff"),
                Error = H("#ff7b72"), Success = H("#56d364"), Warning = H("#e3b341")
            },
            new OsyncTheme
            {
                Name = "Everforest",
                Background = H("#2d353b"), AltBackground = H("#333c43"), Text = H("#d3c6aa"), Muted = H("#9da9a0"),
                Size = H("#7fbbb3"), Params = H("#d699b6"), Quant = H("#dbbc7f"), Family = H("#83c092"), Id = H("#a7b0a2"),
                Loaded = H("#a7c080"), Checked = H("#e69875"), SelectionBackground = H("#4f6a5a"), SelectionText = H("#fdf6e3"),
                BarBackground = H("#232a2e"), BarText = H("#d3c6aa"), BarAccent = H("#a7c080"),
                DialogBackground = H("#3a454a"), DialogText = H("#d3c6aa"), FieldBackground = H("#232a2e"), FieldText = H("#fdf6e3"),
                Error = H("#f08c86"), Success = H("#a7c080"), Warning = H("#dbbc7f")
            },
            new OsyncTheme
            {
                Name = "Rose Pine",
                Background = H("#191724"), AltBackground = H("#1f1d2e"), Text = H("#e0def4"), Muted = H("#908caa"),
                Size = H("#9ccfd8"), Params = H("#c4a7e7"), Quant = H("#f6c177"), Family = H("#ebbcba"), Id = H("#a8a5c0"),
                Loaded = H("#9ccfd8"), Checked = H("#eb6f92"), SelectionBackground = H("#403d52"), SelectionText = H("#ffffff"),
                BarBackground = H("#1f1d2e"), BarText = H("#e0def4"), BarAccent = H("#ebbcba"),
                DialogBackground = H("#26233a"), DialogText = H("#e0def4"), FieldBackground = H("#191724"), FieldText = H("#e0def4"),
                Error = H("#eb6f92"), Success = H("#9ccfd8"), Warning = H("#f6c177")
            },
            new OsyncTheme
            {
                Name = "Kanagawa",
                Background = H("#1f1f28"), AltBackground = H("#252530"), Text = H("#dcd7ba"), Muted = H("#9a9a8c"),
                Size = H("#7e9cd8"), Params = H("#957fb8"), Quant = H("#e6c384"), Family = H("#7aa89f"), Id = H("#a9a695"),
                Loaded = H("#98bb6c"), Checked = H("#ffa066"), SelectionBackground = H("#2d4f67"), SelectionText = H("#ffffff"),
                BarBackground = H("#16161d"), BarText = H("#c8c093"), BarAccent = H("#7fb4ca"),
                DialogBackground = H("#2a2a37"), DialogText = H("#dcd7ba"), FieldBackground = H("#1f1f28"), FieldText = H("#ffffff"),
                Error = H("#ff7a7a"), Success = H("#98bb6c"), Warning = H("#e6c384")
            },
            new OsyncTheme
            {
                Name = "Ayu Mirage",
                Background = H("#1f2430"), AltBackground = H("#252b38"), Text = H("#cccac2"), Muted = H("#8a9199"),
                Size = H("#73d0ff"), Params = H("#dfbfff"), Quant = H("#ffd173"), Family = H("#95e6cb"), Id = H("#a0a6ad"),
                Loaded = H("#d5ff80"), Checked = H("#ffad66"), SelectionBackground = H("#33415e"), SelectionText = H("#ffffff"),
                BarBackground = H("#171b24"), BarText = H("#cccac2"), BarAccent = H("#ffcc66"),
                DialogBackground = H("#242936"), DialogText = H("#cccac2"), FieldBackground = H("#1a1f29"), FieldText = H("#ffffff"),
                Error = H("#ff6666"), Success = H("#d5ff80"), Warning = H("#ffd173")
            },
            new OsyncTheme
            {
                Name = "Night Owl",
                Background = H("#011627"), AltBackground = H("#0b1f33"), Text = H("#d6deeb"), Muted = H("#8b9eb4"),
                Size = H("#82aaff"), Params = H("#c792ea"), Quant = H("#ffcb8b"), Family = H("#7fdbca"), Id = H("#a0b3c7"),
                Loaded = H("#addb67"), Checked = H("#f78c6c"), SelectionBackground = H("#1d3b53"), SelectionText = H("#ffffff"),
                BarBackground = H("#010e1a"), BarText = H("#d6deeb"), BarAccent = H("#82aaff"),
                DialogBackground = H("#0b2942"), DialogText = H("#d6deeb"), FieldBackground = H("#011627"), FieldText = H("#ffffff"),
                Error = H("#ef5350"), Success = H("#addb67"), Warning = H("#ffcb8b")
            },
            new OsyncTheme
            {
                Name = "Material Ocean",
                Background = H("#0f111a"), AltBackground = H("#161926"), Text = H("#e4e8f4"), Muted = H("#8f93a2"),
                Size = H("#82aaff"), Params = H("#c792ea"), Quant = H("#ffcb6b"), Family = H("#89ddff"), Id = H("#a6aabb"),
                Loaded = H("#c3e88d"), Checked = H("#f78c6c"), SelectionBackground = H("#2c3a6b"), SelectionText = H("#ffffff"),
                BarBackground = H("#090b10"), BarText = H("#a6accd"), BarAccent = H("#84ffff"),
                DialogBackground = H("#1a1c25"), DialogText = H("#e4e8f4"), FieldBackground = H("#0f111a"), FieldText = H("#ffffff"),
                Error = H("#ff5370"), Success = H("#c3e88d"), Warning = H("#ffcb6b")
            },
            new OsyncTheme
            {
                Name = "Synthwave",
                Background = H("#262335"), AltBackground = H("#2d2842"), Text = H("#f2f2f8"), Muted = H("#a59fc0"),
                Size = H("#36f9f6"), Params = H("#ff7edb"), Quant = H("#fede5d"), Family = H("#72f1b8"), Id = H("#b6b1d0"),
                Loaded = H("#72f1b8"), Checked = H("#f97e72"), SelectionBackground = H("#583a82"), SelectionText = H("#ffffff"),
                BarBackground = H("#1e1b2b"), BarText = H("#f2f2f8"), BarAccent = H("#ff7edb"),
                DialogBackground = H("#34294f"), DialogText = H("#f2f2f8"), FieldBackground = H("#1e1b2b"), FieldText = H("#ffffff"),
                Error = H("#fe4450"), Success = H("#72f1b8"), Warning = H("#fede5d")
            },
            new OsyncTheme
            {
                Name = "Palenight",
                Background = H("#292d3e"), AltBackground = H("#2f3447"), Text = H("#e0e4f5"), Muted = H("#9aa0bb"),
                Size = H("#82aaff"), Params = H("#c792ea"), Quant = H("#ffcb6b"), Family = H("#89ddff"), Id = H("#a6accd"),
                Loaded = H("#c3e88d"), Checked = H("#f78c6c"), SelectionBackground = H("#444b77"), SelectionText = H("#ffffff"),
                BarBackground = H("#202331"), BarText = H("#a6accd"), BarAccent = H("#c792ea"),
                DialogBackground = H("#32374d"), DialogText = H("#e0e4f5"), FieldBackground = H("#202331"), FieldText = H("#ffffff"),
                Error = H("#ff7b8b"), Success = H("#c3e88d"), Warning = H("#ffcb6b")
            },
            new OsyncTheme
            {
                Name = "Iceberg",
                Background = H("#161821"), AltBackground = H("#1c1e28"), Text = H("#d2d4de"), Muted = H("#8e93ab"),
                Size = H("#84a0c6"), Params = H("#a093c7"), Quant = H("#e2a478"), Family = H("#89b8c2"), Id = H("#9a9fb6"),
                Loaded = H("#b4be82"), Checked = H("#e27878"), SelectionBackground = H("#2e3c5e"), SelectionText = H("#ffffff"),
                BarBackground = H("#0f1117"), BarText = H("#c6c8d1"), BarAccent = H("#84a0c6"),
                DialogBackground = H("#1e2132"), DialogText = H("#d2d4de"), FieldBackground = H("#161821"), FieldText = H("#ffffff"),
                Error = H("#e98989"), Success = H("#b4be82"), Warning = H("#e2a478")
            },
            new OsyncTheme
            {
                Name = "Zenburn",
                Background = H("#3f3f3f"), AltBackground = H("#464646"), Text = H("#f0efd0"), Muted = H("#b4b4a0"),
                Size = H("#8cd0d3"), Params = H("#dc8cc3"), Quant = H("#f0dfaf"), Family = H("#93e0e3"), Id = H("#c0bfa8"),
                Loaded = H("#bfebbf"), Checked = H("#dfaf8f"), SelectionBackground = H("#2f2f2f"), SelectionText = H("#f0efd0"),
                BarBackground = H("#303030"), BarText = H("#dcdccc"), BarAccent = H("#f0dfaf"),
                DialogBackground = H("#4b4b4b"), DialogText = H("#f0efd0"), FieldBackground = H("#353535"), FieldText = H("#ffffff"),
                Error = H("#ff9b9b"), Success = H("#bfebbf"), Warning = H("#f0dfaf")
            },
            new OsyncTheme
            {
                Name = "Cobalt",
                Background = H("#193549"), AltBackground = H("#1e3d55"), Text = H("#ffffff"), Muted = H("#a7bbcc"),
                Size = H("#9effff"), Params = H("#fb94ff"), Quant = H("#ffc600"), Family = H("#3ad900"), Id = H("#b7c7d6"),
                Loaded = H("#3ad900"), Checked = H("#ff9d00"), SelectionBackground = H("#0050a4"), SelectionText = H("#ffffff"),
                BarBackground = H("#15232d"), BarText = H("#ffffff"), BarAccent = H("#ffc600"),
                DialogBackground = H("#1f4662"), DialogText = H("#ffffff"), FieldBackground = H("#122738"), FieldText = H("#ffffff"),
                Error = H("#ff7a8a"), Success = H("#3ad900"), Warning = H("#ffc600")
            },
            new OsyncTheme
            {
                Name = "Horizon",
                Background = H("#1c1e26"), AltBackground = H("#22242d"), Text = H("#e0e0e3"), Muted = H("#9da0a8"),
                Size = H("#25b0bc"), Params = H("#b877db"), Quant = H("#fab795"), Family = H("#59e1e3"), Id = H("#aeb0b8"),
                Loaded = H("#29d398"), Checked = H("#f09383"), SelectionBackground = H("#3a3d4b"), SelectionText = H("#ffffff"),
                BarBackground = H("#16161c"), BarText = H("#d5d8da"), BarAccent = H("#e95678"),
                DialogBackground = H("#232530"), DialogText = H("#e0e0e3"), FieldBackground = H("#1c1e26"), FieldText = H("#ffffff"),
                Error = H("#f06683"), Success = H("#29d398"), Warning = H("#fab795")
            },
            new OsyncTheme
            {
                Name = "Amber",
                Background = H("#0c0800"), AltBackground = H("#140e02"), Text = H("#ffb000"), Muted = H("#b37b00"),
                Size = H("#ffcc55"), Params = H("#e69500"), Quant = H("#ffe0a0"), Family = H("#ffc233"), Id = H("#c98f14"),
                Loaded = H("#fff2cc"), Checked = H("#ffffff"), SelectionBackground = H("#8a5c00"), SelectionText = H("#fff2cc"),
                BarBackground = H("#1f1500"), BarText = H("#ffb000"), BarAccent = H("#fff2cc"),
                DialogBackground = H("#1a1200"), DialogText = H("#ffb000"), FieldBackground = H("#0c0800"), FieldText = H("#ffd27a"),
                Error = H("#ff7a5c"), Success = H("#fff2cc"), Warning = H("#ffe0a0")
            },
            new OsyncTheme
            {
                Name = "Solarized Light",
                Background = H("#fdf6e3"), AltBackground = H("#f5eed8"), Text = H("#073642"), Muted = H("#5f7278"),
                Size = H("#1f6fa8"), Params = H("#5a5fb8"), Quant = H("#8a6800"), Family = H("#16776f"), Id = H("#586e75"),
                Loaded = H("#5c6b00"), Checked = H("#b8420f"), SelectionBackground = H("#1f6fa8"), SelectionText = H("#fdf6e3"),
                BarBackground = H("#eee8d5"), BarText = H("#3d5259"), BarAccent = H("#b8420f"),
                DialogBackground = H("#eee8d5"), DialogText = H("#073642"), FieldBackground = H("#fdf6e3"), FieldText = H("#073642"),
                Error = H("#c42b28"), Success = H("#5c6b00"), Warning = H("#8a6800")
            },
            new OsyncTheme
            {
                Name = "Light",
                Background = H("#ffffff"), AltBackground = H("#f3f5f8"), Text = H("#1f2328"), Muted = H("#5f6770"),
                Size = H("#0969da"), Params = H("#8250df"), Quant = H("#8a5c00"), Family = H("#1a7f37"), Id = H("#57606a"),
                Loaded = H("#1a7f37"), Checked = H("#cf222e"), SelectionBackground = H("#0969da"), SelectionText = H("#ffffff"),
                BarBackground = H("#e6eaef"), BarText = H("#24292f"), BarAccent = H("#0550ae"),
                DialogBackground = H("#f3f5f8"), DialogText = H("#1f2328"), FieldBackground = H("#ffffff"), FieldText = H("#1f2328"),
                Error = H("#cf222e"), Success = H("#1a7f37"), Warning = H("#8a5c00")
            },
            new OsyncTheme
            {
                Name = "Catppuccin Latte",
                Background = H("#eff1f5"), AltBackground = H("#e6e9ef"), Text = H("#4c4f69"), Muted = H("#6c6f85"),
                Size = H("#1e66f5"), Params = H("#8839ef"), Quant = H("#8a5a00"), Family = H("#137d7c"), Id = H("#5c5f77"),
                Loaded = H("#2c7a1d"), Checked = H("#c53b16"), SelectionBackground = H("#1e66f5"), SelectionText = H("#ffffff"),
                BarBackground = H("#dce0e8"), BarText = H("#4c4f69"), BarAccent = H("#8839ef"),
                DialogBackground = H("#e6e9ef"), DialogText = H("#4c4f69"), FieldBackground = H("#ffffff"), FieldText = H("#4c4f69"),
                Error = H("#c1123a"), Success = H("#2c7a1d"), Warning = H("#8a5a00")
            },
            new OsyncTheme
            {
                Name = "Gruvbox Light",
                Background = H("#fbf1c7"), AltBackground = H("#f2e5bc"), Text = H("#3c3836"), Muted = H("#665c54"),
                Size = H("#076678"), Params = H("#8f3f71"), Quant = H("#8a5a0a"), Family = H("#427b58"), Id = H("#504945"),
                Loaded = H("#5f6b0e"), Checked = H("#af3a03"), SelectionBackground = H("#076678"), SelectionText = H("#fbf1c7"),
                BarBackground = H("#ebdbb2"), BarText = H("#3c3836"), BarAccent = H("#9d0006"),
                DialogBackground = H("#f2e5bc"), DialogText = H("#3c3836"), FieldBackground = H("#fbf1c7"), FieldText = H("#282828"),
                Error = H("#9d0006"), Success = H("#5f6b0e"), Warning = H("#8a5a0a")
            },
            new OsyncTheme
            {
                Name = "One Light",
                Background = H("#fafafa"), AltBackground = H("#f0f0f1"), Text = H("#383a42"), Muted = H("#696c77"),
                Size = H("#2f65d6"), Params = H("#9a33a8"), Quant = H("#8a5d00"), Family = H("#0e7a84"), Id = H("#5c5f6b"),
                Loaded = H("#3a7a35"), Checked = H("#c2410c"), SelectionBackground = H("#2f65d6"), SelectionText = H("#ffffff"),
                BarBackground = H("#eaeaeb"), BarText = H("#383a42"), BarAccent = H("#2f65d6"),
                DialogBackground = H("#f0f0f1"), DialogText = H("#383a42"), FieldBackground = H("#ffffff"), FieldText = H("#383a42"),
                Error = H("#c4302b"), Success = H("#3a7a35"), Warning = H("#8a5d00")
            },
            new OsyncTheme
            {
                Name = "Ayu Light",
                Background = H("#fcfcfc"), AltBackground = H("#f3f4f5"), Text = H("#4c525a"), Muted = H("#6b727b"),
                Size = H("#2c6fb0"), Params = H("#8a4fbf"), Quant = H("#8a5d00"), Family = H("#2f7f6b"), Id = H("#5c6168"),
                Loaded = H("#577a00"), Checked = H("#c2520c"), SelectionBackground = H("#2c6fb0"), SelectionText = H("#ffffff"),
                BarBackground = H("#eef0f2"), BarText = H("#4c525a"), BarAccent = H("#c2520c"),
                DialogBackground = H("#f3f4f5"), DialogText = H("#4c525a"), FieldBackground = H("#ffffff"), FieldText = H("#4c525a"),
                Error = H("#d0303a"), Success = H("#577a00"), Warning = H("#8a5d00")
            },
            new OsyncTheme
            {
                Name = "Rose Pine Dawn",
                Background = H("#faf4ed"), AltBackground = H("#f2e9e1"), Text = H("#464261"), Muted = H("#6e6a86"),
                Size = H("#286983"), Params = H("#7f5a99"), Quant = H("#a15c14"), Family = H("#3f7f8a"), Id = H("#5d5977"),
                Loaded = H("#2d7a60"), Checked = H("#b4637a"), SelectionBackground = H("#286983"), SelectionText = H("#ffffff"),
                BarBackground = H("#f2e9e1"), BarText = H("#464261"), BarAccent = H("#b4637a"),
                DialogBackground = H("#fffaf3"), DialogText = H("#464261"), FieldBackground = H("#ffffff"), FieldText = H("#464261"),
                Error = H("#b4304a"), Success = H("#2d7a60"), Warning = H("#a15c14")
            }
        };

        /// <summary>Theme used without colors (NO_COLOR, TERM=dumb, colorMode none): black and white only.</summary>
        public static readonly OsyncTheme Monochrome = new()
        {
            Name = "Monochrome",
            Background = H("#000000"), AltBackground = H("#000000"), Text = H("#ffffff"), Muted = H("#ffffff"),
            Size = H("#ffffff"), Params = H("#ffffff"), Quant = H("#ffffff"), Family = H("#ffffff"), Id = H("#ffffff"),
            Loaded = H("#ffffff"), Checked = H("#ffffff"),
            SelectionBackground = H("#ffffff"), SelectionText = H("#000000"),
            BarBackground = H("#ffffff"), BarText = H("#000000"), BarAccent = H("#000000"),
            DialogBackground = H("#000000"), DialogText = H("#ffffff"), FieldBackground = H("#ffffff"), FieldText = H("#000000"),
            Error = H("#ffffff"), Success = H("#ffffff"), Warning = H("#ffffff")
        };

        public static OsyncTheme Default => All[0];

        /// <summary>Theme by name (case-insensitive); null or unknown names give the default theme.</summary>
        public static OsyncTheme Find(string? name) =>
            All.FirstOrDefault(t => string.Equals(t.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? Default;

        /// <summary>
        /// Theme by name, number (1-based, as listed by `osync setup`) or loose spelling ("tokyo-night",
        /// "tokyonight"); null when nothing matches.
        /// </summary>
        public static OsyncTheme? FindExact(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            if (int.TryParse(name.Trim(), out var number))
                return number >= 1 && number <= All.Count ? All[number - 1] : null;
            static string Key(string s) => new string(s.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
            var key = Key(name);
            return All.FirstOrDefault(t => Key(t.Name) == key);
        }

        public static int IndexOf(OsyncTheme theme)
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
        public static OsyncTheme Adapt(OsyncTheme theme, ColorDepth depth) => depth switch
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
        public static OsyncTheme ToAnsi16(OsyncTheme theme)
        {
            var o = theme;

            // Backgrounds that carry text must allow readable text; the selected row must stand out from the list
            static bool CarriesText(Rgb bg) => Math.Max(Rgb.Contrast(Ansi16BrightWhite, bg), Rgb.Contrast(Ansi16Black, bg)) >= 4.5;
            var background = Nearest16(o.Background, CarriesText);
            var altBackground = Nearest16(o.AltBackground, CarriesText);
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
                Error = On(o.Error, dialog, 3),
                Success = OnList(o.Success, 3),
                Warning = OnList(o.Warning, 3)
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

        /// <summary>Index (16-255) of <paramref name="color"/> in the xterm-256 palette (cube or gray ramp).</summary>
        public static int Xterm256Index(Rgb color)
        {
            var c = ToXterm256(color);
            if (c.R == c.G && c.G == c.B && Array.IndexOf(CubeLevels, c.R) < 0)
                return 232 + (c.R - 8) / 10;
            return 16 + 36 * Array.IndexOf(CubeLevels, c.R) + 6 * Array.IndexOf(CubeLevels, c.G) + Array.IndexOf(CubeLevels, c.B);
        }

        /// <summary>SGR foreground codes of <see cref="Ansi16"/>: 30-37, then the bright 90-97.</summary>
        internal static int Ansi16ForegroundCode(int index) => index < 8 ? 30 + index : 90 + index - 8;

        /// <summary>Typical terminal backgrounds a shell theme must be readable on.</summary>
        internal static Rgb[] TerminalBackgrounds(bool light) => light
            ? new[] { H("#ffffff"), H("#f5f5f5") }
            : new[] { H("#000000"), H("#1e1e1e") };

        /// <summary>
        /// The colors of command output in the shell for <paramref name="theme"/>: text is drawn on the terminal's
        /// own background (dark for dark themes, light for light themes), so only foreground colors are used.
        /// Returns null for <see cref="ColorDepth.None"/>. At 16 colors every role gets the nearest standard color
        /// that stays readable on the terminal background (never black on a dark terminal, never white on a light one).
        /// </summary>
        public static ShellPalette? ForShell(OsyncTheme theme, ColorDepth depth)
        {
            if (depth == ColorDepth.None) return null;
            var backgrounds = TerminalBackgrounds(theme.IsLight);

            ShellColor Role(Rgb color, double minimum)
            {
                switch (depth)
                {
                    case ColorDepth.Standard16:
                        int best = -1;
                        for (int i = 0; i < Ansi16.Length; i++)
                        {
                            var rendered = Ansi16[i].Rendered;
                            if (backgrounds.Any(bg => Rgb.Contrast(rendered, bg) < minimum)) continue;
                            if (best < 0 || Distance(color, rendered) < Distance(color, Ansi16[best].Rendered)) best = i;
                        }
                        if (best < 0) best = theme.IsLight ? 0 : 15;
                        return new ShellColor(Ansi16[best].Rendered, $"{Ansi16ForegroundCode(best)}");
                    case ColorDepth.Colors256:
                        return new ShellColor(ToXterm256(color), $"38;5;{Xterm256Index(color)}");
                    default:
                        return new ShellColor(color, $"38;2;{color.R};{color.G};{color.B}");
                }
            }

            return new ShellPalette(
                Theme: theme.Name,
                Text: Role(theme.Text, 4.5),
                Heading: Role(theme.BarAccent, 3),
                Muted: Role(theme.Muted, 3),
                Size: Role(theme.Size, 3),
                Params: Role(theme.Params, 3),
                Quant: Role(theme.Quant, 3),
                Family: Role(theme.Family, 3),
                Id: Role(theme.Id, 3),
                Loaded: Role(theme.Loaded, 3),
                Success: Role(theme.Success, 3),
                Warning: Role(theme.Warning, 3),
                Error: Role(theme.Error, 3));
        }

        /// <summary>
        /// Default shell theme: the default dark theme, or the default light one when the terminal says it has a
        /// light background (COLORFGBG, set by rxvt, Konsole and others: "foreground;background", 7 or 15 = light).
        /// </summary>
        public static OsyncTheme DefaultForShell(Func<string, string?> env)
        {
            var colorFgBg = env("COLORFGBG");
            if (!string.IsNullOrWhiteSpace(colorFgBg))
            {
                var last = colorFgBg.Split(';').Last().Trim();
                if (last is "7" or "15") return Find("Light");
            }
            return Default;
        }

        private static int Distance(Rgb a, Rgb b)
        {
            int dr = a.R - b.R, dg = a.G - b.G, db = a.B - b.B;
            // Weighted for perceived brightness (green counts most)
            return 2 * dr * dr + 4 * dg * dg + 3 * db * db;
        }
    }

    /// <summary>A foreground color of shell output: its (approximate) RGB and its SGR parameters.</summary>
    internal readonly record struct ShellColor(Rgb Rgb, string Sgr);

    /// <summary>Shell output colors of a theme at the terminal's color depth (see <see cref="Themes.ForShell"/>).</summary>
    internal sealed record ShellPalette(
        string Theme,
        ShellColor Text,
        ShellColor Heading,
        ShellColor Muted,
        ShellColor Size,
        ShellColor Params,
        ShellColor Quant,
        ShellColor Family,
        ShellColor Id,
        ShellColor Loaded,
        ShellColor Success,
        ShellColor Warning,
        ShellColor Error);
}
