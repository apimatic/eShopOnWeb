using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing. Maxio Advanced Billing is the system of record.
/// </summary>
public interface ISubscriptionBillingService
{
    Task<Result<IReadOnlyList<SubscriptionPlan>>> ListPlansAsync(CancellationToken cancellationToken = default);

    Task<Result<SubscribeResult>> SubscribeAsync(
        ShopperBillingIdentity shopper,
        string productHandle,
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<ShopperSubscription>>> ListMySubscriptionsAsync(
        ShopperBillingIdentity shopper,
        CancellationToken cancellationToken = default);
}
