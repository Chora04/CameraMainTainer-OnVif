namespace CameraMaintainer.Core.Models;

public static class AlertSerialNumber
{
    public static string Format(string? cameraName, int? cameraSequenceNumber, int fallbackId)
    {
        if (!cameraSequenceNumber.HasValue)
            return $"SYS-{fallbackId}";

        var symbols = (cameraName ?? string.Empty)
            .Where(char.IsLetterOrDigit)
            .ToArray();
        var prefix = symbols.Length switch
        {
            0 => "CAM",
            1 => string.Concat(symbols[0], symbols[0]),
            _ => string.Concat(symbols.Take(2)) + symbols[^1]
        };

        return $"{prefix.ToUpperInvariant()}-{cameraSequenceNumber.Value}";
    }
}
