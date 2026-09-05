using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Maxio;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Subscription billing operations backed by Maxio Advanced Billing (the system of record
/// for plans, customers and subscriptions).
/// </summary>
public interface IMaxioSubscriptionService
{
    /// <summary>
    /// Lists the active (non-archived) plans in the configured product family.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for <paramref name="customer"/> and enrolls them in
    /// <paramref name="planHandle"/>. Idempotent: reuses the existing Maxio customer (matched
    /// by <see cref="MaxioCustomerProfile.Reference"/>) and, if a live subscription to the
    /// same plan already exists, returns it instead of creating a duplicate.
    /// </summary>
    Task<CustomerSubscription> SubscribeAsync(MaxioCustomerProfile customer, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the subscriptions belonging to the Maxio customer matched by
    /// <paramref name="customerReference"/>. Returns an empty list if no matching customer exists yet.
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(string customerReference, CancellationToken cancellationToken = default);
}
