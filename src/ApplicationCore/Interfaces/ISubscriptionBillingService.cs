using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing, delegated to an external billing system of record.
/// Implementations own the provider contract; callers only see eShopOnWeb concepts.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>
    /// Lists the plans a shopper may subscribe to. Scoped to the configured product family so a
    /// caller can never enroll onto an unrelated product that happens to exist on the billing site.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls a subscriber onto a plan, creating the billing customer if needed.
    /// Idempotent: repeating the same request returns the existing subscription rather than
    /// creating - and billing - a second one.
    /// </summary>
    /// <exception cref="Exceptions.SubscriptionPlanNotFoundException">The plan handle is not offered.</exception>
    /// <exception cref="Exceptions.SubscriptionConflictException">The idempotency key was already consumed by a subscription that is no longer live.</exception>
    /// <exception cref="Exceptions.BillingProviderException">The billing provider rejected the request or was unreachable.</exception>
    Task<SubscribeResult> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists every subscription held by the subscriber. Returns an empty list when the subscriber
    /// has no billing customer record yet.
    /// </summary>
    Task<IReadOnlyList<Subscription>> GetSubscriptionsAsync(Subscriber subscriber, CancellationToken cancellationToken = default);
}
