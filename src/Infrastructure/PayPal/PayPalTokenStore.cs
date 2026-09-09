using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Singleton cache for the PayPal OAuth access token. Tokens are valid for several hours;
/// caching avoids requesting a new one on every API call. Refresh is serialised so that a
/// burst of concurrent requests triggers at most one token call.
/// </summary>
public class PayPalTokenStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _token;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    /// <summary>
    /// Returns a cached token if still valid, otherwise calls <paramref name="fetch"/> once
    /// (under a lock) to obtain a fresh token and its lifetime in seconds.
    /// </summary>
    public async Task<string> GetAsync(Func<CancellationToken, Task<(string token, int expiresInSeconds)>> fetch,
        CancellationToken cancellationToken)
    {
        // Fast path: valid cached token (with a safety margin before expiry).
        if (_token is not null && DateTimeOffset.UtcNow < _expiresAt)
        {
            return _token;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_token is not null && DateTimeOffset.UtcNow < _expiresAt)
            {
                return _token;
            }

            var (token, expiresInSeconds) = await fetch(cancellationToken);
            _token = token;
            // Refresh 60s early to avoid using a token that expires mid-request.
            var margin = Math.Min(60, Math.Max(0, expiresInSeconds - 1));
            _expiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresInSeconds - margin);
            return token;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Invalidate the cached token (e.g. after a 401) so the next call re-fetches.</summary>
    public void Invalidate()
    {
        _token = null;
        _expiresAt = DateTimeOffset.MinValue;
    }
}
