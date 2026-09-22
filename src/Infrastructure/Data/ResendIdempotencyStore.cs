using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Data;

/// <summary>
/// Claims a resend idempotency key by inserting a row whose primary key IS the key. A second
/// insert under the same key is rejected by the store (a SQL Server primary-key constraint at
/// SaveChanges; the EF in-memory provider likewise rejects a duplicate key). The rejection is
/// caught here — not pre-empted by a read — and the failed entity is detached so the shared
/// request context stays usable, then the existing claim is read back so the caller can replay.
/// </summary>
public sealed class ResendIdempotencyStore : IResendIdempotencyStore
{
    private readonly CatalogContext _context;

    public ResendIdempotencyStore(CatalogContext context)
    {
        _context = context;
    }

    public async Task<int?> TryClaimAsync(string idempotencyKey, int notificationId, CancellationToken ct)
    {
        var claim = new SmsIdempotencyKey(idempotencyKey, notificationId);
        _context.Set<SmsIdempotencyKey>().Add(claim);

        try
        {
            await _context.SaveChangesAsync(ct);
            return null; // newly claimed
        }
        catch (Exception ex) when (IsDuplicateKey(ex))
        {
            // Detach the rejected entity so this (request-scoped) context can be used again, then
            // read the committed claim the first request wrote.
            _context.Entry(claim).State = EntityState.Detached;

            var existing = await _context.Set<SmsIdempotencyKey>()
                .AsNoTracking()
                .FirstOrDefaultAsync(k => k.Key == idempotencyKey, ct);

            return existing?.NotificationId ?? notificationId;
        }
    }

    private static bool IsDuplicateKey(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is DbUpdateException)
            {
                return true;
            }

            // The EF in-memory provider surfaces a duplicate primary key as an ArgumentException
            // ("An item with the same key has already been added"), and a duplicate within a single
            // context as an InvalidOperationException ("already being tracked").
            if ((e is ArgumentException || e is InvalidOperationException)
                && (e.Message.Contains("same key") || e.Message.Contains("already being tracked")))
            {
                return true;
            }
        }

        return false;
    }
}
