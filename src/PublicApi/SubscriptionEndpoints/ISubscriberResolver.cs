using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Turns the bearer token's identity into the subscriber the billing system knows about.
/// </summary>
public interface ISubscriberResolver
{
    /// <summary>
    /// Resolves the caller. Returns null when the token carries no usable identity, which the
    /// endpoints answer as 401 rather than subscribing an anonymous shopper.
    /// </summary>
    Task<Subscriber?> ResolveAsync(ClaimsPrincipal principal, CancellationToken cancellationToken);
}
