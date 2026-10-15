namespace CameraMaintainer.Core.Models;

/// <summary>
/// Parsed ONVIF service endpoint URLs extracted from GetCapabilities response.
/// Cached per camera to avoid redundant SOAP round-trips.
/// </summary>
public class OnvifServiceUrls
{
    /// <summary>Base device service URL (the original serviceUrl).</summary>
    public string DeviceServiceUrl { get; init; } = string.Empty;

    /// <summary>PTZ service endpoint URL.</summary>
    public string? PtzUrl { get; init; }

    /// <summary>Media service endpoint URL.</summary>
    public string? MediaUrl { get; init; }

    /// <summary>Events / PullPoint subscription endpoint URL.</summary>
    public string? EventsUrl { get; init; }

    /// <summary>Imaging service endpoint URL.</summary>
    public string? ImagingUrl { get; init; }

    /// <summary>Network interface configuration endpoint URL.</summary>
    public string? NetworkUrl { get; init; }

    /// <summary>Analytics service endpoint URL.</summary>
    public string? AnalyticsUrl { get; init; }

    /// <summary>When these URLs were parsed from the camera.</summary>
    public DateTime FetchedAt { get; init; } = DateTime.UtcNow;

    /// <summary>Whether the capability XML indicated PTZ support.</summary>
    public bool PtzSupported { get; init; }

    /// <summary>Whether the capability XML indicated event support.</summary>
    public bool EventsSupported { get; init; }

    /// <summary>Raw capabilities XML (for debugging).</summary>
    public string RawXml { get; init; } = string.Empty;
}
