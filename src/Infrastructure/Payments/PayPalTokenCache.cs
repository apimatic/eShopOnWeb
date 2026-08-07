using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Caches the PayPal OAuth2 access token across requests and refreshes it (once, under a lock) when it
/// is missing or about to expire. Registered as a singleton so the token is shared process-wide.
/// </summary>
public class PayPalTokenCache
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    /// <summary>Safety margin so a token is refreshed slightly before it actually expires.</summary>
    private static readonly TimeSpan ExpiryBuffer = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Returns a valid access token, invoking <paramref name="fetch"/> to obtain a fresh one when
    /// needed. <paramref name="fetch"/> returns the token and its lifetime in seconds.
    /// </summary>
    public async Task<string> GetAccessTokenAsync(
        Func<CancellationToken, Task<(string token, int expiresInSeconds)>> fetch, CancellationToken cancellationToken)
    {
        if (IsValid())
        {
            return _accessToken!;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (IsValid())
            {
                return _accessToken!;
            }

            var (token, expiresInSeconds) = await fetch(cancellationToken);
            _accessToken = token;
            _expiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresInSeconds).Subtract(ExpiryBuffer);
            return _accessToken;
        }
        finally
        {
            _lock.Release();
        }
    }

    private bool IsValid() => _accessToken is not null && DateTimeOffset.UtcNow < _expiresAt;
}
