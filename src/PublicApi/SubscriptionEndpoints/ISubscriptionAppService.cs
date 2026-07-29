using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Request-scoped facade that resolves the current JWT-authenticated user and delegates to the
/// provider-agnostic <see cref="Microsoft.eShopWeb.ApplicationCore.Interfaces.ISubscriptionBillingService"/>.
/// Keeps the endpoints free of identity plumbing.
/// </summary>
public interface ISubscriptionAppService
{
    Task<IReadOnlyCollection<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken);

    Task<SubscribeResult> SubscribeAsync(string planHandle, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<CustomerSubscription>> GetMySubscriptionsAsync(CancellationToken cancellationToken);
}
