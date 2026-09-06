using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Stands in when the <c>Maxio:</c> section is not configured on this host.
/// <para>
/// Failing the whole application at startup would take the existing storefront down over a capability
/// it does not use; silently returning empty results would be worse still, because a shopper would see
/// "no plans" and believe it. So the subscription endpoints — and only they — report the integration as
/// unavailable, with a message that names what is missing rather than leaking what is configured.
/// </para>
/// </summary>
public class UnconfiguredSubscriptionBillingService : ISubscriptionBillingService
{
    private readonly string _missing;

    public UnconfiguredSubscriptionBillingService(MaxioSettings settings)
    {
        _missing = settings.DescribeMissing();
    }

    public Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default) =>
        throw NotConfigured();

    public Task<SubscribeResult> SubscribeAsync(SubscriberIdentity subscriber, string planHandle, CancellationToken cancellationToken = default) =>
        throw NotConfigured();

    public Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken = default) =>
        throw NotConfigured();

    private BillingProviderException NotConfigured() => new(
        $"Subscription billing is not configured on this server (missing: {_missing}).",
        BillingFailureKind.NotConfigured);
}
