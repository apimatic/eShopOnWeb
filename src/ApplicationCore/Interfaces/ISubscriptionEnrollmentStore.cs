using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISubscriptionEnrollmentStore
{
    /// <summary>
    /// Inserts the claim. Returns null when this caller now holds it, or the existing claim when the
    /// store refused the insert because the buyer already has one.
    /// </summary>
    Task<SubscriptionEnrollment?> TryClaimAsync(SubscriptionEnrollment candidate, CancellationToken cancellationToken);

    /// <summary>
    /// Takes over a stale claim (see <see cref="SubscriptionEnrollment.TakeOver"/>). Returns false when
    /// another request changed the claim first.
    /// </summary>
    Task<bool> TryTakeOverAsync(SubscriptionEnrollment existing, string planHandle, CancellationToken cancellationToken);

    Task<SubscriptionEnrollment?> GetAsync(string buyerId, CancellationToken cancellationToken);

    Task SaveAsync(SubscriptionEnrollment enrollment, CancellationToken cancellationToken);

    /// <summary>Removes the claim after the provider definitively refused the enrollment.</summary>
    Task ReleaseAsync(SubscriptionEnrollment enrollment, CancellationToken cancellationToken);
}
