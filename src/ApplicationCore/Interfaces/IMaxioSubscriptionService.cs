using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Maxio;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Enrolls eShopOnWeb customers into recurring-billing subscriptions via Maxio Advanced Billing.
/// Maxio is the system of record: no local persistence of customers/subscriptions is kept,
/// this service always reflects live Maxio state.
/// </summary>
public interface IMaxioSubscriptionService
{
    /// <summary>
    /// Lists the plans available for subscription under the site's configured product family.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for the given eShopOnWeb user and enrolls them into the
    /// given plan. Idempotent: calling this repeatedly for the same user/plan will not create
    /// duplicate customers or subscriptions; the existing subscription is returned instead.
    /// </summary>
    /// <param name="customerReference">Stable identifier for the user (eShopOnWeb username).</param>
    /// <param name="email">The user's email address, used when creating a new Maxio customer.</param>
    /// <param name="planHandle">The handle of the plan (Maxio product) to subscribe to.</param>
    Task<CustomerSubscription> SubscribeAsync(string customerReference, string email, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all Maxio subscriptions belonging to the given eShopOnWeb user. Returns an empty
    /// list if the user has no corresponding Maxio customer yet.
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsAsync(string customerReference, CancellationToken cancellationToken = default);
}
