using System.Runtime.InteropServices;
using System.Text;

namespace osync;

/// <summary>
/// Windows console output mode. Spectre.Console turns on VT processing together with DISABLE_NEWLINE_AUTO_RETURN,
/// which makes a bare LF move the cursor down without returning it to column 0: any text that carries '\n'
/// line breaks (a Modelfile from /api/show, the output of an `ollama show` child sharing the console) then
/// prints as a staircase. osync keeps VT processing and gives LF back its carriage return.
/// </summary>
public static class WindowsConsoleMode
{
    internal const uint DisableNewlineAutoReturn = 0x0008;

    /// <summary>The console mode with LF returning the carriage again (everything else unchanged).</summary>
    internal static uint WithNewlineAutoReturn(uint mode) => mode & ~DisableNewlineAutoReturn;

    /// <summary>Clears DISABLE_NEWLINE_AUTO_RETURN on stdout and stderr; call after Spectre.Console set up the console.</summary>
    public static void RestoreNewlineAutoReturn()
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            foreach (var handle in new[] { GetStdHandle(StdOutputHandle), GetStdHandle(StdErrorHandle) })
            {
                if (handle == IntPtr.Zero || handle == new IntPtr(-1)) continue;
                // fails for redirected handles (not a console): nothing to fix there
                if (!GetConsoleMode(handle, out var mode)) continue;
                var fixedMode = WithNewlineAutoReturn(mode);
                if (fixedMode != mode) SetConsoleMode(handle, fixedMode);
            }
        }
        catch
        {
            // best effort: never stop osync over the console mode
        }
    }

    /// <summary>
    /// Switches a Windows console to UTF-8 output for this run and gives it its code page back on exit. Must run
    /// before anything captures <see cref="Console.Out"/> (Spectre.Console's AnsiConsole does on first use).
    /// </summary>
    public static void UseUtf8Output()
    {
        if (!OperatingSystem.IsWindows() || Console.IsOutputRedirected) return;
        try
        {
            var original = Console.OutputEncoding;
            if (original.CodePage == Encoding.UTF8.CodePage) return;
            Console.OutputEncoding = Encoding.UTF8;
            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                try { Console.OutputEncoding = original; } catch { }
            };
        }
        catch
        {
            // best effort: never stop osync over the console code page
        }
    }

    private const int StdOutputHandle = -11;
    private const int StdErrorHandle = -12;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);
}
