using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <param name="Claimed">true when this caller inserted the row; false when the store refused it because a row already exists.</param>
/// <param name="Enrollment">the claimed row, or the pre-existing row when the claim was refused.</param>
public record EnrollmentClaimResult(bool Claimed, SubscriptionEnrollment Enrollment);

public interface ISubscriptionEnrollmentStore
{
    /// <summary>Inserts the candidate row. The store (primary key) refuses a second row for the same user.</summary>
    Task<EnrollmentClaimResult> TryClaimAsync(SubscriptionEnrollment candidate, CancellationToken cancellationToken = default);
    Task<SubscriptionEnrollment?> GetAsync(string userId, CancellationToken cancellationToken = default);
    Task SaveAsync(SubscriptionEnrollment enrollment, CancellationToken cancellationToken = default);
    /// <summary>Deletes the claim so the user can subscribe again (provider definitively refused / nothing was created).</summary>
    Task ReleaseAsync(SubscriptionEnrollment enrollment, CancellationToken cancellationToken = default);
}
