namespace CameraMaintainer.Core.Interfaces;

/// <summary>
/// Low-level ONVIF client — handles SOAP communication with a single camera.
/// </summary>
public interface IOnvifClient
{
    // Device
    Task<DeviceInfo> GetDeviceInfoAsync(string serviceUrl, string username, string password, int timeoutMs = 10000);
    Task<SystemDateTime> GetSystemDateTimeAsync(string serviceUrl, string username, string password, int timeoutMs = 10000);
    Task SetSystemDateTimeAsync(string serviceUrl, string username, string password, DateTime dateTime, int timeoutMs = 10000);
    Task<DeviceCapabilities> GetCapabilitiesAsync(string serviceUrl, string username, string password, int timeoutMs = 10000);

    // PTZ
    Task<List<PtzProfileInfo>> GetPtzProfilesAsync(string serviceUrl, string username, string password, int timeoutMs = 10000);
    Task AbsoluteMoveAsync(string serviceUrl, string username, string password, string profileToken, double pan, double tilt, double zoom, int timeoutMs = 10000);
    Task RelativeMoveAsync(string serviceUrl, string username, string password, string profileToken, double pan, double tilt, double zoom, int timeoutMs = 10000);
    Task StopMoveAsync(string serviceUrl, string username, string password, string profileToken, int timeoutMs = 10000);
    Task<List<PtzPresetInfo>> GetPresetsAsync(string serviceUrl, string username, string password, string profileToken, int timeoutMs = 10000);
    Task<string> SetPresetAsync(string serviceUrl, string username, string password, string profileToken, string presetName, int timeoutMs = 10000);
    Task GotoPresetAsync(string serviceUrl, string username, string password, string profileToken, string presetToken, int timeoutMs = 10000);

    // Media
    Task<List<MediaProfile>> GetProfilesAsync(string serviceUrl, string username, string password, int timeoutMs = 10000);
    Task<string> GetSnapshotUriAsync(string serviceUrl, string username, string password, string profileToken, int timeoutMs = 10000);

    // System
    Task RebootAsync(string serviceUrl, string username, string password, int timeoutMs = 10000);
    Task FactoryResetAsync(string serviceUrl, string username, string password, string mode, int timeoutMs = 10000);

    // Network
    Task<NetworkConfig> GetNetworkConfigAsync(string serviceUrl, string username, string password, int timeoutMs = 10000);

    // Users
    Task<List<UserInfo>> GetUsersAsync(string serviceUrl, string username, string password, int timeoutMs = 10000);
    Task CreateUserAsync(string serviceUrl, string username, string password, string newUsername, string newPassword, string userLevel, int timeoutMs = 10000);
    Task DeleteUserAsync(string serviceUrl, string username, string password, string targetUsername, int timeoutMs = 10000);
}

// DTOs
public record DeviceInfo(string Manufacturer, string Model, string SerialNumber, string FirmwareVersion, string HardwareId);
public record SystemDateTime(DateTime UtcDateTime, string TimeZone, bool NtpEnabled);
public record DeviceCapabilities(bool PtzSupported, bool EventsSupported, bool AnalyticsSupported, string RawXml);
public record PtzProfileInfo(string Token, string Name, double MinPan, double MaxPan, double MinTilt, double MaxTilt, double MinZoom, double MaxZoom);
public record PtzPresetInfo(string Token, string Name);
public record MediaProfile(
    string Token,
    string Name,
    string VideoSourceToken,
    string VideoEncoderToken,
    string Resolution,
    int Framerate,
    int Bitrate,
    string VideoEncoding = "");
public record NetworkConfig(bool DhcpEnabled, string? IpAddress, string? SubnetMask, string? DefaultGateway, string? DnsServer);
public record UserInfo(string Username, string UserLevel);
