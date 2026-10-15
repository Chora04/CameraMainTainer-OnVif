using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace CameraMaintainer.Web.Security;

public sealed class AccountSessionStore
{
    private sealed record SessionEntry(int UserId, DateTime ExpiresAt);

    private readonly ConcurrentDictionary<string, SessionEntry> _sessions = new();

    public string Create(int userId, TimeSpan lifetime)
    {
        RemoveExpired();
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _sessions[token] = new SessionEntry(userId, DateTime.UtcNow.Add(lifetime));
        return token;
    }

    public bool TryGetUserId(string token, out int userId)
    {
        userId = 0;
        if (!_sessions.TryGetValue(token, out var session))
            return false;

        if (session.ExpiresAt <= DateTime.UtcNow)
        {
            _sessions.TryRemove(token, out _);
            return false;
        }

        userId = session.UserId;
        return true;
    }

    public void Remove(string token) => _sessions.TryRemove(token, out _);

    public void RemoveUser(int userId)
    {
        foreach (var session in _sessions.Where(x => x.Value.UserId == userId).ToArray())
            _sessions.TryRemove(session.Key, out _);
    }

    private void RemoveExpired()
    {
        var now = DateTime.UtcNow;
        foreach (var session in _sessions.Where(x => x.Value.ExpiresAt <= now).ToArray())
            _sessions.TryRemove(session.Key, out _);
    }
}

public static class SessionTokenReader
{
    private const string Prefix = "Bearer ";
    public const string CookieName = "cm_session";

    public static string? Read(HttpRequest request)
    {
        var authorization = request.Headers.Authorization.ToString();
        if (authorization.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            return authorization[Prefix.Length..].Trim();

        return request.Cookies.TryGetValue(CookieName, out var cookieToken)
            ? cookieToken
            : null;
    }
}
