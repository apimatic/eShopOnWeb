using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Maxio;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Enrolls eShopOnWeb buyers into Maxio Advanced Billing subscriptions and reports on their state.
/// </summary>
public interface IMaxioSubscriptionService
{
    /// <summary>
    /// Lists the subscription plans available for signup in the configured product family.
    /// </summary>
    Task<IReadOnlyList<MaxioPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for the given buyer and enrolls them in the given plan.
    /// Safe to call more than once for the same buyer/plan: an existing, non-terminated
    /// subscription to the same plan is returned instead of creating a duplicate.
    /// </summary>
    Task<MaxioSubscription> SubscribeAsync(string buyerId, string buyerEmail, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the subscriptions belonging to the given buyer. Returns an empty list if the buyer
    /// has no corresponding Maxio customer yet.
    /// </summary>
    Task<IReadOnlyList<MaxioSubscription>> GetSubscriptionsForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);
}
