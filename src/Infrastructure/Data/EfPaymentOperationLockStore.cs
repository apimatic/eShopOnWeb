using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Data;

/// <summary>
/// Claims an order's payment operations by inserting a <see cref="PaymentOperationLock"/> row: the
/// store's primary key refuses a second concurrent insert, so a double-click or a racing instance is
/// stopped before it reaches the payment provider. Each claim runs in its own <see cref="CatalogContext"/>
/// so it is committed independently of the caller's unit of work.
/// </summary>
public class EfPaymentOperationLockStore : IPaymentOperationLock
{
    /// <summary>
    /// A claim older than this is considered abandoned (its holder crashed). Comfortably longer than the
    /// longest request, whose provider calls are bounded well under a minute.
    /// </summary>
    public static readonly TimeSpan Expiry = TimeSpan.FromMinutes(2);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<EfPaymentOperationLockStore> _logger;
    private readonly Dictionary<int, Guid> _held = new();

    public EfPaymentOperationLockStore(IServiceScopeFactory scopeFactory, TimeProvider timeProvider, ILogger<EfPaymentOperationLockStore> logger)
    {
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<bool> TryAcquireAsync(int orderId, CancellationToken cancellationToken)
    {
        if (await TryInsertAsync(orderId, cancellationToken))
            return true;

        // Refused: someone holds the claim. Only take it over if that holder has evidently crashed.
        if (!await TryRemoveAbandonedAsync(orderId, cancellationToken))
            return false;

        return await TryInsertAsync(orderId, cancellationToken);
    }

    public async Task ReleaseAsync(int orderId)
    {
        if (!_held.Remove(orderId, out var holder))
            return;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogContext>();
        var existing = await db.PaymentOperationLocks.FindAsync(orderId);
        if (existing is null || existing.Holder != holder)
            return; // expired and taken over; not ours to release

        db.PaymentOperationLocks.Remove(existing);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            // Already gone.
        }
    }

    private async Task<bool> TryInsertAsync(int orderId, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogContext>();
        var holder = Guid.NewGuid();
        db.PaymentOperationLocks.Add(new PaymentOperationLock(orderId, holder, _timeProvider.GetUtcNow()));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Relational stores: primary-key violation — another caller holds the claim.
            return false;
        }
        catch (ArgumentException)
        {
            // The EF in-memory store reports the same primary-key violation as an ArgumentException.
            return false;
        }

        _held[orderId] = holder;
        return true;
    }

    private async Task<bool> TryRemoveAbandonedAsync(int orderId, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogContext>();
        var existing = await db.PaymentOperationLocks.FindAsync(new object[] { orderId }, cancellationToken);
        if (existing is null)
            return true; // released in the meantime
        if (_timeProvider.GetUtcNow() - existing.AcquiredAt < Expiry)
            return false;

        _logger.LogWarning("Releasing abandoned payment claim on order {OrderId} taken at {AcquiredAt}.", orderId, existing.AcquiredAt);
        db.PaymentOperationLocks.Remove(existing);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false; // someone else cleared it first and may hold it now
        }
    }
}
