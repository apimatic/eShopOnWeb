using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// EF-backed idempotency claim: an atomic insert whose primary-key collision at save time rejects a
/// concurrent duplicate. Backs onto a unique key in a relational deployment; on the in-memory provider
/// the shared store enforces the same primary-key uniqueness within the process.
/// </summary>
public class IdempotencyService : IIdempotencyService
{
    private readonly CatalogContext _context;

    public IdempotencyService(CatalogContext context)
    {
        _context = context;
    }

    public async Task<bool> TryClaimAsync(string key, CancellationToken ct = default)
    {
        var claim = new IdempotencyClaim(key);
        _context.Set<IdempotencyClaim>().Add(claim);
        try
        {
            await _context.SaveChangesAsync(ct);
            return true;
        }
        catch (Exception ex) when (IsDuplicateKey(ex))
        {
            // Detach the failed insert so it does not poison later SaveChanges on the shared context.
            _context.Entry(claim).State = EntityState.Detached;
            return false;
        }
    }

    public async Task ReleaseAsync(string key, CancellationToken ct = default)
    {
        var existing = await _context.Set<IdempotencyClaim>().FindAsync(new object?[] { key }, ct);
        if (existing is not null)
        {
            _context.Set<IdempotencyClaim>().Remove(existing);
            await _context.SaveChangesAsync(ct);
        }
    }

    private static bool IsDuplicateKey(Exception ex)
    {
        // The in-memory provider surfaces a duplicate key as ArgumentException from its backing store;
        // relational providers surface it as DbUpdateException on the unique key. Cover both.
        return ex is DbUpdateException
            || ex is ArgumentException
            || (ex.InnerException is not null && IsDuplicateKey(ex.InnerException));
    }
}
