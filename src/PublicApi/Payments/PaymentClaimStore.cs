using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// Serializes money-moving operations for one order. A process-wide atomic registry refuses the
/// second caller before the database row is written, and the row makes the claim durable for the
/// lifetime of the (in-memory) payments store.
/// </summary>
public sealed class PaymentClaimStore
{
    private static readonly ConcurrentDictionary<string, DateTimeOffset> ActiveClaims = new();

    private readonly PaymentsDbContext _db;
    private readonly ILogger<PaymentClaimStore> _logger;

    public PaymentClaimStore(PaymentsDbContext db, ILogger<PaymentClaimStore> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<bool> TryClaimAsync(string key, CancellationToken ct)
    {
        if (!ActiveClaims.TryAdd(key, DateTimeOffset.UtcNow))
        {
            return false;
        }

        try
        {
            var alreadyStored = await _db.PaymentOperationClaims.AnyAsync(c => c.Key == key, ct);
            if (alreadyStored)
            {
                ActiveClaims.TryRemove(key, out _);
                return false;
            }

            _db.PaymentOperationClaims.Add(new PaymentOperationClaim(key));
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Payment claim {ClaimKey} was refused by the store.", key);
            ActiveClaims.TryRemove(key, out _);
            return false;
        }
    }

    public async Task ReleaseAsync(string key, CancellationToken ct)
    {
        ActiveClaims.TryRemove(key, out _);

        try
        {
            var row = await _db.PaymentOperationClaims.FirstOrDefaultAsync(c => c.Key == key, ct);
            if (row is not null)
            {
                _db.PaymentOperationClaims.Remove(row);
                await _db.SaveChangesAsync(ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Payment claim {ClaimKey} could not be released.", key);
        }
    }
}
