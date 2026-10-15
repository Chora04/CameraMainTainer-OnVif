using System.Collections.Concurrent;
using System.Xml;
using System.Xml.Linq;
using CameraMaintainer.Core.Interfaces;
using CameraMaintainer.Core.Models;
using Microsoft.Extensions.Logging;

namespace CameraMaintainer.Services;

/// <summary>
/// Thread-safe cache for ONVIF device capabilities and service endpoint URLs.
///
/// Stores parsed service URLs (PTZ, Media, Events, Imaging, Network) per camera
/// to eliminate redundant GetCapabilities SOAP round-trips.
///
/// Cache entries expire after a configurable TTL (default: 30 minutes).
/// Capabilities don't change at runtime except after firmware upgrade or reboot.
/// </summary>
public class OnvifCapabilityCache : IOnvifCapabilityCache
{
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
    private readonly ConcurrentDictionary<string, Lazy<Task<OnvifServiceUrls?>>> _inflight = new();
    private readonly ILogger<OnvifCapabilityCache>? _logger;
    private readonly TimeSpan _defaultTtl;

    public OnvifCapabilityCache(ILogger<OnvifCapabilityCache>? logger = null, int ttlMinutes = 30)
    {
        _logger = logger;
        _defaultTtl = TimeSpan.FromMinutes(ttlMinutes);
    }

    /// <inheritdoc />
    public int Count => _cache.Count;

    /// <inheritdoc />
    public async Task<OnvifServiceUrls?> GetOrFetchAsync(
        string serviceUrl,
        Func<Task<string>> fetchCapabilitiesXml,
        TimeSpan? ttl = null)
    {
        // Check cache first
        if (_cache.TryGetValue(serviceUrl, out var entry) && !entry.IsExpired)
        {
            _logger?.LogDebug("Capability cache HIT for {Url}, age={Age:F0}s",
                serviceUrl, (DateTime.UtcNow - entry.FetchedAt).TotalSeconds);
            return entry.Urls;
        }

        _logger?.LogDebug("Capability cache MISS for {Url}, fetching...", serviceUrl);

        // Single-flight: when several cameras operations miss the same expired
        // entry at once, only one SOAP request is sent. All callers await it.
        var request = _inflight.GetOrAdd(
            serviceUrl,
            _ => new Lazy<Task<OnvifServiceUrls?>>(
                () => FetchAndCacheAsync(serviceUrl, fetchCapabilitiesXml, ttl, entry),
                LazyThreadSafetyMode.ExecutionAndPublication));
        try
        {
            return await request.Value;
        }
        finally
        {
            _inflight.TryRemove(
                new KeyValuePair<string, Lazy<Task<OnvifServiceUrls?>>>(serviceUrl, request));
        }
    }

    private async Task<OnvifServiceUrls?> FetchAndCacheAsync(
        string serviceUrl,
        Func<Task<string>> fetchCapabilitiesXml,
        TimeSpan? ttl,
        CacheEntry? staleEntry)
    {
        string rawXml;
        try
        {
            rawXml = await fetchCapabilitiesXml();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or
            XmlException or InvalidDataException or UriFormatException)
        {
            _logger?.LogWarning(ex, "Failed to fetch capabilities for {Url}", serviceUrl);

            // Return stale entry if available (graceful degradation).
            if (staleEntry != null)
            {
                _logger?.LogInformation("Returning stale capabilities for {Url} (age={Age:F0}s)",
                    serviceUrl, (DateTime.UtcNow - staleEntry.FetchedAt).TotalSeconds);
                return staleEntry.Urls;
            }

            return null;
        }

        var urls = ParseCapabilitiesXml(serviceUrl, rawXml);
        var effectiveTtl = ttl ?? _defaultTtl;
        _cache[serviceUrl] = new CacheEntry(urls, DateTime.UtcNow, effectiveTtl);

        _logger?.LogInformation("Capabilities cached for {Url}: PTZ={Ptz}, Media={Media}, Events={Events}",
            serviceUrl,
            urls.PtzUrl != null ? "✓" : "✗",
            urls.MediaUrl != null ? "✓" : "✗",
            urls.EventsUrl != null ? "✓" : "✗");

        return urls;
    }

    /// <inheritdoc />
    public void Invalidate(string serviceUrl)
    {
        _cache.TryRemove(serviceUrl, out _);
        _logger?.LogInformation("Capability cache invalidated for {Url}", serviceUrl);
    }

    /// <summary>
    /// Parse GetCapabilities XML response and extract all service endpoint URLs
    /// in a single XDocument pass (avoids multiple GetCapabilities calls).
    /// </summary>
    private static OnvifServiceUrls ParseCapabilitiesXml(string serviceUrl, string rawXml)
    {
        var urls = new OnvifServiceUrls
        {
            DeviceServiceUrl = serviceUrl,
            RawXml = rawXml,
            FetchedAt = DateTime.UtcNow
        };

        try
        {
            var doc = XDocument.Parse(rawXml);

            // Extract each service XAddr
            var ptzUrl = ExtractXAddr(doc, "PTZ");
            var mediaUrl = ExtractXAddr(doc, "Media");
            var eventsUrl = ExtractXAddr(doc, "Events");
            var imagingUrl = ExtractXAddr(doc, "Imaging");
            var networkUrl = ExtractXAddr(doc, "Network");
            var analyticsUrl = ExtractXAddr(doc, "Analytics");

            urls = new OnvifServiceUrls
            {
                DeviceServiceUrl = serviceUrl,
                PtzUrl = ptzUrl,
                MediaUrl = mediaUrl,
                EventsUrl = eventsUrl,
                ImagingUrl = imagingUrl,
                NetworkUrl = networkUrl,
                AnalyticsUrl = analyticsUrl,
                PtzSupported = ptzUrl != null,
                EventsSupported = eventsUrl != null,
                RawXml = rawXml,
                FetchedAt = DateTime.UtcNow
            };
        }
        catch (XmlException)
        {
            // If parsing fails, return URLs with only the device service URL.
            // Callers will fall back to URL-rewriting heuristics.
        }

        return urls;
    }

    /// <summary>
    /// Extract the XAddr value from a named service element in the capabilities XML.
    /// Handles various ONVIF namespace conventions:
    ///   &lt;tt:PTZ&gt;&lt;tt:XAddr&gt;http://...&lt;/tt:XAddr&gt;&lt;/tt:PTZ&gt;
    ///   &lt;tds:PTZ&gt;&lt;tt:XAddr&gt;http://...&lt;/tt:XAddr&gt;&lt;/tds:PTZ&gt;
    /// </summary>
    private static string? ExtractXAddr(XDocument doc, string serviceName)
    {
        return doc.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == serviceName)
            ?.Elements()
            .FirstOrDefault(e => e.Name.LocalName == "XAddr")
            ?.Value;
    }

    /// <summary>
    /// Internal cache entry with expiration.
    /// </summary>
    private sealed class CacheEntry
    {
        public OnvifServiceUrls Urls { get; }
        public DateTime FetchedAt { get; }
        public DateTime ExpiresAt { get; }

        public bool IsExpired => DateTime.UtcNow > ExpiresAt;

        public CacheEntry(OnvifServiceUrls urls, DateTime fetchedAt, TimeSpan ttl)
        {
            Urls = urls;
            FetchedAt = fetchedAt;
            ExpiresAt = fetchedAt + ttl;
        }
    }
}
