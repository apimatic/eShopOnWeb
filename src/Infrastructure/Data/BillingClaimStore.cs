using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BillingClaimAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Data;

/// <summary>
/// <see cref="IBillingClaimStore"/> over <see cref="CatalogContext"/>. The claim's primary key is
/// its deterministic reference, so both SQL Server and the EF in-memory provider refuse a second
/// insert of the same reference from a different context.
/// </summary>
public class BillingClaimStore : IBillingClaimStore
{
    // SQL Server: 2627 = PRIMARY KEY / UNIQUE constraint violation, 2601 = unique index violation.
    private const int SqlPrimaryKeyViolation = 2627;
    private const int SqlUniqueIndexViolation = 2601;

    private readonly CatalogContext _context;

    public BillingClaimStore(CatalogContext context)
    {
        _context = context;
    }

    public async Task<bool> TryInsertAsync<TClaim>(TClaim claim, CancellationToken cancellationToken = default)
        where TClaim : BillingClaim
    {
        var entry = _context.Set<TClaim>().Add(claim);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (IsDuplicateKey(ex))
        {
            // The store refused the second claim. Detach it so this scoped context can be reused.
            entry.State = EntityState.Detached;
            return false;
        }
        catch (System.ArgumentException) when (_context.Database.IsInMemory())
        {
            // The EF in-memory provider reports a duplicate key across contexts as an ArgumentException
            // ("An item with the same key has already been added"). Pinned by a unit test.
            entry.State = EntityState.Detached;
            return false;
        }
    }

    public async Task<TClaim?> FindAsync<TClaim>(string reference, CancellationToken cancellationToken = default)
        where TClaim : BillingClaim
    {
        var claim = await _context.Set<TClaim>().FirstOrDefaultAsync(c => c.Reference == reference, cancellationToken);
        if (claim == null)
        {
            return null;
        }

        // A query returns an already-tracked instance with its old values; always read the committed state.
        var entry = _context.Entry(claim);
        await entry.ReloadAsync(cancellationToken);
        return entry.State == EntityState.Detached ? null : claim;
    }

    public async Task<bool> TryUpdateAsync<TClaim>(TClaim claim, CancellationToken cancellationToken = default)
        where TClaim : BillingClaim
    {
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            var entry = _context.Entry(claim);
            entry.State = EntityState.Detached;
            return false;
        }
    }

    public async Task ReleaseAsync<TClaim>(TClaim claim, CancellationToken cancellationToken = default)
        where TClaim : BillingClaim
    {
        _context.Set<TClaim>().Remove(claim);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Already removed or changed by another caller: nothing left for this caller to release.
            _context.Entry(claim).State = EntityState.Detached;
        }
    }

    private static bool IsDuplicateKey(DbUpdateException ex)
    {
        for (var inner = ex.InnerException; inner != null; inner = inner.InnerException)
        {
            if (inner is SqlException sql && (sql.Number == SqlPrimaryKeyViolation || sql.Number == SqlUniqueIndexViolation))
            {
                return true;
            }
        }
        return false;
    }
}
