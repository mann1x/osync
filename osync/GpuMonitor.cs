using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using NvAPIWrapper;
using NvAPIWrapper.GPU;

namespace osync;

public enum GpuVendor { None, Nvidia, Amd, Intel }

/// <summary>
/// Comprehensive GPU metrics data
/// </summary>
public class GpuMetrics
{
    // Identity
    public int Index { get; set; }
    public string Name { get; set; } = "";
    public GpuVendor Vendor { get; set; }

    // Utilization
    public double ComputeUtilization { get; set; }  // 0-100%

    // Memory
    public double VramUsedMB { get; set; }
    public double VramTotalMB { get; set; }
    public int VramClockMHz { get; set; }
    public int VramClockMaxMHz { get; set; }
    public double VramUtilization => VramTotalMB > 0 ? (VramUsedMB / VramTotalMB) * 100 : 0;

    // Clocks
    public int ClockSpeedMHz { get; set; }
    public int ClockSpeedMaxMHz { get; set; }

    // Thermal
    public int Temperature { get; set; }
    public int TemperatureLimit { get; set; }
    public int HotspotTemperature { get; set; }
    public int FanSpeedPercent { get; set; }

    // Power
    public double PowerDrawW { get; set; }
    public double PowerLimitW { get; set; }

    // Ollama-specific
    public double OllamaVramMB { get; set; }
    public double OllamaGpuUtilization { get; set; }  // 0-100% (from pmon)

    // Dedicated/Shared VRAM breakdown for Ollama (from D3DKMT or OS)
    public double OllamaDedicatedVramMB { get; set; }
    public double OllamaSharedVramMB { get; set; }

    // Flags to indicate data source (true = per-process data available, false = using system-wide fallback)
    public bool HasPerProcessVram { get; set; }
    public bool HasPerProcessGpuUtil { get; set; }
    public bool HasDedicatedSharedSplit { get; set; }  // True if we have dedicated/shared breakdown

    // Timestamp
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// GPU provider interface for abstracting vendor-specific implementations
/// </summary>
public interface IGpuProvider
{
    GpuVendor Vendor { get; }
    string DriverVersion { get; }
    string SdkVersion { get; }
    bool IsAvailable();
    Task<List<GpuMetrics>> GetMetricsAsync();
}

/// <summary>
/// NVIDIA GPU provider using nvidia-smi
/// </summary>
public class NvidiaGpuProvider : IGpuProvider
{
    public GpuVendor Vendor => GpuVendor.Nvidia;
    public string DriverVersion { get; private set; } = "";
    public string SdkVersion { get; private set; } = ""; // CUDA version

    private bool? _isAvailable;

    // Process name patterns that indicate Ollama-related processes
    private static readonly string[] OllamaProcessPatterns = new[]
    {
        "ollama",              // Main binary
        "ollama_llama_server", // Windows GPU runner
        "ollama-runner",       // Alternative runner name
        "llama-server",        // llama.cpp server spawned by Ollama
        "runner",              // Generic runner
    };

    /// <summary>
    /// Checks if a process name matches Ollama patterns
    /// </summary>
    private static bool IsOllamaProcess(string processName)
    {
        if (string.IsNullOrEmpty(processName)) return false;
        var name = processName.ToLowerInvariant();
        return OllamaProcessPatterns.Any(pattern => name.Contains(pattern.ToLowerInvariant()));
    }

    public bool IsAvailable()
    {
        if (_isAvailable.HasValue) return _isAvailable.Value;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "nvidia-smi",
                Arguments = "--query-gpu=driver_version --format=csv,noheader",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) { _isAvailable = false; return false; }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);

            _isAvailable = process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output);
            if (_isAvailable.Value)
            {
                DriverVersion = output.Trim().Split('\n')[0].Trim();
                DetectCudaVersion();
            }
            return _isAvailable.Value;
        }
        catch
        {
            _isAvailable = false;
            return false;
        }
    }

    private void DetectCudaVersion()
    {
        // CUDA version will be parsed from standard nvidia-smi output in GetMetricsAsync
        // This is a fallback method
        try
        {
            // Try to get CUDA version from the standard nvidia-smi output header
            var psi = new ProcessStartInfo
            {
                FileName = "nvidia-smi",
                Arguments = "",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return;

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);

            // Parse CUDA version from header line like:
            // "| NVIDIA-SMI 591.74    Driver Version: 591.74    CUDA Version: 13.1  |"
            ParseVersionsFromOutput(output);
        }
        catch { }
    }

    private void ParseVersionsFromOutput(string output)
    {
        foreach (var line in output.Split('\n'))
        {
            // Look for the header line with CUDA Version
            if (line.Contains("CUDA Version:"))
            {
                // Extract CUDA version using regex
                var cudaMatch = System.Text.RegularExpressions.Regex.Match(line, @"CUDA Version:\s*(\d+\.?\d*)");
                if (cudaMatch.Success)
                {
                    SdkVersion = cudaMatch.Groups[1].Value;
                }

                // Also try to get driver version from the same line
                var driverMatch = System.Text.RegularExpressions.Regex.Match(line, @"Driver Version:\s*(\d+\.?\d*\.?\d*)");
                if (driverMatch.Success && string.IsNullOrEmpty(DriverVersion))
                {
                    DriverVersion = driverMatch.Groups[1].Value;
                }
                break;
            }
        }
    }

    public async Task<List<GpuMetrics>> GetMetricsAsync()
    {
        var metrics = new List<GpuMetrics>();
        if (!IsAvailable()) return metrics;

        try
        {
            // Use standard nvidia-smi output parsing for maximum compatibility
            var psi = new ProcessStartInfo
            {
                FileName = "nvidia-smi",
                Arguments = "",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return metrics;

            var output = await process.StandardOutput.ReadToEndAsync();
            var errorOutput = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
            {
                _lastError = $"Exit: {process.ExitCode}, Error: {errorOutput}";
                return metrics;
            }

            // Parse versions from header (CUDA and Driver version)
            if (string.IsNullOrEmpty(SdkVersion) || string.IsNullOrEmpty(DriverVersion))
            {
                ParseVersionsFromOutput(output);
            }

            // Parse standard nvidia-smi output
            metrics = ParseStandardNvidiaSmiOutput(output);

            // Get clock speeds for each GPU
            if (metrics.Count > 0)
            {
                await GetGpuClockSpeedsAsync(metrics);
                await GetOllamaGpuUtilizationAsync(metrics);
            }
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
        }

        return metrics;
    }

    private async Task GetGpuClockSpeedsAsync(List<GpuMetrics> metrics)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "nvidia-smi",
                Arguments = "--query-gpu=index,clocks.current.sm,clocks.max.sm,clocks.current.memory,clocks.max.memory --format=csv,noheader,nounits",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return;

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0) return;

            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split(',').Select(p => p.Trim()).ToArray();
                if (parts.Length >= 5 && int.TryParse(parts[0], out var idx))
                {
                    var gpu = metrics.FirstOrDefault(m => m.Index == idx);
                    if (gpu != null)
                    {
                        gpu.ClockSpeedMHz = ParseInt(parts[1]);
                        gpu.ClockSpeedMaxMHz = ParseInt(parts[2]);
                        gpu.VramClockMHz = ParseInt(parts[3]);
                        gpu.VramClockMaxMHz = ParseInt(parts[4]);
                    }
                }
            }
        }
        catch { }
    }

    private List<GpuMetrics> ParseStandardNvidiaSmiOutput(string output)
    {
        var metrics = new List<GpuMetrics>();
        var lines = output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).ToList();

        // Find the GPU info section - it's between the header and the "Processes:" section
        int processesIndex = lines.FindIndex(l => l.Contains("Processes:") || l.Contains("Process name"));
        if (processesIndex < 0) processesIndex = lines.Count;

        // Only process lines before the Processes section
        var gpuLines = lines.Take(processesIndex).ToList();

        // Look for pairs of lines: GPU name line followed by stats line
        // GPU name line: "|   0  NVIDIA GeForce RTX 5080      WDDM  |   00000000:2B:00.0  On |"
        // Stats line:    "| 30%   32C    P8             21W /  380W |    1215MiB /  16303MiB |      2%      Default |"

        int gpuIndex = 0;
        for (int i = 0; i < gpuLines.Count - 1; i++)
        {
            var line = gpuLines[i];
            var nextLine = gpuLines[i + 1];

            // A GPU entry is detected when:
            // 1. Current line contains "NVIDIA" (but not Driver/CUDA/SMI header stuff)
            // 2. Next line has actual metrics: MiB (VRAM), temperature (C), power (W)
            bool isGpuNameLine = line.Contains("NVIDIA") &&
                                 !line.Contains("Driver") &&
                                 !line.Contains("CUDA") &&
                                 !line.Contains("SMI") &&
                                 !line.Contains(".exe");

            bool hasMetrics = nextLine.Contains("MiB") &&
                             nextLine.Contains("/") &&
                             (nextLine.Contains("C ") || nextLine.Contains("C\t") ||
                              System.Text.RegularExpressions.Regex.IsMatch(nextLine, @"\d+C\s")) &&
                             nextLine.Contains("W");

            if (isGpuNameLine && hasMetrics)
            {
                var gpu = new GpuMetrics
                {
                    Index = gpuIndex++,
                    Vendor = GpuVendor.Nvidia,
                    Timestamp = DateTime.UtcNow
                };

                // Extract GPU name from the line
                var nameMatch = System.Text.RegularExpressions.Regex.Match(line, @"(NVIDIA[^|]+)");
                if (nameMatch.Success)
                {
                    var name = nameMatch.Groups[1].Value.Trim();
                    // Remove trailing WDDM, TCC, On, Off
                    name = System.Text.RegularExpressions.Regex.Replace(name, @"\s+(WDDM|TCC|Off|On)\s*$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
                    gpu.Name = name;
                }

                // Parse the stats line (nextLine)
                // Fan: first percentage before temperature
                var fanMatch = System.Text.RegularExpressions.Regex.Match(nextLine, @"\|\s*(\d+)%");
                if (fanMatch.Success)
                    gpu.FanSpeedPercent = int.Parse(fanMatch.Groups[1].Value);

                // Temperature: number followed by C
                var tempMatch = System.Text.RegularExpressions.Regex.Match(nextLine, @"(\d+)\s*[°]?C\s");
                if (tempMatch.Success)
                    gpu.Temperature = int.Parse(tempMatch.Groups[1].Value);

                // Power: numberW / numberW
                var powerMatch = System.Text.RegularExpressions.Regex.Match(nextLine, @"(\d+)W\s*/\s*(\d+)W");
                if (powerMatch.Success)
                {
                    gpu.PowerDrawW = double.Parse(powerMatch.Groups[1].Value);
                    gpu.PowerLimitW = double.Parse(powerMatch.Groups[2].Value);
                }

                // VRAM: numberMiB / numberMiB
                var vramMatch = System.Text.RegularExpressions.Regex.Match(nextLine, @"(\d+)\s*MiB\s*/\s*(\d+)\s*MiB");
                if (vramMatch.Success)
                {
                    gpu.VramUsedMB = double.Parse(vramMatch.Groups[1].Value);
                    gpu.VramTotalMB = double.Parse(vramMatch.Groups[2].Value);
                }

                // GPU Utilization: percentage near the end (after MiB section)
                var mibIndex = nextLine.LastIndexOf("MiB");
                if (mibIndex > 0 && mibIndex + 3 < nextLine.Length)
                {
                    var utilPart = nextLine.Substring(mibIndex + 3);
                    var utilMatch = System.Text.RegularExpressions.Regex.Match(utilPart, @"(\d+)\s*%");
                    if (utilMatch.Success)
                        gpu.ComputeUtilization = double.Parse(utilMatch.Groups[1].Value);
                }

                // Only add GPU if we found actual metrics (VRAM total > 0 indicates a real GPU)
                if (gpu.VramTotalMB > 0)
                {
                    metrics.Add(gpu);
                }

                i++; // Skip the stats line we just parsed
            }
        }

        return metrics;
    }

    private string _lastError = "";
    public string LastError => _lastError;

    /// <summary>
    /// Gets Ollama GPU utilization using nvidia-smi pmon (NVIDIA only).
    /// VRAM is handled separately: D3DKMT on Windows, Ollama API as fallback for Linux/non-NVIDIA.
    /// </summary>
    private async Task GetOllamaGpuUtilizationAsync(List<GpuMetrics> metrics)
    {
        bool gotPerProcessGpuUtil = false;

        try
        {
            // Query pmon for per-process GPU utilization only (not VRAM)
            // Output format with -s u: "# gpu  pid  type  sm  mem  enc  dec  jpg  ofa  command"
            var pmonPsi = new ProcessStartInfo
            {
                FileName = "nvidia-smi",
                Arguments = "pmon -s u -c 1",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var pmonProcess = Process.Start(pmonPsi);
            if (pmonProcess != null)
            {
                var pmonOutput = await pmonProcess.StandardOutput.ReadToEndAsync();
                await pmonProcess.WaitForExitAsync();

                if (pmonProcess.ExitCode == 0)
                {
                    // Track GPU utilization per GPU for Ollama processes
                    var gpuOllamaUtil = new Dictionary<int, double>();

                    foreach (var line in pmonOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    {
                        // Skip header lines starting with #
                        if (line.TrimStart().StartsWith("#")) continue;

                        // Parse columns - command/name is always the LAST column
                        var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 5)
                        {
                            // Name is the last column
                            var command = parts[^1];
                            if (IsOllamaProcess(command))
                            {
                                if (int.TryParse(parts[0], out var gpuIdx))
                                {
                                    // sm column (index 3) is GPU SM utilization %
                                    var smStr = parts[3];
                                    if (smStr != "-" && double.TryParse(smStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var smUtil))
                                    {
                                        gotPerProcessGpuUtil = true;
                                        if (!gpuOllamaUtil.ContainsKey(gpuIdx))
                                            gpuOllamaUtil[gpuIdx] = 0;
                                        gpuOllamaUtil[gpuIdx] += smUtil;
                                    }
                                }
                            }
                        }
                    }

                    // Assign utilization to metrics
                    foreach (var kv in gpuOllamaUtil)
                    {
                        if (kv.Key < metrics.Count)
                        {
                            metrics[kv.Key].OllamaGpuUtilization = Math.Min(100, kv.Value);
                            metrics[kv.Key].HasPerProcessGpuUtil = true;
                        }
                    }
                }
            }
        }
        catch { }

        // On Windows, try NvAPIWrapper as fallback for GPU utilization if pmon didn't work
        if (OperatingSystem.IsWindows() && !gotPerProcessGpuUtil)
        {
            await TryNvApiWrapperFallbackAsync(metrics, needVram: false, needGpuUtil: true);
        }
    }

    /// <summary>
    /// Try to get additional GPU data using NvAPIWrapper (Windows only)
    /// </summary>
    private async Task TryNvApiWrapperFallbackAsync(List<GpuMetrics> metrics, bool needVram, bool needGpuUtil)
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            // Initialize NVAPI
            NVIDIA.Initialize();

            var physicalGpus = PhysicalGPU.GetPhysicalGPUs();
            if (physicalGpus == null || physicalGpus.Length == 0) return;

            foreach (var physicalGpu in physicalGpus)
            {
                // Find matching metric by name or index
                var matchingMetric = metrics.FirstOrDefault(m =>
                    m.Name?.Contains(physicalGpu.FullName ?? "") == true ||
                    m.Index == Array.IndexOf(physicalGpus, physicalGpu));

                if (matchingMetric == null && metrics.Count > 0)
                    matchingMetric = metrics[0];

                if (matchingMetric == null) continue;

                // Try to get active applications (per-process data)
                if (needVram || needGpuUtil)
                {
                    try
                    {
                        var activeApps = physicalGpu.GetActiveApplications();
                        if (activeApps != null)
                        {
                            foreach (var app in activeApps)
                            {
                                if (IsOllamaProcess(app.ProcessName ?? ""))
                                {
                                    // NvAPI may provide memory info per app
                                    // Note: NvAPIWrapper's ActiveApplication doesn't expose memory directly
                                    // but at least we know Ollama is using this GPU
                                }
                            }
                        }
                    }
                    catch { }
                }

                // Get overall GPU utilization from NvAPIWrapper as additional data source
                if (needGpuUtil)
                {
                    try
                    {
                        var usageInfo = physicalGpu.UsageInformation;
                        if (usageInfo != null)
                        {
                            // UsageInformation provides GPU usage domains
                            // We can use this for more accurate total GPU util
                            var gpuUsage = usageInfo.GPU.Percentage;
                            // Update ComputeUtilization if not already set well
                            if (matchingMetric.ComputeUtilization < 0.1 && gpuUsage > 0)
                            {
                                matchingMetric.ComputeUtilization = gpuUsage;
                            }
                        }
                    }
                    catch { }
                }

                // Get memory info from NvAPIWrapper
                if (needVram)
                {
                    try
                    {
                        var memInfo = physicalGpu.MemoryInformation;
                        if (memInfo != null)
                        {
                            // Update memory totals if more accurate
                            var totalMb = memInfo.DedicatedVideoMemoryInkB / 1024.0;
                            var availableMb = memInfo.AvailableDedicatedVideoMemoryInkB / 1024.0;
                            var usedMb = totalMb - availableMb;

                            // Update if we have better data
                            if (totalMb > 0 && matchingMetric.VramTotalMB < 1)
                            {
                                matchingMetric.VramTotalMB = totalMb;
                                matchingMetric.VramUsedMB = usedMb;
                            }
                        }
                    }
                    catch { }
                }

                // Get thermal info from NvAPIWrapper
                try
                {
                    var thermalInfo = physicalGpu.ThermalInformation;
                    if (thermalInfo != null)
                    {
                        var sensors = thermalInfo.ThermalSensors;
                        if (sensors != null && sensors.Any())
                        {
                            var gpuSensor = sensors.FirstOrDefault(s => s.Target == NvAPIWrapper.Native.GPU.ThermalSettingsTarget.GPU);
                            if (gpuSensor != null && gpuSensor.CurrentTemperature > 0 && matchingMetric.Temperature < 1)
                            {
                                matchingMetric.Temperature = gpuSensor.CurrentTemperature;
                            }
                        }
                    }
                }
                catch { }

                // Get power info from NvAPIWrapper
                try
                {
                    var powerInfo = physicalGpu.PowerTopologyInformation;
                    if (powerInfo != null)
                    {
                        var entries = powerInfo.PowerTopologyEntries;
                        if (entries != null && entries.Any())
                        {
                            var gpuEntry = entries.FirstOrDefault();
                            if (gpuEntry != null && gpuEntry.PowerUsageInPercent > 0)
                            {
                                // Power is in percentage, not watts
                                // We'd need to know TDP to convert
                            }
                        }
                    }
                }
                catch { }

                // Get clock frequencies from NvAPIWrapper
                try
                {
                    var clocks = physicalGpu.CurrentClockFrequencies;
                    if (clocks != null && matchingMetric.ClockSpeedMHz < 1)
                    {
                        matchingMetric.ClockSpeedMHz = (int)(clocks.GraphicsClock.Frequency / 1000); // kHz to MHz
                        matchingMetric.VramClockMHz = (int)(clocks.MemoryClock.Frequency / 1000);
                    }
                }
                catch { }
            }
        }
        catch
        {
            // NvAPIWrapper not available or failed - ignore
        }
    }

    private static int ParseInt(string s) => int.TryParse(s, out var v) ? v : 0;
    private static double ParseDouble(string s) => double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0;
}

/// <summary>
/// AMD GPU provider using rocm-smi
/// </summary>
public class AmdGpuProvider : IGpuProvider
{
    public GpuVendor Vendor => GpuVendor.Amd;
    public string DriverVersion { get; private set; } = "";
    public string SdkVersion { get; private set; } = ""; // ROCm version

    private bool? _isAvailable;

    public bool IsAvailable()
    {
        if (_isAvailable.HasValue) return _isAvailable.Value;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "rocm-smi",
                Arguments = "--showdriverversion",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) { _isAvailable = false; return false; }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);

            _isAvailable = process.ExitCode == 0;
            if (_isAvailable.Value)
            {
                // Parse driver version from output
                foreach (var line in output.Split('\n'))
                {
                    if (line.Contains("Driver version:") || line.Contains("driver version"))
                    {
                        var parts = line.Split(':');
                        if (parts.Length >= 2)
                        {
                            DriverVersion = parts[1].Trim();
                            break;
                        }
                    }
                }
                DetectRocmVersion();
            }
            return _isAvailable.Value;
        }
        catch
        {
            _isAvailable = false;
            return false;
        }
    }

    private void DetectRocmVersion()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "rocm-smi",
                Arguments = "--showversion",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return;

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);

            foreach (var line in output.Split('\n'))
            {
                if (line.Contains("ROCm") || line.Contains("version"))
                {
                    SdkVersion = line.Trim();
                    break;
                }
            }
        }
        catch { }
    }

    public async Task<List<GpuMetrics>> GetMetricsAsync()
    {
        var metrics = new List<GpuMetrics>();
        if (!IsAvailable()) return metrics;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "rocm-smi",
                Arguments = "--showuse --showmeminfo vram --showtemp --showpower --showclocks --showfan --json",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return metrics;

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
                return metrics;

            // Parse JSON output
            using var doc = JsonDocument.Parse(output);
            var root = doc.RootElement;

            int index = 0;
            foreach (var prop in root.EnumerateObject())
            {
                if (prop.Name.StartsWith("card"))
                {
                    var card = prop.Value;
                    var gpu = new GpuMetrics
                    {
                        Index = index++,
                        Vendor = GpuVendor.Amd,
                        Timestamp = DateTime.UtcNow
                    };

                    // GPU Name
                    if (card.TryGetProperty("Card series", out var series))
                        gpu.Name = series.GetString() ?? "";

                    // Utilization
                    if (card.TryGetProperty("GPU use (%)", out var use))
                        gpu.ComputeUtilization = ParseJsonDouble(use);

                    // VRAM
                    if (card.TryGetProperty("VRAM Total Memory (B)", out var vramTotal))
                        gpu.VramTotalMB = ParseJsonDouble(vramTotal) / (1024 * 1024);
                    if (card.TryGetProperty("VRAM Total Used Memory (B)", out var vramUsed))
                        gpu.VramUsedMB = ParseJsonDouble(vramUsed) / (1024 * 1024);

                    // Temperature
                    if (card.TryGetProperty("Temperature (Sensor edge) (C)", out var temp))
                        gpu.Temperature = (int)ParseJsonDouble(temp);
                    if (card.TryGetProperty("Temperature (Sensor junction) (C)", out var hotspot))
                        gpu.HotspotTemperature = (int)ParseJsonDouble(hotspot);

                    // Power
                    if (card.TryGetProperty("Average Graphics Package Power (W)", out var power))
                        gpu.PowerDrawW = ParseJsonDouble(power);
                    if (card.TryGetProperty("Max Graphics Package Power (W)", out var powerMax))
                        gpu.PowerLimitW = ParseJsonDouble(powerMax);

                    // Clocks
                    if (card.TryGetProperty("sclk clock speed:", out var sclk))
                        gpu.ClockSpeedMHz = ParseClockValue(sclk.GetString() ?? "");
                    if (card.TryGetProperty("mclk clock speed:", out var mclk))
                        gpu.VramClockMHz = ParseClockValue(mclk.GetString() ?? "");

                    // Fan
                    if (card.TryGetProperty("Fan speed (%)", out var fan))
                        gpu.FanSpeedPercent = (int)ParseJsonDouble(fan);

                    metrics.Add(gpu);
                }
            }
        }
        catch { }

        return metrics;
    }

    private static double ParseJsonDouble(JsonElement elem)
    {
        if (elem.ValueKind == JsonValueKind.Number)
            return elem.GetDouble();
        if (elem.ValueKind == JsonValueKind.String)
            return double.TryParse(elem.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0;
        return 0;
    }

    private static int ParseClockValue(string s)
    {
        // Extract numeric part from strings like "2100Mhz" or "1000 MHz"
        var numStr = new string(s.TakeWhile(c => char.IsDigit(c) || c == '.').ToArray());
        return int.TryParse(numStr, out var v) ? v : 0;
    }
}

/// <summary>
/// Intel GPU provider using xpu-smi
/// </summary>
public class IntelGpuProvider : IGpuProvider
{
    public GpuVendor Vendor => GpuVendor.Intel;
    public string DriverVersion { get; private set; } = "";
    public string SdkVersion { get; private set; } = ""; // Level Zero version

    private bool? _isAvailable;

    public bool IsAvailable()
    {
        if (_isAvailable.HasValue) return _isAvailable.Value;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "xpu-smi.exe" : "xpu-smi",
                Arguments = "discovery",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) { _isAvailable = false; return false; }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);

            _isAvailable = process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output);
            if (_isAvailable.Value)
            {
                DetectVersions();
            }
            return _isAvailable.Value;
        }
        catch
        {
            _isAvailable = false;
            return false;
        }
    }

    private void DetectVersions()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "xpu-smi.exe" : "xpu-smi",
                Arguments = "version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return;

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);

            foreach (var line in output.Split('\n'))
            {
                if (line.Contains("Driver Version") || line.Contains("driver"))
                {
                    var parts = line.Split(':');
                    if (parts.Length >= 2)
                        DriverVersion = parts[1].Trim();
                }
                else if (line.Contains("Level Zero") || line.Contains("version"))
                {
                    SdkVersion = line.Trim();
                }
            }
        }
        catch { }
    }

    public async Task<List<GpuMetrics>> GetMetricsAsync()
    {
        var metrics = new List<GpuMetrics>();
        if (!IsAvailable()) return metrics;

        try
        {
            // Discovery to get device list
            var psiDiscovery = new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "xpu-smi.exe" : "xpu-smi",
                Arguments = "discovery -j",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var discoveryProcess = Process.Start(psiDiscovery);
            if (discoveryProcess == null) return metrics;

            var discoveryOutput = await discoveryProcess.StandardOutput.ReadToEndAsync();
            await discoveryProcess.WaitForExitAsync();

            if (discoveryProcess.ExitCode != 0) return metrics;

            using var discoveryDoc = JsonDocument.Parse(discoveryOutput);

            if (!discoveryDoc.RootElement.TryGetProperty("device_list", out var deviceList))
                return metrics;

            foreach (var device in deviceList.EnumerateArray())
            {
                var deviceId = device.TryGetProperty("device_id", out var id) ? id.GetInt32() : 0;
                var deviceName = device.TryGetProperty("device_name", out var name) ? name.GetString() ?? "" : "";

                var gpu = new GpuMetrics
                {
                    Index = deviceId,
                    Name = deviceName,
                    Vendor = GpuVendor.Intel,
                    Timestamp = DateTime.UtcNow
                };

                // Get stats for this device
                var psiStats = new ProcessStartInfo
                {
                    FileName = OperatingSystem.IsWindows() ? "xpu-smi.exe" : "xpu-smi",
                    Arguments = $"stats -d {deviceId} -j",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var statsProcess = Process.Start(psiStats);
                if (statsProcess != null)
                {
                    var statsOutput = await statsProcess.StandardOutput.ReadToEndAsync();
                    await statsProcess.WaitForExitAsync();

                    if (statsProcess.ExitCode == 0 && !string.IsNullOrWhiteSpace(statsOutput))
                    {
                        try
                        {
                            using var statsDoc = JsonDocument.Parse(statsOutput);
                            var stats = statsDoc.RootElement;

                            // GPU Utilization
                            if (stats.TryGetProperty("compute_engine_utilization", out var util))
                                gpu.ComputeUtilization = ParseJsonNumber(util);
                            else if (stats.TryGetProperty("gpu_utilization", out var gpuUtil))
                                gpu.ComputeUtilization = ParseJsonNumber(gpuUtil);

                            // Memory
                            if (stats.TryGetProperty("memory_used", out var memUsed))
                                gpu.VramUsedMB = ParseJsonNumber(memUsed) / (1024 * 1024);
                            if (stats.TryGetProperty("memory_total", out var memTotal))
                                gpu.VramTotalMB = ParseJsonNumber(memTotal) / (1024 * 1024);

                            // Temperature
                            if (stats.TryGetProperty("gpu_temperature", out var temp))
                                gpu.Temperature = (int)ParseJsonNumber(temp);

                            // Power
                            if (stats.TryGetProperty("power", out var power))
                                gpu.PowerDrawW = ParseJsonNumber(power);

                            // Frequency
                            if (stats.TryGetProperty("gpu_frequency", out var freq))
                                gpu.ClockSpeedMHz = (int)ParseJsonNumber(freq);
                            if (stats.TryGetProperty("max_frequency", out var maxFreq))
                                gpu.ClockSpeedMaxMHz = (int)ParseJsonNumber(maxFreq);
                        }
                        catch { }
                    }
                }

                metrics.Add(gpu);
            }
        }
        catch { }

        return metrics;
    }

    private static double ParseJsonNumber(JsonElement elem)
    {
        if (elem.ValueKind == JsonValueKind.Number)
            return elem.GetDouble();
        if (elem.ValueKind == JsonValueKind.String)
            return double.TryParse(elem.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0;
        return 0;
    }
}

/// <summary>
/// Main GPU monitoring class that aggregates metrics from all available GPU providers
/// </summary>
public class GpuMonitor
{
    private readonly List<IGpuProvider> _providers = new();
    private List<GpuMetrics> _lastMetrics = new();

    // D3DKMT-based per-process GPU monitoring (Windows only) - shared by all providers
    private D3DKMTProcessGpuMonitor? _d3dkmtMonitor = null;
    private bool _d3dkmtInitialized;

    // Process name patterns that indicate Ollama-related processes
    private static readonly string[] OllamaProcessPatterns = new[]
    {
        "ollama",              // Main binary
        "ollama_llama_server", // Windows GPU runner
        "ollama-runner",       // Alternative runner name
        "llama-server",        // llama.cpp server spawned by Ollama
        "runner",              // Generic runner
    };

    public GpuMonitor()
    {
        // Register providers in order of preference
        _providers.Add(new NvidiaGpuProvider());
        _providers.Add(new AmdGpuProvider());
        _providers.Add(new IntelGpuProvider());

        // D3DKMT monitor for per-process GPU metrics (Windows only)
        // Note: D3DKMT only tracks stats for processes that have done GPU work.
        // Processes that haven't used the GPU will return STATUS_INVALID_PARAMETER.
        if (OperatingSystem.IsWindows())
        {
            try
            {
                _d3dkmtMonitor = new D3DKMTProcessGpuMonitor();
                _d3dkmtInitialized = _d3dkmtMonitor.Initialize();
            }
            catch
            {
                _d3dkmtInitialized = false;
            }
        }
    }

    /// <summary>
    /// Gets the active GPU provider (first available)
    /// </summary>
    public IGpuProvider? ActiveProvider => _providers.FirstOrDefault(p => p.IsAvailable());

    /// <summary>
    /// Gets the number of detected GPUs
    /// </summary>
    public int GpuCount => _lastMetrics.Count;

    /// <summary>
    /// Gets the last collected metrics
    /// </summary>
    public IReadOnlyList<GpuMetrics> LastMetrics => _lastMetrics;

    /// <summary>
    /// Gets the driver version string
    /// </summary>
    public string DriverVersion => ActiveProvider?.DriverVersion ?? "";

    /// <summary>
    /// Gets the SDK version string (CUDA, ROCm, Level Zero)
    /// </summary>
    public string SdkVersion => ActiveProvider?.SdkVersion ?? "";

    /// <summary>
    /// Gets the detected vendor
    /// </summary>
    public GpuVendor DetectedVendor => ActiveProvider?.Vendor ?? GpuVendor.None;

    /// <summary>
    /// Checks if any GPU is available
    /// </summary>
    public bool HasGpu => _providers.Any(p => p.IsAvailable());

    /// <summary>
    /// Collects metrics from all available GPUs
    /// </summary>
    public async Task<List<GpuMetrics>> CollectMetricsAsync()
    {
        _lastMetrics.Clear();

        foreach (var provider in _providers)
        {
            if (provider.IsAvailable())
            {
                var metrics = await provider.GetMetricsAsync();
                _lastMetrics.AddRange(metrics);
            }
        }

        // Apply D3DKMT per-process data if available (Windows only, works with any GPU vendor)
        if (_d3dkmtInitialized && _d3dkmtMonitor != null && _lastMetrics.Count > 0)
        {
            ApplyD3DKMTMetrics(_lastMetrics);
        }

        return _lastMetrics;
    }

    /// <summary>
    /// Checks if a process name matches Ollama patterns
    /// </summary>
    private static bool IsOllamaProcess(string processName)
    {
        if (string.IsNullOrEmpty(processName)) return false;
        var name = processName.ToLowerInvariant();
        return OllamaProcessPatterns.Any(pattern => name.Contains(pattern.ToLowerInvariant()));
    }

    /// <summary>
    /// Gets all Ollama-related process IDs
    /// </summary>
    private static List<int> GetOllamaProcessIds()
    {
        var pids = new List<int>();
        try
        {
            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    if (IsOllamaProcess(proc.ProcessName))
                    {
                        pids.Add(proc.Id);
                    }
                }
                catch { }
                finally
                {
                    proc.Dispose();
                }
            }
        }
        catch { }
        return pids;
    }

    /// <summary>
    /// Applies D3DKMT per-process GPU utilization and dedicated/shared VRAM to the collected metrics.
    /// Note: Total VRAM allocation is handled by Ollama API (canonical source) in PsMonitorCommand.
    /// D3DKMT provides the dedicated/shared breakdown.
    /// </summary>
    private void ApplyD3DKMTMetrics(List<GpuMetrics> metrics)
    {
        if (_d3dkmtMonitor == null) return;

        try
        {
            var ollamaPids = GetOllamaProcessIds();
            if (ollamaPids.Count == 0) return;

            var d3dkmtMetrics = _d3dkmtMonitor.GetProcessGpuMetrics(ollamaPids);
            if (d3dkmtMetrics.Count == 0) return;

            // Aggregate GPU utilization and memory per GPU
            var gpuUtilByIndex = new Dictionary<int, double>();
            var gpuDedicatedByIndex = new Dictionary<int, double>();
            var gpuSharedByIndex = new Dictionary<int, double>();

            foreach (var pm in d3dkmtMetrics)
            {
                var gpuIdx = pm.GpuIndex;
                if (!gpuUtilByIndex.ContainsKey(gpuIdx))
                {
                    gpuUtilByIndex[gpuIdx] = 0;
                    gpuDedicatedByIndex[gpuIdx] = 0;
                    gpuSharedByIndex[gpuIdx] = 0;
                }
                gpuUtilByIndex[gpuIdx] += pm.GpuUtilization;
                gpuDedicatedByIndex[gpuIdx] += pm.DedicatedMemoryMB;
                gpuSharedByIndex[gpuIdx] += pm.SharedMemoryMB;
            }

            // Apply to metrics
            foreach (var gpuIdx in gpuUtilByIndex.Keys)
            {
                if (gpuIdx < metrics.Count)
                {
                    // Apply GPU utilization if we don't already have per-process data
                    if (!metrics[gpuIdx].HasPerProcessGpuUtil && gpuUtilByIndex[gpuIdx] > 0)
                    {
                        metrics[gpuIdx].OllamaGpuUtilization = Math.Min(100, gpuUtilByIndex[gpuIdx]);
                        metrics[gpuIdx].HasPerProcessGpuUtil = true;
                    }

                    // Apply dedicated/shared VRAM breakdown
                    var dedicated = gpuDedicatedByIndex[gpuIdx];
                    var shared = gpuSharedByIndex[gpuIdx];
                    if (dedicated > 0 || shared > 0)
                    {
                        metrics[gpuIdx].OllamaDedicatedVramMB = dedicated;
                        metrics[gpuIdx].OllamaSharedVramMB = shared;
                        metrics[gpuIdx].HasDedicatedSharedSplit = true;
                    }
                }
            }
        }
        catch { }
    }

    /// <summary>
    /// Gets a vendor-specific SMI tool name for display
    /// </summary>
    public string GetSmiToolName()
    {
        return DetectedVendor switch
        {
            GpuVendor.Nvidia => "NVIDIA-SMI",
            GpuVendor.Amd => "ROCm-SMI",
            GpuVendor.Intel => "XPU-SMI",
            _ => ""
        };
    }

    /// <summary>
    /// Gets the SDK name for display
    /// </summary>
    public string GetSdkName()
    {
        return DetectedVendor switch
        {
            GpuVendor.Nvidia => "CUDA",
            GpuVendor.Amd => "ROCm",
            GpuVendor.Intel => "Level Zero",
            _ => ""
        };
    }
}
