using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring subscription billing. The billing system is the system of record: nothing about
/// plans or subscriptions is persisted in the eShopOnWeb databases.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>Plans currently offered by the configured product family.</summary>
    Task<SubscriptionResult<IReadOnlyList<SubscriptionPlan>>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls the subscriber in a plan, creating their billing customer record first if
    /// needed. Safe to call repeatedly: a subscriber already enrolled in the plan gets the
    /// existing subscription back instead of a second one.
    /// </summary>
    Task<SubscriptionResult<SubscriptionEnrollment>> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every subscription belonging to the subscriber, newest first. A subscriber with no
    /// billing customer record yet is not an error - they simply have no subscriptions.
    /// </summary>
    Task<SubscriptionResult<IReadOnlyList<CustomerSubscription>>> ListSubscriptionsAsync(Subscriber subscriber, CancellationToken cancellationToken = default);
}
