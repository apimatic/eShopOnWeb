using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Data;

/// <summary>
/// Claim store over <see cref="CatalogContext"/> (works with the in-memory provider too). A claim is an INSERT
/// of its own; the primary key refuses the second one.
/// </summary>
public class BillingClaimStore : IBillingClaimStore
{
    private readonly CatalogContext _db;
    private readonly TimeProvider _time;

    public BillingClaimStore(CatalogContext db, TimeProvider? timeProvider = null)
    {
        _db = db;
        _time = timeProvider ?? TimeProvider.System;
    }

    public async Task<ClaimResult<SubscriptionClaim>> TryClaimSubscriptionAsync(string userId, string planHandle, CancellationToken ct)
    {
        var claim = new SubscriptionClaim(userId, planHandle, _time.GetUtcNow().UtcDateTime);
        try
        {
            _db.SubscriptionClaims.Add(claim);
            await _db.SaveChangesAsync(ct);
            return new ClaimResult<SubscriptionClaim>(true, claim);
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException or ArgumentException)
        {
            // The key is already taken: the store refused the second claim (SQL Server: DbUpdateException; the in-memory
            // provider raises ArgumentException at save, or InvalidOperationException when the same context tracks both).
            _db.Entry(claim).State = EntityState.Detached;
            var existing = await _db.SubscriptionClaims.AsNoTracking()
                .FirstOrDefaultAsync(c => c.UserId == userId && c.PlanHandle == planHandle, ct);
            if (existing is null)
            {
                throw; // not a key conflict (or the holder released it meanwhile): do not guess
            }
            return new ClaimResult<SubscriptionClaim>(false, existing);
        }
    }

    public async Task CompleteSubscriptionAsync(string userId, string planHandle, int subscriptionId, CancellationToken ct)
    {
        var claim = await _db.SubscriptionClaims.FindAsync(new object[] { userId, planHandle }, ct);
        if (claim is null) return;
        claim.Complete(subscriptionId);
        await _db.SaveChangesAsync(ct);
    }

    public async Task MarkSubscriptionUnknownAsync(string userId, string planHandle, CancellationToken ct)
    {
        var claim = await _db.SubscriptionClaims.FindAsync(new object[] { userId, planHandle }, ct);
        if (claim is null) return;
        claim.MarkUnknown();
        await _db.SaveChangesAsync(ct);
    }

    public async Task ReleaseSubscriptionAsync(string userId, string planHandle, DateTime claimedAtUtc, CancellationToken ct)
    {
        var claim = await _db.SubscriptionClaims.FindAsync(new object[] { userId, planHandle }, ct);
        if (claim is null || claim.ClaimedAtUtc != claimedAtUtc) return;
        _db.SubscriptionClaims.Remove(claim);
        await SaveIgnoringLostRaceAsync(ct);
    }

    public async Task<ClaimResult<MaxioCustomerClaim>> TryClaimCustomerAsync(string userId, CancellationToken ct)
    {
        var claim = new MaxioCustomerClaim(userId, _time.GetUtcNow().UtcDateTime);
        try
        {
            _db.MaxioCustomerClaims.Add(claim);
            await _db.SaveChangesAsync(ct);
            return new ClaimResult<MaxioCustomerClaim>(true, claim);
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException or ArgumentException)
        {
            _db.Entry(claim).State = EntityState.Detached;
            var existing = await _db.MaxioCustomerClaims.AsNoTracking().FirstOrDefaultAsync(c => c.UserId == userId, ct);
            if (existing is null)
            {
                throw;
            }
            return new ClaimResult<MaxioCustomerClaim>(false, existing);
        }
    }

    public async Task CompleteCustomerAsync(string userId, int customerId, CancellationToken ct)
    {
        var claim = await _db.MaxioCustomerClaims.FindAsync(new object[] { userId }, ct);
        if (claim is null) return;
        claim.Complete(customerId);
        await _db.SaveChangesAsync(ct);
    }

    public async Task ReleaseCustomerAsync(string userId, DateTime claimedAtUtc, CancellationToken ct)
    {
        var claim = await _db.MaxioCustomerClaims.FindAsync(new object[] { userId }, ct);
        if (claim is null || claim.ClaimedAtUtc != claimedAtUtc) return;
        _db.MaxioCustomerClaims.Remove(claim);
        await SaveIgnoringLostRaceAsync(ct);
    }

    private async Task SaveIgnoringLostRaceAsync(CancellationToken ct)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // someone else already deleted it; the goal (claim gone) is met
            _db.ChangeTracker.Clear();
        }
    }
}
