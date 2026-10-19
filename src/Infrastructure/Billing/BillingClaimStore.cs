using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Duplicate-prevention claims for billing writes, kept in <see cref="CatalogContext"/>.
/// A claim is an insert of its primary key made BEFORE the provider call: the store refuses the second insert.
/// Outcome writes use <see cref="CancellationToken.None"/> so a departed caller cannot leave a claim half-recorded.
/// </summary>
public class BillingClaimStore
{
    private readonly CatalogContext _db;
    private readonly TimeProvider _clock;

    public BillingClaimStore(CatalogContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public DateTimeOffset Now => _clock.GetUtcNow();

    /// <summary>
    /// Inserts the claim. Returns false when the store refuses it because a claim with the same key exists.
    /// </summary>
    public async Task<bool> TryClaimAsync(BillingClaim claim, CancellationToken cancellationToken)
    {
        _db.BillingClaims.Add(claim);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (Exception ex) when (IsDuplicateKey(ex))
        {
            _db.Entry(claim).State = EntityState.Detached;
            return false;
        }
        catch
        {
            _db.Entry(claim).State = EntityState.Detached;
            throw;
        }
    }

    public Task<BillingClaim?> FindAsync(string claimKey, CancellationToken cancellationToken) =>
        _db.BillingClaims.SingleOrDefaultAsync(c => c.ClaimKey == claimKey, cancellationToken);

    public async Task<IReadOnlyList<BillingClaim>> ListUnsettledSubscriptionClaimsAsync(
        string userName, TimeSpan staleAfter, CancellationToken cancellationToken)
    {
        var now = Now;
        var candidates = await _db.BillingClaims
            .Where(c => c.UserName == userName &&
                        c.Kind == BillingClaimKind.Subscription &&
                        c.Status != BillingClaimStatus.Created)
            .ToListAsync(cancellationToken);

        return candidates
            .Where(c => c.Status == BillingClaimStatus.Unknown || c.IsStalePending(now, staleAfter))
            .ToList();
    }

    public async Task MarkCreatedAsync(BillingClaim claim, int? providerCustomerId, int? providerSubscriptionId)
    {
        claim.MarkCreated(providerCustomerId, providerSubscriptionId, Now);
        await TrySaveAsync(claim);
    }

    public async Task MarkUnknownAsync(BillingClaim claim)
    {
        claim.MarkUnknown(Now);
        await TrySaveAsync(claim);
    }

    /// <summary>
    /// Re-takes an Unknown or stale claim. False when another caller changed it first (optimistic concurrency).
    /// </summary>
    public Task<bool> TryTakeOverAsync(BillingClaim claim)
    {
        claim.MarkPending(Now);
        return TrySaveAsync(claim);
    }

    /// <summary>Deletes the claim so the write can be requested again (the provider refused it or it never landed).</summary>
    public async Task ReleaseAsync(BillingClaim claim)
    {
        var entry = _db.Entry(claim);
        if (entry.State == EntityState.Detached)
        {
            return;
        }

        _db.BillingClaims.Remove(claim);
        await TrySaveAsync(claim);
    }

    private async Task<bool> TrySaveAsync(BillingClaim claim)
    {
        try
        {
            await _db.SaveChangesAsync(CancellationToken.None);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            _db.Entry(claim).State = EntityState.Detached;
            return false;
        }
    }

    /// <summary>
    /// What the store raises when a primary key is already taken: <see cref="DbUpdateException"/> on SQL Server
    /// and on the in-memory provider (pinned by a test), excluding optimistic-concurrency failures.
    /// </summary>
    internal static bool IsDuplicateKey(Exception ex) =>
        ex is DbUpdateException and not DbUpdateConcurrencyException;
}
