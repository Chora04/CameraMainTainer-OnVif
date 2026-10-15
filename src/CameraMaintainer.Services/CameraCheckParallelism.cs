using System.Buffers.Binary;
using System.ComponentModel;
using System.Numerics;
using System.Runtime.InteropServices;

namespace CameraMaintainer.Services;

public sealed record CameraCheckParallelism(int PhysicalCoreCount, int LogicalProcessorCount, string DetectionSource, string? Warning = null)
{
    public const int ChecksPerCore = 2;
    public int MaxConcurrency => checked(PhysicalCoreCount * ChecksPerCore);

    public static CameraCheckParallelism Detect()
    {
        try
        {
            if (!OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException("Physical-core detection currently requires Windows.");

            uint length = 0;
            GetLogicalProcessorInformationEx(0, null, ref length);
            if (Marshal.GetLastWin32Error() != 122)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            for (var attempt = 0; attempt < 3; attempt++)
            {
                if (length == 0 || length > 1024 * 1024)
                    throw new InvalidDataException("Invalid processor topology buffer length.");
                var buffer = new byte[length];
                if (!GetLogicalProcessorInformationEx(0, buffer, ref length))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == 122) continue;
                    throw new Win32Exception(error);
                }

                ulong? affinityMask = null;
                // A single affinity mask cannot describe multiple processor groups.
                // On large systems use all reported cores, bounded by the runtime CPU allowance.
                if (GetActiveProcessorGroupCount() == 1)
                {
                    if (!GetProcessAffinityMask(new IntPtr(-1), out var processMask, out _))
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    affinityMask = (ulong)processMask;
                }
                return FromWindowsTopology(buffer.AsSpan(0, checked((int)length)), IntPtr.Size,
                    Environment.ProcessorCount, affinityMask);
            }
            throw new InvalidOperationException("Processor topology changed while it was being read.");
        }
        catch (Exception error) when (error is Win32Exception or InvalidDataException or InvalidOperationException
            or NotSupportedException or DllNotFoundException or EntryPointNotFoundException)
        {
            // Never guess that logical CPUs are physical cores when topology is unavailable.
            return new(1, Math.Max(1, Environment.ProcessorCount), "conservative fallback", error.Message);
        }
    }

    internal static CameraCheckParallelism FromWindowsTopology(ReadOnlySpan<byte> buffer, int pointerSize,
        int runtimeProcessorCount, ulong? affinityMask = null)
    {
        if (pointerSize is not (4 or 8)) throw new ArgumentOutOfRangeException(nameof(pointerSize));
        var cores = 0;
        var logical = 0;
        var offset = 0;
        // SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX: 8-byte header, then the
        // 24-byte PROCESSOR_RELATIONSHIP prefix and variable GROUP_AFFINITY entries.
        while (offset < buffer.Length)
        {
            var record = buffer[offset..];
            if (record.Length < 8) throw new InvalidDataException("Truncated processor topology header.");
            var relation = BinaryPrimitives.ReadInt32LittleEndian(record);
            var size = BinaryPrimitives.ReadInt32LittleEndian(record[4..]);
            if (size < 8 || size > record.Length) throw new InvalidDataException("Invalid processor topology record.");
            record = record[..size];
            if (relation == 0)
            {
                if (size < 40 + pointerSize || BinaryPrimitives.ReadUInt16LittleEndian(record[30..]) != 1)
                    throw new InvalidDataException("Invalid physical-core relationship.");
                var mask = pointerSize == 8
                    ? BinaryPrimitives.ReadUInt64LittleEndian(record[32..])
                    : BinaryPrimitives.ReadUInt32LittleEndian(record[32..]);
                if (affinityMask.HasValue) mask &= affinityMask.Value;
                if (mask != 0)
                {
                    cores++;
                    logical += BitOperations.PopCount(mask);
                }
            }
            offset += size;
        }
        if (cores == 0) throw new InvalidDataException("No available physical cores were reported by Windows.");
        logical = Math.Min(logical, Math.Max(1, runtimeProcessorCount));
        return new(Math.Min(cores, logical), logical, "Windows GetLogicalProcessorInformationEx");
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLogicalProcessorInformationEx(int relationship, [Out] byte[]? buffer, ref uint returnedLength);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll")]
    private static extern ushort GetActiveProcessorGroupCount();

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessAffinityMask(IntPtr process, out nuint processMask, out nuint systemMask);
}
