using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Data;

public class OrderPaymentStore : IOrderPaymentStore
{
    private readonly CatalogContext _db;

    public OrderPaymentStore(CatalogContext db)
    {
        _db = db;
    }

    public Task<Order?> GetOrderWithPaymentsAsync(int orderId, CancellationToken cancellationToken) =>
        _db.Orders
            .Include(o => o.OrderItems)
            .Include(o => o.PaymentAttempts)
            .Include(o => o.Refunds)
            .AsSplitQuery()
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);

    public async Task<IReadOnlyList<Order>> ListOrdersWithPaymentsForBuyerAsync(string buyerId, CancellationToken cancellationToken) =>
        await _db.Orders
            .AsNoTracking()
            .Include(o => o.OrderItems)
            .Include(o => o.PaymentAttempts)
            .Include(o => o.Refunds)
            .AsSplitQuery()
            .Where(o => o.BuyerId == buyerId)
            .OrderByDescending(o => o.Id)
            .ToListAsync(cancellationToken);

    public Task<bool> TryClaimPaymentAttemptAsync(OrderPaymentAttempt attempt, CancellationToken cancellationToken) =>
        TryInsertAsync(attempt,
            () => _db.OrderPaymentAttempts.AsNoTracking()
                .AnyAsync(a => a.OrderId == attempt.OrderId && a.AttemptNumber == attempt.AttemptNumber, cancellationToken),
            cancellationToken);

    public Task<bool> TryClaimRefundAsync(OrderRefund refund, CancellationToken cancellationToken) =>
        TryInsertAsync(refund,
            () => _db.OrderRefunds.AsNoTracking()
                .AnyAsync(r => r.OrderId == refund.OrderId && r.Sequence == refund.Sequence, cancellationToken),
            cancellationToken);

    public async Task<IReadOnlyList<OrderPaymentAttempt>> ReadPaymentAttemptsAsync(int orderId, CancellationToken cancellationToken) =>
        await _db.OrderPaymentAttempts.AsNoTracking().Where(a => a.OrderId == orderId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<OrderRefund>> ReadRefundsAsync(int orderId, CancellationToken cancellationToken) =>
        await _db.OrderRefunds.AsNoTracking().Where(r => r.OrderId == orderId).ToListAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => _db.SaveChangesAsync(cancellationToken);

    /// <summary>
    /// Inserts <paramref name="claim"/> on its own. The primary key is the claim: when another request already
    /// holds it the store refuses the insert, and that refusal (and only that) is reported as false.
    /// </summary>
    private async Task<bool> TryInsertAsync<T>(T claim, System.Func<Task<bool>> keyAlreadyStored, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            _db.Add(claim);
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (System.Exception ex) when (ex is DbUpdateException or System.InvalidOperationException or System.ArgumentException)
        {
            // Relational stores raise DbUpdateException on the duplicate key; the in-memory store and the
            // change tracker raise InvalidOperationException / ArgumentException. Any of them only means
            // "already claimed" if the key is really there — anything else is a genuine failure.
            _db.Entry(claim).State = EntityState.Detached;
            if (await keyAlreadyStored())
            {
                return false;
            }

            throw;
        }
    }
}
