using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Claims on billing-provider writes, kept in <see cref="CatalogContext"/>. A claim is an insert keyed by the
/// write's deterministic reference, so the store itself refuses a second claim for the same write.
/// </summary>
public class BillingClaimStore
{
    private readonly CatalogContext _db;
    private readonly TimeProvider _clock;
    private readonly ILogger<BillingClaimStore> _logger;

    public BillingClaimStore(CatalogContext db, TimeProvider clock, ILogger<BillingClaimStore> logger)
    {
        _db = db;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>
    /// Inserts a new claim. Returns <c>Acquired = true</c> with the new row when this caller owns the write, or
    /// <c>Acquired = false</c> with the existing row when an earlier request already claimed it.
    /// </summary>
    public async Task<(bool Acquired, BillingClaim Claim)> TryAcquireAsync(string reference, BillingClaimKind kind,
        string buyerId, string? planHandle, CancellationToken cancellationToken)
    {
        var tracked = _db.BillingClaims.Local.FirstOrDefault(c => c.Reference == reference);
        if (tracked is not null)
        {
            return (false, tracked);
        }

        var claim = new BillingClaim(reference, kind, buyerId, planHandle, _clock.GetUtcNow());
        _db.BillingClaims.Add(claim);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return (true, claim);
        }
        catch (Exception ex) when (ex is DbUpdateException or ArgumentException)
        {
            // Relational providers raise DbUpdateException for a key violation; the in-memory provider raises
            // ArgumentException. Either way, the insert was refused: if the row now exists, it was refused because
            // another request holds the claim. Anything else is a genuine failure.
            _db.Entry(claim).State = EntityState.Detached;
            var existing = await _db.BillingClaims.SingleOrDefaultAsync(c => c.Reference == reference, cancellationToken);
            if (existing is null)
            {
                throw;
            }
            _logger.LogInformation("Billing claim {Reference} is already held; using the existing claim.", reference);
            return (false, existing);
        }
        catch
        {
            _db.Entry(claim).State = EntityState.Detached;
            throw;
        }
    }

    /// <summary>
    /// Transfers an existing claim to this caller. Fails (returns false) when another request changed the claim
    /// since it was read — the concurrency token guarantees only one taker wins.
    /// </summary>
    public async Task<bool> TryTakeOverAsync(BillingClaim claim, CancellationToken cancellationToken)
    {
        claim.TakeOver(_clock.GetUtcNow());
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            _db.Entry(claim).State = EntityState.Detached;
            _logger.LogInformation("Billing claim {Reference} was taken over by another request.", claim.Reference);
            return false;
        }
    }

    public Task<bool> CompleteCustomerAsync(BillingClaim claim, int providerCustomerId) =>
        SaveTransitionAsync(claim, c => c.CompleteCustomer(providerCustomerId, _clock.GetUtcNow()));

    public Task<bool> CompleteSubscriptionAsync(BillingClaim claim, int providerCustomerId, int providerSubscriptionId) =>
        SaveTransitionAsync(claim, c => c.CompleteSubscription(providerCustomerId, providerSubscriptionId, _clock.GetUtcNow()));

    public Task<bool> MarkUnknownAsync(BillingClaim claim) =>
        SaveTransitionAsync(claim, c => c.MarkUnknown(_clock.GetUtcNow()));

    public Task<bool> ReleaseAsync(BillingClaim claim) =>
        SaveTransitionAsync(claim, c => c.Release(_clock.GetUtcNow()));

    public async Task<int?> FindCustomerIdAsync(string reference, CancellationToken cancellationToken)
    {
        var claim = await _db.BillingClaims.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Reference == reference && c.State == BillingClaimState.Completed, cancellationToken);
        return claim?.ProviderCustomerId;
    }

    /// <summary>Provider subscription id → plan handle, for the buyer's completed subscription claims.</summary>
    public async Task<IReadOnlyDictionary<int, string>> GetSubscriptionPlansAsync(string buyerId, CancellationToken cancellationToken)
    {
        var claims = await _db.BillingClaims.AsNoTracking()
            .Where(c => c.BuyerId == buyerId && c.Kind == BillingClaimKind.Subscription &&
                        c.State == BillingClaimState.Completed && c.ProviderSubscriptionId != null && c.PlanHandle != null)
            .ToListAsync(cancellationToken);
        return claims.ToDictionary(c => c.ProviderSubscriptionId!.Value, c => c.PlanHandle!);
    }

    // Transitions are recorded even when the caller's request was cancelled: the record is what settles the write.
    private async Task<bool> SaveTransitionAsync(BillingClaim claim, Action<BillingClaim> transition)
    {
        transition(claim);
        try
        {
            await _db.SaveChangesAsync(CancellationToken.None);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another request took the claim over after our lease ran out; it re-reads the provider before writing,
            // so it will find what we did. Our transition is dropped rather than overwriting theirs.
            _db.Entry(claim).State = EntityState.Detached;
            _logger.LogWarning("Billing claim {Reference} changed hands before its state could be recorded.", claim.Reference);
            return false;
        }
    }
}
