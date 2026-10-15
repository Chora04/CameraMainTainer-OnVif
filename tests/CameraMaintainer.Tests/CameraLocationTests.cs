using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json;
using CameraMaintainer.Web.Models;
using CameraMaintainer.Web.Services;
using Microsoft.Extensions.Configuration;

namespace CameraMaintainer.Tests;

public sealed class CameraLocationTests
{
    [Theory]
    [InlineData(null, null, true)]
    [InlineData(0d, 0d, true)]
    [InlineData(-90d, 180d, true)]
    [InlineData(41.3, 69.2, true)]
    [InlineData(91d, 0d, false)]
    [InlineData(0d, -181d, false)]
    [InlineData(41d, null, false)]
    [InlineData(double.NaN, 0d, false)]
    [InlineData(double.PositiveInfinity, 0d, false)]
    public void Coordinates_ValidatePairAndBounds(double? latitude, double? longitude, bool valid)
    {
        var input = new CameraLocationInput { Latitude = latitude, Longitude = longitude };
        Assert.Equal(valid, Validator.TryValidateObject(input, new ValidationContext(input), [], true));
    }

    [Fact]
    public async Task ReverseLookup_CachesResultsAndCanUseLocalProvider()
    {
        using var handler = new ResponseHandler();
        using var geocoder = Service(handler, "http://localhost:8080/reverse");
        var result = await geocoder.ResolveAsync(41.311081, 69.240562, default);
        Assert.Equal("Test city, Test street, 7", result);
        Assert.Equal(result, await geocoder.ResolveAsync(41.311081, 69.240562, default));
        Assert.Equal(1, handler.Calls);
        Assert.Contains("lat=41.311081", handler.LastUri);
        Assert.Contains("lon=69.240562", handler.LastUri);
        Assert.DoesNotContain("password", handler.LastUri);
    }

    [Fact]
    public async Task ReverseLookup_DisabledAndInvalidCoordinatesDoNotCallNetwork()
    {
        using var handler = new ResponseHandler();
        using var disabled = Service(handler, "");
        Assert.Equal(503, (await Assert.ThrowsAsync<GeocodingUnavailableException>(() => disabled.ResolveAsync(41, 69, default))).StatusCode);
        using var service = Service(handler, "https://geo.example/reverse");
        Assert.Equal(400, (await Assert.ThrowsAsync<GeocodingUnavailableException>(() => service.ResolveAsync(91, 69, default))).StatusCode);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task ReverseLookup_RespectsProviderBackoff()
    {
        using var handler = new ResponseHandler { Status = HttpStatusCode.TooManyRequests };
        using var service = Service(handler, "https://geo.example/reverse");
        Assert.Equal(429, (await Assert.ThrowsAsync<GeocodingUnavailableException>(() => service.ResolveAsync(41, 69, default))).StatusCode);
        Assert.Equal(429, (await Assert.ThrowsAsync<GeocodingUnavailableException>(() => service.ResolveAsync(42, 69, default))).StatusCode);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task ReverseLookup_MissingStreetIsNotInventedAndIsCached()
    {
        using var handler = new ResponseHandler { Body = "{\"address\":{\"city\":\"Test city\"}}" };
        using var service = Service(handler, "https://geo.example/reverse");
        Assert.Null(await service.ResolveAsync(41, 69, default));
        Assert.Null(await service.ResolveAsync(41, 69, default));
        Assert.Equal(1, handler.Calls);
    }

    private static CameraGeocodingService Service(ResponseHandler handler, string endpoint) => new(new ClientFactory(handler),
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Geocoding:ReverseUrl"] = endpoint }).Build());

    private sealed class ClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
    private sealed class ResponseHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string LastUri { get; private set; } = "";
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
        public string Body { get; init; } = "{\"address\":{\"city\":\"Test city\",\"road\":\"Test street\",\"house_number\":\"7\"}}";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastUri = request.RequestUri!.ToString();
            return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(Body) });
        }
    }
}
