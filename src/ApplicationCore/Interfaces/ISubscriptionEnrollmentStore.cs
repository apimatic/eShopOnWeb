using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Persists <see cref="SubscriptionEnrollment"/> rows with the atomic guarantees the subscribe flow needs.
/// </summary>
public interface ISubscriptionEnrollmentStore
{
    Task<SubscriptionEnrollment?> GetAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts the enrollment. Returns <c>false</c> when the store refuses it because the buyer already
    /// has one — the duplicate-subscribe guard.
    /// </summary>
    Task<bool> TryClaimAsync(SubscriptionEnrollment enrollment, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves changes made to a loaded enrollment. Returns <c>false</c> when another request changed or
    /// removed it first (optimistic concurrency).
    /// </summary>
    Task<bool> TryUpdateAsync(SubscriptionEnrollment enrollment, CancellationToken cancellationToken = default);

    /// <summary>Releases the claim. Returns <c>false</c> when another request changed or removed it first.</summary>
    Task<bool> TryReleaseAsync(SubscriptionEnrollment enrollment, CancellationToken cancellationToken = default);
}
