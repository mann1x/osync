using Spectre.Console;

namespace osync
{
    internal enum ColorDepth
    {
        None,
        Standard16,
        Colors256,
        TrueColor
    }

    /// <summary>
    /// Terminal color depth, decided once per run and applied to every console library (Spectre.Console for
    /// command output, Terminal.Gui for `manage`). In order:
    ///   1. OSYNC_COLOR_MODE           none | 16 | 256 | truecolor (aliases: 0, standard, 8bit, 24bit, true)
    ///   2. NO_COLOR (any value)       none (https://no-color.org)
    ///   3. colorMode in settings.json same values as OSYNC_COLOR_MODE; "auto" = detect
    ///   4. detection                  COLORTERM, the original TERM, TERM_PROGRAM, Windows Terminal / VT console
    /// Detection uses the TERM the user had, not the xterm-16color osync sets at startup to keep some
    /// libraries from querying the terminal (which could hang over SSH/tmux).
    /// SSH usually does not forward COLORTERM: terminals that support true color over SSH/tmux are detected as
    /// 256 colors unless COLORTERM=truecolor is passed through or colorMode/OSYNC_COLOR_MODE says truecolor.
    /// </summary>
    internal static class ColorSupport
    {
        private static (ColorDepth Depth, string Reason)? _current;

        public static ColorDepth Current => (_current ??= Detect(Environment.GetEnvironmentVariable, OsyncSettings.Current.ColorMode,
            TerminalInitializer.OriginalTerm, OperatingSystem.IsWindows())).Depth;

        /// <summary>Forgets the detected depth (after the settings changed).</summary>
        public static void Reset() => _current = null;

        /// <summary>Why <see cref="Current"/> was chosen, e.g. "COLORTERM=truecolor".</summary>
        public static string Reason => _current?.Reason ?? (Current.ToString());

        public static string DisplayName(ColorDepth depth) => depth switch
        {
            ColorDepth.TrueColor => "true color (24-bit)",
            ColorDepth.Colors256 => "256 colors",
            ColorDepth.Standard16 => "16 colors",
            _ => "no colors"
        };

        public static ColorDepth? Parse(string? value) => value?.Trim().ToLowerInvariant() switch
        {
            "none" or "0" or "off" or "no" => ColorDepth.None,
            "16" or "standard" or "legacy" or "8" or "ansi" => ColorDepth.Standard16,
            "256" or "8bit" or "eightbit" => ColorDepth.Colors256,
            "truecolor" or "true" or "24bit" or "rgb" => ColorDepth.TrueColor,
            _ => null
        };

        /// <summary>Pure detection logic (unit-tested): environment lookup, settings value, original TERM, OS.</summary>
        internal static (ColorDepth Depth, string Reason) Detect(Func<string, string?> env, string? settingsMode, string? originalTerm, bool isWindows)
        {
            if (Parse(env("OSYNC_COLOR_MODE")) is { } forced)
                return (forced, $"OSYNC_COLOR_MODE={env("OSYNC_COLOR_MODE")}");

            if (!string.IsNullOrEmpty(env("NO_COLOR")))
                return (ColorDepth.None, "NO_COLOR");

            if (Parse(settingsMode) is { } configured)
                return (configured, $"colorMode={settingsMode} in {OsyncSettings.FileName}");

            var colorTerm = (env("COLORTERM") ?? "").Trim().ToLowerInvariant();
            if (colorTerm is "truecolor" or "24bit")
                return (ColorDepth.TrueColor, $"COLORTERM={colorTerm}");

            if (isWindows)
            {
                // Windows Terminal and the Windows 10+ console (VT processing) render 24-bit color
                if (!string.IsNullOrEmpty(env("WT_SESSION")))
                    return (ColorDepth.TrueColor, "Windows Terminal");
                return OperatingSystem.IsWindowsVersionAtLeast(10, 0, 14393)
                    ? (ColorDepth.TrueColor, "Windows 10+ console")
                    : (ColorDepth.Standard16, "legacy Windows console");
            }

            var term = (originalTerm ?? env("TERM") ?? "").Trim().ToLowerInvariant();
            if (term.Length == 0 || term == "dumb")
                return (ColorDepth.None, term.Length == 0 ? "no TERM" : "TERM=dumb");

            var termProgram = (env("TERM_PROGRAM") ?? "").Trim();
            if (termProgram is "iTerm.app" or "WezTerm" or "vscode" or "Hyper" or "ghostty")
                return (ColorDepth.TrueColor, $"TERM_PROGRAM={termProgram}");

            if (term.Contains("direct") || term.Contains("truecolor") || term.Contains("24bit"))
                return (ColorDepth.TrueColor, $"TERM={term}");
            if (term.Contains("256"))
                return (ColorDepth.Colors256, $"TERM={term}");
            if (term == "linux")
                return (ColorDepth.Standard16, "Linux console");
            return (ColorDepth.Standard16, $"TERM={term}");
        }

        /// <summary>Configures Spectre.Console for the detected depth (only when writing to a terminal).</summary>
        public static void ApplyToSpectre()
        {
            if (System.Console.IsOutputRedirected) return;
            AnsiConsole.Profile.Capabilities.ColorSystem = Current switch
            {
                ColorDepth.TrueColor => ColorSystem.TrueColor,
                ColorDepth.Colors256 => ColorSystem.EightBit,
                ColorDepth.Standard16 => ColorSystem.Standard,
                _ => ColorSystem.NoColors
            };
        }
    }
}
