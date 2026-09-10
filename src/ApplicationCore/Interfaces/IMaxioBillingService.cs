using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over Maxio Advanced Billing, the system of record for recurring subscriptions.
/// Implementations talk to the Maxio HTTP API; callers work only in domain terms.
/// </summary>
public interface IMaxioBillingService
{
    /// <summary>
    /// Lists the active subscription plans available in the configured product family.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls <paramref name="subscriber"/> in the plan identified by <paramref name="planHandle"/>.
    /// <para>
    /// Idempotent: ensures a single Maxio customer exists for the subscriber (keyed by their
    /// user id) and will not create a second subscription for a plan the subscriber is already
    /// actively enrolled in — a double submission returns the existing subscription.
    /// </para>
    /// </summary>
    Task<CustomerSubscription> SubscribeAsync(
        BillingSubscriber subscriber,
        string planHandle,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the subscriber's subscriptions. Returns an empty list if the subscriber has never
    /// been provisioned as a Maxio customer.
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(
        BillingSubscriber subscriber,
        CancellationToken cancellationToken = default);
}
