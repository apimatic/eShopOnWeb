using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Maxio;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Subscription-billing capability backed by Maxio Advanced Billing.</summary>
public interface IMaxioBillingService
{
    /// <summary>Lists the plans available for subscription in the configured product family.</summary>
    Task<IReadOnlyList<MaxioPlan>> ListPlansAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a Maxio customer exists for the given eShopOnWeb user and enrolls them in the
    /// given plan. Idempotent: if the customer already has a live subscription to this plan,
    /// that subscription is returned instead of creating a duplicate.
    /// </summary>
    Task<MaxioSubscription> SubscribeAsync(string customerReference, string customerEmail, string planHandle, CancellationToken cancellationToken = default);

    /// <summary>Lists the given eShopOnWeb user's subscriptions. Returns an empty list if they have no Maxio customer yet.</summary>
    Task<IReadOnlyList<MaxioSubscription>> ListSubscriptionsForCustomerAsync(string customerReference, CancellationToken cancellationToken = default);
}
