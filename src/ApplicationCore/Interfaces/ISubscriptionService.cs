using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription capability for eShopOnWeb shoppers. Runs alongside - and
/// independently of - the existing basket / order checkout flow.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>Plans a shopper can subscribe to.</summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls the user in a plan, creating their billing customer first if needed.
    /// Idempotent: repeating the call returns the existing subscription instead of
    /// creating a second one.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(SubscribeCommand command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscriptions held by the user, newest first. Returns an empty list when the user
    /// has never subscribed.
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(string userName, CancellationToken cancellationToken = default);
}
