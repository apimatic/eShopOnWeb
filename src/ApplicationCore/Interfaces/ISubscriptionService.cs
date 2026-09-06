using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription capability, parallel to the one-time Basket/Order flow.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>Plans a shopper may sign up for.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The hero flow. Ensures a billing customer exists for the shopper and enrolls them on the
    /// requested plan. Safe to call repeatedly: a repeat of the same signup returns the existing
    /// subscription with <see cref="SubscribeResult.Created"/> set to false.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default);

    /// <summary>Everything the shopper is currently subscribed to.</summary>
    Task<SubscriberSubscriptions> GetSubscriptionsAsync(string userKey, CancellationToken cancellationToken = default);
}
