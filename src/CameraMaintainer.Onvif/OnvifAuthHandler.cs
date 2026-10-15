using System.Security.Cryptography;
using System.Text;
using System.Globalization;

namespace CameraMaintainer.Onvif;

/// <summary>
/// Builds WS-Security UsernameToken headers for ONVIF authentication.
/// ONVIF requires WS-Security with:
/// - PasswordDigest = Base64(SHA1(nonce + created + password))
/// - Nonce = random bytes (Base64)
/// - Created = ISO 8601 timestamp
/// </summary>
public class OnvifAuthHandler
{
    public static string BuildSecurityHeader(string username, string password)
    {
        var nonce = GenerateNonce();
        var created = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        var passwordDigest = ComputePasswordDigest(nonce, created, password);

        return $@"
<wsse:Security xmlns:wsse=""http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd"" xmlns:wsu=""http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd"" SOAP-ENV:mustUnderstand=""1"">
  <wsse:UsernameToken>
    <wsse:Username>{EscapeXml(username)}</wsse:Username>
    <wsse:Password Type=""http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-username-token-profile-1.0#PasswordDigest"">{passwordDigest}</wsse:Password>
    <wsse:Nonce EncodingType=""http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-soap-message-security-1.0#Base64Binary"">{Convert.ToBase64String(nonce)}</wsse:Nonce>
    <wsu:Created>{created}</wsu:Created>
  </wsse:UsernameToken>
</wsse:Security>";
    }

    public static string BuildSoapEnvelope(string serviceUrl, string action, string bodyXml, string? securityHeader = null)
    {
        var securityXml = securityHeader ?? string.Empty;

        return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<SOAP-ENV:Envelope
  xmlns:SOAP-ENV=""http://www.w3.org/2003/05/soap-envelope""
  xmlns:tds=""http://www.onvif.org/ver10/device/wsdl""
  xmlns:tt=""http://www.onvif.org/ver10/schema""
  xmlns:trt=""http://www.onvif.org/ver10/media/wsdl""
  xmlns:tptz=""http://www.onvif.org/ver20/ptz/wsdl""
  xmlns:tev=""http://www.onvif.org/ver10/events/wsdl""
  xmlns:timg=""http://www.onvif.org/ver20/imaging/wsdl""
  xmlns:tdn=""http://www.onvif.org/ver10/network/wsdl""
  xmlns:wsa=""http://www.w3.org/2005/08/addressing""
  xmlns:wsse=""http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd""
  xmlns:wsu=""http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd"">
  <SOAP-ENV:Header>
    <wsa:Action>{EscapeXml(action)}</wsa:Action>
    <wsa:To>{EscapeXml(serviceUrl)}</wsa:To>
    {securityXml}
  </SOAP-ENV:Header>
  <SOAP-ENV:Body>
    {bodyXml}
  </SOAP-ENV:Body>
</SOAP-ENV:Envelope>";
    }

    private static byte[] GenerateNonce()
    {
        return RandomNumberGenerator.GetBytes(16);
    }

    private static string ComputePasswordDigest(byte[] nonce, string created, string password)
    {
        var createdBytes = Encoding.UTF8.GetBytes(created);
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        var combined = new byte[nonce.Length + createdBytes.Length + passwordBytes.Length];
        Buffer.BlockCopy(nonce, 0, combined, 0, nonce.Length);
        Buffer.BlockCopy(createdBytes, 0, combined, nonce.Length, createdBytes.Length);
        Buffer.BlockCopy(passwordBytes, 0, combined, nonce.Length + createdBytes.Length, passwordBytes.Length);

        // ONVIF WS-Security UsernameToken PasswordDigest mandates SHA-1 for interoperability.
#pragma warning disable CA5350
        var hash = SHA1.HashData(combined);
#pragma warning restore CA5350
        return Convert.ToBase64String(hash);
    }

    private static string EscapeXml(string value)
    {
        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("'", "&apos;", StringComparison.Ordinal);
    }
}
