namespace osync
{
    /// <summary>
    /// Colored command output in the shell. Colors come from the shell theme (settings: shell.theme, default: the
    /// default dark theme, or the light one on a light terminal) at the terminal's color depth (<see cref="ColorSupport"/>).
    /// Output is plain text when it is redirected (pipes, files, scripts), with NO_COLOR / colorMode none, with the
    /// "plain" shell theme, or when the terminal does not understand escape sequences.
    /// Only foreground colors and bold are used, so the text stays readable on the terminal's own background.
    /// </summary>
    internal static class Out
    {
        private static readonly object PaletteLock = new();
        private static ShellPalette? _palette;
        private static bool _resolved;

        /// <summary>Shell theme name that turns colors off.</summary>
        public const string PlainTheme = "plain";

        /// <summary>Colors of this run, or null for plain output.</summary>
        public static ShellPalette? Palette
        {
            get
            {
                lock (PaletteLock)
                {
                    if (!_resolved)
                    {
                        _palette = Resolve();
                        _resolved = true;
                    }
                    return _palette;
                }
            }
        }

        /// <summary>Forgets the palette (after the settings changed).</summary>
        public static void Reset()
        {
            lock (PaletteLock)
            {
                _resolved = false;
                _palette = null;
            }
        }

        private static ShellPalette? Resolve()
        {
            try
            {
                if (System.Console.IsOutputRedirected) return null;
                var depth = ColorSupport.Current;
                if (depth == ColorDepth.None) return null;
                // Spectre.Console enables VT processing on Windows and reports whether escape sequences work
                if (!Spectre.Console.AnsiConsole.Profile.Capabilities.Ansi) return null;
                return PaletteFor(OsyncSettings.Current.Shell.Theme, depth);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Shell palette for a theme name (null/empty = default, "plain" = no colors) at <paramref name="depth"/>.</summary>
        public static ShellPalette? PaletteFor(string? themeName, ColorDepth depth)
        {
            if (string.Equals(themeName?.Trim(), PlainTheme, StringComparison.OrdinalIgnoreCase)) return null;
            var theme = string.IsNullOrWhiteSpace(themeName)
                ? Themes.DefaultForShell(Environment.GetEnvironmentVariable)
                : Themes.Find(themeName);
            return Themes.ForShell(theme, depth);
        }

        /// <summary><paramref name="text"/> in the color of <paramref name="role"/> (plain without colors).</summary>
        public static string Paint(string text, Func<ShellPalette, ShellColor> role, bool bold = false) =>
            Paint(Palette, text, role, bold);

        public static string Paint(ShellPalette? palette, string text, Func<ShellPalette, ShellColor> role, bool bold = false)
        {
            if (palette == null || text.Length == 0) return text;
            return $"\u001b[{(bold ? "1;" : "")}{role(palette).Sgr}m{text}\u001b[0m";
        }

        /// <summary>Bold text (plain without colors).</summary>
        public static string Bold(string text) => Palette == null || text.Length == 0 ? text : $"\u001b[1m{text}\u001b[0m";

        public static string Heading(string text) => Paint(text, p => p.Heading, bold: true);
        public static string Muted(string text) => Paint(text, p => p.Muted);
        public static string Model(string text) => Paint(text, p => p.Text, bold: true);
        public static string Server(string text) => Paint(text, p => p.Family);
        public static string Number(string text) => Paint(text, p => p.Size);

        public static void WriteLine(string text = "") => System.Console.WriteLine(text);

        /// <summary>Writes a message line; leading newlines of <paramref name="message"/> go before the label.</summary>
        private static void Message(string label, Func<ShellPalette, ShellColor> role, string message, bool paintMessage)
        {
            var body = message.TrimStart('\n');
            var blankLines = message.Length - body.Length;
            System.Console.Write(new string('\n', blankLines));
            WriteLine(Paint(label, role, bold: true) + " " + (paintMessage ? Paint(body, role) : body));
        }

        /// <summary>"Error: message", in the error color.</summary>
        public static void Error(string message) => Message("Error:", p => p.Error, message, paintMessage: true);

        /// <summary>"Warning: message", with the label in the warning color.</summary>
        public static void Warning(string message) => Message("Warning:", p => p.Warning, message, paintMessage: false);

        /// <summary>"✓ message", with the check mark in the success color.</summary>
        public static void Success(string message) => Message("✓", p => p.Success, message, paintMessage: false);

        /// <summary>"✗ message", with the cross in the error color.</summary>
        public static void Failure(string message) => Message("✗", p => p.Error, message, paintMessage: false);

        private static readonly System.Text.RegularExpressions.Regex QuotedOrUrl =
            new(@"'(?<model>[^']+)'|(?<url>https?://[^\s'""]+?)(?=\.{2,}|[\s,;)]|$)", System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>
        /// A progress/status line: 'quoted' model names and server URLs are highlighted, a leading "Successfully"
        /// is in the success color.
        /// </summary>
        public static string Status(string message) => Status(Palette, message);

        public static string Status(ShellPalette? palette, string message)
        {
            if (palette == null) return message;
            var text = QuotedOrUrl.Replace(message, m => m.Groups["model"].Success
                ? "'" + Paint(palette, m.Groups["model"].Value, p => p.Text, bold: true) + "'"
                : Paint(palette, m.Groups["url"].Value, p => p.Family));
            const string successfully = "Successfully";
            if (text.StartsWith(successfully, StringComparison.Ordinal))
                text = Paint(palette, successfully, p => p.Success, bold: true) + text[successfully.Length..];
            return text;
        }

        /// <summary>Writes a <see cref="Status"/> line; leading newlines of <paramref name="message"/> stay in front.</summary>
        public static void StatusLine(string message) => WriteLine(Status(message));

        /// <summary>A "label: value" line with the label highlighted.</summary>
        public static void Field(string label, string value) => WriteLine(Heading(label + ":") + " " + value);
    }
}
