using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Caches the PayPal OAuth access token across requests so the client credentials are exchanged
/// only when the token is missing or about to expire. Registered as a singleton; the typed HTTP
/// client that acquires tokens is transient, so the cache must live outside it.
/// </summary>
public sealed class PayPalTokenProvider
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _token;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    /// <summary>
    /// Returns a valid access token, calling <paramref name="acquire"/> to fetch a fresh one only when
    /// needed. The acquire delegate returns the token and its lifetime in seconds.
    /// </summary>
    public async Task<string> GetAsync(Func<CancellationToken, Task<(string Token, int ExpiresInSeconds)>> acquire,
        CancellationToken cancellationToken)
    {
        // A 60-second safety margin avoids using a token that expires mid-flight.
        if (_token is not null && DateTimeOffset.UtcNow < _expiresAt.AddSeconds(-60))
            return _token;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_token is not null && DateTimeOffset.UtcNow < _expiresAt.AddSeconds(-60))
                return _token;

            var (token, expiresIn) = await acquire(cancellationToken);
            _token = token;
            _expiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
            return token;
        }
        finally
        {
            _gate.Release();
        }
    }
}
