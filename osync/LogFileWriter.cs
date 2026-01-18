using System.Text;
using System.Text.RegularExpressions;

namespace osync;

/// <summary>
/// Utility class for writing timestamped, sanitized log output to a file.
/// Strips ANSI escape codes and Spectre.Console markup from output.
/// </summary>
public class LogFileWriter : IDisposable
{
    private readonly StreamWriter? _writer;
    private readonly string _filePath;
    private bool _disposed;
    private readonly object _lock = new();

    // Regex patterns for stripping formatting
    private static readonly Regex AnsiEscapePattern = new(@"\x1B\[[0-9;]*[A-Za-z]", RegexOptions.Compiled);
    // Match Spectre.Console markup: [tag], [/], [/tag], [tag attr], [#hex], etc.
    private static readonly Regex SpectreMarkupPattern = new(@"\[/?[a-zA-Z0-9_#\s\-\.]*\]", RegexOptions.Compiled);
    private static readonly Regex SpectreEscapedBrackets = new(@"\[\[|\]\]", RegexOptions.Compiled);

    /// <summary>
    /// Creates a new LogFileWriter that writes to the specified file path.
    /// </summary>
    /// <param name="filePath">Path to the log file. If null or empty, no logging is performed.</param>
    public LogFileWriter(string? filePath)
    {
        _filePath = filePath ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(filePath))
        {
            try
            {
                // Create directory if it doesn't exist
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Open file in append mode with UTF-8 encoding
                _writer = new StreamWriter(filePath, append: true, encoding: Encoding.UTF8)
                {
                    AutoFlush = true
                };

                // Write session header
                var separator = new string('-', 80);
                _writer.WriteLine(separator);
                _writer.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Log session started");
                _writer.WriteLine(separator);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Warning: Could not open log file '{filePath}': {ex.Message}");
                _writer = null;
            }
        }
    }

    /// <summary>
    /// Gets whether logging is enabled (a valid file path was provided and file was opened successfully).
    /// </summary>
    public bool IsEnabled => _writer != null;

    /// <summary>
    /// Gets the log file path.
    /// </summary>
    public string FilePath => _filePath;

    /// <summary>
    /// Writes a line to the log file with timestamp.
    /// </summary>
    /// <param name="message">The message to write (can contain markup/ANSI codes).</param>
    public void WriteLine(string? message = null)
    {
        if (_writer == null || _disposed) return;

        lock (_lock)
        {
            try
            {
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                var sanitized = SanitizeText(message ?? string.Empty);

                if (string.IsNullOrEmpty(sanitized))
                {
                    _writer.WriteLine($"[{timestamp}]");
                }
                else
                {
                    // Handle multi-line messages
                    var lines = sanitized.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                    foreach (var line in lines)
                    {
                        _writer.WriteLine($"[{timestamp}] {line}");
                    }
                }
            }
            catch
            {
                // Silently ignore write errors to not interrupt main process
            }
        }
    }

    /// <summary>
    /// Writes text to the log file without a newline, with timestamp on first write.
    /// </summary>
    /// <param name="message">The message to write.</param>
    public void Write(string? message)
    {
        if (_writer == null || _disposed || string.IsNullOrEmpty(message)) return;

        lock (_lock)
        {
            try
            {
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                var sanitized = SanitizeText(message);
                _writer.Write($"[{timestamp}] {sanitized}");
            }
            catch
            {
                // Silently ignore write errors
            }
        }
    }

    /// <summary>
    /// Writes a formatted line to the log file.
    /// </summary>
    /// <param name="format">Format string.</param>
    /// <param name="args">Format arguments.</param>
    public void WriteLine(string format, params object[] args)
    {
        WriteLine(string.Format(format, args));
    }

    /// <summary>
    /// Writes a log entry with a specific log level prefix.
    /// </summary>
    /// <param name="level">Log level (INFO, WARN, ERROR, etc.).</param>
    /// <param name="message">The message to write.</param>
    public void Log(string level, string message)
    {
        if (_writer == null || _disposed) return;

        lock (_lock)
        {
            try
            {
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                var sanitized = SanitizeText(message);
                _writer.WriteLine($"[{timestamp}] [{level}] {sanitized}");
            }
            catch
            {
                // Silently ignore write errors
            }
        }
    }

    /// <summary>
    /// Logs an informational message.
    /// </summary>
    public void Info(string message) => Log("INFO", message);

    /// <summary>
    /// Logs a warning message.
    /// </summary>
    public void Warn(string message) => Log("WARN", message);

    /// <summary>
    /// Logs an error message.
    /// </summary>
    public void Error(string message) => Log("ERROR", message);

    /// <summary>
    /// Logs an error message with exception details.
    /// </summary>
    public void Error(string message, Exception ex)
    {
        Error($"{message}: {ex.Message}");
        if (ex.StackTrace != null)
        {
            WriteLine($"  Stack trace: {ex.StackTrace}");
        }
    }

    /// <summary>
    /// Strips ANSI escape codes and Spectre.Console markup from text.
    /// </summary>
    /// <param name="text">Text that may contain formatting.</param>
    /// <returns>Plain text with all formatting removed.</returns>
    public static string SanitizeText(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        var result = text;

        // Remove ANSI escape sequences (e.g., \x1B[31m for red)
        result = AnsiEscapePattern.Replace(result, string.Empty);

        // Remove Spectre.Console markup (e.g., [red]text[/], [cyan]text[/], [dim]text[/])
        result = SpectreMarkupPattern.Replace(result, string.Empty);

        // Convert escaped brackets back to single brackets
        result = SpectreEscapedBrackets.Replace(result, m => m.Value == "[[" ? "[" : "]");

        // Remove any remaining control characters except newlines and tabs
        var sb = new StringBuilder(result.Length);
        foreach (var c in result)
        {
            if (c == '\n' || c == '\r' || c == '\t' || !char.IsControl(c))
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Flushes the log file buffer.
    /// </summary>
    public void Flush()
    {
        if (_writer == null || _disposed) return;

        lock (_lock)
        {
            try
            {
                _writer.Flush();
            }
            catch
            {
                // Silently ignore flush errors
            }
        }
    }

    /// <summary>
    /// Writes a session end marker and closes the log file.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;

        lock (_lock)
        {
            if (_writer != null)
            {
                try
                {
                    var separator = new string('-', 80);
                    _writer.WriteLine(separator);
                    _writer.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Log session ended");
                    _writer.WriteLine(separator);
                    _writer.WriteLine();
                    _writer.Dispose();
                }
                catch
                {
                    // Silently ignore close errors
                }
            }
            _disposed = true;
        }
    }
}

/// <summary>
/// Extension methods for dual-output logging (console + file).
/// </summary>
public static class LogFileWriterExtensions
{
    /// <summary>
    /// Writes a line to both console (with markup) and log file (plain text).
    /// </summary>
    public static void MarkupLineAndLog(this LogFileWriter? logger, string markup)
    {
        Spectre.Console.AnsiConsole.MarkupLine(markup);
        logger?.WriteLine(markup);
    }

    /// <summary>
    /// Writes a line to both console (plain) and log file.
    /// </summary>
    public static void WriteLineAndLog(this LogFileWriter? logger, string? message = null)
    {
        Console.WriteLine(message ?? string.Empty);
        logger?.WriteLine(message);
    }
}
