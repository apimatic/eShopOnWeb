using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Persists <see cref="SubscriptionEnrollment"/> claims. Claims are taken atomically: the store refuses a
/// second claim for the same shopper (primary key) and a takeover by a caller holding a stale token.
/// </summary>
public interface ISubscriptionEnrollmentStore
{
    Task<SubscriptionEnrollment?> GetAsync(string userReference, CancellationToken cancellationToken = default);

    /// <summary>Inserts a new claim. Returns false when another caller already holds a claim for the shopper.</summary>
    Task<bool> TryClaimAsync(SubscriptionEnrollment enrollment, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves changes to a claim, provided the stored claim token still equals <paramref name="expectedClaimToken"/>.
    /// Returns false when another caller changed (or took over) the claim first.
    /// </summary>
    Task<bool> TryUpdateAsync(SubscriptionEnrollment enrollment, Guid expectedClaimToken, CancellationToken cancellationToken = default);

    /// <summary>Removes a claim (the provider refused the write, so nothing is held at the provider).</summary>
    Task ReleaseAsync(SubscriptionEnrollment enrollment, CancellationToken cancellationToken = default);
}
