using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

/// <summary>
/// Caches the PayPal OAuth access token across requests (registered as a singleton). The token is
/// refreshed proactively before it expires; concurrent callers coordinate through a semaphore so the
/// token is fetched once, not per request.
/// </summary>
public class PayPalTokenStore
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _token;
    private DateTimeOffset _expiresAt;

    public async Task<string> GetTokenAsync(
        Func<CancellationToken, Task<(string Token, int ExpiresInSeconds)>> fetch,
        CancellationToken cancellationToken)
    {
        if (_token is not null && DateTimeOffset.UtcNow < _expiresAt)
        {
            return _token;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_token is not null && DateTimeOffset.UtcNow < _expiresAt)
            {
                return _token;
            }

            var (token, expiresIn) = await fetch(cancellationToken);
            _token = token;
            // Refresh a minute early to avoid using a token that expires mid-flight.
            _expiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, expiresIn - 60));
            return token;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Invalidate()
    {
        _token = null;
        _expiresAt = DateTimeOffset.MinValue;
    }
}
