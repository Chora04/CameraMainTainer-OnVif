using System.Xml.Linq;
using System.Xml;
using System.Collections.Concurrent;
using System.Globalization;
using CameraMaintainer.Core.Interfaces;
using CameraMaintainer.Core.Models;

namespace CameraMaintainer.Onvif;

/// <summary>
/// Full ONVIF client implementing IOnvifClient.
/// Uses OnvifSoapClient for HTTP/SOAP communication with ONVIF cameras.
///
/// Supported ONVIF services:
/// - Device: GetDeviceInformation, GetSystemDateAndTime, GetCapabilities, SystemReboot
/// - Media: GetProfiles, GetSnapshotUri
/// - PTZ: AbsoluteMove, RelativeMove, Stop, GetPresets, SetPreset, GotoPreset
/// - Network: GetNetworkInterfaces, GetNetworkDefaultGateway, GetDNS
///
/// Optimisation: Uses IOnvifCapabilityCache to avoid redundant GetCapabilities calls.
/// Service URLs (PTZ, Media, Events, Imaging, Network) are parsed once and cached
/// per camera for 30 minutes, reducing SOAP round-trips by up to 50%.
/// </summary>
public class OnvifClient : IOnvifClient
{
    private readonly OnvifSoapClient _soap;
    private readonly IOnvifCapabilityCache? _capabilityCache;
    private readonly ConcurrentDictionary<string, ProfileTokenEntry> _defaultProfileTokens =
        new(StringComparer.OrdinalIgnoreCase);

    public OnvifClient(HttpClient? httpClient = null, IOnvifCapabilityCache? capabilityCache = null)
    {
        _soap = new OnvifSoapClient(httpClient);
        _capabilityCache = capabilityCache;
    }

    // ==================== DEVICE SERVICE ====================

    public async Task<DeviceInfo> GetDeviceInfoAsync(string serviceUrl, string username, string password, int timeoutMs = 10000)
    {
        var body = "<tds:GetDeviceInformation/>";
        var element = await _soap.SendAndExtractAsync(
            serviceUrl,
            "http://www.onvif.org/ver10/device/wsdl/GetDeviceInformation",
            body, "GetDeviceInformationResponse",
            username, password, timeoutMs);

        if (element == null) throw new InvalidOperationException("No response from device");

        return new DeviceInfo(
            Manufacturer: element.ElementValue("Manufacturer") ?? "Unknown",
            Model: element.ElementValue("Model") ?? "Unknown",
            SerialNumber: element.ElementValue("SerialNumber") ?? "Unknown",
            FirmwareVersion: element.ElementValue("FirmwareVersion") ?? "Unknown",
            HardwareId: element.ElementValue("HardwareId") ?? "Unknown"
        );
    }

    public async Task<Core.Interfaces.SystemDateTime> GetSystemDateTimeAsync(string serviceUrl, string username, string password, int timeoutMs = 10000)
    {
        var body = "<tds:GetSystemDateAndTime/>";
        const string action = "http://www.onvif.org/ver10/device/wsdl/GetSystemDateAndTime";
        XElement? element;
        try
        {
            // ONVIF devices normally expose their time before authentication so a
            // client can generate a valid WS-Security timestamp even when clocks drift.
            element = await _soap.SendAndExtractAsync(
                serviceUrl,
                action,
                body,
                "GetSystemDateAndTimeResponse",
                timeoutMs: timeoutMs);
        }
        catch (OnvifSoapException ex) when (
            ex.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
        {
            element = await _soap.SendAndExtractAsync(
                serviceUrl,
                action,
                body,
                "GetSystemDateAndTimeResponse",
                username,
                password,
                timeoutMs);
        }

        if (element == null) throw new InvalidOperationException("No response from device");

        var utcEl = element.Descendants().FirstOrDefault(e => e.Name.LocalName == "UTCDateTime");
        var tzEl = element.Descendants().FirstOrDefault(e => e.Name.LocalName == "TimeZone");
        var ntpEl = element.Descendants().FirstOrDefault(e => e.Name.LocalName == "NTP");

        DateTime? utc = null;
        if (utcEl != null)
        {
            var timeEl = utcEl.Elements().FirstOrDefault(e => e.Name.LocalName == "Time");
            var dateEl = utcEl.Elements().FirstOrDefault(e => e.Name.LocalName == "Date");
            if (timeEl != null && dateEl != null)
            {
                var hour = int.Parse(timeEl.ElementValue("Hour") ?? "0", CultureInfo.InvariantCulture);
                var minute = int.Parse(timeEl.ElementValue("Minute") ?? "0", CultureInfo.InvariantCulture);
                var second = int.Parse(timeEl.ElementValue("Second") ?? "0", CultureInfo.InvariantCulture);
                var year = int.Parse(dateEl.ElementValue("Year") ?? "2000", CultureInfo.InvariantCulture);
                var month = int.Parse(dateEl.ElementValue("Month") ?? "1", CultureInfo.InvariantCulture);
                var day = int.Parse(dateEl.ElementValue("Day") ?? "1", CultureInfo.InvariantCulture);

                utc = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Utc);
            }
        }

        var tz = tzEl?.ElementValue("TZ") ?? "UTC";
        var ntpEnabled = ntpEl != null && ntpEl.ElementValue("FromDHCP") != null;

        if (!utc.HasValue)
            throw new InvalidDataException("Camera returned no valid UTC date and time.");

        return new Core.Interfaces.SystemDateTime(utc.Value, tz, ntpEnabled);
    }

    public async Task SetSystemDateTimeAsync(string serviceUrl, string username, string password, DateTime dateTime, int timeoutMs = 10000)
    {
        var dt = dateTime.ToUniversalTime();
        var body = $@"
<tds:SetSystemDateAndTime>
  <tds:DateTimeType>Manual</tds:DateTimeType>
  <tds:DaylightSavings>false</tds:DaylightSavings>
  <tds:UTCDateTime>
    <tt:Time>
      <tt:Hour>{dt.Hour}</tt:Hour>
      <tt:Minute>{dt.Minute}</tt:Minute>
      <tt:Second>{dt.Second}</tt:Second>
    </tt:Time>
    <tt:Date>
      <tt:Year>{dt.Year}</tt:Year>
      <tt:Month>{dt.Month}</tt:Month>
      <tt:Day>{dt.Day}</tt:Day>
    </tt:Date>
  </tds:UTCDateTime>
</tds:SetSystemDateAndTime>";

        await _soap.SendRequestAsync(serviceUrl,
            "http://www.onvif.org/ver10/device/wsdl/SetSystemDateAndTime",
            body, username, password, timeoutMs);
    }

    public async Task<DeviceCapabilities> GetCapabilitiesAsync(string serviceUrl, string username, string password, int timeoutMs = 10000)
    {
        var body = "<tds:GetCapabilities><tds:Category>All</tds:Category></tds:GetCapabilities>";
        var doc = await _soap.SendRequestAsync(serviceUrl,
            "http://www.onvif.org/ver10/device/wsdl/GetCapabilities",
            body, username, password, timeoutMs);

        var raw = doc.ToString();
        var ptzSupported = raw.Contains("PTZ", StringComparison.OrdinalIgnoreCase);
        var eventsSupported = raw.Contains("Event", StringComparison.OrdinalIgnoreCase);
        var analyticsSupported = raw.Contains("Analytics", StringComparison.OrdinalIgnoreCase);

        return new DeviceCapabilities(ptzSupported, eventsSupported, analyticsSupported, raw);
    }

    // ==================== PTZ SERVICE ====================

    public async Task<List<PtzProfileInfo>> GetPtzProfilesAsync(string serviceUrl, string username, string password, int timeoutMs = 10000)
    {
        // ONVIF GetNodes has no profile-token argument. Query it once and then
        // expose the media profiles that can be used with the PTZ service.
        var mediaProfiles = await GetProfilesAsync(serviceUrl, username, password, timeoutMs);
        if (mediaProfiles.FirstOrDefault() is { } firstProfile)
            CacheDefaultProfileToken(serviceUrl, firstProfile.Token);
        var ptzUrl = await GetPtzServiceUrl(serviceUrl, username, password, timeoutMs);
        var element = await _soap.SendAndExtractAsync(
            ptzUrl ?? serviceUrl,
            "http://www.onvif.org/ver20/ptz/wsdl/GetNodes",
            "<tptz:GetNodes/>",
            "GetNodesResponse",
            username,
            password,
            timeoutMs);

        if (element == null)
            return [];

        var panRange = ReadAxisRange(element, "PanTilt", "XRange", -1, 1);
        var tiltRange = ReadAxisRange(element, "PanTilt", "YRange", -1, 1);
        var zoomRange = ReadAxisRange(element, "Zoom", "XRange", 0, 1);
        return mediaProfiles
            .Select(profile => new PtzProfileInfo(
                profile.Token,
                profile.Name,
                panRange.Minimum,
                panRange.Maximum,
                tiltRange.Minimum,
                tiltRange.Maximum,
                zoomRange.Minimum,
                zoomRange.Maximum))
            .ToList();
    }

    public async Task AbsoluteMoveAsync(string serviceUrl, string username, string password,
        string profileToken, double pan, double tilt, double zoom, int timeoutMs = 10000)
    {
        profileToken = await ResolveProfileTokenAsync(
            serviceUrl, username, password, profileToken, timeoutMs);
        var ptzUrl = await GetPtzServiceUrl(serviceUrl, username, password, timeoutMs);
        var body = $@"
<tptz:AbsoluteMove>
  <tptz:ProfileToken>{EscapeXml(profileToken)}</tptz:ProfileToken>
  <tptz:Position>
    <tt:PanTilt x=""{ToXmlNumber(pan)}"" y=""{ToXmlNumber(tilt)}"" space=""http://www.onvif.org/ver10/tptz/PanTiltSpaces/PositionGenericSpace""/>
    <tt:Zoom x=""{ToXmlNumber(zoom)}"" space=""http://www.onvif.org/ver10/tptz/ZoomSpaces/PositionGenericSpace""/>
  </tptz:Position>
</tptz:AbsoluteMove>";

        await _soap.SendRequestAsync(ptzUrl ?? serviceUrl,
            "http://www.onvif.org/ver20/ptz/wsdl/AbsoluteMove",
            body, username, password, timeoutMs);
    }

    public async Task RelativeMoveAsync(string serviceUrl, string username, string password,
        string profileToken, double pan, double tilt, double zoom, int timeoutMs = 10000)
    {
        profileToken = await ResolveProfileTokenAsync(
            serviceUrl, username, password, profileToken, timeoutMs);
        var ptzUrl = await GetPtzServiceUrl(serviceUrl, username, password, timeoutMs);
        var body = $@"
<tptz:RelativeMove>
  <tptz:ProfileToken>{EscapeXml(profileToken)}</tptz:ProfileToken>
  <tptz:Translation>
    <tt:PanTilt x=""{ToXmlNumber(pan)}"" y=""{ToXmlNumber(tilt)}"" space=""http://www.onvif.org/ver10/tptz/PanTiltSpaces/TranslationGenericSpace""/>
    <tt:Zoom x=""{ToXmlNumber(zoom)}"" space=""http://www.onvif.org/ver10/tptz/ZoomSpaces/TranslationGenericSpace""/>
  </tptz:Translation>
</tptz:RelativeMove>";

        await _soap.SendRequestAsync(ptzUrl ?? serviceUrl,
            "http://www.onvif.org/ver20/ptz/wsdl/RelativeMove",
            body, username, password, timeoutMs);
    }

    public async Task StopMoveAsync(string serviceUrl, string username, string password,
        string profileToken, int timeoutMs = 10000)
    {
        profileToken = await ResolveProfileTokenAsync(
            serviceUrl, username, password, profileToken, timeoutMs);
        var ptzUrl = await GetPtzServiceUrl(serviceUrl, username, password, timeoutMs);
        var body = $@"
<tptz:Stop>
  <tptz:ProfileToken>{EscapeXml(profileToken)}</tptz:ProfileToken>
  <tptz:PanTilt>true</tptz:PanTilt>
  <tptz:Zoom>true</tptz:Zoom>
</tptz:Stop>";

        await _soap.SendRequestAsync(ptzUrl ?? serviceUrl,
            "http://www.onvif.org/ver20/ptz/wsdl/Stop",
            body, username, password, timeoutMs);
    }

    public async Task<List<PtzPresetInfo>> GetPresetsAsync(string serviceUrl, string username, string password,
        string profileToken, int timeoutMs = 10000)
    {
        profileToken = await ResolveProfileTokenAsync(
            serviceUrl, username, password, profileToken, timeoutMs);
        var ptzUrl = await GetPtzServiceUrl(serviceUrl, username, password, timeoutMs);
        var body = $@"
<tptz:GetPresets>
  <tptz:ProfileToken>{EscapeXml(profileToken)}</tptz:ProfileToken>
</tptz:GetPresets>";

        var element = await _soap.SendAndExtractAsync(ptzUrl ?? serviceUrl,
            "http://www.onvif.org/ver20/ptz/wsdl/GetPresets",
            body, "GetPresetsResponse",
            username, password, timeoutMs);

        var result = new List<PtzPresetInfo>();
        if (element != null)
        {
            foreach (var preset in element.Elements())
            {
                if (preset.Name.LocalName == "Preset")
                {
                    var token = preset.Attribute("token")?.Value ?? preset.ElementValue("token") ?? "";
                    var name = preset.ElementValue("Name") ?? preset.ElementValue("name") ?? "";
                    if (!string.IsNullOrEmpty(token))
                        result.Add(new PtzPresetInfo(token, name));
                }
            }
        }

        return result;
    }

    public async Task<string> SetPresetAsync(string serviceUrl, string username, string password,
        string profileToken, string presetName, int timeoutMs = 10000)
    {
        profileToken = await ResolveProfileTokenAsync(
            serviceUrl, username, password, profileToken, timeoutMs);
        var ptzUrl = await GetPtzServiceUrl(serviceUrl, username, password, timeoutMs);
        var body = $@"
<tptz:SetPreset>
  <tptz:ProfileToken>{EscapeXml(profileToken)}</tptz:ProfileToken>
  <tptz:PresetName>{EscapeXml(presetName)}</tptz:PresetName>
</tptz:SetPreset>";

        var element = await _soap.SendAndExtractAsync(ptzUrl ?? serviceUrl,
            "http://www.onvif.org/ver20/ptz/wsdl/SetPreset",
            body, "SetPresetResponse",
            username, password, timeoutMs);

        var presetToken = element?.ElementValue("PresetToken");
        if (string.IsNullOrWhiteSpace(presetToken))
            throw new InvalidDataException("Camera did not return a preset token.");

        return presetToken;
    }

    public async Task GotoPresetAsync(string serviceUrl, string username, string password,
        string profileToken, string presetToken, int timeoutMs = 10000)
    {
        profileToken = await ResolveProfileTokenAsync(
            serviceUrl, username, password, profileToken, timeoutMs);
        var ptzUrl = await GetPtzServiceUrl(serviceUrl, username, password, timeoutMs);
        var body = $@"
<tptz:GotoPreset>
  <tptz:ProfileToken>{EscapeXml(profileToken)}</tptz:ProfileToken>
  <tptz:PresetToken>{EscapeXml(presetToken)}</tptz:PresetToken>
</tptz:GotoPreset>";

        await _soap.SendRequestAsync(ptzUrl ?? serviceUrl,
            "http://www.onvif.org/ver20/ptz/wsdl/GotoPreset",
            body, username, password, timeoutMs);
    }

    // ==================== MEDIA SERVICE ====================

    public async Task<List<MediaProfile>> GetProfilesAsync(string serviceUrl, string username, string password, int timeoutMs = 10000)
    {
        var mediaUrl = await GetMediaServiceUrl(serviceUrl, username, password, timeoutMs);
        var body = "<trt:GetProfiles/>";
        var element = await _soap.SendAndExtractAsync(mediaUrl ?? serviceUrl,
            "http://www.onvif.org/ver10/media/wsdl/GetProfiles",
            body, "GetProfilesResponse",
            username, password, timeoutMs);

        var result = new List<MediaProfile>();
        if (element != null)
        {
            foreach (var profile in element.Descendants().Where(e => e.Name.LocalName == "Profiles"))
            {
                var token = profile.Attribute("token")?.Value ?? "";
                var name = profile.ElementValue("Name") ?? "";
                var videoSourceToken = profile.Elements()
                    .FirstOrDefault(e => e.Name.LocalName == "VideoSourceConfiguration")
                    ?.Attribute("token")?.Value ?? "";
                var encoder = profile.Elements()
                    .FirstOrDefault(e => e.Name.LocalName == "VideoEncoderConfiguration");
                var encoderToken = encoder?.Attribute("token")?.Value ?? "";
                var encoding = encoder?.Elements()
                    .FirstOrDefault(e => e.Name.LocalName == "Encoding")?.Value ?? "";
                var resolutionElement = encoder?.Elements()
                    .FirstOrDefault(e => e.Name.LocalName == "Resolution");
                var width = ParseInt(resolutionElement?.Elements()
                    .FirstOrDefault(e => e.Name.LocalName == "Width")?.Value);
                var height = ParseInt(resolutionElement?.Elements()
                    .FirstOrDefault(e => e.Name.LocalName == "Height")?.Value);
                var resolution = width > 0 && height > 0 ? $"{width}x{height}" : "";
                var framerate = ParseInt(encoder?.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName == "FrameRateLimit")?.Value);
                var bitrate = ParseInt(encoder?.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName == "BitrateLimit")?.Value);

                if (!string.IsNullOrWhiteSpace(token))
                    result.Add(new MediaProfile(
                        token,
                        name,
                        videoSourceToken,
                        encoderToken,
                        resolution,
                        framerate,
                        bitrate,
                        encoding));
            }
        }

        if (result.FirstOrDefault() is { } firstProfile)
            CacheDefaultProfileToken(serviceUrl, firstProfile.Token);

        return result;
    }

    private static int ParseInt(string? value) =>
        int.TryParse(value, out var result) ? result : 0;

    public async Task<string> GetSnapshotUriAsync(string serviceUrl, string username, string password,
        string profileToken, int timeoutMs = 10000)
    {
        var mediaUrl = await GetMediaServiceUrl(serviceUrl, username, password, timeoutMs);
        var body = $@"
<trt:GetSnapshotUri>
  <trt:ProfileToken>{EscapeXml(profileToken)}</trt:ProfileToken>
</trt:GetSnapshotUri>";

        var element = await _soap.SendAndExtractAsync(mediaUrl ?? serviceUrl,
            "http://www.onvif.org/ver10/media/wsdl/GetSnapshotUri",
            body, "GetSnapshotUriResponse",
            username, password, timeoutMs);

        return element?.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "Uri")
            ?.Value ?? string.Empty;
    }

    // ==================== SYSTEM ====================

    public async Task RebootAsync(string serviceUrl, string username, string password, int timeoutMs = 10000)
    {
        var body = "<tds:SystemReboot/>";
        await _soap.SendRequestAsync(serviceUrl,
            "http://www.onvif.org/ver10/device/wsdl/SystemReboot",
            body, username, password, timeoutMs);
    }

    public async Task FactoryResetAsync(string serviceUrl, string username, string password, string mode, int timeoutMs = 10000)
    {
        var factoryDefault = mode.Trim().ToLowerInvariant() switch
        {
            "hard" => "Hard",
            "soft" => "Soft",
            _ => throw new ArgumentException("Factory reset mode must be 'hard' or 'soft'.", nameof(mode))
        };
        var body = $"<tds:SetSystemFactoryDefault><tds:FactoryDefault>{factoryDefault}</tds:FactoryDefault></tds:SetSystemFactoryDefault>";
        await _soap.SendRequestAsync(serviceUrl,
            "http://www.onvif.org/ver10/device/wsdl/SetSystemFactoryDefault",
            body, username, password, timeoutMs);
    }

    // ==================== NETWORK ====================

    public async Task<NetworkConfig> GetNetworkConfigAsync(string serviceUrl, string username, string password, int timeoutMs = 10000)
    {
        var body = "<tds:GetNetworkInterfaces/>";
        var element = await _soap.SendAndExtractAsync(serviceUrl,
            "http://www.onvif.org/ver10/device/wsdl/GetNetworkInterfaces",
            body, "GetNetworkInterfacesResponse",
            username, password, timeoutMs);

        if (element == null)
            return new NetworkConfig(false, null, null, null, null);

        var networkInterface = element.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "NetworkInterfaces");
        var ipv4Config = networkInterface?.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "IPv4")?
            .Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "Config");
        var manualElement = ipv4Config?.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "Manual");
        var fromDhcpElement = ipv4Config?.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "FromDHCP");
        var dhcp = ParseBool(ipv4Config?.ElementValue("DHCP"));
        var addressElement = dhcp ? fromDhcpElement ?? manualElement : manualElement ?? fromDhcpElement;
        var ip = addressElement?.ElementValue("Address");
        var prefixLength = ParseInt(addressElement?.ElementValue("PrefixLength"));
        var gateway = await TryGetNetworkValueAsync(
            serviceUrl,
            username,
            password,
            "GetNetworkDefaultGateway",
            "IPv4Address",
            timeoutMs);
        var dns = await TryGetNetworkValueAsync(
            serviceUrl,
            username,
            password,
            "GetDNS",
            "IPv4Address",
            timeoutMs);

        return new NetworkConfig(
            DhcpEnabled: dhcp,
            IpAddress: ip,
            SubnetMask: PrefixLengthToSubnetMask(prefixLength),
            DefaultGateway: gateway,
            DnsServer: dns
        );
    }

    // ==================== USERS ====================

    public async Task<List<UserInfo>> GetUsersAsync(string serviceUrl, string username, string password, int timeoutMs = 10000)
    {
        var body = "<tds:GetUsers/>";
        var element = await _soap.SendAndExtractAsync(serviceUrl,
            "http://www.onvif.org/ver10/device/wsdl/GetUsers",
            body, "GetUsersResponse",
            username, password, timeoutMs);

        var result = new List<UserInfo>();
        if (element != null)
        {
            foreach (var user in element.Elements())
            {
                if (user.Name.LocalName == "User")
                {
                    var uname = user.ElementValue("Username") ?? "";
                    var level = user.ElementValue("UserLevel") ?? "User";
                    result.Add(new UserInfo(uname, level));
                }
            }
        }

        return result;
    }

    public async Task CreateUserAsync(string serviceUrl, string username, string password,
        string newUsername, string newPassword, string userLevel, int timeoutMs = 10000)
    {
        var body = $@"
<tds:CreateUsers>
  <tds:User>
    <tt:Username>{EscapeXml(newUsername)}</tt:Username>
    <tt:Password>{EscapeXml(newPassword)}</tt:Password>
    <tt:UserLevel>{EscapeXml(userLevel)}</tt:UserLevel>
  </tds:User>
</tds:CreateUsers>";

        await _soap.SendRequestAsync(serviceUrl,
            "http://www.onvif.org/ver10/device/wsdl/CreateUsers",
            body, username, password, timeoutMs);
    }

    public async Task DeleteUserAsync(string serviceUrl, string username, string password,
        string targetUsername, int timeoutMs = 10000)
    {
        var body = $@"
<tds:DeleteUsers>
  <tds:Username>{EscapeXml(targetUsername)}</tds:Username>
</tds:DeleteUsers>";

        await _soap.SendRequestAsync(serviceUrl,
            "http://www.onvif.org/ver10/device/wsdl/DeleteUsers",
            body, username, password, timeoutMs);
    }

    // ==================== HELPERS ====================

    /// <summary>
    /// Get or fetch cached ONVIF service URLs for this camera.
    /// Uses IOnvifCapabilityCache to avoid redundant GetCapabilities SOAP calls.
    /// Falls back to URL-rewriting heuristic if cache is unavailable or fetch fails.
    /// </summary>
    private async Task<OnvifServiceUrls?> GetServiceUrlsAsync(
        string serviceUrl, string username, string password, int timeoutMs)
    {
        if (_capabilityCache != null)
        {
            return await _capabilityCache.GetOrFetchAsync(
                serviceUrl,
                async () =>
                {
                    var doc = await _soap.SendRequestAsync(serviceUrl,
                        "http://www.onvif.org/ver10/device/wsdl/GetCapabilities",
                        "<tds:GetCapabilities><tds:Category>All</tds:Category></tds:GetCapabilities>",
                        username, password, timeoutMs);
                    return doc.ToString();
                });
        }

        // No cache — fall back to fetching and parsing capabilities directly
        try
        {
            var capabilities = await GetCapabilitiesAsync(serviceUrl, username, password, timeoutMs);
            return new OnvifServiceUrls
            {
                DeviceServiceUrl = serviceUrl,
                RawXml = capabilities.RawXml,
                PtzSupported = capabilities.PtzSupported,
                EventsSupported = capabilities.EventsSupported
            };
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or
            XmlException or InvalidDataException or UriFormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// Resolve the PTZ service URL from cache or fall back to URL rewriting.
    /// </summary>
    private async Task<string?> GetPtzServiceUrl(string serviceUrl, string username, string password, int timeoutMs)
    {
        var urls = await GetServiceUrlsAsync(serviceUrl, username, password, timeoutMs);
        if (urls?.PtzUrl != null) return urls.PtzUrl;

        // Fallback: try replacing "device_service" with "ptz" in URL
        return ReplaceServicePath(serviceUrl, "ptz_service");
    }

    /// <summary>
    /// Resolve the Media service URL from cache or fall back to URL rewriting.
    /// </summary>
    private async Task<string?> GetMediaServiceUrl(string serviceUrl, string username, string password, int timeoutMs)
    {
        var urls = await GetServiceUrlsAsync(serviceUrl, username, password, timeoutMs);
        if (urls?.MediaUrl != null) return urls.MediaUrl;

        return ReplaceServicePath(serviceUrl, "media_service");
    }

    private async Task<string?> TryGetNetworkValueAsync(
        string serviceUrl,
        string username,
        string password,
        string operation,
        string valueElement,
        int timeoutMs)
    {
        try
        {
            var element = await _soap.SendAndExtractAsync(
                serviceUrl,
                $"http://www.onvif.org/ver10/device/wsdl/{operation}",
                $"<tds:{operation}/>",
                $"{operation}Response",
                username,
                password,
                timeoutMs);
            return element?.Descendants()
                .FirstOrDefault(item => item.Name.LocalName == valueElement)
                ?.Value;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or
            XmlException or InvalidDataException)
        {
            return null;
        }
    }

    private static (double Minimum, double Maximum) ReadAxisRange(
        XElement root,
        string spaceName,
        string rangeName,
        double fallbackMinimum,
        double fallbackMaximum)
    {
        var space = root.Descendants()
            .FirstOrDefault(element => element.Name.LocalName.Contains(spaceName, StringComparison.Ordinal));
        var range = space?.Descendants()
            .FirstOrDefault(element => element.Name.LocalName == rangeName);
        var minimum = ParseDouble(range?.ElementValue("Min"), fallbackMinimum);
        var maximum = ParseDouble(range?.ElementValue("Max"), fallbackMaximum);
        return minimum <= maximum
            ? (minimum, maximum)
            : (fallbackMinimum, fallbackMaximum);
    }

    private static double ParseDouble(string? value, double fallback)
        => double.TryParse(
            value,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out var result)
            ? result
            : fallback;

    private static bool ParseBool(string? value)
        => bool.TryParse(value, out var result) && result;

    private static string? PrefixLengthToSubnetMask(int prefixLength)
    {
        if (prefixLength is < 0 or > 32)
            return null;

        var mask = prefixLength == 0 ? 0U : uint.MaxValue << (32 - prefixLength);
        return string.Join('.', new[]
        {
            (mask >> 24) & 0xff,
            (mask >> 16) & 0xff,
            (mask >> 8) & 0xff,
            mask & 0xff
        });
    }

    private static string ReplaceServicePath(string serviceUrl, string replacement)
    {
        var markerIndex = serviceUrl.IndexOf("device_service", StringComparison.OrdinalIgnoreCase);
        return markerIndex < 0
            ? serviceUrl
            : string.Concat(
                serviceUrl.AsSpan(0, markerIndex),
                replacement,
                serviceUrl.AsSpan(markerIndex + "device_service".Length));
    }

    private static string EscapeXml(string value)
        => System.Security.SecurityElement.Escape(value) ?? string.Empty;

    private static string ToXmlNumber(double value)
    {
        if (!double.IsFinite(value))
            throw new ArgumentOutOfRangeException(nameof(value), "PTZ position must be a finite number.");

        return System.Xml.XmlConvert.ToString(value);
    }

    private async Task<string> ResolveProfileTokenAsync(
        string serviceUrl,
        string username,
        string password,
        string profileToken,
        int timeoutMs)
    {
        if (!string.IsNullOrWhiteSpace(profileToken) &&
            !profileToken.Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            return profileToken;
        }

        if (_defaultProfileTokens.TryGetValue(serviceUrl, out var cached) &&
            cached.ExpiresAt > DateTime.UtcNow)
        {
            return cached.Token;
        }

        var profiles = await GetProfilesAsync(serviceUrl, username, password, timeoutMs);
        var token = profiles.FirstOrDefault()?.Token;
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("Camera has no ONVIF media profile for PTZ commands.");

        CacheDefaultProfileToken(serviceUrl, token);
        return token;
    }

    private void CacheDefaultProfileToken(string serviceUrl, string token)
    {
        if (!string.IsNullOrWhiteSpace(token))
            _defaultProfileTokens[serviceUrl] = new ProfileTokenEntry(token, DateTime.UtcNow.AddMinutes(30));
    }

    private sealed record ProfileTokenEntry(string Token, DateTime ExpiresAt);
}

/// <summary>
/// Extension method for safely extracting element values from XElement.
/// </summary>
internal static class XElementExtensions
{
    public static string? ElementValue(this XElement? parent, string localName)
    {
        if (parent == null) return null;

        return parent.Elements()
            .FirstOrDefault(e => e.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase))
            ?.Value;
    }
}
