using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.Subscriptions;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Recurring-subscription billing operations backed by Maxio Advanced Billing.
/// This is an additive capability that runs in parallel with the existing
/// one-time commerce (Catalog → Basket → Order) flow.
/// </summary>
public interface IMaxioSubscriptionService
{
    /// <summary>
    /// Lists the plans a shopper can subscribe to (the active products in the
    /// configured Maxio product family).
    /// </summary>
    Task<IReadOnlyCollection<SubscriptionPlan>> GetPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrolls <paramref name="subscriber"/> in the plan identified by
    /// <paramref name="planHandle"/>.
    ///
    /// The operation is idempotent: it ensures a single Maxio customer exists for
    /// the subscriber (keyed by <see cref="Subscriber.Reference"/>) and will return
    /// the existing subscription rather than create a duplicate when the subscriber
    /// is already enrolled in the same plan — so a double-click never creates two
    /// customers or two subscriptions.
    /// </summary>
    Task<CustomerSubscription> SubscribeAsync(Subscriber subscriber, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the subscriptions that belong to <paramref name="subscriber"/>.
    /// Returns an empty collection when the subscriber has no Maxio customer yet.
    /// </summary>
    Task<IReadOnlyCollection<CustomerSubscription>> GetSubscriptionsAsync(Subscriber subscriber, CancellationToken cancellationToken = default);
}
