using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, backed by the billing system of record (Maxio).
/// All failures surface as <see cref="Exceptions.BillingException"/>.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>Lists the purchasable plans in the configured product family.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls the shopper in a plan. Idempotent: the provider-side customer is found or
    /// created by stable reference, and a shopper with a live subscription gets that
    /// existing subscription back rather than a second enrollment.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(ShopperIdentity shopper, string productHandle, CancellationToken cancellationToken = default);

    /// <summary>Lists the shopper's subscriptions; empty when none exist yet.</summary>
    Task<IReadOnlyList<CustomerSubscription>> ListSubscriptionsAsync(string username, CancellationToken cancellationToken = default);
}
