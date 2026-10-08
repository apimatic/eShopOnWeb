using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Claims are plain inserts keyed by a value a concurrent duplicate computes too, so the database refuses the
/// second one with a duplicate-key error (SQL Server: unique violation; in-memory provider: duplicate key).
/// Shares the request's <see cref="CatalogContext"/> with the order repository, so status changes on loaded
/// attempts and refunds are saved with <see cref="SaveChangesAsync"/>.
/// </summary>
public class EfPaymentStore : IPaymentStore
{
    private readonly CatalogContext _dbContext;

    public EfPaymentStore(CatalogContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task AddPaymentClaimAsync(OrderPayment payment, CancellationToken cancellationToken) =>
        InsertClaimAsync(payment,
            ct => _dbContext.OrderPayments.AsNoTracking().AnyAsync(p => p.Id == payment.Id, ct),
            "A payment for this order is already being processed. Check the order status before paying again.",
            cancellationToken);

    public Task AddRefundClaimAsync(OrderRefund refund, CancellationToken cancellationToken) =>
        InsertClaimAsync(refund,
            ct => _dbContext.OrderRefunds.AsNoTracking().AnyAsync(r => r.Id == refund.Id, ct),
            "Another refund on this order is being processed. Try again in a moment.",
            cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => _dbContext.SaveChangesAsync(cancellationToken);

    private async Task InsertClaimAsync<TClaim>(TClaim claim, Func<CancellationToken, Task<bool>> claimExists, string conflictMessage,
        CancellationToken cancellationToken) where TClaim : class
    {
        try
        {
            _dbContext.Add(claim);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is DbUpdateException or ArgumentException or InvalidOperationException)
        {
            // Stop tracking the refused claim so it is not re-inserted by a later save in this unit of work.
            _dbContext.Entry(claim).State = EntityState.Detached;

            // Only a claim that now exists means "someone else got there first"; anything else is a real failure.
            if (await claimExists(CancellationToken.None))
                throw new PaymentConflictException(conflictMessage);
            throw;
        }
    }
}
