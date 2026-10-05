using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Data;

/// <summary>
/// Claims as rows keyed by the claim itself. A second insert of the same key is refused by the store
/// (SQL Server: primary-key violation; in-memory provider: duplicate key) — never by a read-then-write check.
/// </summary>
public class EfPaymentClaimStore : IPaymentClaimStore
{
    private readonly CatalogContext _db;
    private readonly TimeProvider _clock;

    public EfPaymentClaimStore(CatalogContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<bool> TryClaimAsync(string key, CancellationToken cancellationToken)
    {
        var claim = new PaymentClaim(key, _clock.GetUtcNow());
        try
        {
            // Add itself throws when this context already tracks the key.
            _db.PaymentClaims.Add(claim);
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (Exception ex) when (IsDuplicateKey(ex))
        {
            Detach(claim);
            return false;
        }
        catch
        {
            Detach(claim);
            throw;
        }
    }

    public async Task<DateTimeOffset?> GetClaimedAtAsync(string key, CancellationToken cancellationToken)
    {
        var claim = await _db.PaymentClaims.AsNoTracking().FirstOrDefaultAsync(c => c.Key == key, cancellationToken);
        return claim?.ClaimedAt;
    }

    public async Task ReleaseAsync(string key, CancellationToken cancellationToken)
    {
        var claim = _db.PaymentClaims.Local.FirstOrDefault(c => c.Key == key)
                    ?? await _db.PaymentClaims.FirstOrDefaultAsync(c => c.Key == key, cancellationToken);
        if (claim is null) return;
        _db.PaymentClaims.Remove(claim);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Already released by someone else.
            Detach(claim);
        }
    }

    private void Detach(PaymentClaim claim)
    {
        var entry = _db.ChangeTracker.Entries<PaymentClaim>().FirstOrDefault(e => ReferenceEquals(e.Entity, claim));
        if (entry is not null) entry.State = EntityState.Detached;
    }

    // Relational providers surface the constraint violation as DbUpdateException; the in-memory provider
    // throws ArgumentException ("An item with the same key has already been added") on save, and the change
    // tracker throws InvalidOperationException when this context already tracks an entity with that key.
    private static bool IsDuplicateKey(Exception ex) =>
        ex is DbUpdateException or ArgumentException
        || (ex is InvalidOperationException && ex.Message.Contains("same key", StringComparison.OrdinalIgnoreCase));
}
