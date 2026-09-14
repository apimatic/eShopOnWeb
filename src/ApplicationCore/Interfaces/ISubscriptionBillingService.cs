using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Subscription billing capability backed by an external billing system of record.
/// Customers are correlated to the eShopOnWeb user name via a stable customer reference.
/// </summary>
public interface ISubscriptionBillingService
{
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Idempotently subscribes <paramref name="customerReference"/> to the plan identified by
    /// <paramref name="planHandle"/>. The Maxio customer is created on first use. Subscribing to a
    /// plan the user already holds returns the existing subscription.
    /// </summary>
    Task<SubscriptionEnrollmentResult> SubscribeAsync(string customerReference, string customerEmail, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the current subscriptions held by the customer identified by <paramref name="customerReference"/>.
    /// Returns an empty list when the user has no billing customer record yet.
    /// </summary>
    Task<IReadOnlyList<SubscriptionDetails>> ListSubscriptionsAsync(string customerReference, CancellationToken cancellationToken = default);
}
