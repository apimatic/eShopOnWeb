using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Persistence for <see cref="SubscriptionEnrollment"/> claims. Claiming is insert-or-fail on the primary key,
/// so a second concurrent caller is refused by the store itself, not by a read-then-write check.
/// </summary>
public interface ISubscriptionEnrollmentStore
{
    /// <summary>Inserts the claim. Returns <c>null</c> when it was stored, or the enrollment that already holds the key.</summary>
    Task<SubscriptionEnrollment?> TryClaimAsync(SubscriptionEnrollment claim, CancellationToken cancellationToken = default);

    Task<SubscriptionEnrollment?> GetAsync(string claimKey, CancellationToken cancellationToken = default);

    /// <summary>Saves changes made to an enrollment. Returns <c>false</c> when another caller changed it first.</summary>
    Task<bool> TryUpdateAsync(SubscriptionEnrollment enrollment, CancellationToken cancellationToken = default);

    /// <summary>Removes a claim whose provider write was definitively refused, so the shopper can try again.</summary>
    Task ReleaseAsync(SubscriptionEnrollment enrollment, CancellationToken cancellationToken = default);
}
