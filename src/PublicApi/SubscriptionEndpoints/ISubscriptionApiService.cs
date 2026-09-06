using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Per-request entry point the subscription endpoints call. It turns the caller's bearer token into
/// a <see cref="Subscriber"/> and forwards the billing work to the billing system of record.
/// </summary>
public interface ISubscriptionApiService
{
    /// <summary>
    /// Resolves the eShopOnWeb user behind the bearer token, or null when the token names a user
    /// that no longer exists.
    /// </summary>
    Task<Subscriber?> ResolveSubscriberAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    Task<SubscriptionEnrollment> SubscribeAsync(
        Subscriber subscriber,
        string planHandle,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Subscription>> ListSubscriptionsAsync(
        Subscriber subscriber,
        CancellationToken cancellationToken = default);
}
