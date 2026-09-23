using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Application port for recurring-subscription billing. Maxio Advanced Billing is the system of record;
/// implementations translate provider failures into <see cref="Exceptions.SubscriptionBillingException"/>
/// (and <see cref="Exceptions.UnknownSubscriptionPlanException"/>) at this boundary.
/// </summary>
public interface ISubscriptionBillingService
{
    /// <summary>Lists the subscribable plans in the configured product family.</summary>
    Task<SubscriptionPlansResult> GetPlansAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Ensures a Maxio customer exists for the buyer (idempotent) and enrolls them in a plan
    /// (idempotent — a double-click never creates two customers or subscriptions), returning a
    /// confirmation of the plan, price, state and next billing date.
    /// </summary>
    /// <param name="planHandle">The plan to subscribe to; when null/empty the default plan is used.</param>
    Task<SubscribeResult> SubscribeAsync(SubscriberIdentity subscriber, string? planHandle, CancellationToken cancellationToken);

    /// <summary>Lists the buyer's own subscriptions. Returns an empty list when the buyer has no Maxio customer yet.</summary>
    Task<IReadOnlyList<CustomerSubscriptionInfo>> GetMySubscriptionsAsync(SubscriberIdentity subscriber, CancellationToken cancellationToken);
}
