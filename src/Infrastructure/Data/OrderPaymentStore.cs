using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Data;

public class OrderPaymentStore : IOrderPaymentStore
{
    // Each retry re-reads the order and re-applies the change, so only genuinely racing writers loop here.
    private const int MaxAttempts = 5;

    // See SaveAsync: makes compare-and-save atomic for the in-memory provider, whose store lives in this process.
    private static readonly SemaphoreSlim InMemorySaveGate = new(1, 1);

    private readonly CatalogContext _db;
    private readonly ILogger<OrderPaymentStore> _logger;

    public OrderPaymentStore(CatalogContext db, ILogger<OrderPaymentStore> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<TResult?> UpdateAsync<TResult>(int orderId, Func<Order, TResult> change,
        CancellationToken cancellationToken = default) where TResult : class
    {
        for (var attempt = 1; ; attempt++)
        {
            _db.ChangeTracker.Clear();
            var order = await _db.Orders
                .Include(o => o.OrderItems)
                .Include(o => o.PaymentAttempts)
                .Include(o => o.Refunds)
                .AsSplitQuery()
                .SingleOrDefaultAsync(o => o.Id == orderId, cancellationToken);
            if (order is null)
                return null;

            var result = change(order);
            try
            {
                await SaveAsync(order, cancellationToken);
                return result;
            }
            catch (Exception ex) when (IsLostRace(ex) && attempt < MaxAttempts)
            {
                // Another request saved this order first: its PaymentVersion moved on (concurrency token), or the
                // payment attempt / refund we tried to claim already exists (primary key). Decide again on fresh state.
                _logger.LogInformation("Order {OrderId} changed concurrently ({Reason}); re-reading and re-applying (attempt {Attempt}).",
                    orderId, ex.GetType().Name, attempt + 1);
            }
            catch (Exception ex) when (IsLostRace(ex))
            {
                throw new PaymentConcurrencyException(orderId, ex);
            }
        }
    }

    private async Task SaveAsync(Order order, CancellationToken cancellationToken)
    {
        if (!_db.Database.IsInMemory())
        {
            // Relational providers run SaveChanges in one transaction: the PaymentVersion concurrency token and the
            // primary keys of the claimed rows reject a racing writer atomically.
            await _db.SaveChangesAsync(cancellationToken);
            return;
        }

        // The in-memory provider applies a SaveChanges entry by entry with no rollback, so a writer that loses the
        // race on the order row could still leave its new rows behind. Compare the version and save under one gate
        // instead; nothing outside this process can reach an in-memory store.
        await InMemorySaveGate.WaitAsync(cancellationToken);
        try
        {
            var expected = (Guid)_db.Entry(order).Property(o => o.PaymentVersion).OriginalValue!;
            var stored = await _db.Orders.AsNoTracking()
                .Where(o => o.Id == order.Id)
                .Select(o => o.PaymentVersion)
                .SingleAsync(cancellationToken);
            if (stored != expected)
                throw new DbUpdateConcurrencyException($"Order {order.Id} was changed by another request.");

            await _db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            InMemorySaveGate.Release();
        }
    }

    public async Task<Order?> GetAsync(int orderId, bool includePaymentRecord, CancellationToken cancellationToken = default)
    {
        IQueryable<Order> query = _db.Orders.AsNoTracking()
            .Include(o => o.OrderItems)
            .Include(o => o.PaymentAttempts)
            .Include(o => o.Refunds);
        if (includePaymentRecord)
            query = query.Include(o => o.PaymentRecord);

        return await query.AsSplitQuery().SingleOrDefaultAsync(o => o.Id == orderId, cancellationToken);
    }

    public async Task<IReadOnlyList<Order>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _db.Orders.AsNoTracking()
            .Where(o => o.BuyerId == buyerId)
            .Include(o => o.OrderItems)
            .Include(o => o.PaymentAttempts)
            .Include(o => o.Refunds)
            .AsSplitQuery()
            .OrderByDescending(o => o.Id)
            .ToListAsync(cancellationToken);
    }

    // DbUpdateConcurrencyException: the version token moved. DbUpdateException: a relational store refused a
    // duplicate primary key. The in-memory provider reports a duplicate key as InvalidOperationException
    // ("...another instance with the same key value...") or ArgumentException, depending on where it is detected.
    private static bool IsLostRace(Exception ex) =>
        ex is DbUpdateException
        || (ex is InvalidOperationException or ArgumentException && ex.Message.Contains("same key", StringComparison.OrdinalIgnoreCase));
}
