using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace CameraMaintainer.Services;

public sealed record SystemMetricsSnapshot(
    DateTime CapturedAt,
    double CpuPercent,
    double ProcessCpuPercent,
    double MemoryPercent,
    long MemoryUsedMb,
    long MemoryTotalMb,
    double ProcessWorkingSetMb,
    double GcHeapMb,
    int ProcessThreadCount,
    int ActiveChecks,
    int MaxConcurrentChecks,
    int PhysicalCoreCount,
    int LogicalProcessorCount,
    long UptimeSeconds);

public sealed class SystemMetricsService : IDisposable
{
    private readonly HealthCheckConcurrencyGate _concurrencyGate;
    private readonly CameraCheckParallelism _parallelism;
    private readonly ILogger<SystemMetricsService> _logger;
    private readonly Timer _timer;
    private readonly object _sampleLock = new();
    private readonly DateTime _startedAt = DateTime.UtcNow;
    private SystemMetricsSnapshot _snapshot;
    private ulong _lastIdle;
    private ulong _lastKernel;
    private ulong _lastUser;
    private TimeSpan _lastProcessCpu;
    private long _lastTimestamp;
    private bool _hasCpuBaseline;

    public SystemMetricsService(
        HealthCheckConcurrencyGate concurrencyGate,
        CameraCheckParallelism parallelism,
        ILogger<SystemMetricsService> logger)
    {
        _concurrencyGate = concurrencyGate;
        _parallelism = parallelism;
        _logger = logger;
        _snapshot = EmptySnapshot();
        Sample();
        _timer = new Timer(_ => Sample(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
    }

    public SystemMetricsSnapshot GetSnapshot()
    {
        var snapshot = Volatile.Read(ref _snapshot);
        if (DateTime.UtcNow - snapshot.CapturedAt > TimeSpan.FromSeconds(4)) Sample();
        return Volatile.Read(ref _snapshot);
    }

    internal static double CalculateSystemCpuPercent(
        ulong previousIdle,
        ulong previousKernel,
        ulong previousUser,
        ulong idle,
        ulong kernel,
        ulong user)
    {
        if (idle < previousIdle || kernel < previousKernel || user < previousUser) return 0;
        var idleDelta = idle - previousIdle;
        var totalDelta = kernel - previousKernel + user - previousUser;
        if (totalDelta == 0 || idleDelta > totalDelta) return 0;
        return Math.Round(
            Math.Clamp((double)(totalDelta - idleDelta) / totalDelta * 100, 0, 100),
            1,
            MidpointRounding.AwayFromZero);
    }

    internal static double CalculateProcessCpuPercent(TimeSpan cpuDelta, TimeSpan elapsed, int logicalProcessors)
    {
        if (cpuDelta <= TimeSpan.Zero || elapsed <= TimeSpan.Zero || logicalProcessors <= 0) return 0;
        return Math.Round(
            Math.Clamp(cpuDelta.TotalSeconds / (elapsed.TotalSeconds * logicalProcessors) * 100, 0, 100),
            1,
            MidpointRounding.AwayFromZero);
    }

    private void Sample()
    {
        if (!Monitor.TryEnter(_sampleLock)) return;
        try
        {
            using var process = Process.GetCurrentProcess();
            process.Refresh();
            var now = DateTime.UtcNow;
            var timestamp = Stopwatch.GetTimestamp();
            var processCpu = process.TotalProcessorTime;
            var current = Volatile.Read(ref _snapshot);
            var systemCpuPercent = current.CpuPercent;
            var processCpuPercent = current.ProcessCpuPercent;

            if (TryGetSystemCpuTimes(out var idle, out var kernel, out var user))
            {
                if (_hasCpuBaseline)
                    systemCpuPercent = CalculateSystemCpuPercent(_lastIdle, _lastKernel, _lastUser, idle, kernel, user);
                _lastIdle = idle;
                _lastKernel = kernel;
                _lastUser = user;
            }

            if (_hasCpuBaseline && timestamp > _lastTimestamp)
            {
                var elapsed = Stopwatch.GetElapsedTime(_lastTimestamp, timestamp);
                processCpuPercent = CalculateProcessCpuPercent(
                    processCpu - _lastProcessCpu,
                    elapsed,
                    Math.Max(1, _parallelism.LogicalProcessorCount));
            }

            _lastProcessCpu = processCpu;
            _lastTimestamp = timestamp;
            _hasCpuBaseline = true;

            var totalMemoryBytes = 0UL;
            var availableMemoryBytes = 0UL;
            TryGetPhysicalMemory(out totalMemoryBytes, out availableMemoryBytes);
            var usedMemoryBytes = totalMemoryBytes >= availableMemoryBytes
                ? totalMemoryBytes - availableMemoryBytes
                : 0;
            var memoryPercent = totalMemoryBytes > 0
                ? Math.Round((double)usedMemoryBytes / totalMemoryBytes * 100, 1)
                : 0;

            var threadCount = TryGetProcessThreadCount(process);
            Volatile.Write(ref _snapshot, new SystemMetricsSnapshot(
                CapturedAt: now,
                CpuPercent: systemCpuPercent,
                ProcessCpuPercent: processCpuPercent,
                MemoryPercent: memoryPercent,
                MemoryUsedMb: checked((long)(usedMemoryBytes / 1024 / 1024)),
                MemoryTotalMb: checked((long)(totalMemoryBytes / 1024 / 1024)),
                ProcessWorkingSetMb: Math.Round(process.WorkingSet64 / 1024d / 1024d, 1),
                GcHeapMb: Math.Round(GC.GetTotalMemory(false) / 1024d / 1024d, 1),
                ProcessThreadCount: threadCount,
                ActiveChecks: _concurrencyGate.ActiveChecks,
                MaxConcurrentChecks: _concurrencyGate.MaxConcurrency,
                PhysicalCoreCount: _parallelism.PhysicalCoreCount,
                LogicalProcessorCount: _parallelism.LogicalProcessorCount,
                UptimeSeconds: Math.Max(0, (long)(now - _startedAt).TotalSeconds)));
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "System metrics sampling failed");
        }
        finally
        {
            Monitor.Exit(_sampleLock);
        }
    }

    private SystemMetricsSnapshot EmptySnapshot() => new(
        DateTime.UtcNow, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        _concurrencyGate.MaxConcurrency,
        _parallelism.PhysicalCoreCount,
        _parallelism.LogicalProcessorCount,
        0);

    private static bool TryGetSystemCpuTimes(out ulong idle, out ulong kernel, out ulong user)
    {
        idle = kernel = user = 0;
        if (!OperatingSystem.IsWindows() || !GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
            return false;
        idle = idleTime.Value;
        kernel = kernelTime.Value;
        user = userTime.Value;
        return true;
    }

    private static bool TryGetPhysicalMemory(out ulong total, out ulong available)
    {
        total = available = 0;
        if (!OperatingSystem.IsWindows()) return false;
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref status)) return false;
        total = status.TotalPhysical;
        available = status.AvailablePhysical;
        return true;
    }

    private static int TryGetProcessThreadCount(Process process)
    {
        try
        {
            return process.Threads.Count;
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or NotSupportedException
            or Win32Exception)
        {
            return 0;
        }
    }

    public void Dispose() => _timer.Dispose();

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint Low;
        public uint High;
        public readonly ulong Value => ((ulong)High << 32) | Low;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}
