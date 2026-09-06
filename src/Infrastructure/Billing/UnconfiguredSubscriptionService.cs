using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Stand-in used when no billing provider is configured.
/// </summary>
/// <remarks>
/// Subscription billing is an additive capability alongside the catalog, basket and order flows,
/// so a deployment that has not configured it should still start and serve everything else. Each
/// subscription call fails loudly and specifically instead, which the API surface reports as
/// <c>503 Service Unavailable</c>.
/// </remarks>
public class UnconfiguredSubscriptionService : ISubscriptionService
{
    private readonly string _reason;

    public UnconfiguredSubscriptionService(string reason) => _reason = reason;

    public Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default) =>
        throw new BillingNotConfiguredException(_reason);

    public Task<SubscribeResult> SubscribeAsync(
        Subscriber subscriber,
        SubscribeRequest request,
        CancellationToken cancellationToken = default) =>
        throw new BillingNotConfiguredException(_reason);

    public Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(
        Subscriber subscriber,
        CancellationToken cancellationToken = default) =>
        throw new BillingNotConfiguredException(_reason);
}
