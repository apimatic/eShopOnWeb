using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring subscription billing. The billing provider is the system of record for plans,
/// customers and subscriptions; eShopOnWeb only maps its own identities onto them.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>
    /// Lists the plans a shopper may subscribe to (the configured product family's catalog).
    /// </summary>
    Task<IReadOnlyCollection<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls the subscriber onto a plan, creating the billing customer first if needed.
    /// The operation is idempotent: repeating it (double click, client retry) returns the
    /// existing subscription instead of creating a second one.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the subscriptions belonging to the subscriber. Returns an empty collection when
    /// the subscriber has never been enrolled.
    /// </summary>
    Task<IReadOnlyCollection<CustomerSubscription>> ListSubscriptionsAsync(Subscriber subscriber, CancellationToken cancellationToken = default);
}
