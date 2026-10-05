using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.Infrastructure.Data;

/// <summary>
/// Claims as rows keyed by the claim itself in <see cref="CatalogContext.PaymentClaims"/>. The second
/// insert of a key is refused by the store's primary key (SQL Server and the in-memory provider alike).
/// Each call uses its own short-lived context so a claim never drags unrelated pending changes with it.
/// </summary>
public class EfPaymentClaimStore : IPaymentClaimStore
{
    private readonly DbContextOptions<CatalogContext> _options;
    private readonly TimeProvider _clock;

    public EfPaymentClaimStore(DbContextOptions<CatalogContext> options, TimeProvider clock)
    {
        _options = options;
        _clock = clock;
    }

    public async Task<PaymentClaimLease?> TryAcquireAsync(string key, TimeSpan timeToLive, CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var now = _clock.GetUtcNow();
            var expiresAt = timeToLive >= DateTimeOffset.MaxValue - now ? DateTimeOffset.MaxValue : now + timeToLive;
            var lease = new PaymentClaimLease(key, Guid.NewGuid());

            await using (var db = new CatalogContext(_options))
            {
                db.PaymentClaims.Add(new PaymentClaim(key, lease.Owner, now, expiresAt));
                try
                {
                    await db.SaveChangesAsync(cancellationToken);
                    return lease;
                }
                catch (Exception ex) when (ex is DbUpdateException or ArgumentException or InvalidOperationException)
                {
                    // Fall through: find out whether the key is held.
                }
            }

            await using var check = new CatalogContext(_options);
            var existing = await check.PaymentClaims.FirstOrDefaultAsync(c => c.Key == key, cancellationToken);
            if (existing is null)
            {
                if (attempt == 0) continue; // released between our insert and the check
                return null;
            }
            if (existing.ExpiresAt > now || attempt > 0)
                return null;

            // An abandoned claim (its holder died): take it over. A concurrent taker is still arbitrated
            // by the primary key on the next insert.
            check.PaymentClaims.Remove(existing);
            try
            {
                await check.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                return null;
            }
        }

        return null;
    }

    public async Task ReleaseAsync(PaymentClaimLease lease, CancellationToken cancellationToken = default)
    {
        await using var db = new CatalogContext(_options);
        var existing = await db.PaymentClaims.FirstOrDefaultAsync(c => c.Key == lease.Key, cancellationToken);
        if (existing is null || existing.Owner != lease.Owner)
            return;

        db.PaymentClaims.Remove(existing);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Already gone.
        }
    }
}
