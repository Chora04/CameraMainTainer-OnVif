using System.Buffers.Binary;
using CameraMaintainer.Services;

namespace CameraMaintainer.Tests;

public sealed class CameraCheckParallelismTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(4, 1, 8)]
    [InlineData(4, 2, 8)]
    [InlineData(8, 1, 16)]
    [InlineData(8, 2, 16)]
    [InlineData(40, 2, 80)]
    public void PhysicalCores_AlwaysHaveTwoSlotsRegardlessOfSmt(int cores, int logicalPerCore, int expected)
    {
        var policy = CameraCheckParallelism.FromWindowsTopology(Topology(cores, logicalPerCore), 8, cores * logicalPerCore);
        Assert.Equal(cores, policy.PhysicalCoreCount);
        Assert.Equal(expected, policy.MaxConcurrency);
        Assert.Equal(cores * logicalPerCore, policy.LogicalProcessorCount);
    }

    [Theory]
    [InlineData(0b0011UL, 1, 2)]
    [InlineData(0b0101UL, 2, 2)]
    [InlineData(0b1111UL, 2, 4)]
    public void ProcessAffinity_CountsOnlyCoresWithAvailableLogicalProcessors(ulong affinity, int physical, int logical)
    {
        var policy = CameraCheckParallelism.FromWindowsTopology(Topology(8, 2), 8, 16, affinity);
        Assert.Equal(physical, policy.PhysicalCoreCount);
        Assert.Equal(logical, policy.LogicalProcessorCount);
        Assert.Equal(physical * 2, policy.MaxConcurrency);
    }

    [Fact]
    public void RuntimeCpuAllowance_LimitsAvailableCoreBudget()
    {
        var policy = CameraCheckParallelism.FromWindowsTopology(Topology(8, 2), 8, 2);
        Assert.Equal(2, policy.PhysicalCoreCount);
        Assert.Equal(4, policy.MaxConcurrency);
    }

    [Fact]
    public void ThirtyTwoBitLayout_IsParsedWithoutAssuming64BitPointers()
    {
        var policy = CameraCheckParallelism.FromWindowsTopology(Topology(4, 2, 4), 4, 8);
        Assert.Equal(4, policy.PhysicalCoreCount);
        Assert.Equal(8, policy.MaxConcurrency);
    }

    [Fact]
    public void HybridProcessor_DoesNotInferCoreCountFromLogicalThreadCount()
    {
        var performanceCores = Topology(8, 2);
        var efficiencyCores = Topology(4, 1, firstLogicalProcessor: 16);
        var policy = CameraCheckParallelism.FromWindowsTopology(performanceCores.Concat(efficiencyCores).ToArray(), 8, 20);
        Assert.Equal(12, policy.PhysicalCoreCount);
        Assert.Equal(24, policy.MaxConcurrency);
    }

    [Fact]
    public void CorruptOrEmptyTopology_IsNotSilentlyTreatedAsValid()
    {
        Assert.Throws<InvalidDataException>(() => CameraCheckParallelism.FromWindowsTopology([], 8, 4));
        Assert.Throws<InvalidDataException>(() => CameraCheckParallelism.FromWindowsTopology(new byte[7], 8, 4));
        Assert.Throws<InvalidDataException>(() => CameraCheckParallelism.FromWindowsTopology(new byte[48], 8, 4));
        var topology = Topology(1, 1);
        BinaryPrimitives.WriteInt32LittleEndian(topology.AsSpan(4), int.MaxValue);
        Assert.Throws<InvalidDataException>(() => CameraCheckParallelism.FromWindowsTopology(topology, 8, 4));
    }

    [Fact]
    public void RunningWindowsMachine_DetectsTopologyWithoutFallback()
    {
        var policy = CameraCheckParallelism.Detect();
        output.WriteLine($"OS topology: physical={policy.PhysicalCoreCount}, logical={policy.LogicalProcessorCount}, check slots={policy.MaxConcurrency}, source={policy.DetectionSource}");
        Assert.Equal(2 * policy.PhysicalCoreCount, policy.MaxConcurrency);
        Assert.InRange(policy.PhysicalCoreCount, 1, Environment.ProcessorCount);
        if (OperatingSystem.IsWindows()) Assert.Null(policy.Warning);
    }

    private static byte[] Topology(int cores, int logicalPerCore, int pointerSize = 8, int firstLogicalProcessor = 0)
    {
        var recordSize = 40 + pointerSize;
        var data = new byte[cores * recordSize];
        for (var core = 0; core < cores; core++)
        {
            var record = data.AsSpan(core * recordSize, recordSize);
            BinaryPrimitives.WriteInt32LittleEndian(record[4..], recordSize);
            BinaryPrimitives.WriteUInt16LittleEndian(record[30..], 1);
            var logicalIndex = firstLogicalProcessor + core * logicalPerCore;
            var groupWidth = pointerSize * 8;
            var mask = ((1UL << logicalPerCore) - 1) << (logicalIndex % groupWidth);
            if (pointerSize == 8) BinaryPrimitives.WriteUInt64LittleEndian(record[32..], mask);
            else BinaryPrimitives.WriteUInt32LittleEndian(record[32..], (uint)mask);
            BinaryPrimitives.WriteUInt16LittleEndian(record[(32 + pointerSize)..], (ushort)(logicalIndex / groupWidth));
        }
        return data;
    }
}
