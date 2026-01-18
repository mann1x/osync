using System;
using System.Collections.Generic;
using System.Text;

namespace osync;

/// <summary>
/// Renders time-series data as a braille dot graph for terminal display.
/// Uses Unicode braille characters (U+2800 to U+28FF) for 2x4 dot resolution per character.
/// </summary>
public class BrailleGraph
{
    // Braille base character (empty pattern)
    private const char BrailleBase = '\u2800';

    // Braille dot positions (2 columns x 4 rows per character)
    // Dot pattern values:
    // [0,0]=1  [1,0]=8
    // [0,1]=2  [1,1]=16
    // [0,2]=4  [1,2]=32
    // [0,3]=64 [1,3]=128
    private static readonly int[,] DotValues = {
        { 1, 2, 4, 64 },     // Left column (x=0)
        { 8, 16, 32, 128 }   // Right column (x=1)
    };

    private readonly int _widthChars;
    private readonly int _heightChars;
    private readonly int _maxDataPoints;
    private readonly object _lock = new();
    private readonly Queue<(double value, string color)> _data = new();
    private double _minValue;
    private double _maxValue;
    private bool _autoScale;

    /// <summary>
    /// Creates a new braille graph renderer.
    /// </summary>
    /// <param name="widthChars">Width in characters (each char = 2 pixels wide)</param>
    /// <param name="heightChars">Height in characters (each char = 4 pixels tall)</param>
    /// <param name="minValue">Minimum value for Y-axis (default: 0)</param>
    /// <param name="maxValue">Maximum value for Y-axis (default: 100)</param>
    /// <param name="autoScale">Auto-scale Y-axis based on data (default: false)</param>
    public BrailleGraph(int widthChars, int heightChars, double minValue = 0, double maxValue = 100, bool autoScale = false)
    {
        _widthChars = Math.Max(1, widthChars);
        _heightChars = Math.Max(1, heightChars);
        _maxDataPoints = _widthChars * 2; // 2 pixels per character width
        _minValue = minValue;
        _maxValue = maxValue;
        _autoScale = autoScale;
    }

    /// <summary>Width in pixels (2 per character)</summary>
    public int PixelWidth => _widthChars * 2;

    /// <summary>Height in pixels (4 per character)</summary>
    public int PixelHeight => _heightChars * 4;

    /// <summary>Number of data points currently stored</summary>
    public int DataCount
    {
        get { lock (_lock) return _data.Count; }
    }

    /// <summary>Current minimum value</summary>
    public double MinValue => _minValue;

    /// <summary>Current maximum value</summary>
    public double MaxValue => _maxValue;

    /// <summary>
    /// Adds a new data point to the graph with color based on value.
    /// Oldest data is removed when the graph is full.
    /// </summary>
    public void AddDataPoint(double value)
    {
        // Auto-determine color based on percentage (0-100 scale)
        var color = value < 50 ? "green" : value < 80 ? "yellow" : "red";
        AddDataPoint(value, color);
    }

    /// <summary>
    /// Adds a new data point to the graph with specified color.
    /// Oldest data is removed when the graph is full.
    /// </summary>
    public void AddDataPoint(double value, string color)
    {
        lock (_lock)
        {
            // Ensure color is never empty - default based on value if needed
            if (string.IsNullOrEmpty(color))
                color = value < 50 ? "green" : value < 80 ? "yellow" : "red";

            _data.Enqueue((value, color));
            while (_data.Count > _maxDataPoints)
                _data.Dequeue();

            if (_autoScale && _data.Count > 0)
            {
                _minValue = double.MaxValue;
                _maxValue = double.MinValue;
                foreach (var (v, _) in _data)
                {
                    if (v < _minValue) _minValue = v;
                    if (v > _maxValue) _maxValue = v;
                }
                // Add 10% padding
                var range = _maxValue - _minValue;
                if (range < 0.01) range = 1;
                _minValue = Math.Max(0, _minValue - range * 0.1);
                _maxValue = _maxValue + range * 0.1;
            }
        }
    }

    /// <summary>
    /// Clears all data from the graph.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _data.Clear();
        }
    }

    /// <summary>
    /// Renders the graph as an array of strings (one per line).
    /// Oldest data on left, newest on right.
    /// Uses area-fill style (filled from bottom to data point).
    /// </summary>
    public string[] Render()
    {
        return Render(_widthChars);
    }

    /// <summary>
    /// Renders the graph at a target width (in characters).
    /// Data is always right-aligned to the rightmost edge.
    /// </summary>
    public string[] Render(int targetWidthChars)
    {
        // Default: no scaling, 1 data point = 1 pixel
        return Render(targetWidthChars, 0);
    }

    /// <summary>
    /// Renders the graph at a target width with scaling to fill the width.
    /// </summary>
    /// <param name="targetWidthChars">Width in characters</param>
    /// <param name="expectedDataPoints">Expected data points for full history (0 = no scaling)</param>
    public string[] Render(int targetWidthChars, int expectedDataPoints)
    {
        lock (_lock)
        {
            var lines = new string[_heightChars];
            var dataArray = _data.ToArray();

            // Use target width for rendering
            int renderWidthChars = Math.Max(1, targetWidthChars);
            int pixelWidth = renderWidthChars * 2;
            int pixelHeight = _heightChars * 4;

            if (dataArray.Length == 0)
            {
                // Return empty graph at target width
                var emptyLine = new string(BrailleBase, renderWidthChars);
                for (int i = 0; i < _heightChars; i++)
                    lines[i] = $"[dim]{emptyLine}[/]";
                return lines;
            }

            // Calculate value range
            double range = _maxValue - _minValue;
            if (range < 0.001) range = 1;

            // Create pixel buffer and color buffer at render width
            var pixels = new bool[pixelWidth, pixelHeight];
            var pixelColors = new string?[pixelWidth];

            // Calculate scaling factor
            // If expectedDataPoints > 0, scale so that expectedDataPoints would fill the entire width
            // Otherwise, 1 data point = 1 pixel
            double pixelsPerDataPoint;
            if (expectedDataPoints > 0)
            {
                // Scale so the expected data points fill the entire width
                pixelsPerDataPoint = (double)pixelWidth / expectedDataPoints;
            }
            else
            {
                pixelsPerDataPoint = 1.0;
            }

            // Calculate how much width our actual data will occupy
            double dataPixelWidth = dataArray.Length * pixelsPerDataPoint;

            // Start position - right-aligned
            // If we have less data than expected, it won't fill the full width (which is correct)
            double startX = pixelWidth - dataPixelWidth;

            for (int i = 0; i < dataArray.Length; i++)
            {
                var (value, color) = dataArray[i];

                // Calculate the pixel range this data point covers
                double xStart = startX + i * pixelsPerDataPoint;
                double xEnd = startX + (i + 1) * pixelsPerDataPoint;

                // Clamp to valid pixel range
                int xStartInt = Math.Max(0, (int)Math.Floor(xStart));
                int xEndInt = Math.Min(pixelWidth, (int)Math.Ceiling(xEnd));

                // Y position (0 = top, pixelHeight-1 = bottom)
                double normalized = (value - _minValue) / range;
                normalized = Math.Clamp(normalized, 0, 1);
                int y = pixelHeight - 1 - (int)(normalized * (pixelHeight - 1));
                y = Math.Clamp(y, 0, pixelHeight - 1);

                // Fill all pixels that this data point covers
                for (int x = xStartInt; x < xEndInt; x++)
                {
                    pixelColors[x] = color;
                    // Fill column from bottom to data point (area graph style)
                    for (int fillY = y; fillY < pixelHeight; fillY++)
                    {
                        pixels[x, fillY] = true;
                    }
                }
            }

            // Convert pixel buffer to braille characters with colors
            for (int row = 0; row < _heightChars; row++)
            {
                var sb = new StringBuilder();
                for (int col = 0; col < renderWidthChars; col++)
                {
                    int charValue = 0;
                    bool hasAnyPixel = false;

                    // Map 2x4 pixel block to braille dots
                    for (int dx = 0; dx < 2; dx++)
                    {
                        for (int dy = 0; dy < 4; dy++)
                        {
                            int px = col * 2 + dx;
                            int py = row * 4 + dy;
                            if (px < pixelWidth && py < pixelHeight && pixels[px, py])
                            {
                                charValue += DotValues[dx, dy];
                                hasAnyPixel = true;
                            }
                        }
                    }

                    // Get the color for this character (prefer left pixel's color)
                    int leftPx = col * 2;
                    int rightPx = col * 2 + 1;
                    string? charColor = null;
                    if (leftPx < pixelWidth)
                        charColor = pixelColors[leftPx];
                    // If left pixel has no color or empty color, try right pixel
                    if (string.IsNullOrEmpty(charColor) && rightPx < pixelWidth)
                        charColor = pixelColors[rightPx];

                    char brailleChar = (char)(BrailleBase + charValue);
                    if (hasAnyPixel && !string.IsNullOrEmpty(charColor))
                        sb.Append($"[{charColor}]{brailleChar}[/]");
                    else
                        sb.Append($"[dim]{brailleChar}[/]");
                }
                lines[row] = sb.ToString();
            }

            return lines;
        }
    }

    /// <summary>
    /// Renders the graph at specified width and height with expected data points for scaling.
    /// Height can be overridden from the construction-time height for dynamic resizing.
    /// </summary>
    /// <param name="targetWidthChars">Width in characters</param>
    /// <param name="expectedDataPoints">Expected data points for full history (0 = no scaling)</param>
    /// <param name="heightChars">Height in characters (overrides construction height)</param>
    public string[] Render(int targetWidthChars, int expectedDataPoints, int heightChars)
    {
        lock (_lock)
        {
            heightChars = Math.Max(1, heightChars);
            var lines = new string[heightChars];
            var dataArray = _data.ToArray();

            // Use target width for rendering
            int renderWidthChars = Math.Max(1, targetWidthChars);
            int pixelWidth = renderWidthChars * 2;
            int pixelHeight = heightChars * 4;

            if (dataArray.Length == 0)
            {
                // Return empty graph at target width
                var emptyLine = new string(BrailleBase, renderWidthChars);
                for (int i = 0; i < heightChars; i++)
                    lines[i] = $"[dim]{emptyLine}[/]";
                return lines;
            }

            // Calculate value range
            double range = _maxValue - _minValue;
            if (range < 0.001) range = 1;

            // Create pixel buffer and color buffer at render width
            var pixels = new bool[pixelWidth, pixelHeight];
            var pixelColors = new string?[pixelWidth];

            // Calculate scaling factor
            double pixelsPerDataPoint;
            if (expectedDataPoints > 0)
            {
                pixelsPerDataPoint = (double)pixelWidth / expectedDataPoints;
            }
            else
            {
                pixelsPerDataPoint = 1.0;
            }

            // Calculate how much width our actual data will occupy
            double dataPixelWidth = dataArray.Length * pixelsPerDataPoint;

            // Start position - right-aligned
            double startX = pixelWidth - dataPixelWidth;

            for (int i = 0; i < dataArray.Length; i++)
            {
                var (value, color) = dataArray[i];

                // Calculate the pixel range this data point covers
                double xStart = startX + i * pixelsPerDataPoint;
                double xEnd = startX + (i + 1) * pixelsPerDataPoint;

                // Clamp to valid pixel range
                int xStartInt = Math.Max(0, (int)Math.Floor(xStart));
                int xEndInt = Math.Min(pixelWidth, (int)Math.Ceiling(xEnd));

                // Y position (0 = top, pixelHeight-1 = bottom)
                double normalized = (value - _minValue) / range;
                normalized = Math.Clamp(normalized, 0, 1);
                int y = pixelHeight - 1 - (int)(normalized * (pixelHeight - 1));
                y = Math.Clamp(y, 0, pixelHeight - 1);

                // Fill all pixels that this data point covers
                for (int x = xStartInt; x < xEndInt; x++)
                {
                    pixelColors[x] = color;
                    // Fill column from bottom to data point (area graph style)
                    for (int fillY = y; fillY < pixelHeight; fillY++)
                    {
                        pixels[x, fillY] = true;
                    }
                }
            }

            // Convert pixel buffer to braille characters with colors
            for (int row = 0; row < heightChars; row++)
            {
                var sb = new StringBuilder();
                for (int col = 0; col < renderWidthChars; col++)
                {
                    int charValue = 0;
                    bool hasAnyPixel = false;

                    // Map 2x4 pixel block to braille dots
                    for (int dx = 0; dx < 2; dx++)
                    {
                        for (int dy = 0; dy < 4; dy++)
                        {
                            int px = col * 2 + dx;
                            int py = row * 4 + dy;
                            if (px < pixelWidth && py < pixelHeight && pixels[px, py])
                            {
                                charValue += DotValues[dx, dy];
                                hasAnyPixel = true;
                            }
                        }
                    }

                    // Get the color for this character (prefer left pixel's color)
                    int leftPx = col * 2;
                    int rightPx = col * 2 + 1;
                    string? charColor = null;
                    if (leftPx < pixelWidth)
                        charColor = pixelColors[leftPx];
                    if (string.IsNullOrEmpty(charColor) && rightPx < pixelWidth)
                        charColor = pixelColors[rightPx];

                    char brailleChar = (char)(BrailleBase + charValue);
                    if (hasAnyPixel && !string.IsNullOrEmpty(charColor))
                        sb.Append($"[{charColor}]{brailleChar}[/]");
                    else if (hasAnyPixel)
                        sb.Append(brailleChar);
                    else
                        sb.Append($"[dim]{brailleChar}[/]");
                }
                lines[row] = sb.ToString();
            }

            return lines;
        }
    }

    /// <summary>
    /// Renders the graph as a single string with newlines between rows.
    /// </summary>
    public string RenderString()
    {
        return string.Join("\n", Render());
    }

    /// <summary>
    /// Renders a single row of the graph.
    /// </summary>
    public string RenderLine(int row)
    {
        var lines = Render();
        if (row >= 0 && row < lines.Length)
            return lines[row];
        return new string(BrailleBase, _widthChars);
    }

    /// <summary>
    /// Gets the latest value in the graph.
    /// </summary>
    public double? GetLatestValue()
    {
        lock (_lock)
        {
            if (_data.Count == 0) return null;
            var array = _data.ToArray();
            return array[^1].value;
        }
    }
}

/// <summary>
/// Renders a horizontal bar using block characters.
/// </summary>
public static class BarGraph
{
    // Block characters for smooth bar rendering (1/8 increments)
    private static readonly char[] BlockChars = { ' ', '▏', '▎', '▍', '▌', '▋', '▊', '▉', '█' };

    // ANSI color codes
    private const string AnsiReset = "\x1b[0m";
    private const string AnsiGreen = "\x1b[38;5;2m";
    private const string AnsiYellow = "\x1b[38;5;3m";
    private const string AnsiRed = "\x1b[38;5;1m";
    private const string AnsiCyan = "\x1b[38;5;6m";
    private const string AnsiBrightGreen = "\x1b[38;5;10m";
    private const string AnsiBrightYellow = "\x1b[38;5;11m";
    private const string AnsiBrightRed = "\x1b[38;5;9m";
    private const string AnsiDimGray = "\x1b[38;5;8m";

    /// <summary>
    /// Gets the appropriate ANSI color for a percentage value.
    /// Green for low values, yellow for medium, red for high.
    /// </summary>
    public static string GetColorForPercent(double percent, bool inverted = false)
    {
        if (inverted)
        {
            // For metrics where higher is worse (like temp, usage)
            if (percent < 50) return AnsiBrightGreen;
            if (percent < 80) return AnsiBrightYellow;
            return AnsiBrightRed;
        }
        else
        {
            // For metrics where higher is better (or neutral)
            if (percent < 50) return AnsiBrightGreen;
            if (percent < 80) return AnsiBrightYellow;
            return AnsiBrightRed;
        }
    }

    /// <summary>
    /// Renders a horizontal bar showing a percentage.
    /// </summary>
    /// <param name="percent">Value 0-100</param>
    /// <param name="width">Width in characters</param>
    /// <param name="filled">Character for filled portion (default: '=')</param>
    /// <param name="empty">Character for empty portion (default: '-')</param>
    /// <returns>String like "[====-----]"</returns>
    public static string RenderBar(double percent, int width, char filled = '=', char empty = '-')
    {
        percent = Math.Clamp(percent, 0, 100);
        int innerWidth = width - 2; // Account for brackets
        int filledCount = (int)Math.Round(percent / 100.0 * innerWidth);

        var sb = new StringBuilder();
        sb.Append('[');
        sb.Append(new string(filled, filledCount));
        sb.Append(new string(empty, innerWidth - filledCount));
        sb.Append(']');
        return sb.ToString();
    }

    /// <summary>
    /// Renders a horizontal bar using Unicode block characters for smooth appearance.
    /// </summary>
    /// <param name="percent">Value 0-100</param>
    /// <param name="width">Width in characters (including brackets)</param>
    /// <returns>String like "[████▌    ]"</returns>
    public static string RenderBlockBar(double percent, int width)
    {
        percent = Math.Clamp(percent, 0, 100);
        int innerWidth = width - 2; // Account for brackets

        double fillAmount = percent / 100.0 * innerWidth;
        int fullBlocks = (int)fillAmount;
        double remainder = fillAmount - fullBlocks;
        int partialIndex = (int)Math.Round(remainder * 8);

        var sb = new StringBuilder();
        sb.Append('[');

        // Full blocks
        sb.Append(new string('█', fullBlocks));

        // Partial block
        if (fullBlocks < innerWidth && partialIndex > 0)
        {
            sb.Append(BlockChars[partialIndex]);
            fullBlocks++;
        }

        // Empty space
        sb.Append(new string(' ', innerWidth - fullBlocks));

        sb.Append(']');
        return sb.ToString();
    }

    /// <summary>
    /// Renders a colored horizontal bar using Unicode block characters.
    /// Color changes based on fill percentage (green->yellow->red).
    /// </summary>
    /// <param name="percent">Value 0-100</param>
    /// <param name="width">Width in characters (including brackets)</param>
    /// <returns>String with ANSI color codes</returns>
    public static string RenderColoredBlockBar(double percent, int width)
    {
        percent = Math.Clamp(percent, 0, 100);
        int innerWidth = width - 2; // Account for brackets

        double fillAmount = percent / 100.0 * innerWidth;
        int fullBlocks = (int)fillAmount;
        double remainder = fillAmount - fullBlocks;
        int partialIndex = (int)Math.Round(remainder * 8);

        var color = GetColorForPercent(percent);
        var sb = new StringBuilder();
        sb.Append(AnsiDimGray);
        sb.Append('[');
        sb.Append(color);

        // Full blocks
        sb.Append(new string('█', fullBlocks));

        // Partial block
        if (fullBlocks < innerWidth && partialIndex > 0)
        {
            sb.Append(BlockChars[partialIndex]);
            fullBlocks++;
        }

        sb.Append(AnsiDimGray);
        // Empty space
        sb.Append(new string(' ', innerWidth - fullBlocks));

        sb.Append(']');
        sb.Append(AnsiReset);
        return sb.ToString();
    }

    /// <summary>
    /// Renders a braille graph string with color based on average value.
    /// </summary>
    public static string ColorizeGraph(string graphStr, double currentPercent)
    {
        var color = GetColorForPercent(currentPercent);
        return $"{color}{graphStr}{AnsiReset}";
    }
}
