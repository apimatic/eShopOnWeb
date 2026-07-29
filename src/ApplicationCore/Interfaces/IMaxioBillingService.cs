using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the Maxio Advanced Billing (Chargify) API for the
/// recurring-subscription capability. Maxio is the system of record; this app
/// never persists billing state itself.
/// </summary>
public interface IMaxioBillingService
{
    /// <summary>
    /// Lists the subscribable plans (products) in the configured product family.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for the given eShopOnWeb user, keyed on a
    /// stable <paramref name="reference"/>. Idempotent: an existing customer with
    /// the same reference is returned rather than duplicated.
    /// </summary>
    Task<MaxioCustomer> EnsureCustomerAsync(string reference, string email, string firstName, string lastName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes the customer (identified by <paramref name="customerReference"/>)
    /// to the plan identified by <paramref name="planHandle"/>. Idempotent: if the
    /// customer already has a live subscription to that plan it is returned instead
    /// of creating a second one, so a double-click never enrolls twice.
    /// </summary>
    Task<Subscription> SubscribeAsync(string customerReference, string email, string firstName, string lastName,
        string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the subscriptions belonging to the customer keyed on
    /// <paramref name="reference"/>. Returns an empty list when no customer exists yet.
    /// </summary>
    Task<IReadOnlyList<Subscription>> ListSubscriptionsAsync(string reference, CancellationToken cancellationToken = default);
}
