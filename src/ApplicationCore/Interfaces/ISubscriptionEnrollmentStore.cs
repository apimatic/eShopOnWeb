using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISubscriptionEnrollmentStore
{
    /// <summary>Inserts the claim; returns false when the store already holds a claim with the same key.</summary>
    Task<bool> TryClaimAsync(SubscriptionEnrollment enrollment, CancellationToken cancellationToken);

    Task<SubscriptionEnrollment?> GetAsync(string enrollmentKey, CancellationToken cancellationToken);

    /// <summary>Saves changes to the claim; returns false when someone else changed it first.</summary>
    Task<bool> TryUpdateAsync(SubscriptionEnrollment enrollment, CancellationToken cancellationToken);

    Task ReleaseAsync(SubscriptionEnrollment enrollment, CancellationToken cancellationToken);
}
