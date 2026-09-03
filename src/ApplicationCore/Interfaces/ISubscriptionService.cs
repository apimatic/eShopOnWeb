using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, with the billing provider (Maxio Advanced Billing) as the
/// system of record. This is an additive capability alongside the existing one-time commerce flow.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>
    /// Lists the plans available to subscribe to (the products in the configured product family).
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls the user in a plan, ensuring a billing customer exists first. Idempotent: a repeated
    /// call for a user already actively subscribed to the plan returns the existing subscription
    /// (<see cref="SubscribeResult.AlreadyExisted"/> = true) instead of creating a duplicate.
    /// </summary>
    /// <param name="subscriber">The eShopOnWeb user to enroll.</param>
    /// <param name="productHandle">The plan to subscribe to; falls back to the configured default when null/blank.</param>
    Task<SubscribeResult> SubscribeAsync(SubscriberInfo subscriber, string? productHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the subscriptions belonging to the user identified by <paramref name="customerReference"/>.
    /// Returns an empty list when no billing customer exists yet for that reference.
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(string customerReference, CancellationToken cancellationToken = default);
}
