using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

/// <summary>
/// Supplies (and caches) PayPal OAuth 2.0 access tokens. Registered as a singleton so the token is
/// reused across requests and refreshed proactively rather than fetched per call.
/// </summary>
public interface IPayPalTokenProvider
{
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);

    /// <summary>Drops the cached token so the next request fetches a fresh one (e.g. after a 401).</summary>
    void Invalidate();
}
