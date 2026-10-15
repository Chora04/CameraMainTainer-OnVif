using CameraMaintainer.Core.Enums;
using CameraMaintainer.Core.Models;
using CameraMaintainer.Services;

namespace CameraMaintainer.Tests;

public sealed class CameraDirectionAnalyzerTests
{
    private readonly CameraDirectionAnalyzer _analyzer = new();

    [Fact]
    public void Analyze_InvalidFrame_ReturnsUnknown()
    {
        var result = _analyzer.Analyze(new FrameData());

        Assert.Equal(DirectionStatus.Unknown, result.Status);
        Assert.Equal(0, result.Confidence);
    }

    [Theory]
    [InlineData(0, DirectionStatus.DarkScene)]
    [InlineData(10, DirectionStatus.DarkScene)]
    [InlineData(128, DirectionStatus.WallObstructed)]
    [InlineData(250, DirectionStatus.Overexposed)]
    public void Analyze_UniformFrame_ReturnsExpectedStatus(
        byte brightness,
        DirectionStatus expected)
    {
        var frame = SolidFrame(64, 48, brightness);

        var result = _analyzer.Analyze(frame);

        Assert.Equal(expected, result.Status);
        Assert.InRange(result.Confidence, 0.7, 1.0);
    }

    [Fact]
    public void Baseline_CanBeStoredReadAndReset()
    {
        var histogram = Enumerable.Range(0, 256).ToArray();

        _analyzer.UpdateBaseline(7, histogram);

        Assert.Equal(histogram, _analyzer.GetBaseline(7));
        _analyzer.ResetBaseline(7);
        Assert.Null(_analyzer.GetBaseline(7));
    }

    [Fact]
    public void Analyze_HighDetailScene_DoesNotReportObstruction()
    {
        const int width = 128;
        const int height = 96;
        var pixels = new byte[width * height];
        var random = new Random(32);
        random.NextBytes(pixels);

        var result = _analyzer.Analyze(new FrameData
        {
            Width = width,
            Height = height,
            Pixels = pixels,
            IsValid = true
        });

        Assert.Equal(DirectionStatus.Normal, result.Status);
        Assert.True(result.BrightnessStdDev >= 24);
        Assert.True(result.Entropy >= 5.5);
    }

    private static FrameData SolidFrame(int width, int height, byte value)
    {
        return new FrameData
        {
            Width = width,
            Height = height,
            Pixels = Enumerable.Repeat(value, width * height).ToArray(),
            IsValid = true
        };
    }
}
