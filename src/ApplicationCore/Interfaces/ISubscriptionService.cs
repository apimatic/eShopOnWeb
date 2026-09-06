using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing. The billing provider is the system of record: every method
/// reads or writes provider state and returns what the provider reports.
/// Implementations throw <see cref="Exceptions.BillingException"/> (or a subclass) for every
/// failure, so callers have a single failure type to handle.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>Plans a shopper may subscribe to, in the configured product family.</summary>
    Task<IReadOnlyCollection<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls the shopper on a plan. Idempotent: repeating the call for a shopper who already
    /// has a live subscription to that plan returns the existing one instead of creating a second.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every subscription held by the shopper, newest first. Returns an empty collection when
    /// the shopper has never subscribed.
    /// </summary>
    Task<IReadOnlyCollection<CustomerSubscription>> ListSubscriptionsAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken = default);
}
