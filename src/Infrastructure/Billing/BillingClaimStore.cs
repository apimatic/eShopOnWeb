using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public enum ClaimOutcome
{
    /// <summary>This caller inserted the claim; nothing was sent to the provider before.</summary>
    Acquired,

    /// <summary>This caller took over an abandoned/unknown claim; a previous attempt may have reached the provider.</summary>
    TakenOver,

    /// <summary>The claim is already settled (<see cref="BillingClaimState.Active"/>).</summary>
    Existing,

    /// <summary>Another caller holds a live claim.</summary>
    Busy
}

public sealed record ClaimResult<T>(ClaimOutcome Outcome, T Claim) where T : BillingClaim;

/// <summary>
/// Claims taken in <see cref="CatalogContext"/> before a provider write. The second caller's insert is refused by
/// the primary key; take-overs and settlements are guarded by the <see cref="BillingClaim.LeaseId"/> concurrency token.
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

    public async Task<ClaimResult<T>> AcquireAsync<T>(T claim, TimeSpan lease, CancellationToken cancellationToken)
        where T : BillingClaim
    {
        try
        {
            _db.Set<T>().Add(claim);
            await _db.SaveChangesAsync(cancellationToken);
            return new ClaimResult<T>(ClaimOutcome.Acquired, claim);
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException or ArgumentException)
        {
            // The store refused the insert. Confirm it was the key (another caller's claim) and not some other failure.
            _db.Entry(claim).State = EntityState.Detached;
            var existing = await _db.Set<T>().FindAsync(new object[] { claim.Id }, cancellationToken);
            if (existing is null)
            {
                throw;
            }

            if (existing.State == BillingClaimState.Active)
            {
                return new ClaimResult<T>(ClaimOutcome.Existing, existing);
            }

            if (!existing.IsStale(Now, lease))
            {
                return new ClaimResult<T>(ClaimOutcome.Busy, existing);
            }

            return await TryRenewAsync(existing, cancellationToken)
                ? new ClaimResult<T>(ClaimOutcome.TakenOver, existing)
                : new ClaimResult<T>(ClaimOutcome.Busy, existing);
        }
    }

    /// <summary>Takes over a claim; false when a concurrent caller renewed or settled it first.</summary>
    public async Task<bool> TryRenewAsync<T>(T claim, CancellationToken cancellationToken) where T : BillingClaim
    {
        claim.Renew(Now);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            _db.Entry(claim).State = EntityState.Detached;
            return false;
        }
    }

    /// <summary>Persists a state change made on a tracked claim (activate / mark unknown).</summary>
    public Task SaveAsync() =>
        // Never tied to the caller's cancellation: the provider has already acted and the result must be recorded.
        _db.SaveChangesAsync(CancellationToken.None);

    /// <summary>Removes a claim whose provider write is known not to have happened, so a later attempt can proceed.</summary>
    public async Task ReleaseAsync<T>(T claim) where T : BillingClaim
    {
        _db.Set<T>().Remove(claim);
        try
        {
            await _db.SaveChangesAsync(CancellationToken.None);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Already removed or taken over by someone else — nothing left to release.
            _db.Entry(claim).State = EntityState.Detached;
        }
    }

    /// <summary>Unresolved claims not touched since <paramref name="untouchedSince"/>.</summary>
    public async Task<IReadOnlyList<T>> ListUnresolvedAsync<T>(DateTimeOffset untouchedSince, int max, CancellationToken cancellationToken)
        where T : BillingClaim =>
        await _db.Set<T>()
            .Where(c => c.State != BillingClaimState.Active && c.UpdatedAt <= untouchedSince)
            .OrderBy(c => c.UpdatedAt)
            .Take(max)
            .ToListAsync(cancellationToken);
}
