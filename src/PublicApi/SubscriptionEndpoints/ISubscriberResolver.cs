using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Turns the bearer token's principal into the account we bill for. Subscription endpoints take the
/// subscriber from here and never from the request body, so a caller cannot subscribe on someone
/// else's behalf.
/// </summary>
public interface ISubscriberResolver
{
    /// <summary>Returns null when the principal does not map to a known account.</summary>
    Task<SubscriberIdentity?> ResolveAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default);
}
