using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Maxio.Models;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>
/// Application service that drives the subscribe experience against Maxio Advanced Billing.
/// Maxio is the system of record: customers and subscriptions live in Maxio and are joined to
/// eShopOnWeb shoppers through a deterministic customer reference.
/// </summary>
public interface ISubscriptionManager
{
    /// <summary>Returns the subscription plans offered by the configured product family.</summary>
    Task<IReadOnlyList<MaxioProduct>> GetSubscriptionPlansAsync(CancellationToken ct = default);

    /// <summary>
    /// Enrolls the shopper: returns the Maxio customer for the user, creating it (idempotently)
    /// the first time it is needed.
    /// </summary>
    Task<MaxioCustomer> EnsureCustomerAsync(string userName, CancellationToken ct = default);

    /// <summary>Returns the Maxio customer for the user, or null when they have never enrolled.</summary>
    Task<MaxioCustomer?> FindCustomerAsync(string userName, CancellationToken ct = default);

    /// <summary>Lists the shopper's subscriptions from Maxio (empty when they have never enrolled).</summary>
    Task<IReadOnlyList<MaxioSubscription>> GetCustomerSubscriptionsAsync(string userName, CancellationToken ct = default);

    /// <summary>
    /// Subscribes the shopper to a plan. Idempotent: if the shopper already has a live
    /// subscription to the plan it is returned instead of creating a second one.
    /// </summary>
    Task<SubscriptionEnrollmentResult> SubscribeAsync(string userName, string planHandle, CancellationToken ct = default);
}
