using System.Net.Sockets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CameraMaintainer.Web.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/time")]
public class TimeController : ControllerBase
{
    private static readonly string[] NtpHosts =
    {
        "time.google.com",
        "time.cloudflare.com",
        "pool.ntp.org"
    };

    [HttpGet("tashkent")]
    public async Task<ActionResult<TashkentTimeDto>> GetTashkentTime(CancellationToken cancellationToken)
    {
        foreach (var host in NtpHosts)
        {
            try
            {
                var utc = await QueryNtpAsync(host, cancellationToken);
                return Ok(ToDto(utc, "ntp", host));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error) when (error is SocketException or OperationCanceledException or InvalidOperationException)
            {
                // Try the next NTP host. If all fail, return a clearly marked fallback.
            }
        }

        return Ok(ToDto(DateTimeOffset.UtcNow, "system_fallback", null));
    }

    private static TashkentTimeDto ToDto(DateTimeOffset utc, string source, string? sourceHost)
    {
        var tashkent = utc.ToOffset(TimeSpan.FromHours(5));
        return new TashkentTimeDto
        {
            UtcTime = utc,
            UnixTimeMs = utc.ToUnixTimeMilliseconds(),
            TashkentTime = tashkent,
            OffsetMinutes = 300,
            Source = source,
            SourceHost = sourceHost
        };
    }

    private static async Task<DateTimeOffset> QueryNtpAsync(string host, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));

        using var udp = new UdpClient();
        udp.Connect(host, 123);

        var request = new byte[48];
        request[0] = 0x1B;
        await udp.SendAsync(request, timeout.Token);

        var response = await udp.ReceiveAsync(timeout.Token);
        if (response.Buffer.Length < 48)
            throw new InvalidOperationException("Invalid NTP response");

        ulong seconds =
            ((ulong)response.Buffer[40] << 24) |
            ((ulong)response.Buffer[41] << 16) |
            ((ulong)response.Buffer[42] << 8) |
            response.Buffer[43];

        ulong fraction =
            ((ulong)response.Buffer[44] << 24) |
            ((ulong)response.Buffer[45] << 16) |
            ((ulong)response.Buffer[46] << 8) |
            response.Buffer[47];

        const ulong ntpToUnixSeconds = 2_208_988_800UL;
        if (seconds < ntpToUnixSeconds)
            throw new InvalidOperationException("Invalid NTP timestamp");

        var unixSeconds = (long)(seconds - ntpToUnixSeconds);
        var milliseconds = (long)(fraction * 1000UL / 0x1_0000_0000UL);

        return DateTimeOffset.FromUnixTimeMilliseconds(unixSeconds * 1000L + milliseconds);
    }
}

public class TashkentTimeDto
{
    public DateTimeOffset UtcTime { get; set; }
    public long UnixTimeMs { get; set; }
    public DateTimeOffset TashkentTime { get; set; }
    public int OffsetMinutes { get; set; }
    public string Source { get; set; } = string.Empty;
    public string? SourceHost { get; set; }
}
