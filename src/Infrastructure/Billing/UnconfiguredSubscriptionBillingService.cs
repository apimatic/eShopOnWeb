using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

internal sealed class UnconfiguredSubscriptionBillingService : ISubscriptionBillingService
{
    public Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken)
        => Task.FromException<IReadOnlyList<SubscriptionPlan>>(NotConfigured());

    public Task<ShopperSubscription> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken)
        => Task.FromException<ShopperSubscription>(NotConfigured());

    public Task<IReadOnlyList<ShopperSubscription>> ListMySubscriptionsAsync(string userId, CancellationToken cancellationToken)
        => Task.FromException<IReadOnlyList<ShopperSubscription>>(NotConfigured());

    private static BillingProviderException NotConfigured() =>
        new("Maxio billing is not configured. Set Maxio:ApiKey, Maxio:Subdomain, and Maxio:ProductFamilyHandle.", 503);
}
