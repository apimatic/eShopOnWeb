using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <inheritdoc />
public sealed class PayPalTokenProvider : IPayPalTokenProvider
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    // Refresh a little early so an in-flight request never uses a just-expired token.
    private static readonly TimeSpan SkewBeforeExpiry = TimeSpan.FromMinutes(2);

    private PayPalAccessToken? _cached;

    public async Task<string> GetAccessTokenAsync(Func<CancellationToken, Task<PayPalAccessToken>> fetch,
        CancellationToken ct)
    {
        var current = _cached;
        if (IsUsable(current))
            return current!.AccessToken;

        await _gate.WaitAsync(ct);
        try
        {
            if (IsUsable(_cached))
                return _cached!.AccessToken;

            _cached = await fetch(ct);
            return _cached.AccessToken;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Invalidate() => _cached = null;

    private static bool IsUsable(PayPalAccessToken? token) =>
        token is not null && DateTimeOffset.UtcNow < token.ExpiresAt - SkewBeforeExpiry;
}
