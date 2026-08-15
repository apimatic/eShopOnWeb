using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Application-facing seam over the Maxio Advanced Billing system. Implementations live in
/// Infrastructure and own the Maxio SDK; callers (e.g. PublicApi endpoints) work only with
/// the plain read models in <see cref="Subscriptions"/>.
/// </summary>
public interface IMaxioBillingService
{
    /// <summary>
    /// Lists the subscription plans available to shoppers (the products in the configured
    /// default product family).
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetSubscriptionPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for the shopper (idempotent by user reference) and
    /// enrolls them in the requested plan. If an active subscription to that plan already
    /// exists, it is returned instead of creating a duplicate.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(SubscribeRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the subscriptions belonging to the shopper identified by <paramref name="userReference"/>.
    /// Returns an empty list when the shopper has no Maxio customer yet.
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsForUserAsync(
        string userReference, CancellationToken cancellationToken = default);
}
