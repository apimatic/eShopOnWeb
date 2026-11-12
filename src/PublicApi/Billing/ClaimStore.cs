using System;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Claim before the SDK call: the primary key of <see cref="MaxioClaim"/> refuses a second concurrent caller.
/// </summary>
public class ClaimStore
{
    private static readonly TimeSpan ClaimLifetime = TimeSpan.FromMinutes(5);
    private readonly CatalogContext _db;

    public ClaimStore(CatalogContext db)
    {
        _db = db;
    }

    /// <summary>Returns false when somebody else holds the claim.</summary>
    public async Task<bool> TryClaimAsync(string key, CancellationToken ct)
    {
        // A claim whose holder died never settles; expire it so one crash is not a permanent refusal.
        var stale = DateTimeOffset.UtcNow - ClaimLifetime;
        var old = await _db.MaxioClaims.Where(c => c.Key == key && c.CreatedAt < stale).ToListAsync(ct);
        if (old.Count > 0)
        {
            _db.MaxioClaims.RemoveRange(old);
            await _db.SaveChangesAsync(ct);
        }

        _db.MaxioClaims.Add(new MaxioClaim { Key = key, CreatedAt = DateTimeOffset.UtcNow });
        try
        {
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException)
        {
            _db.ChangeTracker.Clear();
            // Only a held claim is a conflict; any other store failure is not.
            if (await _db.MaxioClaims.AsNoTracking().AnyAsync(c => c.Key == key, CancellationToken.None))
                return false;
            throw;
        }
    }

    public async Task ReleaseAsync(string key)
    {
        _db.ChangeTracker.Clear();
        var claim = await _db.MaxioClaims.FirstOrDefaultAsync(c => c.Key == key);
        if (claim != null)
        {
            _db.MaxioClaims.Remove(claim);
            await _db.SaveChangesAsync();
        }
    }
}
