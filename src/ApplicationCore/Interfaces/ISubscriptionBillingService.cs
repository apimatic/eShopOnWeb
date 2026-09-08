using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Application-level service for recurring subscription billing.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>
    /// Lists the plans available for subscription in the configured billing catalog.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListAvailablePlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Idempotently enrolls a shopper on a plan: ensures a billing customer exists for the
    /// given customer reference and creates the subscription unless a live subscription to
    /// the same plan already exists, in which case the existing subscription is returned.
    /// </summary>
    Task<SubscriptionEnrollment> SubscribeAsync(SubscriptionSignup signup, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the subscriptions belonging to the billing customer identified by the given
    /// customer reference. Returns an empty list when no customer exists yet.
    /// </summary>
    Task<IReadOnlyList<SubscriptionDetails>> ListSubscriptionsAsync(string customerReference, CancellationToken cancellationToken = default);
}
