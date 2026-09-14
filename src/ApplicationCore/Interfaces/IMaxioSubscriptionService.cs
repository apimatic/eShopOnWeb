using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Maxio;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates eShopOnWeb subscription billing on top of Maxio: ensures a Maxio customer exists
/// for a given eShopOnWeb user (idempotently) and enrolls them in a plan.
/// </summary>
public interface IMaxioSubscriptionService
{
    Task<IReadOnlyList<MaxioPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for <paramref name="userReference"/> and enrolls it in the given plan.
    /// Idempotent: if the user already has a live subscription to that plan, the existing subscription is
    /// returned rather than creating a duplicate.
    /// </summary>
    Task<MaxioSubscription> SubscribeAsync(string userReference, string email, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the subscriptions belonging to the eShopOnWeb user, or an empty list if they have no
    /// corresponding Maxio customer yet.
    /// </summary>
    Task<IReadOnlyList<MaxioSubscription>> GetSubscriptionsForUserAsync(string userReference, CancellationToken cancellationToken = default);
}
