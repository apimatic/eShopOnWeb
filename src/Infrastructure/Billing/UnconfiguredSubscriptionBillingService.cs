using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Stand-in used when no billing provider is configured. Subscription billing is an additive
/// capability, so its absence must not stop the host or the one-time commerce flow from starting -
/// it only makes the subscription endpoints answer 503 with an actionable message.
/// </summary>
public class UnconfiguredSubscriptionBillingService : ISubscriptionBillingService
{
    private const string Explanation =
        "Subscription billing is not configured. Set Maxio:ApiKey, Maxio:Subdomain and Maxio:ProductFamilyHandle "
        + "(user-secrets or environment configuration) and restart the API.";

    public Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default) =>
        throw new BillingNotConfiguredException(Explanation);

    public Task<SubscribeResult> SubscribeAsync(SubscribeRequest request,
        CancellationToken cancellationToken = default) =>
        throw new BillingNotConfiguredException(Explanation);

    public Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(SubscriberIdentity subscriber,
        CancellationToken cancellationToken = default) =>
        throw new BillingNotConfiguredException(Explanation);
}
