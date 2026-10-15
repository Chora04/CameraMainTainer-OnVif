using System.Buffers;
using System.Net.Http.Headers;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace CameraMaintainer.Onvif;

public sealed class OnvifSoapException : HttpRequestException
{
    public OnvifSoapException(
        string message,
        System.Net.HttpStatusCode statusCode,
        string? faultCode,
        string? faultReason)
        : base(message, null, statusCode)
    {
        FaultCode = faultCode;
        FaultReason = faultReason;
    }

    public string? FaultCode { get; }
    public string? FaultReason { get; }
    public bool IsAuthenticationFailure =>
        FaultCode?.Contains("NotAuthorized", StringComparison.OrdinalIgnoreCase) == true ||
        FaultReason?.Contains("not authorized", StringComparison.OrdinalIgnoreCase) == true ||
        FaultReason?.Contains("username or password", StringComparison.OrdinalIgnoreCase) == true;
}

/// <summary>
/// Low-level SOAP HTTP client for ONVIF communication.
/// Handles SOAP envelope construction, HTTP transport, and XML response parsing.
/// </summary>
public class OnvifSoapClient
{
    private const int MaxSoapResponseBytes = 2 * 1024 * 1024;
    private readonly HttpClient _httpClient;

    public OnvifSoapClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task<XDocument> SendRequestAsync(
        string serviceUrl,
        string action,
        string bodyXml,
        string? username = null,
        string? password = null,
        int timeoutMs = 10000)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        var securityHeader = (username != null && password != null)
            ? OnvifAuthHandler.BuildSecurityHeader(username, password)
            : null;

        var soapEnvelope = OnvifAuthHandler.BuildSoapEnvelope(serviceUrl, action, bodyXml, securityHeader);
        var content = new StringContent(soapEnvelope, Encoding.UTF8, "application/soap+xml");

        // Set SOAP action header
        content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/soap+xml; charset=utf-8");

        using var request = new HttpRequestMessage(HttpMethod.Post, serviceUrl)
        {
            Content = content
        };
        request.Headers.TryAddWithoutValidation("SOAPAction", action);

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cts.Token);
        var responseBody = await ReadBoundedResponseAsync(response, cts.Token);
        if (!response.IsSuccessStatusCode)
            throw BuildSoapException(response.StatusCode, responseBody);

        return XDocument.Parse(responseBody);
    }

    private static async Task<string> ReadBoundedResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is > MaxSoapResponseBytes)
            throw new InvalidDataException("ONVIF SOAP response exceeds the 2 MB safety limit.");

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var destination = new MemoryStream(
            response.Content.Headers.ContentLength is > 0
                ? (int)Math.Min(response.Content.Headers.ContentLength.Value, MaxSoapResponseBytes)
                : 16 * 1024);
        var buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
        try
        {
            while (true)
            {
                var read = await source.ReadAsync(buffer.AsMemory(), cancellationToken);
                if (read == 0)
                    break;
                if (destination.Length + read > MaxSoapResponseBytes)
                    throw new InvalidDataException("ONVIF SOAP response exceeds the 2 MB safety limit.");

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            return Encoding.UTF8.GetString(destination.GetBuffer(), 0, (int)destination.Length);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Send request and extract a specific element from the SOAP body response.
    /// </summary>
    public async Task<XElement?> SendAndExtractAsync(
        string serviceUrl,
        string action,
        string bodyXml,
        string elementName,
        string? username = null,
        string? password = null,
        int timeoutMs = 10000)
    {
        var doc = await SendRequestAsync(serviceUrl, action, bodyXml, username, password, timeoutMs);
        return doc.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == elementName);
    }

    private static OnvifSoapException BuildSoapException(
        System.Net.HttpStatusCode statusCode,
        string responseBody)
    {
        string? faultCode = null;
        string? faultReason = null;

        try
        {
            var document = XDocument.Parse(responseBody);
            faultCode = document.Descendants()
                .Where(element => element.Name.LocalName is "Value" or "Subcode")
                .Select(element => element.Value.Trim())
                .FirstOrDefault(value => value.Contains("NotAuthorized", StringComparison.OrdinalIgnoreCase))
                ?? document.Descendants().FirstOrDefault(element => element.Name.LocalName == "Value")?.Value.Trim();
            faultReason = document.Descendants()
                .FirstOrDefault(element => element.Name.LocalName is "Text" or "Reason")
                ?.Value.Trim();
        }
        catch (XmlException)
        {
            // Some devices return an empty or non-XML error body.
        }

        var detail = string.IsNullOrWhiteSpace(faultReason)
            ? $"HTTP {(int)statusCode} ({statusCode})"
            : faultReason;
        return new OnvifSoapException(
            $"ONVIF request failed: {detail}",
            statusCode,
            faultCode,
            faultReason);
    }
}
