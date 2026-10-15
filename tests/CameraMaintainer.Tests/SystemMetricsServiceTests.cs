using CameraMaintainer.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace CameraMaintainer.Tests;

public sealed class SystemMetricsServiceTests
{
    [Fact]
    public async Task Snapshot_ReportsWindowsMemoryAndConfiguredCheckCapacity()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var gate = new HealthCheckConcurrencyGate(8);
        using var metrics = new SystemMetricsService(
            gate,
            new CameraCheckParallelism(4, 8, "test"),
            NullLogger<SystemMetricsService>.Instance);

        await Task.Delay(TimeSpan.FromMilliseconds(2100));
        var snapshot = metrics.GetSnapshot();

        Assert.InRange(snapshot.CpuPercent, 0, 100);
        Assert.InRange(snapshot.ProcessCpuPercent, 0, 100);
        Assert.True(snapshot.MemoryTotalMb > 0);
        Assert.InRange(snapshot.MemoryUsedMb, 1, snapshot.MemoryTotalMb);
        Assert.Equal(8, snapshot.MaxConcurrentChecks);
        Assert.Equal(4, snapshot.PhysicalCoreCount);
        Assert.Equal(8, snapshot.LogicalProcessorCount);
    }

    [Fact]
    public void SystemCpu_ExcludesIdleTimeFromTheWindowsCounter()
    {
        var percent = SystemMetricsService.CalculateSystemCpuPercent(
            previousIdle: 100,
            previousKernel: 300,
            previousUser: 100,
            idle: 140,
            kernel: 380,
            user: 140);

        Assert.Equal(66.7, percent);
    }

    [Fact]
    public void SystemCpu_ReturnsZeroForInvalidOrEmptySamples()
    {
        Assert.Equal(0, SystemMetricsService.CalculateSystemCpuPercent(100, 300, 100, 99, 380, 140));
        Assert.Equal(0, SystemMetricsService.CalculateSystemCpuPercent(100, 300, 100, 100, 300, 100));
    }

    [Fact]
    public void ProcessCpu_IsNormalizedAcrossLogicalProcessors()
    {
        var percent = SystemMetricsService.CalculateProcessCpuPercent(
            cpuDelta: TimeSpan.FromSeconds(2),
            elapsed: TimeSpan.FromSeconds(4),
            logicalProcessors: 8);

        Assert.Equal(6.3, percent);
    }

    [Fact]
    public void ProcessCpu_ReturnsZeroForInvalidIntervals()
    {
        Assert.Equal(0, SystemMetricsService.CalculateProcessCpuPercent(TimeSpan.Zero, TimeSpan.FromSeconds(1), 8));
        Assert.Equal(0, SystemMetricsService.CalculateProcessCpuPercent(TimeSpan.FromSeconds(1), TimeSpan.Zero, 8));
        Assert.Equal(0, SystemMetricsService.CalculateProcessCpuPercent(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), 0));
    }
}
