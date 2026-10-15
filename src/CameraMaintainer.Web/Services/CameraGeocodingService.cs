using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace CameraMaintainer.Web.Services;

public sealed class GeocodingUnavailableException(string message, int statusCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

// Interactive reverse lookup only: never called from the monitoring queue.
public sealed class CameraGeocodingService(IHttpClientFactory clients, IConfiguration configuration) : IDisposable
{
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 1024 });
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _nextRequest;

    public async Task<string?> ResolveAsync(double latitude, double longitude, CancellationToken token)
    {
        if (!double.IsFinite(latitude) || !double.IsFinite(longitude) || Math.Abs(latitude) > 90 || Math.Abs(longitude) > 180)
            throw new GeocodingUnavailableException("Некорректные координаты.", 400);
        var endpoint = configuration["Geocoding:ReverseUrl"];
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)))
            throw new GeocodingUnavailableException("Сервис определения улицы не настроен. Укажите адрес вручную или настройте Geocoding:ReverseUrl.", 503);
        var key = string.Create(CultureInfo.InvariantCulture, $"{endpoint}|{latitude:F6}|{longitude:F6}");
        if (_cache.TryGetValue(key, out string? cached)) return cached;
        if (!await _gate.WaitAsync(0, token))
            throw new GeocodingUnavailableException("Поиск адреса занят. Повторите через несколько секунд.", 429);
        try
        {
            if (_cache.TryGetValue(key, out cached)) return cached;
            var delay = _nextRequest - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.FromMilliseconds(1200))
                throw new GeocodingUnavailableException("Сервис адресов временно ограничил запросы. Повторите позже.", 429);
            if (delay > TimeSpan.Zero) await Task.Delay(delay, token);
            _nextRequest = DateTimeOffset.UtcNow.AddMilliseconds(1100);
            var url = Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(uri.ToString(), new Dictionary<string, string?>
            {
                ["lat"] = latitude.ToString("F6", CultureInfo.InvariantCulture),
                ["lon"] = longitude.ToString("F6", CultureInfo.InvariantCulture),
                ["format"] = "jsonv2",
                ["addressdetails"] = "1",
                ["zoom"] = "18",
                ["accept-language"] = "ru,uz,en"
            });
            using var client = clients.CreateClient("CameraGeocoding");
            using var response = await client.GetAsync(url, token);
            if ((int)response.StatusCode == 429)
            {
                var retry = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromMinutes(1);
                _nextRequest = DateTimeOffset.UtcNow.AddSeconds(Math.Clamp(retry.TotalSeconds, 1, 300));
                throw new GeocodingUnavailableException("Сервис адресов ограничил запросы. Повторите позже.", 429);
            }
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            var address = ReadStreetAddress(document.RootElement);
            _cache.Set(key, address, new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(7) });
            return address;
        }
        catch (JsonException) { throw new GeocodingUnavailableException("Сервис адресов вернул некорректный ответ.", 502); }
        finally { _gate.Release(); }
    }

    public static string? ReadStreetAddress(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("address", out var address) || address.ValueKind != JsonValueKind.Object) return null;
        string? Pick(params string[] keys) => keys.Select(key => address.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        var street = Pick("road", "pedestrian", "residential", "footway", "path");
        if (street == null) return null;
        var parts = new[] { Pick("city", "town", "village", "municipality"), street, Pick("house_number") };
        var result = string.Join(", ", parts.Where(value => !string.IsNullOrWhiteSpace(value)));
        return result.Length <= 600 ? result : result[..600];
    }

    public void Dispose() { _cache.Dispose(); _gate.Dispose(); }
}
