using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Application-facing abstraction over the recurring-subscription billing system
/// (implemented against Maxio Advanced Billing). This is intentionally free of any
/// Maxio-specific types so the rest of the application never depends on the vendor.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>
    /// Lists the subscription plans a shopper may subscribe to. Plans are the products
    /// belonging to the configured billing product family.
    /// </summary>
    Task<IReadOnlyCollection<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls the given shopper in the plan identified by <paramref name="planHandle"/>.
    /// The operation is idempotent: it ensures a single billing customer exists for the
    /// shopper (keyed on <see cref="SubscriberIdentity.Reference"/>) and will not create a
    /// second active subscription to the same plan if one already exists.
    /// </summary>
    Task<CustomerSubscription> SubscribeAsync(
        SubscriberIdentity subscriber,
        string planHandle,
        string? pricePointHandle = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all subscriptions currently on record for the shopper. Returns an empty
    /// collection when no billing customer exists for the shopper yet.
    /// </summary>
    Task<IReadOnlyCollection<CustomerSubscription>> GetSubscriptionsAsync(
        SubscriberIdentity subscriber,
        CancellationToken cancellationToken = default);
}
