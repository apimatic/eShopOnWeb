using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class EfSubscriptionEnrollmentStore : ISubscriptionEnrollmentStore
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(30);
    private readonly CatalogContext _context;
    private readonly TimeProvider _timeProvider;
    private readonly string _owner = $"{Environment.MachineName}-{Environment.ProcessId}-{Guid.NewGuid():N}";

    public EfSubscriptionEnrollmentStore(CatalogContext context, TimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    public async Task<EnrollmentLease> AcquireAsync(
        string userId,
        string productHandle,
        string customerReference,
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var enrollment = await _context.SubscriptionEnrollments.SingleOrDefaultAsync(
            x => x.UserId == userId && x.ProductHandle == productHandle,
            cancellationToken);

        if (enrollment is null)
        {
            enrollment = new SubscriptionEnrollment(
                userId,
                productHandle,
                customerReference,
                subscriptionReference,
                _owner,
                now,
                now.Add(LeaseDuration));
            _context.SubscriptionEnrollments.Add(enrollment);

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                _context.Entry(enrollment).State = EntityState.Detached;
                enrollment = await _context.SubscriptionEnrollments.SingleAsync(
                    x => x.UserId == userId && x.ProductHandle == productHandle,
                    cancellationToken);
            }
        }

        if (enrollment.State == SubscriptionEnrollmentState.Rejected)
        {
            return Lease(enrollment, EnrollmentLeaseStatus.Rejected);
        }

        if (enrollment.HasLiveLease(now) && enrollment.LeaseOwner != _owner)
        {
            return Lease(enrollment, EnrollmentLeaseStatus.InProgress);
        }

        var status = enrollment.State switch
        {
            SubscriptionEnrollmentState.Active => EnrollmentLeaseStatus.Confirmed,
            SubscriptionEnrollmentState.NeedsReconciliation => EnrollmentLeaseStatus.ReconcileOnly,
            _ => EnrollmentLeaseStatus.Acquired
        };

        enrollment.AcquireLease(_owner, now, now.Add(LeaseDuration));
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Lease(enrollment, EnrollmentLeaseStatus.InProgress);
        }

        return Lease(enrollment, status);
    }

    public Task RecordCustomerAsync(Guid enrollmentId, string owner, int customerId, CancellationToken cancellationToken) =>
        MutateAsync(enrollmentId, owner, x => x.RecordCustomer(customerId, _timeProvider.GetUtcNow()), cancellationToken);

    public Task ConfirmAsync(
        Guid enrollmentId,
        string owner,
        int customerId,
        int subscriptionId,
        string? providerState,
        CancellationToken cancellationToken) =>
        MutateAsync(
            enrollmentId,
            owner,
            x => x.Confirm(customerId, subscriptionId, providerState, _timeProvider.GetUtcNow()),
            cancellationToken);

    public Task MarkNeedsReconciliationAsync(
        Guid enrollmentId,
        string owner,
        ReconciliationTarget target,
        string safeError,
        CancellationToken cancellationToken) =>
        MutateAsync(
            enrollmentId,
            owner,
            x => x.MarkNeedsReconciliation(target, safeError, _timeProvider.GetUtcNow()),
            cancellationToken);

    public Task MarkRejectedAsync(Guid enrollmentId, string owner, string safeError, CancellationToken cancellationToken) =>
        MutateAsync(enrollmentId, owner, x => x.Reject(safeError, _timeProvider.GetUtcNow()), cancellationToken);

    public Task ReleaseAsync(Guid enrollmentId, string owner, CancellationToken cancellationToken) =>
        MutateAsync(enrollmentId, owner, x => x.ReleaseLease(_timeProvider.GetUtcNow()), cancellationToken);

    private async Task MutateAsync(
        Guid enrollmentId,
        string owner,
        Action<SubscriptionEnrollment> mutation,
        CancellationToken cancellationToken)
    {
        var enrollment = await _context.SubscriptionEnrollments.SingleAsync(x => x.Id == enrollmentId, cancellationToken);
        if (!string.Equals(enrollment.LeaseOwner, owner, StringComparison.Ordinal))
        {
            throw new DbUpdateConcurrencyException("The subscription enrollment lease is no longer owned by this request.");
        }

        mutation(enrollment);
        await _context.SaveChangesAsync(cancellationToken);
    }

    private static EnrollmentLease Lease(SubscriptionEnrollment enrollment, EnrollmentLeaseStatus status) =>
        new(
            enrollment.Id,
            enrollment.LeaseOwner ?? string.Empty,
            status,
            enrollment.ReconciliationTarget,
            enrollment.MaxioCustomerId,
            enrollment.LastSafeError);
}
