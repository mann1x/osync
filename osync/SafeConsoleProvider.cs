using PowerArgs;

namespace osync
{
    /// <summary>
    /// PowerArgs console provider that never reports a console width of 0.
    ///
    /// On Linux/macOS, Console.BufferWidth/WindowWidth return 0 when output is redirected (pipes, CI,
    /// scripts) or the terminal reports no size. PowerArgs wraps its usage tables to that width, and
    /// with a width &lt;= 0 its word-wrapping loop never terminates (100% CPU, unbounded memory). That made
    /// every "osync &lt;command&gt; -h" and every missing-required-argument error hang forever outside an
    /// interactive terminal. (On Windows the same call throws and PowerArgs falls back to 80 columns.)
    /// </summary>
    internal sealed class SafeConsoleProvider : IConsoleProvider
    {
        private const int FallbackWidth = 120;
        private readonly StdConsoleProvider _inner = new();

        public static void Install() => ConsoleProvider.Current = new SafeConsoleProvider();

        /// <summary>A usable console width: the real one when known, else $COLUMNS, else 120.</summary>
        public static int SafeWidth(int reported)
        {
            if (reported > 0) return reported;
            return int.TryParse(Environment.GetEnvironmentVariable("COLUMNS"), out var columns) && columns > 0
                ? columns
                : FallbackWidth;
        }

        public int BufferWidth
        {
            get => SafeWidth(_inner.BufferWidth);
            set => _inner.BufferWidth = value;
        }

        public int WindowWidth
        {
            get
            {
                int width;
                try { width = _inner.WindowWidth; }
                catch (IOException) { width = 0; }
                return SafeWidth(width);
            }
            set => _inner.WindowWidth = value;
        }

        public bool KeyAvailable => _inner.KeyAvailable;
        public RGB ForegroundColor { get => _inner.ForegroundColor; set => _inner.ForegroundColor = value; }
        public RGB BackgroundColor { get => _inner.BackgroundColor; set => _inner.BackgroundColor = value; }
        public int CursorLeft { get => _inner.CursorLeft; set => _inner.CursorLeft = value; }
        public int CursorTop { get => _inner.CursorTop; set => _inner.CursorTop = value; }
        public int WindowHeight { get => _inner.WindowHeight; set => _inner.WindowHeight = value; }
        public void Write(object output) => _inner.Write(output);
        public void WriteLine(object output) => _inner.WriteLine(output);
        public void Write(ConsoleString consoleString) => _inner.Write(consoleString);
        public void Write(in ConsoleCharacter consoleCharacter) => _inner.Write(in consoleCharacter);
        public void Write(char[] buffer, int length) => _inner.Write(buffer, length);
        public void WriteLine(ConsoleString consoleString) => _inner.WriteLine(consoleString);
        public void WriteLine() => _inner.WriteLine();
        public void Clear() => _inner.Clear();
        public int Read() => _inner.Read();
        public ConsoleKeyInfo ReadKey(bool intercept) => _inner.ReadKey(intercept);
        public ConsoleKeyInfo ReadKey() => _inner.ReadKey();
        public string ReadLine() => _inner.ReadLine();
    }
}
