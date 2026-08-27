using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Billing;

public interface ISubscriptionEnrollmentStore
{
    Task<SubscriptionEnrollment> GetOrCreateAsync(string userId, string productHandle, string customerReference, string subscriptionReference, CancellationToken cancellationToken);
    Task<bool> TryAcquireLeaseAsync(int enrollmentId, string leaseOwner, DateTimeOffset now, CancellationToken cancellationToken);
    Task<SubscriptionEnrollment?> GetAsync(int enrollmentId, CancellationToken cancellationToken);
    Task CompleteAsync(int enrollmentId, int? customerId, int subscriptionId, CancellationToken cancellationToken);
    Task FailAsync(int enrollmentId, string code, CancellationToken cancellationToken);
}

public sealed class SubscriptionEnrollmentStore : ISubscriptionEnrollmentStore
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(30);
    private readonly AppIdentityDbContext _dbContext;

    public SubscriptionEnrollmentStore(AppIdentityDbContext dbContext) => _dbContext = dbContext;

    public async Task<SubscriptionEnrollment> GetOrCreateAsync(
        string userId,
        string productHandle,
        string customerReference,
        string subscriptionReference,
        CancellationToken cancellationToken)
    {
        var existing = await _dbContext.SubscriptionEnrollments
            .SingleOrDefaultAsync(x => x.UserId == userId && x.ProductHandle == productHandle, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var now = DateTimeOffset.UtcNow;
        var enrollment = new SubscriptionEnrollment
        {
            UserId = userId,
            ProductHandle = productHandle,
            CustomerReference = customerReference,
            SubscriptionReference = subscriptionReference,
            Status = "Pending",
            CreatedAt = now,
            UpdatedAt = now
        };
        _dbContext.SubscriptionEnrollments.Add(enrollment);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return enrollment;
        }
        catch (DbUpdateException)
        {
            _dbContext.Entry(enrollment).State = EntityState.Detached;
            return await _dbContext.SubscriptionEnrollments
                .SingleAsync(x => x.UserId == userId && x.ProductHandle == productHandle, cancellationToken);
        }
    }

    public async Task<bool> TryAcquireLeaseAsync(int enrollmentId, string leaseOwner, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var enrollment = await _dbContext.SubscriptionEnrollments.SingleAsync(x => x.Id == enrollmentId, cancellationToken);
        if (enrollment.Status == "Completed" ||
            (enrollment.LeaseExpiresAt > now && enrollment.LeaseOwner != leaseOwner))
        {
            return false;
        }

        enrollment.Status = "Pending";
        enrollment.LeaseOwner = leaseOwner;
        enrollment.LeaseExpiresAt = now.Add(LeaseDuration);
        enrollment.UpdatedAt = now;
        enrollment.Version++;
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }

    public Task<SubscriptionEnrollment?> GetAsync(int enrollmentId, CancellationToken cancellationToken) =>
        _dbContext.SubscriptionEnrollments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == enrollmentId, cancellationToken);

    public async Task CompleteAsync(int enrollmentId, int? customerId, int subscriptionId, CancellationToken cancellationToken)
    {
        var enrollment = await _dbContext.SubscriptionEnrollments.SingleAsync(x => x.Id == enrollmentId, cancellationToken);
        enrollment.MaxioCustomerId = customerId;
        enrollment.MaxioSubscriptionId = subscriptionId;
        enrollment.Status = "Completed";
        enrollment.LeaseOwner = null;
        enrollment.LeaseExpiresAt = null;
        enrollment.LastFailureCode = null;
        enrollment.UpdatedAt = DateTimeOffset.UtcNow;
        enrollment.Version++;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task FailAsync(int enrollmentId, string code, CancellationToken cancellationToken)
    {
        var enrollment = await _dbContext.SubscriptionEnrollments.SingleAsync(x => x.Id == enrollmentId, cancellationToken);
        enrollment.Status = "Failed";
        enrollment.LeaseOwner = null;
        enrollment.LeaseExpiresAt = null;
        enrollment.LastFailureCode = code;
        enrollment.UpdatedAt = DateTimeOffset.UtcNow;
        enrollment.Version++;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
