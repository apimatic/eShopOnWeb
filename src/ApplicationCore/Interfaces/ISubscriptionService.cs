using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, backed by Maxio Advanced Billing as the system of record.
/// This is an additive capability, parallel to the existing one-time Catalog/Basket/Order flow.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>
    /// Lists the plans a shopper may subscribe to (the products in the configured product family).
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls the shopper in the given plan. Ensures a Maxio customer exists for the shopper
    /// (idempotent on <see cref="SubscriberInfo.Reference"/>) and, if the shopper is not already
    /// subscribed to the plan, creates the subscription. Safe to call repeatedly: a double-click
    /// returns the existing subscription rather than creating a second one.
    /// </summary>
    /// <exception cref="Exceptions.SubscriptionPlanNotFoundException">
    /// Thrown when <paramref name="planHandle"/> is not a plan in the configured product family.
    /// </exception>
    Task<SubscribeResult> SubscribeAsync(SubscriberInfo subscriber, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the shopper's subscriptions. Returns an empty list when the shopper has no Maxio
    /// customer record yet.
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(string customerReference, CancellationToken cancellationToken = default);
}
