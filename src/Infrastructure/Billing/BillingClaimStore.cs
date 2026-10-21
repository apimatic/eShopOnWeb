using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Durable claims that guard billing-provider writes against double submission. Every claim is an
/// insert keyed by the user id, made before the provider is called, so the store itself refuses the
/// second caller. Updates are guarded by a concurrency stamp so two requests cannot both settle or take
/// over the same claim. Rows are never left tracked between operations.
/// </summary>
public class BillingClaimStore
{
    private readonly CatalogContext _db;

    public BillingClaimStore(CatalogContext db)
    {
        _db = db;
    }

    public Task<BillingCustomerLink?> FindCustomerLinkAsync(string userId, CancellationToken ct) =>
        _db.BillingCustomerLinks.AsNoTracking().FirstOrDefaultAsync(l => l.UserId == userId, ct);

    public Task<SubscriptionEnrollment?> FindEnrollmentAsync(string userId, CancellationToken ct) =>
        _db.SubscriptionEnrollments.AsNoTracking().FirstOrDefaultAsync(e => e.UserId == userId, ct);

    /// <summary>Inserts the claim. Returns false when another request already holds a claim for the same user.</summary>
    public Task<bool> TryClaimAsync<T>(T claim, CancellationToken ct) where T : class
    {
        var entry = _db.Entry(claim);
        entry.State = EntityState.Added;
        return SaveAsync(entry, ct);
    }

    /// <summary>Applies <paramref name="change"/> to a claim previously read from this store. Returns false when
    /// another request changed or removed the row since it was read (the concurrency stamp no longer matches).</summary>
    public Task<bool> TryUpdateAsync<T>(T claim, Action<T> change, CancellationToken ct) where T : class
    {
        // Attaching records the values as read (including the stamp) as the originals the update is checked against.
        var entry = _db.Attach(claim);
        change(claim);
        entry.DetectChanges();
        return SaveAsync(entry, ct);
    }

    /// <summary>Releases a claim. Returns false when another request changed or removed the row since it was read.</summary>
    public Task<bool> TryReleaseAsync<T>(T claim, CancellationToken ct) where T : class
    {
        var entry = _db.Entry(claim);
        entry.State = EntityState.Deleted;
        return SaveAsync(entry, ct);
    }

    private async Task<bool> SaveAsync(EntityEntry entry, CancellationToken ct)
    {
        var isInsert = entry.State == EntityState.Added;
        try
        {
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
        catch (Exception ex) when (isInsert && IsDuplicateKey(ex))
        {
            return false;
        }
        finally
        {
            entry.State = EntityState.Detached;
        }
    }

    private bool IsDuplicateKey(Exception ex) =>
        // Relational providers (SQL Server) report a primary-key violation as DbUpdateException;
        // the EF in-memory provider throws ArgumentException ("An item with the same key has already been added").
        ex is DbUpdateException || (ex is ArgumentException && _db.Database.IsInMemory());
}
