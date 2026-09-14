using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The billing capability, expressed in eShopOnWeb's own terms. Backed by Maxio Advanced
/// Billing (the system of record) but deliberately free of any SDK type so the API surface,
/// domain and tests never depend on the transport.
/// </summary>
public interface IMaxioBillingService
{
    /// <summary>
    /// Lists the plans a shopper can subscribe to — the products in the configured product family.
    /// </summary>
    Task<IReadOnlyCollection<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls <paramref name="user"/> in the plan identified by <paramref name="planHandle"/>.
    /// Idempotent: ensures a single Maxio customer exists for the shopper (matched by reference)
    /// and, if they already hold an active subscription to that plan, returns it instead of
    /// creating a duplicate.
    /// </summary>
    Task<SubscribeResult> SubscribeAsync(BillingUser user, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the subscriptions currently held by <paramref name="user"/>. Returns an empty set
    /// (never creates anything) when no Maxio customer exists for the shopper yet.
    /// </summary>
    Task<IReadOnlyCollection<CustomerSubscription>> ListSubscriptionsAsync(BillingUser user, CancellationToken cancellationToken = default);
}
