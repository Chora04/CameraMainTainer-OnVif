using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CameraMaintainer.Web.Security;

public static class AccessVersion
{
    public static string For(ClaimsPrincipal user)
    {
        var claims = user.Claims.Select(claim => new { claim.Type, claim.Value })
            .OrderBy(claim => claim.Type, StringComparer.Ordinal)
            .ThenBy(claim => claim.Value, StringComparer.Ordinal);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(claims))));
    }
}
