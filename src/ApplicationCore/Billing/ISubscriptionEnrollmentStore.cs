using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public enum EnrollmentLeaseStatus
{
    Acquired,
    Confirmed,
    ReconcileOnly,
    Rejected,
    InProgress
}

public sealed record EnrollmentLease(
    Guid EnrollmentId,
    string Owner,
    EnrollmentLeaseStatus Status,
    ReconciliationTarget ReconciliationTarget,
    int? MaxioCustomerId,
    string? LastSafeError);

public interface ISubscriptionEnrollmentStore
{
    Task<EnrollmentLease> AcquireAsync(
        string userId,
        string productHandle,
        string customerReference,
        string subscriptionReference,
        CancellationToken cancellationToken);
    Task RecordCustomerAsync(Guid enrollmentId, string owner, int customerId, CancellationToken cancellationToken);
    Task ConfirmAsync(
        Guid enrollmentId,
        string owner,
        int customerId,
        int subscriptionId,
        string? providerState,
        CancellationToken cancellationToken);
    Task MarkNeedsReconciliationAsync(
        Guid enrollmentId,
        string owner,
        ReconciliationTarget target,
        string safeError,
        CancellationToken cancellationToken);
    Task MarkRejectedAsync(Guid enrollmentId, string owner, string safeError, CancellationToken cancellationToken);
    Task ReleaseAsync(Guid enrollmentId, string owner, CancellationToken cancellationToken);
}
