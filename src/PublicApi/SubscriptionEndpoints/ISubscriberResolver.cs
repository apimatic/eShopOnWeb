using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Turns the caller's bearer token into the <see cref="Subscriber"/> the billing capability works
/// with. Subscription endpoints never accept a user identifier from the request body.
/// </summary>
public interface ISubscriberResolver
{
    Task<Subscriber> ResolveAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default);
}
