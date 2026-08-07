using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

/// <summary>
/// Caches the PayPal OAuth2 access token across requests (the gateway itself is a short-lived typed
/// HttpClient) and serialises refreshes so concurrent callers don't each fetch a token. Registered as a
/// singleton.
/// </summary>
public sealed class PayPalAccessTokenCache
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _token;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    /// <summary>Returns a cached token if still valid, otherwise refreshes via <paramref name="refresh"/>.</summary>
    public async Task<string> GetTokenAsync(
        Func<CancellationToken, Task<(string token, int expiresInSeconds)>> refresh,
        CancellationToken cancellationToken)
    {
        if (IsValid())
        {
            return _token!;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsValid())
            {
                return _token!;
            }

            var (token, expiresInSeconds) = await refresh(cancellationToken).ConfigureAwait(false);
            _token = token;
            // Renew a minute early to avoid using a token that expires in-flight.
            _expiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(30, expiresInSeconds - 60));
            return _token;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Forces the next call to refresh (used after a 401 from the API).</summary>
    public void Invalidate() => _expiresAt = DateTimeOffset.MinValue;

    private bool IsValid() => _token != null && DateTimeOffset.UtcNow < _expiresAt;
}
