using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Stand-in used when no billing configuration is present at all. It keeps deployments that do not offer
/// subscriptions (and the existing test hosts) starting normally, while every subscription endpoint answers
/// with a clear "not configured" failure instead of a null-reference or a misleading provider error.
/// </summary>
/// <remarks>
/// A <em>partially</em> configured section is a different case and is rejected at startup: silently serving
/// "not configured" would hide a real deployment mistake.
/// </remarks>
public sealed class UnconfiguredSubscriptionBillingService : ISubscriptionBillingService
{
    public Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default) =>
        throw NotConfigured();

    public Task<SubscriptionEnrollment> SubscribeAsync(
        SubscriberIdentity subscriber,
        string planHandle,
        CancellationToken cancellationToken = default) =>
        throw NotConfigured();

    public Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(
        SubscriberIdentity subscriber,
        CancellationToken cancellationToken = default) =>
        throw NotConfigured();

    private static SubscriptionBillingException NotConfigured() =>
        new(SubscriptionBillingFailure.NotConfigured,
            "Subscription billing is not configured for this deployment.");
}
