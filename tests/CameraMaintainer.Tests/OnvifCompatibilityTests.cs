using System.Net;
using System.Globalization;
using System.Text;
using System.Xml.Linq;
using CameraMaintainer.Onvif;
using CameraMaintainer.Services;

namespace CameraMaintainer.Tests;

public class OnvifCompatibilityTests
{
    [Fact]
    public async Task SoapClient_ExposesNotAuthorizedFaultReturnedWithHttp400()
    {
        const string fault = """
            <s:Envelope xmlns:s="http://www.w3.org/2003/05/soap-envelope"
                        xmlns:ter="http://www.onvif.org/ver10/error">
              <s:Body><s:Fault><s:Code><s:Value>s:Sender</s:Value>
                <s:Subcode><s:Value>ter:NotAuthorized</s:Value></s:Subcode>
              </s:Code><s:Reason><s:Text>Sender not Authorized. Invalid username or password!</s:Text>
              </s:Reason></s:Fault></s:Body>
            </s:Envelope>
            """;
        using var http = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(fault, Encoding.UTF8, "application/soap+xml")
        }));
        var soap = new OnvifSoapClient(http);

        var exception = await Assert.ThrowsAsync<OnvifSoapException>(() => soap.SendRequestAsync(
            "http://camera/onvif/device_service",
            "http://www.onvif.org/ver10/device/wsdl/GetDeviceInformation",
            "<tds:GetDeviceInformation/>",
            "admin",
            "password"));

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        Assert.True(exception.IsAuthenticationFailure);
        Assert.Contains("NotAuthorized", exception.FaultCode);
    }

    [Fact]
    public async Task SystemTime_IsRequestedWithoutWsSecurityFirst()
    {
        string? requestBody = null;
        const string response = """
            <s:Envelope xmlns:s="http://www.w3.org/2003/05/soap-envelope"
                        xmlns:tds="http://www.onvif.org/ver10/device/wsdl"
                        xmlns:tt="http://www.onvif.org/ver10/schema">
              <s:Body><tds:GetSystemDateAndTimeResponse><tds:SystemDateAndTime>
                <tt:UTCDateTime><tt:Time><tt:Hour>8</tt:Hour><tt:Minute>5</tt:Minute><tt:Second>0</tt:Second></tt:Time>
                <tt:Date><tt:Year>2026</tt:Year><tt:Month>8</tt:Month><tt:Day>5</tt:Day></tt:Date></tt:UTCDateTime>
                <tt:TimeZone><tt:TZ>UTC+5</tt:TZ></tt:TimeZone>
              </tds:SystemDateAndTime></tds:GetSystemDateAndTimeResponse></s:Body>
            </s:Envelope>
            """;
        using var http = new HttpClient(new StubHandler(async request =>
        {
            requestBody = await request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/soap+xml")
            };
        }));
        var client = new OnvifClient(http);

        var result = await client.GetSystemDateTimeAsync(
            "http://camera/onvif/device_service",
            "admin",
            "password");

        Assert.Equal(new DateTime(2026, 8, 5, 8, 5, 0, DateTimeKind.Utc), result.UtcDateTime);
        Assert.DoesNotContain("UsernameToken", requestBody);
    }

    [Fact]
    public async Task MediaProfiles_ExposeEncodingAndLoadParameters()
    {
        const string capabilities = """
            <s:Envelope xmlns:s="http://www.w3.org/2003/05/soap-envelope"
                        xmlns:tds="http://www.onvif.org/ver10/device/wsdl"
                        xmlns:tt="http://www.onvif.org/ver10/schema">
              <s:Body><tds:GetCapabilitiesResponse><tds:Capabilities>
                <tt:Media><tt:XAddr>http://camera/onvif/media_service</tt:XAddr></tt:Media>
              </tds:Capabilities></tds:GetCapabilitiesResponse></s:Body>
            </s:Envelope>
            """;
        const string profiles = """
            <s:Envelope xmlns:s="http://www.w3.org/2003/05/soap-envelope"
                        xmlns:trt="http://www.onvif.org/ver10/media/wsdl"
                        xmlns:tt="http://www.onvif.org/ver10/schema">
              <s:Body><trt:GetProfilesResponse><trt:Profiles token="sub-1">
                <tt:Name>Sub stream</tt:Name>
                <tt:VideoSourceConfiguration token="source-1" />
                <tt:VideoEncoderConfiguration token="encoder-1">
                  <tt:Encoding>H264</tt:Encoding>
                  <tt:Resolution><tt:Width>640</tt:Width><tt:Height>360</tt:Height></tt:Resolution>
                  <tt:RateControl><tt:FrameRateLimit>15</tt:FrameRateLimit><tt:BitrateLimit>512</tt:BitrateLimit></tt:RateControl>
                </tt:VideoEncoderConfiguration>
              </trt:Profiles></trt:GetProfilesResponse></s:Body>
            </s:Envelope>
            """;
        using var http = new HttpClient(new StubHandler(async request =>
        {
            var body = await request.Content!.ReadAsStringAsync();
            var responseBody = body.Contains("GetCapabilities", StringComparison.Ordinal)
                ? capabilities
                : profiles;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/soap+xml")
            };
        }));
        var client = new OnvifClient(http);

        var result = Assert.Single(await client.GetProfilesAsync(
            "http://camera/onvif/device_service",
            "admin",
            "password"));

        Assert.Equal("H264", result.VideoEncoding);
        Assert.Equal("640x360", result.Resolution);
        Assert.Equal(15, result.Framerate);
        Assert.Equal(512, result.Bitrate);
        Assert.Equal("encoder-1", result.VideoEncoderToken);
    }

    [Fact]
    public async Task CapabilityCache_SharesConcurrentFetch()
    {
        var cache = new OnvifCapabilityCache(ttlMinutes: 30);
        var fetchStarted = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFetch = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var fetchCount = 0;

        async Task<string> FetchAsync()
        {
            Interlocked.Increment(ref fetchCount);
            fetchStarted.SetResult(true);
            return await releaseFetch.Task;
        }

        var first = cache.GetOrFetchAsync(
            "http://camera/onvif/device_service", FetchAsync);
        await fetchStarted.Task;
        var second = cache.GetOrFetchAsync(
            "http://camera/onvif/device_service", FetchAsync);

        releaseFetch.SetResult("""
            <s:Envelope xmlns:s="http://www.w3.org/2003/05/soap-envelope"
                        xmlns:tt="http://www.onvif.org/ver10/schema">
              <s:Body><tt:Capabilities>
                <tt:Media><tt:XAddr>http://camera/onvif/media_service</tt:XAddr></tt:Media>
              </tt:Capabilities></s:Body>
            </s:Envelope>
            """);

        var results = await Task.WhenAll(first, second);

        Assert.Equal(1, fetchCount);
        Assert.All(results, result => Assert.Equal(
            "http://camera/onvif/media_service", result!.MediaUrl));
    }

    [Fact]
    public async Task AbsoluteMove_UsesInvariantNumbersAndEscapesProfileToken()
    {
        string? moveRequest = null;
        using var http = new HttpClient(new StubHandler(async request =>
        {
            var body = await request.Content!.ReadAsStringAsync();
            if (body.Contains("GetCapabilities", StringComparison.Ordinal))
            {
                return SoapResponse("""
                    <tds:GetCapabilitiesResponse xmlns:tds="http://www.onvif.org/ver10/device/wsdl">
                      <tds:Capabilities><tds:PTZ /></tds:Capabilities>
                    </tds:GetCapabilitiesResponse>
                    """);
            }

            moveRequest = body;
            return SoapResponse("""
                <tptz:AbsoluteMoveResponse xmlns:tptz="http://www.onvif.org/ver20/ptz/wsdl" />
                """);
        }));
        var client = new OnvifClient(http);
        var originalCulture = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
            await client.AbsoluteMoveAsync(
                "http://camera/onvif/device_service",
                "admin",
                "password",
                "profile<&\"1",
                0.25,
                -0.5,
                0.75);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }

        Assert.NotNull(moveRequest);
        Assert.Contains("x=\"0.25\" y=\"-0.5\"", moveRequest);
        Assert.Contains(">profile&lt;&amp;&quot;1<", moveRequest);
        _ = XDocument.Parse(moveRequest);
    }

    [Theory]
    [InlineData("hard", "Hard")]
    [InlineData("SOFT", "Soft")]
    public async Task FactoryReset_UsesOnvifEnumValue(string mode, string expectedValue)
    {
        string? requestBody = null;
        using var http = new HttpClient(new StubHandler(async request =>
        {
            requestBody = await request.Content!.ReadAsStringAsync();
            return SoapResponse("""
                <tds:SetSystemFactoryDefaultResponse xmlns:tds="http://www.onvif.org/ver10/device/wsdl" />
                """);
        }));
        var client = new OnvifClient(http);

        await client.FactoryResetAsync(
            "http://camera/onvif/device_service",
            "admin",
            "password",
            mode);

        Assert.Contains($"<tds:FactoryDefault>{expectedValue}</tds:FactoryDefault>", requestBody);
    }

    [Fact]
    public async Task FactoryReset_RejectsUnknownModeBeforeSendingRequest()
    {
        var requestCount = 0;
        using var http = new HttpClient(new StubHandler(_ =>
        {
            requestCount++;
            return SoapResponse(string.Empty);
        }));
        var client = new OnvifClient(http);

        await Assert.ThrowsAsync<ArgumentException>(() => client.FactoryResetAsync(
            "http://camera/onvif/device_service",
            "admin",
            "password",
            "unknown"));

        Assert.Equal(0, requestCount);
    }

    [Fact]
    public async Task NetworkConfig_ReadsStandardDeviceManagementResponses()
    {
        using var http = new HttpClient(new StubHandler(async request =>
        {
            var body = await request.Content!.ReadAsStringAsync();
            if (body.Contains("GetNetworkInterfaces", StringComparison.Ordinal))
            {
                return SoapResponse("""
                    <tds:GetNetworkInterfacesResponse xmlns:tds="http://www.onvif.org/ver10/device/wsdl" xmlns:tt="http://www.onvif.org/ver10/schema">
                      <tds:NetworkInterfaces token="eth0"><tt:IPv4><tt:Config>
                        <tt:Manual><tt:Address>192.168.4.31</tt:Address><tt:PrefixLength>24</tt:PrefixLength></tt:Manual>
                        <tt:DHCP>false</tt:DHCP>
                      </tt:Config></tt:IPv4></tds:NetworkInterfaces>
                    </tds:GetNetworkInterfacesResponse>
                    """);
            }
            if (body.Contains("GetNetworkDefaultGateway", StringComparison.Ordinal))
            {
                return SoapResponse("""
                    <tds:GetNetworkDefaultGatewayResponse xmlns:tds="http://www.onvif.org/ver10/device/wsdl" xmlns:tt="http://www.onvif.org/ver10/schema">
                      <tds:NetworkGateway><tt:IPv4Address>192.168.4.1</tt:IPv4Address></tds:NetworkGateway>
                    </tds:GetNetworkDefaultGatewayResponse>
                    """);
            }

            return SoapResponse("""
                <tds:GetDNSResponse xmlns:tds="http://www.onvif.org/ver10/device/wsdl" xmlns:tt="http://www.onvif.org/ver10/schema">
                  <tds:DNSInformation><tt:DNSManual><tt:Type>IPv4</tt:Type><tt:IPv4Address>1.1.1.1</tt:IPv4Address></tt:DNSManual></tds:DNSInformation>
                </tds:GetDNSResponse>
                """);
        }));
        var client = new OnvifClient(http);

        var config = await client.GetNetworkConfigAsync(
            "http://camera/onvif/device_service",
            "admin",
            "password");

        Assert.False(config.DhcpEnabled);
        Assert.Equal("192.168.4.31", config.IpAddress);
        Assert.Equal("255.255.255.0", config.SubnetMask);
        Assert.Equal("192.168.4.1", config.DefaultGateway);
        Assert.Equal("1.1.1.1", config.DnsServer);
    }

    private static HttpResponseMessage SoapResponse(string body)
    {
        var envelope = $"""
            <s:Envelope xmlns:s="http://www.w3.org/2003/05/soap-envelope"><s:Body>{body}</s:Body></s:Envelope>
            """;
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(envelope, Encoding.UTF8, "application/soap+xml")
        };
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
            : this(request => Task.FromResult(handler(request)))
        {
        }

        public StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return _handler(request);
        }
    }
}
