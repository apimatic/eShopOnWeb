using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing operations backed by Maxio Advanced Billing.
/// This is an additive capability that runs in parallel with the existing one-time
/// commerce flow (Catalog -&gt; Basket -&gt; Order).
/// </summary>
public interface IMaxioSubscriptionService
{
    /// <summary>
    /// Lists the subscription plans on offer — the products belonging to the configured
    /// product family.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls the shopper in a plan. Ensures a Maxio customer exists for the eShopOnWeb
    /// user (keyed idempotently on the user id) and that a duplicate active subscription is
    /// never created — a repeated call returns the existing subscription.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(SubscribeCommand command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the subscriptions currently held by the given shopper. Empty when the shopper
    /// has no Maxio customer record yet.
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken = default);
}
