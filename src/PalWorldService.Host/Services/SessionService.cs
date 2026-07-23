using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace PalWorldService.Host.Services;

public class SessionInfo
{
    public required string ServerId { get; init; }
    public DateTime ExpiresAtUtc { get; set; }
}

public class SessionService
{
    public const string CookieName = "pal_session";
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(12);
    private readonly ConcurrentDictionary<string, SessionInfo> _sessions = new();

    public string Create(string serverId)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        _sessions[token] = new SessionInfo
        {
            ServerId = serverId,
            ExpiresAtUtc = DateTime.UtcNow.Add(Ttl)
        };
        return token;
    }

    public bool TryValidate(string? token, string serverId, out SessionInfo? info)
    {
        info = null;
        if (string.IsNullOrWhiteSpace(token)) return false;
        if (!_sessions.TryGetValue(token, out var session)) return false;
        if (session.ExpiresAtUtc < DateTime.UtcNow)
        {
            _sessions.TryRemove(token, out _);
            return false;
        }
        if (!string.Equals(session.ServerId, serverId, StringComparison.OrdinalIgnoreCase))
            return false;

        session.ExpiresAtUtc = DateTime.UtcNow.Add(Ttl);
        info = session;
        return true;
    }

    public void Revoke(string? token)
    {
        if (!string.IsNullOrWhiteSpace(token))
            _sessions.TryRemove(token, out _);
    }
}
