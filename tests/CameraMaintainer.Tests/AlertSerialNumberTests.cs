using CameraMaintainer.Core.Models;

namespace CameraMaintainer.Tests;

public sealed class AlertSerialNumberTests
{
    [Theory]
    [InlineData("34", 1, 539, "344-1")]
    [InlineData("Camera 34", 12, 540, "CA4-12")]
    [InlineData("Я", 3, 541, "ЯЯ-3")]
    public void Format_UsesCameraNameAndPerCameraSequence(
        string cameraName,
        int sequence,
        int fallbackId,
        string expected)
    {
        Assert.Equal(expected, AlertSerialNumber.Format(cameraName, sequence, fallbackId));
    }

    [Fact]
    public void Format_UsesSystemFallbackWithoutCamera()
    {
        Assert.Equal("SYS-539", AlertSerialNumber.Format(null, null, 539));
    }
}
