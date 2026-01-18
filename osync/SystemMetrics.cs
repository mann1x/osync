using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace osync;

/// <summary>
/// System-wide metrics data
/// </summary>
public class SystemMetricsData
{
    public double CpuUsagePercent { get; set; }
    public long RamUsedBytes { get; set; }
    public long RamTotalBytes { get; set; }
    public long SwapUsedBytes { get; set; }
    public long SwapTotalBytes { get; set; }
    public double[] LoadAverage { get; set; } = Array.Empty<double>(); // Linux only: 1, 5, 15 min
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public double RamUsagePercent => RamTotalBytes > 0 ? (RamUsedBytes * 100.0 / RamTotalBytes) : 0;
    public double SwapUsagePercent => SwapTotalBytes > 0 ? (SwapUsedBytes * 100.0 / SwapTotalBytes) : 0;

    public double RamUsedGB => RamUsedBytes / (1024.0 * 1024 * 1024);
    public double RamTotalGB => RamTotalBytes / (1024.0 * 1024 * 1024);
    public double SwapUsedGB => SwapUsedBytes / (1024.0 * 1024 * 1024);
    public double SwapTotalGB => SwapTotalBytes / (1024.0 * 1024 * 1024);
}

/// <summary>
/// Ollama process metrics (aggregated from all Ollama-related processes)
/// </summary>
public class OllamaProcessMetrics
{
    public int ProcessCount { get; set; }
    public int[] ProcessIds { get; set; } = Array.Empty<int>();
    public string ProcessName { get; set; } = "ollama";
    public double CpuUsagePercent { get; set; }
    public long WorkingSetBytes { get; set; }
    public long PrivateBytes { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public double WorkingSetMB => WorkingSetBytes / (1024.0 * 1024);
    public double WorkingSetGB => WorkingSetBytes / (1024.0 * 1024 * 1024);
    public double PrivateMB => PrivateBytes / (1024.0 * 1024);
}

/// <summary>
/// System metrics collector for CPU, RAM, and Swap
/// </summary>
public class SystemMetricsCollector
{
    // For CPU measurement
    private DateTime _lastCpuSampleTime = DateTime.MinValue;
    private TimeSpan _lastTotalProcessorTime = TimeSpan.Zero;

    // For Ollama CPU measurement (aggregated across all Ollama processes)
    private DateTime _lastOllamaCpuSampleTime = DateTime.MinValue;
    private TimeSpan _lastOllamaTotalCpuTime = TimeSpan.Zero;
    private HashSet<int> _lastOllamaPids = new();

    // Process name patterns that indicate Ollama-related processes
    private static readonly string[] OllamaProcessPatterns = new[]
    {
        "ollama",              // Main binary (ollama serve, ollama run, etc.)
        "ollama_llama_server", // Windows GPU runner
        "ollama-runner",       // Alternative runner name
        "llama-server",        // llama.cpp server spawned by Ollama
        "llama_server",        // Alternative naming
    };

    // Command line patterns for Linux (process names may be truncated)
    private static readonly string[] OllamaCommandPatterns = new[]
    {
        "/ollama",             // Full path containing ollama
        "ollama serve",        // Main server
        "ollama run",          // Running inference
        "ollama_llama_server", // GPU runner
        "llama-server",        // llama.cpp server
        "runner",              // Generic runner subprocess
    };

    // Windows native structures for memory info
    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    /// <summary>
    /// Collects system-wide metrics (CPU, RAM, Swap)
    /// </summary>
    public async Task<SystemMetricsData> GetSystemMetricsAsync()
    {
        var data = new SystemMetricsData { Timestamp = DateTime.UtcNow };

        // Get memory info
        GetMemoryInfo(data);

        // Get CPU usage
        data.CpuUsagePercent = await GetSystemCpuUsageAsync();

        // Get load average on Linux
        if (!OperatingSystem.IsWindows())
        {
            data.LoadAverage = GetLinuxLoadAverage();
        }

        return data;
    }

    private void GetMemoryInfo(SystemMetricsData data)
    {
        if (OperatingSystem.IsWindows())
        {
            var memStatus = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (GlobalMemoryStatusEx(ref memStatus))
            {
                data.RamTotalBytes = (long)memStatus.ullTotalPhys;
                data.RamUsedBytes = (long)(memStatus.ullTotalPhys - memStatus.ullAvailPhys);

                // Swap = PageFile - Physical (Windows uses PageFile for both RAM and Swap)
                var totalSwap = (long)memStatus.ullTotalPageFile - (long)memStatus.ullTotalPhys;
                var availSwap = (long)memStatus.ullAvailPageFile - (long)memStatus.ullAvailPhys;
                data.SwapTotalBytes = Math.Max(0, totalSwap);
                data.SwapUsedBytes = Math.Max(0, totalSwap - availSwap);
            }
        }
        else
        {
            // Linux: read from /proc/meminfo
            try
            {
                var lines = System.IO.File.ReadAllLines("/proc/meminfo");
                long memTotal = 0, memFree = 0, buffers = 0, cached = 0;
                long swapTotal = 0, swapFree = 0;

                foreach (var line in lines)
                {
                    var parts = line.Split(':', StringSplitOptions.TrimEntries);
                    if (parts.Length < 2) continue;

                    var valueStr = parts[1].Split(' ')[0];
                    if (!long.TryParse(valueStr, out var value)) continue;
                    value *= 1024; // Convert from KB to bytes

                    switch (parts[0])
                    {
                        case "MemTotal": memTotal = value; break;
                        case "MemFree": memFree = value; break;
                        case "Buffers": buffers = value; break;
                        case "Cached": cached = value; break;
                        case "SwapTotal": swapTotal = value; break;
                        case "SwapFree": swapFree = value; break;
                    }
                }

                data.RamTotalBytes = memTotal;
                data.RamUsedBytes = memTotal - memFree - buffers - cached;
                data.SwapTotalBytes = swapTotal;
                data.SwapUsedBytes = swapTotal - swapFree;
            }
            catch { }
        }
    }

    private async Task<double> GetSystemCpuUsageAsync()
    {
        if (OperatingSystem.IsWindows())
        {
            return await GetWindowsCpuUsageAsync();
        }
        else
        {
            return GetLinuxCpuUsage();
        }
    }

    private async Task<double> GetWindowsCpuUsageAsync()
    {
        try
        {
            // Use total processor time from all processes
            var allProcesses = Process.GetProcesses();
            var totalTime = TimeSpan.Zero;

            foreach (var proc in allProcesses)
            {
                try
                {
                    totalTime += proc.TotalProcessorTime;
                }
                catch { } // Some processes may not be accessible
                finally
                {
                    proc.Dispose();
                }
            }

            var now = DateTime.UtcNow;

            if (_lastCpuSampleTime != DateTime.MinValue)
            {
                var timeDiff = (now - _lastCpuSampleTime).TotalMilliseconds;
                var cpuDiff = (totalTime - _lastTotalProcessorTime).TotalMilliseconds;

                if (timeDiff > 0)
                {
                    var cpuPercent = cpuDiff / (timeDiff * Environment.ProcessorCount) * 100;
                    _lastCpuSampleTime = now;
                    _lastTotalProcessorTime = totalTime;
                    return Math.Clamp(cpuPercent, 0, 100);
                }
            }

            _lastCpuSampleTime = now;
            _lastTotalProcessorTime = totalTime;

            // First call: wait a bit and sample again
            await Task.Delay(100);
            return await GetWindowsCpuUsageAsync();
        }
        catch
        {
            return 0;
        }
    }

    private double GetLinuxCpuUsage()
    {
        try
        {
            var lines = System.IO.File.ReadAllLines("/proc/stat");
            var cpuLine = lines.FirstOrDefault(l => l.StartsWith("cpu "));
            if (cpuLine == null) return 0;

            var parts = cpuLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 5) return 0;

            // user, nice, system, idle, iowait, irq, softirq
            var user = long.Parse(parts[1]);
            var nice = long.Parse(parts[2]);
            var system = long.Parse(parts[3]);
            var idle = long.Parse(parts[4]);
            var total = user + nice + system + idle;

            // This is a simplified calculation; would need to track deltas for accuracy
            return 100.0 * (user + nice + system) / total;
        }
        catch
        {
            return 0;
        }
    }

    private double[] GetLinuxLoadAverage()
    {
        try
        {
            var content = System.IO.File.ReadAllText("/proc/loadavg");
            var parts = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3)
            {
                return new[]
                {
                    double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
                    double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture),
                    double.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture)
                };
            }
        }
        catch { }
        return Array.Empty<double>();
    }

    /// <summary>
    /// Finds and returns aggregated metrics for all Ollama-related processes
    /// </summary>
    public OllamaProcessMetrics? GetOllamaMetrics()
    {
        try
        {
            // Find all Ollama-related processes
            var ollamaProcesses = FindAllOllamaProcesses();

            if (ollamaProcesses.Count == 0)
            {
                _lastOllamaPids.Clear();
                return null;
            }

            var now = DateTime.UtcNow;
            var currentPids = new HashSet<int>(ollamaProcesses.Select(p => p.Id));

            // Aggregate memory across all processes
            long totalWorkingSet = 0;
            long totalPrivateBytes = 0;
            var totalCpuTime = TimeSpan.Zero;

            foreach (var proc in ollamaProcesses)
            {
                try
                {
                    proc.Refresh();
                    totalWorkingSet += proc.WorkingSet64;
                    totalPrivateBytes += proc.PrivateMemorySize64;
                    totalCpuTime += proc.TotalProcessorTime;
                }
                catch { }
            }

            // Calculate CPU usage
            double cpuPercent = 0;

            // Only calculate delta if we have previous samples and process set is similar
            // (allow some tolerance for processes coming/going)
            if (_lastOllamaCpuSampleTime != DateTime.MinValue && _lastOllamaPids.Count > 0)
            {
                var timeDiff = (now - _lastOllamaCpuSampleTime).TotalMilliseconds;
                var cpuDiff = (totalCpuTime - _lastOllamaTotalCpuTime).TotalMilliseconds;

                if (timeDiff > 0 && cpuDiff >= 0)
                {
                    cpuPercent = cpuDiff / (timeDiff * Environment.ProcessorCount) * 100;
                    cpuPercent = Math.Clamp(cpuPercent, 0, 100);
                }
            }

            // Update tracking state
            _lastOllamaCpuSampleTime = now;
            _lastOllamaTotalCpuTime = totalCpuTime;
            _lastOllamaPids = currentPids;

            // Dispose the process handles
            foreach (var proc in ollamaProcesses)
            {
                proc.Dispose();
            }

            return new OllamaProcessMetrics
            {
                ProcessCount = ollamaProcesses.Count,
                ProcessIds = currentPids.ToArray(),
                ProcessName = "ollama",
                CpuUsagePercent = cpuPercent,
                WorkingSetBytes = totalWorkingSet,
                PrivateBytes = totalPrivateBytes,
                Timestamp = DateTime.UtcNow
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Finds all processes related to Ollama (main process, runners, servers)
    /// </summary>
    private List<Process> FindAllOllamaProcesses()
    {
        var result = new List<Process>();
        var seenPids = new HashSet<int>();

        try
        {
            // Get all processes
            var allProcesses = Process.GetProcesses();

            foreach (var proc in allProcesses)
            {
                try
                {
                    var name = proc.ProcessName.ToLowerInvariant();

                    // Check if process name matches any Ollama pattern
                    bool isOllama = OllamaProcessPatterns.Any(pattern =>
                        name.Contains(pattern.ToLowerInvariant()));

                    // On Linux, also check command line if process name didn't match
                    // (process names can be truncated to 15 chars)
                    if (!isOllama && !OperatingSystem.IsWindows())
                    {
                        isOllama = CheckLinuxProcessCmdline(proc.Id);
                    }

                    if (isOllama && !seenPids.Contains(proc.Id))
                    {
                        seenPids.Add(proc.Id);
                        result.Add(proc);
                    }
                    else
                    {
                        proc.Dispose();
                    }
                }
                catch
                {
                    proc.Dispose();
                }
            }
        }
        catch { }

        return result;
    }

    /// <summary>
    /// Checks if a Linux process is Ollama-related by examining /proc/[pid]/cmdline
    /// </summary>
    private static bool CheckLinuxProcessCmdline(int pid)
    {
        try
        {
            var cmdlinePath = $"/proc/{pid}/cmdline";
            if (!System.IO.File.Exists(cmdlinePath)) return false;

            // cmdline uses null bytes as separators
            var cmdline = System.IO.File.ReadAllText(cmdlinePath)
                .Replace('\0', ' ')
                .ToLowerInvariant();

            return OllamaCommandPatterns.Any(pattern =>
                cmdline.Contains(pattern.ToLowerInvariant()));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Disposes resources
    /// </summary>
    public void Dispose()
    {
        _lastOllamaPids.Clear();
    }
}
