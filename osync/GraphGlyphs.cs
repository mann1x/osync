using System;
using System.Runtime.InteropServices;

namespace osync;

/// <summary>Characters the monitor graphs are drawn with.</summary>
public enum GraphStyle
{
    /// <summary>Braille dots (U+2800-U+28FF): 2x4 dots per character.</summary>
    Braille,
    /// <summary>Full, lower and upper half blocks: 1x2 per character, in every console font.</summary>
    Blocks
}

/// <summary>
/// Picks the graph glyphs the terminal can show. The classic Windows console (conhost) has no
/// font fallback and its default fonts (Consolas, Lucida Console, raster) have no braille, so
/// braille graphs turn into rows of boxes there; it gets block graphs instead.
/// Order:
///   1. OSYNC_GRAPH=braille|blocks
///   2. not Windows                 braille
///   3. a terminal with font fallback (Windows Terminal, VS Code, ConEmu, WezTerm, Alacritty, mintty...)
///   4. a console font with braille (Cascadia, DejaVu Sans Mono, Iosevka)
///   5. otherwise                   blocks
/// </summary>
public static class GraphGlyphs
{
    private static (GraphStyle Style, string Reason)? _current;

    public static GraphStyle Current => (_current ??= Detect(Environment.GetEnvironmentVariable,
        OperatingSystem.IsWindows(), OperatingSystem.IsWindows() ? ConsoleFontFace() : null)).Style;

    public static string Reason => _current?.Reason ?? Current.ToString();

    public static GraphStyle? Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "braille" or "dots" => GraphStyle.Braille,
        "blocks" or "block" => GraphStyle.Blocks,
        _ => null
    };

    // Console fonts that have the braille block (conhost draws only what the font has)
    private static readonly string[] BrailleFonts = { "Cascadia", "DejaVu Sans Mono", "Iosevka" };

    internal static (GraphStyle Style, string Reason) Detect(Func<string, string?> env, bool isWindows, string? consoleFont)
    {
        var forced = Parse(env("OSYNC_GRAPH"));
        if (forced.HasValue)
            return (forced.Value, "OSYNC_GRAPH");

        if (!isWindows)
            return (GraphStyle.Braille, "not Windows");

        if (!string.IsNullOrEmpty(env("WT_SESSION")))
            return (GraphStyle.Braille, "Windows Terminal");
        var termProgram = env("TERM_PROGRAM");
        if (!string.IsNullOrWhiteSpace(termProgram))
            return (GraphStyle.Braille, $"TERM_PROGRAM={termProgram.Trim()}");
        foreach (var name in new[] { "ConEmuANSI", "WEZTERM_EXECUTABLE", "ALACRITTY_WINDOW_ID", "ALACRITTY_LOG" })
        {
            if (!string.IsNullOrEmpty(env(name)))
                return (GraphStyle.Braille, name);
        }

        if (!string.IsNullOrWhiteSpace(consoleFont))
        {
            foreach (var font in BrailleFonts)
            {
                if (consoleFont.Contains(font, StringComparison.OrdinalIgnoreCase))
                    return (GraphStyle.Braille, $"console font {consoleFont}");
            }
            return (GraphStyle.Blocks, $"console font {consoleFont} has no braille");
        }

        return (GraphStyle.Blocks, "classic Windows console");
    }

    /// <summary>Face name of the console window's font, or null (not a console, or not Windows).</summary>
    private static string? ConsoleFontFace()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            var handle = GetStdHandle(StdOutputHandle);
            if (handle == IntPtr.Zero || handle == new IntPtr(-1)) return null;
            var info = new ConsoleFontInfoEx { cbSize = (uint)Marshal.SizeOf<ConsoleFontInfoEx>() };
            return GetCurrentConsoleFontEx(handle, false, ref info) ? info.FaceName?.Trim() : null;
        }
        catch
        {
            return null;
        }
    }

    private const int StdOutputHandle = -11;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ConsoleFontInfoEx
    {
        public uint cbSize;
        public uint nFont;
        public short FontWidth;
        public short FontHeight;
        public int FontFamily;
        public int FontWeight;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string FaceName;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool GetCurrentConsoleFontEx(IntPtr hConsoleOutput, bool bMaximumWindow, ref ConsoleFontInfoEx lpConsoleCurrentFontEx);
}
