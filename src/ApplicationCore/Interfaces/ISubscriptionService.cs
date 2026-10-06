using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Models.Subscription;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing backed by Maxio Advanced Billing as the billing system of record.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>
    /// Lists the subscription plans available for enrollment (the products of the configured Maxio product family).
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Idempotently ensures a Maxio customer exists for the given eShopOnWeb user and enrolls them
    /// in the plan identified by its Maxio product handle. Calling it again with the same user and
    /// plan never creates a second customer or subscription.
    /// </summary>
    Task<SubscriptionDetails> SubscribeAsync(string userName, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the caller's Maxio subscriptions with plan, price, state and next-billing date.
    /// </summary>
    Task<IReadOnlyList<SubscriptionDetails>> GetMySubscriptionsAsync(string userName, CancellationToken cancellationToken = default);
}