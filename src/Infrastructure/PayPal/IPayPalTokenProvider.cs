using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>A freshly fetched OAuth token together with its absolute expiry.</summary>
public record PayPalAccessToken(string AccessToken, DateTimeOffset ExpiresAt);

/// <summary>
/// Caches the PayPal OAuth access token across requests (registered as a singleton). The actual HTTP
/// fetch is supplied by the caller so the HTTP concern stays with the <see cref="PayPalClient"/>.
/// </summary>
public interface IPayPalTokenProvider
{
    Task<string> GetAccessTokenAsync(Func<CancellationToken, Task<PayPalAccessToken>> fetch, CancellationToken ct);

    /// <summary>Drops the cached token so the next call fetches a fresh one (used after a 401).</summary>
    void Invalidate();
}
