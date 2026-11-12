using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Claims an operation in the catalog database before billing is called. The claim key is the primary key,
/// so a second concurrent claim is refused by the store itself.
/// </summary>
public class OperationClaimStore
{
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(5);
    private readonly CatalogContext _db;

    public OperationClaimStore(CatalogContext db)
    {
        _db = db;
    }

    public async Task<bool> TryClaimAsync(string key, CancellationToken ct)
    {
        // A claim left behind by a crashed request expires.
        var staleBefore = DateTimeOffset.UtcNow - StaleAfter;
        var existing = await _db.OperationClaims.Where(c => c.Key == key).ToListAsync(ct);
        var stale = existing.Where(c => c.ClaimedAt < staleBefore).ToList();
        if (stale.Count > 0)
        {
            _db.OperationClaims.RemoveRange(stale);
            try { await _db.SaveChangesAsync(ct); } catch (DbUpdateException) { /* already cleaned */ }
            _db.ChangeTracker.Clear();
        }

        _db.OperationClaims.Add(new OperationClaim { Key = key, ClaimedAt = DateTimeOffset.UtcNow });
        try
        {
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            return false;
        }
    }

    public async Task ReleaseAsync(string key)
    {
        try
        {
            _db.ChangeTracker.Clear();
            var claim = await _db.OperationClaims.FirstOrDefaultAsync(c => c.Key == key);
            if (claim is not null)
            {
                _db.OperationClaims.Remove(claim);
                await _db.SaveChangesAsync();
            }
        }
        catch (DbUpdateException)
        {
            // already released
        }
    }
}
