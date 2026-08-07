using System;
using System.Threading;

namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

/// <summary>
/// Process-wide cache for the PayPal OAuth2 access token. Registered as a singleton
/// so the token is fetched once and shared across requests until it nears expiry.
/// </summary>
public class PayPalAccessTokenCache
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _token;
    private DateTimeOffset _expiresAt;

    public SemaphoreSlim Gate => _lock;

    public bool TryGet(out string token)
    {
        token = _token ?? string.Empty;
        return _token is not null && DateTimeOffset.UtcNow < _expiresAt;
    }

    public void Set(string token, TimeSpan lifetime)
    {
        _token = token;
        // Refresh a minute early to avoid using a token that expires mid-flight.
        var buffer = TimeSpan.FromSeconds(60);
        var effective = lifetime > buffer ? lifetime - buffer : lifetime;
        _expiresAt = DateTimeOffset.UtcNow.Add(effective);
    }
}
