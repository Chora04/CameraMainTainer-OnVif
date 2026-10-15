using CameraMaintainer.Core.Models;

namespace CameraMaintainer.Core.Interfaces;

/// <summary>
/// Thread-safe cache for ONVIF device capabilities and service endpoint URLs.
/// Avoids redundant GetCapabilities SOAP calls by caching parsed service URLs per camera.
/// </summary>
public interface IOnvifCapabilityCache
{
    /// <summary>
    /// Get cached capabilities or fetch from camera, parse, and store.
    /// Returns null if the camera is unreachable.
    /// </summary>
    Task<OnvifServiceUrls?> GetOrFetchAsync(
        string serviceUrl,
        Func<Task<string>> fetchCapabilitiesXml,
        TimeSpan? ttl = null);

    /// <summary>
    /// Invalidate cached capabilities for a camera.
    /// Call after camera reboot, firmware upgrade, or network change.
    /// </summary>
    void Invalidate(string serviceUrl);

    /// <summary>
    /// Number of currently cached entries.
    /// </summary>
    int Count { get; }
}
