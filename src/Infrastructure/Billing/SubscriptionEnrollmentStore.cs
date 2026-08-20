using System;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class SubscriptionEnrollmentStore
{
    private static readonly TimeSpan AttemptLease = TimeSpan.FromSeconds(130);
    private readonly CatalogContext _context;

    public SubscriptionEnrollmentStore(CatalogContext context)
    {
        _context = context;
    }

    public async Task<EnrollmentLease> TryAcquireAsync(
        string userId,
        string productHandle,
        string reference,
        CancellationToken cancellationToken)
    {
        IDbContextTransaction? transaction = null;
        if (_context.Database.IsRelational())
        {
            transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        }

        try
        {
            var enrollment = await _context.SubscriptionEnrollments
                .SingleOrDefaultAsync(
                    item => item.UserId == userId && item.ProductHandle == productHandle,
                    cancellationToken);
            var now = DateTimeOffset.UtcNow;
            var token = Guid.NewGuid().ToString("N");
            var ownsLease = false;

            if (enrollment is null)
            {
                enrollment = new SubscriptionEnrollment(userId, productHandle, reference, token);
                _context.SubscriptionEnrollments.Add(enrollment);
                ownsLease = true;
            }
            else if (enrollment.Status == SubscriptionEnrollmentStatus.Failed ||
                     (enrollment.Status == SubscriptionEnrollmentStatus.Pending &&
                      enrollment.UpdatedAt <= now.Subtract(AttemptLease)))
            {
                enrollment.TakeOwnership(token);
                ownsLease = true;
            }

            await _context.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return new EnrollmentLease(enrollment, ownsLease, ownsLease ? token : null);
        }
        catch (DbUpdateException)
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            _context.ChangeTracker.Clear();
            var enrollment = await GetAsync(userId, productHandle, cancellationToken)
                ?? throw new InvalidOperationException("The subscription enrollment could not be loaded after a concurrency conflict.");
            return new EnrollmentLease(enrollment, false, null);
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }

    public Task<SubscriptionEnrollment?> GetAsync(
        string userId,
        string productHandle,
        CancellationToken cancellationToken) =>
        _context.SubscriptionEnrollments
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.UserId == userId && item.ProductHandle == productHandle,
                cancellationToken);

    public async Task CompleteAsync(
        int enrollmentId,
        string attemptToken,
        long customerId,
        long subscriptionId,
        CancellationToken cancellationToken)
    {
        var enrollment = await _context.SubscriptionEnrollments
            .SingleAsync(item => item.Id == enrollmentId, cancellationToken);
        if (!string.Equals(enrollment.AttemptToken, attemptToken, StringComparison.Ordinal))
        {
            return;
        }

        enrollment.Complete(customerId, subscriptionId);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task FailAsync(int enrollmentId, string attemptToken, CancellationToken cancellationToken)
    {
        var enrollment = await _context.SubscriptionEnrollments
            .SingleAsync(item => item.Id == enrollmentId, cancellationToken);
        if (!string.Equals(enrollment.AttemptToken, attemptToken, StringComparison.Ordinal))
        {
            return;
        }

        enrollment.Fail();
        await _context.SaveChangesAsync(cancellationToken);
    }
}

public sealed record EnrollmentLease(
    SubscriptionEnrollment Enrollment,
    bool IsOwner,
    string? AttemptToken);
