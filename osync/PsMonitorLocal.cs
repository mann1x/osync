using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace osync;

/// <summary>
/// The System panel and GPU list of psmonitor for a server on this machine that sends no system telemetry (stock
/// Ollama, an older xOllama): this machine's own readings, put in the shape a server's /api/xollama/system has so
/// that a local and a remote server are shown the same way. A reading the collectors do not have stays null.
/// </summary>
internal static class LocalReadings
{
    private const double MiB = 1024.0 * 1024.0;

    /// <param name="system">This machine's CPU, memory and swap; null when they could not be read.</param>
    /// <param name="server">The server's processes (every process named like ollama); null when none was found.</param>
    /// <param name="gpus">The GPUs the vendor tools report.</param>
    /// <param name="serverDevices">xOllama's own device list (backend, engine), or null when the server has none.</param>
    public static RemoteSystem Build(SystemMetricsData? system, OllamaProcessMetrics? server, IReadOnlyList<GpuMetrics> gpus,
        IReadOnlyList<RemoteDevice>? serverDevices, int cpuCount, string hostname, long uptimeSeconds)
    {
        RemoteHost? host = null;
        if (system != null)
        {
            host = new RemoteHost(OsName(), ArchName(), hostname, uptimeSeconds, system.CpuUsagePercent, cpuCount,
                system.LoadAverage.Length == 3 ? system.LoadAverage : null,
                system.RamTotalBytes > 0 ? (ulong)system.RamTotalBytes : null,
                system.RamTotalBytes > 0 ? (ulong)Math.Max(0, system.RamUsedBytes) : null,
                // a machine without swap reports a total of 0 ("none"), not an unknown
                (ulong)Math.Max(0, system.SwapTotalBytes), (ulong)Math.Max(0, system.SwapUsedBytes));
        }

        RemoteServerProcess? process = null;
        if (server is { ProcessCount: > 0 })
        {
            // The collector's CPU is of all cores; a server's cpu_pct is of one core (four busy cores read 400)
            process = new RemoteServerProcess(null, server.CpuUsagePercent * Math.Max(1, cpuCount),
                (ulong)Math.Max(0, server.WorkingSetBytes), server.ProcessCount, new List<RemoteProcess>());
        }

        return new RemoteSystem(DateTime.UtcNow, host, process, MergeDevices(gpus, serverDevices));
    }

    /// <summary>
    /// This machine's GPUs, each with the backend and engine xOllama serves it through when its device list names
    /// the same card; cards only xOllama lists (an integrated GPU the vendor tools do not see) follow, without
    /// live readings.
    /// </summary>
    public static List<RemoteDevice> MergeDevices(IReadOnlyList<GpuMetrics> gpus, IReadOnlyList<RemoteDevice>? serverDevices)
    {
        var unmatched = serverDevices?.ToList() ?? new List<RemoteDevice>();
        var result = new List<RemoteDevice>();
        foreach (var g in gpus)
        {
            var match = unmatched.FirstOrDefault(d => SameCard(d.Name, g.Name));
            if (match != null) unmatched.Remove(match);

            ulong? total = g.VramTotalMB > 0 ? (ulong)(g.VramTotalMB * MiB) : null;
            ulong? used = total != null ? (ulong)(Math.Max(0, g.VramUsedMB) * MiB) : null;
            ulong? free = total is { } t && used is { } u ? (t > u ? t - u : 0) : null;
            result.Add(new RemoteDevice(match?.Backend ?? "", match?.Id ?? g.Index.ToString(), match?.PciId ?? "",
                g.Name, match?.Integrated ?? false, total, free, match?.Engine ?? "")
            {
                ReportedUsed = used,
                Vendor = g.Vendor.ToString(),
                BackendKnown = serverDevices != null,
                Telemetry = new RemoteGpuTelemetry("local", g.ComputeUtilization, null,
                    g.Temperature > 0 ? g.Temperature : null,
                    g.PowerDrawW > 0 ? g.PowerDrawW : null,
                    g.PowerLimitW > 0 ? g.PowerLimitW : null,
                    g.ClockSpeedMHz > 0 ? g.ClockSpeedMHz : null,
                    g.ClockSpeedMaxMHz > 0 ? g.ClockSpeedMaxMHz : null,
                    // 0 is what the tools answer for a card without a fan reading, as before
                    g.FanSpeedPercent > 0 ? g.FanSpeedPercent : null,
                    null, null, null, null),
            });
        }
        result.AddRange(unmatched);
        return result;
    }

    /// <summary>Whether two names are one card ("NVIDIA GeForce RTX 5080" and "GeForce RTX 5080").</summary>
    internal static bool SameCard(string a, string b)
    {
        a = a.Trim();
        b = b.Trim();
        if (a.Length == 0 || b.Length == 0) return false;
        return a.Equals(b, StringComparison.OrdinalIgnoreCase) ||
               a.Contains(b, StringComparison.OrdinalIgnoreCase) || b.Contains(a, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The OS as Go names it (what a server reports): windows, linux, darwin.</summary>
    internal static string OsName() =>
        OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "darwin" : OperatingSystem.IsLinux() ? "linux" : RuntimeInformation.OSDescription;

    internal static string ArchName() => RuntimeInformation.OSArchitecture switch
    {
        Architecture.X64 => "amd64",
        Architecture.Arm64 => "arm64",
        Architecture.X86 => "386",
        var other => other.ToString().ToLowerInvariant()
    };
}
