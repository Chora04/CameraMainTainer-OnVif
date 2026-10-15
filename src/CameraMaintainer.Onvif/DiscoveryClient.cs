using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace CameraMaintainer.Onvif;

/// <summary>
/// WS-Discovery client — discovers ONVIF cameras via UDP multicast (239.255.255.250:3702).
/// Supports both multicast Probe and unicast Probe to specific IP ranges.
/// </summary>
public static class DiscoveryClient
{
    private const int DiscoveryPort = 3702;
    private const string MulticastAddress = "239.255.255.250";
    private const string ProbeMessage = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<SOAP-ENV:Envelope
  xmlns:SOAP-ENV=""http://www.w3.org/2003/05/soap-envelope""
  xmlns:wsa=""http://www.w3.org/2005/08/addressing""
  xmlns:d=""http://schemas.xmlsoap.org/ws/2005/04/discovery""
  xmlns:dn=""http://www.onvif.org/ver10/network/wsdl"">
  <SOAP-ENV:Header>
    <wsa:Action>http://schemas.xmlsoap.org/ws/2005/04/discovery/Probe</wsa:Action>
    <wsa:MessageID>uuid:{0}</wsa:MessageID>
    <wsa:To>urn:schemas-xmlsoap-org:ws:2005:04:discovery</wsa:To>
  </SOAP-ENV:Header>
  <SOAP-ENV:Body>
    <d:Probe>
      <d:Types>dn:NetworkVideoTransmitter</d:Types>
    </d:Probe>
  </SOAP-ENV:Body>
</SOAP-ENV:Envelope>";

    public record DiscoveredDevice(string ServiceUrl, string IpAddress, string? Manufacturer,
        string? Model, string? SerialNumber, string? HardwareId, List<string> Scopes);

    /// <summary>
    /// Multicast WS-Discovery — sends Probe to 239.255.255.250:3702 and collects responses.
    /// </summary>
    public static async Task<List<DiscoveredDevice>> DiscoverAsync(
        int timeoutSeconds = 5,
        CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutSeconds);

        var results = new List<DiscoveredDevice>();
        var seenEndpoints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var udpClient = new UdpClient();
        udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, 0));

        // Join multicast group
        udpClient.JoinMulticastGroup(IPAddress.Parse(MulticastAddress));

        var messageId = Guid.NewGuid().ToString("D");
        var probeBytes = Encoding.UTF8.GetBytes(
            ProbeMessage.Replace("{0}", messageId, StringComparison.Ordinal));

        // Send Probe
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        await udpClient.SendAsync(
            probeBytes,
            new IPEndPoint(IPAddress.Parse(MulticastAddress), DiscoveryPort),
            timeoutCts.Token);

        try
        {
            while (true)
            {
                UdpReceiveResult result;
                try
                {
                    result = await udpClient.ReceiveAsync(timeoutCts.Token);
                }
                catch (SocketException) { break; }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested) { break; }

                var responseXml = Encoding.UTF8.GetString(result.Buffer);
                var devices = ParseProbeMatches(responseXml, result.RemoteEndPoint.Address.ToString());

                foreach (var device in devices)
                {
                    if (seenEndpoints.Add(device.ServiceUrl))
                        results.Add(device);
                }
            }
        }
        finally
        {
            udpClient.DropMulticastGroup(IPAddress.Parse(MulticastAddress));
        }

        return results;
    }

    /// <summary>
    /// Unicast WS-Discovery — sends Probe to a specific IP.
    /// Useful for cameras on different subnets or when multicast doesn't reach.
    /// </summary>
    private static async Task<DiscoveredDevice?> ProbeAsync(
        string ipAddress,
        int timeoutMs = 5000,
        CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);
        if (!IPAddress.TryParse(ipAddress, out var address) || address.AddressFamily != AddressFamily.InterNetwork)
            throw new ArgumentException("A valid IPv4 address is required.", nameof(ipAddress));

        var messageId = Guid.NewGuid().ToString("D");
        var probeBytes = Encoding.UTF8.GetBytes(
            ProbeMessage.Replace("{0}", messageId, StringComparison.Ordinal));

        using var udpClient = new UdpClient();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(timeoutMs));

        await udpClient.SendAsync(
            probeBytes,
            new IPEndPoint(address, DiscoveryPort),
            timeoutCts.Token);

        try
        {
            var result = await udpClient.ReceiveAsync(timeoutCts.Token);
            var responseXml = Encoding.UTF8.GetString(result.Buffer);
            var devices = ParseProbeMatches(responseXml, result.RemoteEndPoint.Address.ToString());
            return devices.FirstOrDefault();
        }
        catch (SocketException)
        {
            return null;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>
    /// Scan an IP range for ONVIF cameras using unicast probes.
    /// </summary>
    public static async Task<List<DiscoveredDevice>> ScanIpRangeAsync(string subnet, int timeoutMs = 5000,
        CancellationToken ct = default)
    {
        var addresses = BuildScanAddresses(subnet);
        var results = new ConcurrentBag<DiscoveredDevice>();
        await Parallel.ForEachAsync(
            addresses,
            new ParallelOptions { MaxDegreeOfParallelism = 50, CancellationToken = ct },
            async (ip, cancellationToken) =>
            {
                try
                {
                    var device = await ProbeAsync(ip, timeoutMs, cancellationToken);
                    if (device != null)
                        results.Add(device);
                }
                catch (SocketException)
                {
                }
            });

        return results
            .OrderBy(device => device.IpAddress, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal static IReadOnlyList<string> BuildScanAddresses(string subnet)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subnet);
        var parts = subnet.Split('/', 2, StringSplitOptions.TrimEntries);
        if (!IPAddress.TryParse(parts[0], out var address) || address.AddressFamily != AddressFamily.InterNetwork)
            throw new ArgumentException("A valid IPv4 CIDR range is required.", nameof(subnet));

        var prefix = 24;
        if (parts.Length == 2 &&
            (!int.TryParse(parts[1], out prefix) || prefix is < 0 or > 32))
        {
            throw new ArgumentException("The IPv4 prefix must be between 0 and 32.", nameof(subnet));
        }

        var bytes = address.GetAddressBytes();
        var value = ((uint)bytes[0] << 24) |
                    ((uint)bytes[1] << 16) |
                    ((uint)bytes[2] << 8) |
                    bytes[3];
        var mask = prefix == 0 ? 0U : uint.MaxValue << (32 - prefix);
        var network = value & mask;
        var addressCount = 1L << (32 - prefix);
        var firstOffset = prefix < 31 ? 1L : 0L;
        var usableCount = prefix switch
        {
            32 => 1L,
            31 => 2L,
            _ => addressCount - 2
        };
        var count = (int)Math.Min(254, usableCount);
        var result = new List<string>(count);
        for (var index = 0; index < count; index++)
        {
            var candidate = network + firstOffset + (uint)index;
            result.Add(new IPAddress(new[]
            {
                (byte)(candidate >> 24),
                (byte)(candidate >> 16),
                (byte)(candidate >> 8),
                (byte)candidate
            }).ToString());
        }

        return result;
    }

    private static List<DiscoveredDevice> ParseProbeMatches(string xml, string sourceIp)
    {
        var devices = new List<DiscoveredDevice>();

        try
        {
            var doc = XDocument.Parse(xml);
            var dNs = XNamespace.Get("http://schemas.xmlsoap.org/ws/2005/04/discovery");

            var probeMatches = doc.Descendants(dNs + "ProbeMatches").FirstOrDefault()
                ?? doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "ProbeMatches");

            if (probeMatches == null) return devices;

            foreach (var match in probeMatches.Elements())
            {
                if (!match.Name.LocalName.Equals("ProbeMatch", StringComparison.OrdinalIgnoreCase))
                    continue;

                var xaddrs = match.Elements().FirstOrDefault(e => e.Name.LocalName == "XAddrs")?.Value;
                var scopes = match.Elements().FirstOrDefault(e => e.Name.LocalName == "Scopes")?.Value;
                if (string.IsNullOrWhiteSpace(xaddrs)) continue;

                // XAddrs contains space-separated URLs — take the first one
                var urls = xaddrs.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var serviceUrl = urls.FirstOrDefault(u =>
                    u.Contains("device_service", StringComparison.OrdinalIgnoreCase) ||
                    u.Contains("onvif", StringComparison.OrdinalIgnoreCase));
                if (serviceUrl == null) continue;

                var ip = ExtractIpFromUrl(serviceUrl) ?? sourceIp;
                var scopeList = ParseScopes(scopes ?? string.Empty);

                // Try to extract manufacturer/model from scopes
                var manufacturer = scopeList.FirstOrDefault(s =>
                    s.Contains("manufacturer", StringComparison.OrdinalIgnoreCase))
                    ?.Split('/').LastOrDefault();
                var hardware = scopeList.FirstOrDefault(s =>
                    s.Contains("hardware", StringComparison.OrdinalIgnoreCase));

                devices.Add(new DiscoveredDevice(serviceUrl, ip, manufacturer, null, null, hardware, scopeList));
            }
        }
        catch (XmlException)
        {
            // Skip malformed responses
        }

        return devices;
    }

    private static List<string> ParseScopes(string scopes)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(scopes)) return result;

        // Scopes are space-separated URIs
        var parts = scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            // Remove the "onvif://www.onvif.org/" prefix for readability
            var cleaned = part.Trim();
            if (cleaned.StartsWith("onvif://www.onvif.org/", StringComparison.Ordinal))
                cleaned = cleaned["onvif://www.onvif.org/".Length..];

            result.Add(cleaned);
        }

        return result;
    }

    private static string? ExtractIpFromUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            return uri.Host;
        }
        catch (UriFormatException)
        {
            return null;
        }
    }
}
