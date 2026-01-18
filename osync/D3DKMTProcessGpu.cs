using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace osync;

/// <summary>
/// Per-process GPU metrics obtained via D3DKMT (Windows only)
/// </summary>
public class ProcessGpuMetrics
{
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = "";
    public long GpuAdapterLuid { get; set; }
    public int GpuIndex { get; set; }
    public long RunningTimeNs { get; set; }  // GPU running time in 100ns units
    public long DedicatedMemoryBytes { get; set; }
    public long SharedMemoryBytes { get; set; }
    public double GpuUtilization { get; set; }  // Calculated as percentage 0-100
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public double DedicatedMemoryMB => DedicatedMemoryBytes / (1024.0 * 1024.0);
    public double SharedMemoryMB => SharedMemoryBytes / (1024.0 * 1024.0);
}

/// <summary>
/// Queries per-process GPU utilization on Windows using D3DKMT
/// Based on System Informer / Process Hacker implementation
/// Uses raw memory access for reliable native interop
/// </summary>
public class D3DKMTProcessGpuMonitor : IDisposable
{
    // Track previous running times for delta calculation
    private readonly Dictionary<(int ProcessId, long AdapterLuid), long> _lastRunningTimes = new();
    private DateTime _lastSampleTime = DateTime.MinValue;
    private readonly List<GpuAdapterInfo> _adapters = new();
    private bool _initialized;

    public class GpuAdapterInfo
    {
        public long Luid { get; set; }
        public int Index { get; set; }
        public uint NodeCount { get; set; }
        public uint SegmentCount { get; set; }
        public string DeviceName { get; set; } = "";
        // Track which segments are aperture (shared/system memory) vs local (dedicated VRAM)
        // Index = segment ID, value = true if aperture (shared)
        public Dictionary<uint, bool> SegmentIsAperture { get; set; } = new();
    }

    /// <summary>
    /// Initialize the monitor by enumerating GPU adapters
    /// </summary>
    public bool Initialize()
    {
        if (_initialized) return true;
        if (!OperatingSystem.IsWindows()) return false;

        try
        {
            EnumerateGpuAdapters();
            _initialized = _adapters.Count > 0;
            return _initialized;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"D3DKMT Initialize failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Get diagnostic information about D3DKMT state
    /// </summary>
    public string GetDiagnostics()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"D3DKMT Initialized: {_initialized}");
        sb.AppendLine($"Adapters found: {_adapters.Count}");

        foreach (var adapter in _adapters)
        {
            sb.AppendLine($"  GPU {adapter.Index}: LUID=0x{adapter.Luid:X}, Nodes={adapter.NodeCount}, Segments={adapter.SegmentCount}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Test D3DKMT by querying metrics for a specific process
    /// </summary>
    public string TestQueryProcess(int processId)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Testing D3DKMT query for PID {processId}");

        if (!_initialized)
        {
            sb.AppendLine("ERROR: D3DKMT not initialized");
            return sb.ToString();
        }

        // First, test adapter-level queries (no process handle needed)
        sb.AppendLine("\n  === Adapter-level queries (baseline test) ===");
        foreach (var adapter in _adapters)
        {
            var luid = new LUID
            {
                LowPart = (uint)(adapter.Luid & 0xFFFFFFFF),
                HighPart = (int)(adapter.Luid >> 32)
            };

            // Test NODE query (adapter level, not process level)
            if (adapter.NodeCount > 0)
            {
                var nodeStatus = TestNodeQuery(luid, 0);
                sb.AppendLine($"  Adapter {adapter.Index}: NODE query status=0x{nodeStatus:X}");
            }

            // Test SEGMENT query (adapter level)
            if (adapter.SegmentCount > 0)
            {
                var segStatus = TestSegmentQuery(luid, 0);
                sb.AppendLine($"  Adapter {adapter.Index}: SEGMENT query status=0x{segStatus:X}");
            }
        }

        // First test with current process (use real handle, not pseudo-handle)
        sb.AppendLine($"\n  === Testing current process (self-test) ===");
        var currentPid = (uint)Process.GetCurrentProcess().Id;
        IntPtr currentProcessHandle = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ, false, currentPid);
        sb.AppendLine($"  Current PID: {currentPid}, handle: 0x{currentProcessHandle:X}");

        foreach (var adapter in _adapters)
        {
            if (adapter.NodeCount > 0)
            {
                var luid = new LUID
                {
                    LowPart = (uint)(adapter.Luid & 0xFFFFFFFF),
                    HighPart = (int)(adapter.Luid >> 32)
                };
                var status = TestProcessAdapterQueryExtended(luid, currentProcessHandle);
                sb.AppendLine($"  Adapter {adapter.Index}: PROCESS_ADAPTER with current process: {status}");
            }
        }
        if (currentProcessHandle != IntPtr.Zero)
            CloseHandle(currentProcessHandle);

        // Now test process-level queries for target process
        sb.AppendLine($"\n  === Process-level queries for PID {processId} ===");

        // Try both access levels
        IntPtr processHandle = OpenProcess(PROCESS_QUERY_INFORMATION, false, (uint)processId);
        if (processHandle == IntPtr.Zero)
        {
            // Fall back to limited info
            processHandle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)processId);
            sb.AppendLine($"  Using PROCESS_QUERY_LIMITED_INFORMATION");
        }
        else
        {
            sb.AppendLine($"  Using PROCESS_QUERY_INFORMATION");
        }

        if (processHandle == IntPtr.Zero)
        {
            sb.AppendLine($"  ERROR: Failed to open process, error code: {Marshal.GetLastWin32Error()}");
            return sb.ToString();
        }

        try
        {
            sb.AppendLine($"  Process handle: 0x{processHandle:X}");

            foreach (var adapter in _adapters)
            {
                var luid = new LUID
                {
                    LowPart = (uint)(adapter.Luid & 0xFFFFFFFF),
                    HighPart = (int)(adapter.Luid >> 32)
                };

                sb.AppendLine($"  Adapter {adapter.Index} (LUID=0x{adapter.Luid:X}):");
                sb.AppendLine($"    NodeCount={adapter.NodeCount}, SegmentCount={adapter.SegmentCount}");

                // Test PROCESS_ADAPTER query first (simplest process query)
                // This tests multiple layouts to find the working one
                var procAdapterStatus = TestProcessAdapterQuery(luid, processHandle);
                sb.AppendLine($"    PROCESS_ADAPTER query: Status=0x{procAdapterStatus:X}, hProcessOffset={_workingProcessHandleOffset}");

                if (adapter.NodeCount == 0)
                {
                    sb.AppendLine($"    [SKIP] No compute nodes available");
                }
                else
                {
                    // Test node queries
                    for (uint nodeId = 0; nodeId < Math.Min(adapter.NodeCount, 8u); nodeId++)
                    {
                        _lastQueryStatus = -1;
                        _debugInfo = "";
                        var runningTime = QueryProcessNodeRunningTime(luid, processHandle, nodeId);
                        sb.AppendLine($"    Node {nodeId}: RunningTime={runningTime}, Status=0x{_lastQueryStatus:X}");
                        if (!string.IsNullOrEmpty(_debugInfo))
                            sb.AppendLine($"      Debug: {_debugInfo}");
                    }
                }

                // Test segment queries
                for (uint segmentId = 0; segmentId < Math.Min(adapter.SegmentCount, 8u); segmentId++)
                {
                    _lastSegmentQueryStatus = -1;
                    var memory = QueryProcessSegmentMemory(luid, processHandle, segmentId);
                    sb.AppendLine($"    Segment {segmentId}: Memory={memory} bytes ({memory / 1024.0 / 1024.0:F2} MB), Status=0x{_lastSegmentQueryStatus:X}");
                }
            }
        }
        finally
        {
            CloseHandle(processHandle);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Test adapter-level NODE query
    /// </summary>
    private int TestNodeQuery(LUID adapterLuid, uint nodeId)
    {
        const int bufferSize = 2048;
        IntPtr buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            for (int i = 0; i < bufferSize; i++)
                Marshal.WriteByte(buffer, i, 0);

            Marshal.WriteInt32(buffer, 0, (int)D3DKMT_QUERYSTATISTICS_TYPE.D3DKMT_QUERYSTATISTICS_NODE);
            Marshal.WriteInt32(buffer, 4, (int)adapterLuid.LowPart);
            Marshal.WriteInt32(buffer, 8, adapterLuid.HighPart);
            // hProcess at offset 16 = NULL for adapter-level query
            Marshal.WriteInt32(buffer, 24, (int)nodeId);

            return D3DKMTQueryStatistics(buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>
    /// Test adapter-level SEGMENT query
    /// </summary>
    private int TestSegmentQuery(LUID adapterLuid, uint segmentId)
    {
        const int bufferSize = 2048;
        IntPtr buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            for (int i = 0; i < bufferSize; i++)
                Marshal.WriteByte(buffer, i, 0);

            Marshal.WriteInt32(buffer, 0, (int)D3DKMT_QUERYSTATISTICS_TYPE.D3DKMT_QUERYSTATISTICS_SEGMENT);
            Marshal.WriteInt32(buffer, 4, (int)adapterLuid.LowPart);
            Marshal.WriteInt32(buffer, 8, adapterLuid.HighPart);
            Marshal.WriteInt32(buffer, 24, (int)segmentId);

            return D3DKMTQueryStatistics(buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>
    /// Test PROCESS_ADAPTER query (simplest process-level query)
    /// Tests multiple offset layouts to find the correct one
    /// </summary>
    private int TestProcessAdapterQuery(LUID adapterLuid, IntPtr processHandle)
    {
        const int bufferSize = 2048;
        IntPtr buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            // Try different hProcess offset layouts
            // Layout A: hProcess at offset 12 (no padding after LUID)
            // Layout B: hProcess at offset 16 (4 bytes padding after LUID)
            int[] offsetsToTry = { 12, 16 };

            foreach (var hProcessOffset in offsetsToTry)
            {
                for (int i = 0; i < bufferSize; i++)
                    Marshal.WriteByte(buffer, i, 0);

                Marshal.WriteInt32(buffer, 0, (int)D3DKMT_QUERYSTATISTICS_TYPE.D3DKMT_QUERYSTATISTICS_PROCESS_ADAPTER);
                Marshal.WriteInt32(buffer, 4, (int)adapterLuid.LowPart);
                Marshal.WriteInt32(buffer, 8, adapterLuid.HighPart);
                Marshal.WriteIntPtr(buffer, hProcessOffset, processHandle);

                var status = D3DKMTQueryStatistics(buffer);
                if (status == 0)
                {
                    _workingProcessHandleOffset = hProcessOffset;
                    return status;
                }
            }

            // Return last status if none worked
            return -1;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private int _workingProcessHandleOffset = 16; // Default, will be updated by test

    /// <summary>
    /// Extended test that tries many different struct layouts based on Windows SDK
    /// </summary>
    private string TestProcessAdapterQueryExtended(LUID adapterLuid, IntPtr processHandle)
    {
        const int bufferSize = 4096;
        IntPtr buffer = Marshal.AllocHGlobal(bufferSize);
        var sb = new System.Text.StringBuilder();
        try
        {
            // Based on Windows SDK d3dkmthk.h, try using proper struct marshaling
            // The D3DKMT_QUERYSTATISTICS struct on x64 should be:
            // - Type: UINT (4 bytes) at offset 0
            // - AdapterLuid: LUID (8 bytes) at offset 4
            // - hProcess: HANDLE (8 bytes) - but where exactly?
            // - QueryResult: Large union

            // Try using StructLayout with explicit offsets via managed struct
            var result = TestWithManagedStruct(adapterLuid, processHandle);
            sb.AppendLine(result);

            return sb.ToString();
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>
    /// Test using properly marshaled struct - tries multiple layouts
    /// </summary>
    private string TestWithManagedStruct(LUID adapterLuid, IntPtr processHandle)
    {
        var sb = new System.Text.StringBuilder();

        // Layout 1: Original managed struct
        sb.AppendLine("=== Layout 1: Sequential with byte array ===");
        {
            var queryStats = new D3DKMT_QUERYSTATISTICS_MANAGED();
            queryStats.Type = D3DKMT_QUERYSTATISTICS_TYPE.D3DKMT_QUERYSTATISTICS_PROCESS_ADAPTER;
            queryStats.AdapterLuid = adapterLuid;
            queryStats.hProcess = processHandle;
            queryStats.QueryResult = new byte[2048];

            int structSize = Marshal.SizeOf<D3DKMT_QUERYSTATISTICS_MANAGED>();
            sb.AppendLine($"Size: {structSize}, hProcess@{Marshal.OffsetOf<D3DKMT_QUERYSTATISTICS_MANAGED>("hProcess")}, QueryResult@{Marshal.OffsetOf<D3DKMT_QUERYSTATISTICS_MANAGED>("QueryResult")}");

            IntPtr buffer = Marshal.AllocHGlobal(structSize);
            try
            {
                Marshal.StructureToPtr(queryStats, buffer, false);
                var status = D3DKMTQueryStatistics(buffer);
                sb.AppendLine($"Status: 0x{status:X}");
                if (status == 0) sb.AppendLine("SUCCESS!");
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        // Layout 2: V2 struct with QueryUnion after QueryResult (GCMonitor style)
        sb.AppendLine("=== Layout 2: V2 with QueryUnion ===");
        {
            var queryStats = new D3DKMT_QUERYSTATISTICS_V2();
            queryStats.Type = D3DKMT_QUERYSTATISTICS_TYPE.D3DKMT_QUERYSTATISTICS_PROCESS_ADAPTER;
            queryStats.AdapterLuid = adapterLuid;
            queryStats.hProcess = processHandle;

            int structSize = Marshal.SizeOf<D3DKMT_QUERYSTATISTICS_V2>();
            sb.AppendLine($"Size: {structSize}");
            sb.AppendLine($"hProcess@{Marshal.OffsetOf<D3DKMT_QUERYSTATISTICS_V2>("hProcess")}");
            sb.AppendLine($"QueryResult@{Marshal.OffsetOf<D3DKMT_QUERYSTATISTICS_V2>("QueryResult")}");
            sb.AppendLine($"QueryUnion@{Marshal.OffsetOf<D3DKMT_QUERYSTATISTICS_V2>("QueryUnion")}");

            IntPtr buffer = Marshal.AllocHGlobal(structSize);
            try
            {
                Marshal.StructureToPtr(queryStats, buffer, false);
                var status = D3DKMTQueryStatistics(buffer);
                sb.AppendLine($"Status: 0x{status:X}");
                if (status == 0) sb.AppendLine("SUCCESS!");
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        // Layout 3: Try PROCESS_NODE query with V2 struct
        sb.AppendLine("=== Layout 3: V2 PROCESS_NODE query ===");
        {
            var queryStats = new D3DKMT_QUERYSTATISTICS_V2();
            queryStats.Type = D3DKMT_QUERYSTATISTICS_TYPE.D3DKMT_QUERYSTATISTICS_PROCESS_NODE;
            queryStats.AdapterLuid = adapterLuid;
            queryStats.hProcess = processHandle;
            queryStats.QueryUnion.NodeId = 0;

            int structSize = Marshal.SizeOf<D3DKMT_QUERYSTATISTICS_V2>();
            IntPtr buffer = Marshal.AllocHGlobal(structSize);
            try
            {
                Marshal.StructureToPtr(queryStats, buffer, false);
                var status = D3DKMTQueryStatistics(buffer);
                sb.AppendLine($"Status: 0x{status:X}");
                if (status == 0)
                {
                    var result = Marshal.PtrToStructure<D3DKMT_QUERYSTATISTICS_V2>(buffer);
                    sb.AppendLine($"SUCCESS! RunningTime={result.QueryResult.RunningTime}");
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        return sb.ToString();
    }

    // Managed struct matching Windows SDK layout
    [StructLayout(LayoutKind.Sequential)]
    private struct D3DKMT_QUERYSTATISTICS_MANAGED
    {
        public D3DKMT_QUERYSTATISTICS_TYPE Type;
        public LUID AdapterLuid;
        public IntPtr hProcess;
        // QueryResult is a large union - we just need enough space
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2048)]
        public byte[] QueryResult;
    }

    // Alternative struct based on GCMonitor implementation
    // QueryResult comes first, then QueryUnion for input
    [StructLayout(LayoutKind.Sequential)]
    private struct D3DKMT_QUERYSTATISTICS_V2
    {
        public D3DKMT_QUERYSTATISTICS_TYPE Type;
        public LUID AdapterLuid;
        public IntPtr hProcess;
        public D3DKMT_QUERYSTATISTICS_RESULT_V2 QueryResult;
        public D3DKMT_QUERYSTATISTICS_QUERY_UNION QueryUnion;
    }

    [StructLayout(LayoutKind.Explicit, Size = 1024)]
    private struct D3DKMT_QUERYSTATISTICS_RESULT_V2
    {
        [FieldOffset(0)]
        public long RunningTime;  // For ProcessNodeInformation
        [FieldOffset(0)]
        public uint NbSegments;   // For ProcessAdapterInformation
        [FieldOffset(4)]
        public uint NodeCount;
        [FieldOffset(0)]
        public long BytesCommitted; // For ProcessSegmentInformation
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct D3DKMT_QUERYSTATISTICS_QUERY_UNION
    {
        [FieldOffset(0)]
        public uint NodeId;
        [FieldOffset(0)]
        public uint SegmentId;
    }

    private int _lastSegmentQueryStatus;
    private const uint PROCESS_QUERY_INFORMATION = 0x0400;
    private const uint PROCESS_VM_READ = 0x0010;

    /// <summary>
    /// Get per-process GPU metrics for a list of process IDs
    /// </summary>
    public List<ProcessGpuMetrics> GetProcessGpuMetrics(IEnumerable<int> processIds)
    {
        var results = new List<ProcessGpuMetrics>();
        if (!_initialized || !OperatingSystem.IsWindows()) return results;

        var now = DateTime.UtcNow;
        var elapsedMs = _lastSampleTime != DateTime.MinValue
            ? (now - _lastSampleTime).TotalMilliseconds
            : 0;

        foreach (var pid in processIds)
        {
            try
            {
                var metrics = QueryProcessGpuUsage(pid, elapsedMs);
                if (metrics != null)
                {
                    results.Add(metrics);
                }
            }
            catch
            {
                // Process may have exited or access denied
            }
        }

        _lastSampleTime = now;
        return results;
    }

    private ProcessGpuMetrics? QueryProcessGpuUsage(int processId, double elapsedMs)
    {
        IntPtr processHandle = IntPtr.Zero;
        try
        {
            // Open process with query rights - try PROCESS_QUERY_INFORMATION first
            // as D3DKMT queries may require more access than PROCESS_QUERY_LIMITED_INFORMATION
            processHandle = OpenProcess(PROCESS_QUERY_INFORMATION, false, (uint)processId);
            if (processHandle == IntPtr.Zero)
            {
                // Fallback to limited info
                processHandle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)processId);
            }
            if (processHandle == IntPtr.Zero)
                return null;

            long totalRunningTime = 0;
            long totalDedicatedMemory = 0;
            long totalSharedMemory = 0;
            long primaryAdapterLuid = 0;
            int primaryGpuIndex = 0;

            foreach (var adapter in _adapters)
            {
                var luid = new LUID
                {
                    LowPart = (uint)(adapter.Luid & 0xFFFFFFFF),
                    HighPart = (int)(adapter.Luid >> 32)
                };

                // Query all nodes for this adapter and process
                for (uint nodeId = 0; nodeId < adapter.NodeCount; nodeId++)
                {
                    var runningTime = QueryProcessNodeRunningTime(luid, processHandle, nodeId);
                    if (runningTime > 0)
                    {
                        totalRunningTime += runningTime;
                        if (primaryAdapterLuid == 0)
                        {
                            primaryAdapterLuid = adapter.Luid;
                            primaryGpuIndex = adapter.Index;
                        }
                    }
                }

                // Query segment memory usage - separate dedicated vs shared
                for (uint segmentId = 0; segmentId < adapter.SegmentCount; segmentId++)
                {
                    var committed = QueryProcessSegmentMemory(luid, processHandle, segmentId);
                    if (committed > 0)
                    {
                        // Check if this segment is aperture (shared) or local (dedicated)
                        bool isAperture = adapter.SegmentIsAperture.TryGetValue(segmentId, out var aperture) && aperture;
                        if (isAperture)
                        {
                            totalSharedMemory += committed;
                        }
                        else
                        {
                            totalDedicatedMemory += committed;
                        }
                    }
                }
            }

            // Calculate GPU utilization from running time delta
            double gpuUtilization = 0;
            var key = (processId, primaryAdapterLuid);
            if (_lastRunningTimes.TryGetValue(key, out var lastRunningTime) && elapsedMs > 0)
            {
                var runningTimeDelta = totalRunningTime - lastRunningTime;
                if (runningTimeDelta >= 0)
                {
                    // Running time is in 100ns units, convert to milliseconds
                    var runningTimeMs = runningTimeDelta / 10000.0;
                    gpuUtilization = (runningTimeMs / elapsedMs) * 100.0;
                    gpuUtilization = Math.Clamp(gpuUtilization, 0, 100);
                }
            }
            _lastRunningTimes[key] = totalRunningTime;

            if (totalRunningTime > 0 || totalDedicatedMemory > 0 || totalSharedMemory > 0)
            {
                string processName = "";
                try
                {
                    using var proc = Process.GetProcessById(processId);
                    processName = proc.ProcessName;
                }
                catch { }

                return new ProcessGpuMetrics
                {
                    ProcessId = processId,
                    ProcessName = processName,
                    GpuAdapterLuid = primaryAdapterLuid,
                    GpuIndex = primaryGpuIndex,
                    RunningTimeNs = totalRunningTime,
                    DedicatedMemoryBytes = totalDedicatedMemory,
                    SharedMemoryBytes = totalSharedMemory,
                    GpuUtilization = gpuUtilization,
                    Timestamp = DateTime.UtcNow
                };
            }

            return null;
        }
        finally
        {
            if (processHandle != IntPtr.Zero)
                CloseHandle(processHandle);
        }
    }

    /// <summary>
    /// Query process node running time using raw memory access
    /// </summary>
    private long QueryProcessNodeRunningTime(LUID adapterLuid, IntPtr processHandle, uint nodeId)
    {
        // Allocate buffer for D3DKMT_QUERYSTATISTICS
        // Use large buffer - the struct size varies by Windows version
        const int bufferSize = 2048;
        IntPtr buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            // Zero the buffer
            for (int i = 0; i < bufferSize; i++)
                Marshal.WriteByte(buffer, i, 0);

            // D3DKMT_QUERYSTATISTICS layout on x64 (WDDM 2.0+):
            // Offset 0:  Type (4 bytes)
            // Offset 4:  AdapterLuid.LowPart (4 bytes)
            // Offset 8:  AdapterLuid.HighPart (4 bytes)
            // Offset 12: padding (4 bytes) for 8-byte alignment of hProcess
            // Offset 16: hProcess (8 bytes)
            // Offset 24: QueryResult union start
            //
            // For PROCESS_NODE queries, the NodeId input is at the start of QueryResult
            // The kernel reads NodeId, then writes the result starting at the same offset
            Marshal.WriteInt32(buffer, 0, (int)D3DKMT_QUERYSTATISTICS_TYPE.D3DKMT_QUERYSTATISTICS_PROCESS_NODE);
            Marshal.WriteInt32(buffer, 4, (int)adapterLuid.LowPart);
            Marshal.WriteInt32(buffer, 8, adapterLuid.HighPart);
            Marshal.WriteIntPtr(buffer, 16, processHandle);
            // NodeId goes at offset 24 (start of QueryResult)
            Marshal.WriteInt32(buffer, 24, (int)nodeId);

            var status = D3DKMTQueryStatistics(buffer);
            _lastQueryStatus = status;

            if (status == 0) // STATUS_SUCCESS
            {
                // RunningTime is at offset 24 (first field of ProcessNodeInformation)
                return Marshal.ReadInt64(buffer, 24);
            }

            // Debug: try reading at different offsets if status is error
            _debugInfo = $"Status=0x{status:X}, Offsets[24]={Marshal.ReadInt64(buffer, 24)}, [32]={Marshal.ReadInt64(buffer, 32)}, [40]={Marshal.ReadInt64(buffer, 40)}";

            return 0;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private string _debugInfo = "";

    private int _lastQueryStatus;

    /// <summary>
    /// Query process segment memory using raw memory access
    /// For PROCESS_SEGMENT queries, the D3DKMT_QUERYSTATISTICS_PROCESS_SEGMENT_INFORMATION result
    /// contains BytesCommitted as the first field at the start of QueryResult.
    /// </summary>
    private long QueryProcessSegmentMemory(LUID adapterLuid, IntPtr processHandle, uint segmentId)
    {
        const int bufferSize = 1024;
        IntPtr buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            for (int i = 0; i < bufferSize; i++)
                Marshal.WriteByte(buffer, i, 0);

            // D3DKMT_QUERYSTATISTICS layout on x64:
            // Offset 0:  Type (4 bytes)
            // Offset 4:  AdapterLuid.LowPart (4 bytes)
            // Offset 8:  AdapterLuid.HighPart (4 bytes)
            // Offset 12: padding (4 bytes for 8-byte alignment)
            // Offset 16: hProcess (8 bytes)
            // Offset 24: QueryResult union start - for input, this contains SegmentId
            Marshal.WriteInt32(buffer, 0, (int)D3DKMT_QUERYSTATISTICS_TYPE.D3DKMT_QUERYSTATISTICS_PROCESS_SEGMENT);
            Marshal.WriteInt32(buffer, 4, (int)adapterLuid.LowPart);
            Marshal.WriteInt32(buffer, 8, adapterLuid.HighPart);
            Marshal.WriteIntPtr(buffer, 16, processHandle);
            Marshal.WriteInt32(buffer, 24, (int)segmentId);  // SegmentId input at start of QueryResult

            var status = D3DKMTQueryStatistics(buffer);
            _lastSegmentQueryStatus = status;
            if (status == 0)
            {
                // Read BytesCommitted from QueryResult
                // D3DKMT_QUERYSTATISTICS_PROCESS_SEGMENT_INFORMATION starts at offset 24
                // BytesCommitted (ULONGLONG) is at offset 0 within the structure
                // Try multiple offsets as layout may vary by Windows version
                var bytesAt24 = Marshal.ReadInt64(buffer, 24);
                var bytesAt32 = Marshal.ReadInt64(buffer, 32);

                // Return the larger non-zero value (BytesCommitted should be > 0 if segment is in use)
                if (bytesAt24 > 0) return bytesAt24;
                if (bytesAt32 > 0) return bytesAt32;
            }
            return 0;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private void EnumerateGpuAdapters()
    {
        _adapters.Clear();

        // Get device interface list size
        uint size = 0;
        var guid = GUID_DISPLAY_DEVICE_ARRIVAL;
        var result = CM_Get_Device_Interface_List_Size(
            ref size,
            ref guid,
            null,
            CM_GET_DEVICE_INTERFACE_LIST_PRESENT);

        if (result != 0 || size == 0)
            return;

        // Get device interface list
        var buffer = new char[size];
        result = CM_Get_Device_Interface_List(
            ref guid,
            null,
            buffer,
            size,
            CM_GET_DEVICE_INTERFACE_LIST_PRESENT);

        if (result != 0)
            return;

        // Parse device names
        var deviceList = new string(buffer);
        var devices = deviceList.Split('\0', StringSplitOptions.RemoveEmptyEntries);

        int gpuIndex = 0;
        foreach (var deviceName in devices)
        {
            if (string.IsNullOrEmpty(deviceName))
                continue;

            // Skip Microsoft Basic Render Driver - it's a software renderer, not a real GPU
            if (deviceName.Contains("BasicRender", StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                var adapter = OpenAdapter(deviceName);
                if (adapter != null)
                {
                    adapter.Index = gpuIndex++;
                    adapter.DeviceName = deviceName;
                    _adapters.Add(adapter);
                }
            }
            catch
            {
                // Skip failed adapters
            }
        }
    }

    private GpuAdapterInfo? OpenAdapter(string deviceName)
    {
        // Allocate buffer for D3DKMT_OPENADAPTERFROMDEVICENAME
        // Layout: pDeviceName (IntPtr) + hAdapter (uint) + padding + AdapterLuid (8)
        const int structSize = 32;
        IntPtr structBuffer = Marshal.AllocHGlobal(structSize);
        IntPtr deviceNamePtr = Marshal.StringToHGlobalUni(deviceName);

        try
        {
            for (int i = 0; i < structSize; i++)
                Marshal.WriteByte(structBuffer, i, 0);

            Marshal.WriteIntPtr(structBuffer, 0, deviceNamePtr);  // pDeviceName

            var status = D3DKMTOpenAdapterFromDeviceName(structBuffer);
            if (status != 0)
                return null;

            // Read results
            uint hAdapter = (uint)Marshal.ReadInt32(structBuffer, IntPtr.Size);
            uint luidLow = (uint)Marshal.ReadInt32(structBuffer, IntPtr.Size + 4);
            int luidHigh = Marshal.ReadInt32(structBuffer, IntPtr.Size + 8);
            var luid = ((long)luidHigh << 32) | luidLow;

            // Query adapter info to get node/segment count
            var adapterInfo = QueryAdapterInfo(new LUID { LowPart = luidLow, HighPart = luidHigh });

            // Close adapter handle
            CloseAdapter(hAdapter);

            if (adapterInfo != null)
            {
                adapterInfo.Luid = luid;
                return adapterInfo;
            }

            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(structBuffer);
            Marshal.FreeHGlobal(deviceNamePtr);
        }
    }

    private GpuAdapterInfo? QueryAdapterInfo(LUID adapterLuid)
    {
        const int bufferSize = 1024;
        IntPtr buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            for (int i = 0; i < bufferSize; i++)
                Marshal.WriteByte(buffer, i, 0);

            Marshal.WriteInt32(buffer, 0, (int)D3DKMT_QUERYSTATISTICS_TYPE.D3DKMT_QUERYSTATISTICS_ADAPTER);
            Marshal.WriteInt32(buffer, 4, (int)adapterLuid.LowPart);
            Marshal.WriteInt32(buffer, 8, adapterLuid.HighPart);

            var status = D3DKMTQueryStatistics(buffer);
            if (status == 0)
            {
                // Read AdapterInformation from QueryResult (starts at offset 32)
                // NbSegments is at offset 0, NodeCount at offset 4
                uint nbSegments = (uint)Marshal.ReadInt32(buffer, 32);
                uint nodeCount = (uint)Marshal.ReadInt32(buffer, 36);

                var adapterInfo = new GpuAdapterInfo
                {
                    NodeCount = nodeCount,
                    SegmentCount = nbSegments
                };

                // Query segment types to determine which are aperture (shared) vs local (dedicated)
                for (uint segmentId = 0; segmentId < nbSegments; segmentId++)
                {
                    var isAperture = QuerySegmentIsAperture(adapterLuid, segmentId);
                    adapterInfo.SegmentIsAperture[segmentId] = isAperture;
                }

                return adapterInfo;
            }
            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>
    /// Query whether a segment is an aperture segment (shared/system memory) or local (dedicated VRAM)
    /// </summary>
    private bool QuerySegmentIsAperture(LUID adapterLuid, uint segmentId)
    {
        const int bufferSize = 1024;
        IntPtr buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            for (int i = 0; i < bufferSize; i++)
                Marshal.WriteByte(buffer, i, 0);

            // Query SEGMENT statistics (adapter-level, no process handle)
            Marshal.WriteInt32(buffer, 0, (int)D3DKMT_QUERYSTATISTICS_TYPE.D3DKMT_QUERYSTATISTICS_SEGMENT);
            Marshal.WriteInt32(buffer, 4, (int)adapterLuid.LowPart);
            Marshal.WriteInt32(buffer, 8, adapterLuid.HighPart);
            // hProcess at offset 16 = NULL for adapter-level query
            Marshal.WriteInt32(buffer, 24, (int)segmentId);  // SegmentId at offset 24

            var status = D3DKMTQueryStatistics(buffer);
            if (status == 0)
            {
                // D3DKMT_QUERYSTATISTICS_SEGMENT_INFORMATION structure:
                // The Aperture flag is typically part of the segment flags
                // In the result, we need to check for the aperture flag
                // Based on Windows SDK, the segment info contains:
                // - CommitLimit, BytesCommitted, etc. (64-bit values)
                // - Flags which includes Aperture bit

                // The exact layout varies, but typically:
                // Offset 24+0: CommitLimit (8 bytes)
                // Offset 24+8: BytesCommitted (8 bytes)
                // ... more stats ...
                // The Aperture flag is usually in a flags field

                // A simpler heuristic: segment 0 is typically local (dedicated VRAM),
                // while higher segments are often aperture (shared)
                // For more accurate detection, we'd need to parse the full structure

                // Try to read a flags field - common layouts put it around offset 56-64
                // The Aperture bit is typically bit 0 of the flags field
                var flags = Marshal.ReadInt32(buffer, 32 + 56);  // Try offset 56 from QueryResult start
                if (flags != 0)
                {
                    return (flags & 1) != 0;  // Aperture is bit 0
                }

                // Fallback: try another common offset
                flags = Marshal.ReadInt32(buffer, 32 + 48);
                if (flags != 0)
                {
                    return (flags & 1) != 0;
                }

                // If we can't determine, use heuristic: segment 0 is local, others may be aperture
                // This is a rough approximation but better than nothing
                return segmentId > 0;
            }
            return segmentId > 0;  // Default heuristic
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private void CloseAdapter(uint hAdapter)
    {
        IntPtr buffer = Marshal.AllocHGlobal(4);
        try
        {
            Marshal.WriteInt32(buffer, 0, (int)hAdapter);
            D3DKMTCloseAdapter(buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public void Dispose()
    {
        _lastRunningTimes.Clear();
        _adapters.Clear();
    }

    #region P/Invoke Declarations

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const uint CM_GET_DEVICE_INTERFACE_LIST_PRESENT = 0;

    private static readonly Guid GUID_DISPLAY_DEVICE_ARRIVAL = new("1CA05180-A699-450A-9A0C-DE4FBE3DDD89");

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern uint CM_Get_Device_Interface_List_Size(
        ref uint pulLen,
        ref Guid InterfaceClassGuid,
        string? pDeviceID,
        uint ulFlags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern uint CM_Get_Device_Interface_List(
        ref Guid InterfaceClassGuid,
        string? pDeviceID,
        [Out] char[] Buffer,
        uint BufferLen,
        uint ulFlags);

    [DllImport("gdi32.dll")]
    private static extern int D3DKMTOpenAdapterFromDeviceName(IntPtr pData);

    [DllImport("gdi32.dll")]
    private static extern int D3DKMTCloseAdapter(IntPtr pData);

    [DllImport("gdi32.dll")]
    private static extern int D3DKMTQueryStatistics(IntPtr pData);

    #endregion

    #region Structures

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    private enum D3DKMT_QUERYSTATISTICS_TYPE
    {
        D3DKMT_QUERYSTATISTICS_ADAPTER = 0,
        D3DKMT_QUERYSTATISTICS_PROCESS = 1,
        D3DKMT_QUERYSTATISTICS_PROCESS_ADAPTER = 2,
        D3DKMT_QUERYSTATISTICS_SEGMENT = 3,
        D3DKMT_QUERYSTATISTICS_PROCESS_SEGMENT = 4,
        D3DKMT_QUERYSTATISTICS_NODE = 5,
        D3DKMT_QUERYSTATISTICS_PROCESS_NODE = 6,
        D3DKMT_QUERYSTATISTICS_VIDPNSOURCE = 7,
        D3DKMT_QUERYSTATISTICS_PROCESS_VIDPNSOURCE = 8
    }

    #endregion
}
