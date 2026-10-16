using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public class BillingClaimStore(CatalogContext db) : IBillingClaimStore
{
    public async Task<(bool Acquired, BillingClaim Claim)> AcquireAsync(BillingClaim claim, CancellationToken ct)
    {
        db.BillingClaims.Add(claim);
        try
        {
            await db.SaveChangesAsync(ct);
            db.Entry(claim).State = EntityState.Detached;
            return (true, claim);
        }
        catch (Exception ex) when (ex is ArgumentException ||
            ex is DbUpdateException { InnerException: SqlException { Number: 2601 or 2627 } })
        {
            db.Entry(claim).State = EntityState.Detached;
            var winner = await db.BillingClaims.AsNoTracking().SingleOrDefaultAsync(x => x.Id == claim.Id, ct);
            if (winner is null) throw;
            return (false, winner);
        }
    }

    public async Task CompleteAsync(string id, int providerId, CancellationToken ct)
    {
        var claim = await db.BillingClaims.SingleAsync(x => x.Id == id, ct);
        claim.State = "Completed";
        claim.ProviderId = providerId;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            db.Entry(claim).State = EntityState.Detached;
            if (!await db.BillingClaims.AnyAsync(x => x.Id == id && x.State == "Completed" && x.ProviderId == providerId, ct)) throw;
        }
        db.Entry(claim).State = EntityState.Detached;
    }

    public async Task MarkUnknownAsync(string id, CancellationToken ct)
    {
        var claim = await db.BillingClaims.SingleAsync(x => x.Id == id, ct);
        if (claim.State != "Completed") claim.State = "Unknown";
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { /* A concurrent completion wins. */ }
        db.Entry(claim).State = EntityState.Detached;
    }

    public async Task ReleaseAsync(string id, CancellationToken ct, string? expectedState = null)
    {
        var claim = await db.BillingClaims.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (claim is null) return;
        if (expectedState is not null && claim.State != expectedState)
        {
            db.Entry(claim).State = EntityState.Detached;
            return;
        }
        db.Remove(claim);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { db.Entry(claim).State = EntityState.Detached; }
    }

    public async Task<bool> TryBeginEnrollmentAsync(string id, CancellationToken ct)
    {
        var claim = await db.BillingClaims.SingleAsync(x => x.Id == id, ct);
        if (claim.State != "AwaitingCustomer")
        {
            db.Entry(claim).State = EntityState.Detached;
            return false;
        }
        claim.State = "Pending";
        try { await db.SaveChangesAsync(ct); return true; }
        catch (DbUpdateConcurrencyException) { return false; }
        finally { db.Entry(claim).State = EntityState.Detached; }
    }

    public async Task<IReadOnlyList<BillingClaim>> GetPendingAsync(string scope, string userId, CancellationToken ct) =>
        await db.BillingClaims.AsNoTracking().Where(x => x.Scope == scope && x.UserId == userId && x.State != "Completed").ToListAsync(ct);

    public async Task<IReadOnlyList<BillingClaim>> GetUnsettledAsync(string scope, CancellationToken ct) =>
        await db.BillingClaims.AsNoTracking().Where(x => x.Scope == scope && x.State != "Completed")
            .OrderBy(x => x.CreatedAt).Take(100).ToListAsync(ct);
}
