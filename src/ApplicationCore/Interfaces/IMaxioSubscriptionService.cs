using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Maxio;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Application-facing port onto Maxio Advanced Billing. eShopOnWeb's own identity/user store
/// remains the source of truth for "who is this shopper" - Maxio is the system of record for
/// billing state only.
/// </summary>
public interface IMaxioSubscriptionService
{
    /// <summary>
    /// Lists the subscribable plans in the configured product family, straight from Maxio.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPlan>> GetAvailablePlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for the given eShopOnWeb user and enrolls them in the
    /// given plan. Idempotent: repeating the call for the same user/plan returns the existing
    /// customer/subscription instead of creating duplicates.
    /// </summary>
    Task<CustomerSubscription> SubscribeAsync(string userReference, string userEmail, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the subscriptions Maxio has on file for the given eShopOnWeb user. Returns an
    /// empty list if the user has never subscribed (i.e. no Maxio customer exists yet).
    /// </summary>
    Task<IReadOnlyList<CustomerSubscription>> GetSubscriptionsForUserAsync(string userReference, CancellationToken cancellationToken = default);
}
