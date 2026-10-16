using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Microsoft.eShopWeb.Infrastructure.Data;

public sealed class BillingClaimStore
{
    private readonly CatalogContext _db;

    public BillingClaimStore(CatalogContext db)
    {
        _db = db;
    }

    public Task<bool> TryInsertCustomerAsync(string userId, CancellationToken cancellationToken) =>
        TryInsertAsync(new MaxioCustomerClaim
        {
            UserId = userId,
            State = BillingClaimStates.Pending,
            ClaimedAt = DateTimeOffset.UtcNow
        }, cancellationToken);

    public Task<bool> TryInsertSubscriptionAsync(string reference, string userId, string productHandle, CancellationToken cancellationToken) =>
        TryInsertAsync(new SubscriptionEnrollmentClaim
        {
            Reference = reference,
            UserId = userId,
            ProductHandle = productHandle,
            State = BillingClaimStates.Pending,
            ClaimedAt = DateTimeOffset.UtcNow
        }, cancellationToken);

    public Task<MaxioCustomerClaim?> GetCustomerAsync(string userId, CancellationToken cancellationToken) =>
        _db.MaxioCustomerClaims.AsNoTracking().FirstOrDefaultAsync(claim => claim.UserId == userId, cancellationToken);

    public Task<SubscriptionEnrollmentClaim?> GetSubscriptionAsync(string reference, CancellationToken cancellationToken) =>
        _db.SubscriptionEnrollmentClaims.AsNoTracking().FirstOrDefaultAsync(claim => claim.Reference == reference, cancellationToken);

    public Task<List<SubscriptionEnrollmentClaim>> ListSubscriptionsAsync(string userId, CancellationToken cancellationToken) =>
        _db.SubscriptionEnrollmentClaims.AsNoTracking().Where(claim => claim.UserId == userId).ToListAsync(cancellationToken);

    public async Task CompleteCustomerAsync(string userId, int maxioCustomerId, CancellationToken cancellationToken)
    {
        var claim = await _db.MaxioCustomerClaims.FirstAsync(row => row.UserId == userId, cancellationToken);
        claim.State = BillingClaimStates.Completed;
        claim.MaxioCustomerId = maxioCustomerId;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkCustomerUnknownAsync(string userId, CancellationToken cancellationToken)
    {
        var claim = await _db.MaxioCustomerClaims.FirstOrDefaultAsync(row => row.UserId == userId, cancellationToken);
        if (claim is null)
            return;
        claim.State = BillingClaimStates.Unknown;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteCustomerAsync(string userId, CancellationToken cancellationToken)
    {
        var claim = await _db.MaxioCustomerClaims.FirstOrDefaultAsync(row => row.UserId == userId, cancellationToken);
        if (claim is null)
            return;
        _db.MaxioCustomerClaims.Remove(claim);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task CompleteSubscriptionAsync(string reference, int maxioSubscriptionId, CancellationToken cancellationToken)
    {
        var claim = await _db.SubscriptionEnrollmentClaims.FirstAsync(row => row.Reference == reference, cancellationToken);
        claim.State = BillingClaimStates.Completed;
        claim.MaxioSubscriptionId = maxioSubscriptionId;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkSubscriptionUnknownAsync(string reference, CancellationToken cancellationToken)
    {
        var claim = await _db.SubscriptionEnrollmentClaims.FirstOrDefaultAsync(row => row.Reference == reference, cancellationToken);
        if (claim is null)
            return;
        claim.State = BillingClaimStates.Unknown;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteSubscriptionAsync(string reference, CancellationToken cancellationToken)
    {
        var claim = await _db.SubscriptionEnrollmentClaims.FirstOrDefaultAsync(row => row.Reference == reference, cancellationToken);
        if (claim is null)
            return;
        _db.SubscriptionEnrollmentClaims.Remove(claim);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<bool> TryInsertAsync<TClaim>(TClaim claim, CancellationToken cancellationToken)
        where TClaim : class
    {
        try
        {
            _db.Set<TClaim>().Add(claim);
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (IsDuplicateKey(ex))
        {
            Detach(claim);
            return false;
        }
        catch (InvalidOperationException ex) when (IsDuplicateKey(ex))
        {
            Detach(claim);
            return false;
        }
        catch (ArgumentException ex) when (IsDuplicateKey(ex))
        {
            Detach(claim);
            return false;
        }
    }

    private void Detach<TClaim>(TClaim claim)
        where TClaim : class
    {
        var entry = _db.Entry(claim);
        if (entry.State != EntityState.Detached)
            entry.State = EntityState.Detached;
    }

    private static bool IsDuplicateKey(Exception exception)
    {
        var text = exception.InnerException?.Message ?? exception.Message;
        return text.Contains("duplicate", StringComparison.OrdinalIgnoreCase)
            || text.Contains("PRIMARY KEY", StringComparison.OrdinalIgnoreCase)
            || text.Contains("same key", StringComparison.OrdinalIgnoreCase)
            || text.Contains("already being tracked", StringComparison.OrdinalIgnoreCase)
            || exception.InnerException is ArgumentException;
    }
}
