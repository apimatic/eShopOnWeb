using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, backed by the billing system of record.
/// Failures surface as <see cref="Exceptions.BillingProviderException"/> or
/// <see cref="Exceptions.SubscriptionPlanNotOfferedException"/>.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>The plans currently offered for subscription.</summary>
    Task<SubscriptionPlanCatalog> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes the buyer to the plan. Idempotent per buyer and plan: repeating the call returns the
    /// subscription the first call created instead of creating another one.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(BillingBuyer buyer, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>The buyer's subscriptions; empty when the buyer has never subscribed.</summary>
    Task<IReadOnlyList<SubscriptionSummary>> ListSubscriptionsAsync(BillingBuyer buyer, CancellationToken cancellationToken = default);
}
