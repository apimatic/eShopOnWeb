using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Thread-safe, process-wide cache for PayPal's short-lived OAuth access token. Registered as a singleton
/// so a fresh token is fetched only when the current one is missing or near expiry, and concurrent callers
/// share a single refresh instead of stampeding the token endpoint.
/// </summary>
public class PayPalTokenStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _token;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    /// <summary>Returns a valid token, invoking <paramref name="fetch"/> only when a refresh is needed.</summary>
    public async Task<string> GetAsync(Func<CancellationToken, Task<(string token, int expiresInSeconds)>> fetch,
        CancellationToken cancellationToken)
    {
        if (IsValid())
        {
            return _token!;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (IsValid())
            {
                return _token!;
            }

            var (token, expiresIn) = await fetch(cancellationToken);
            _token = token;
            // Refresh a minute early to avoid using a token that expires mid-request.
            _expiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(30, expiresIn - 60));
            return _token;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Forces the next <see cref="GetAsync"/> to refresh (used after a 401).</summary>
    public void Invalidate()
    {
        _expiresAt = DateTimeOffset.MinValue;
        _token = null;
    }

    private bool IsValid() => _token is not null && DateTimeOffset.UtcNow < _expiresAt;
}
