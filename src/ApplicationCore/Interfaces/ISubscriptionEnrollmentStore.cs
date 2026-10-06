using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISubscriptionEnrollmentStore
{
    Task<SubscriptionEnrollment?> FindAsync(string userName, CancellationToken cancellationToken);

    /// <summary>
    /// Inserts the claim. Returns false when the store refuses it because a claim for the same user
    /// already exists (primary-key violation) — i.e. another request got there first.
    /// </summary>
    Task<bool> TryClaimAsync(SubscriptionEnrollment claim, CancellationToken cancellationToken);

    /// <summary>
    /// Persists changes made to <paramref name="enrollment"/> if nobody else changed the row since it was read.
    /// Returns false on an optimistic-concurrency conflict.
    /// </summary>
    Task<bool> TrySaveAsync(SubscriptionEnrollment enrollment, CancellationToken cancellationToken);

    /// <summary>Removes the claim so a later request can try again.</summary>
    Task ReleaseAsync(SubscriptionEnrollment enrollment, CancellationToken cancellationToken);
}
