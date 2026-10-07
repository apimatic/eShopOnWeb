using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Data;

public class OrderPaymentStore : IOrderPaymentStore
{
    private readonly CatalogContext _dbContext;

    public OrderPaymentStore(CatalogContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Order?> GetOrderAsync(int orderId, CancellationToken cancellationToken) =>
        _dbContext.Orders
            .Include(o => o.OrderItems)
            .Include(o => o.PaymentAttempts)
            .Include(o => o.Refunds)
            .Include(o => o.ProviderResponses)
            .AsSplitQuery()
            .SingleOrDefaultAsync(o => o.Id == orderId, cancellationToken);

    public async Task<IReadOnlyList<Order>> ListBuyerOrdersAsync(string buyerId, CancellationToken cancellationToken) =>
        await _dbContext.Orders
            .AsNoTracking()
            .Where(o => o.BuyerId == buyerId)
            .Include(o => o.OrderItems)
            .Include(o => o.PaymentAttempts)
            .Include(o => o.Refunds)
            .AsSplitQuery()
            .OrderByDescending(o => o.Id)
            .ToListAsync(cancellationToken);

    public async Task<bool> TrySaveClaimAsync(Order order, CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (Exception ex) when (IsDuplicateKey(ex, _dbContext))
        {
            // Another request inserted the same claim first. Nothing of ours was saved; drop the rejected changes so
            // nothing later in this scope can save them by accident.
            _dbContext.ChangeTracker.Clear();
            return false;
        }
    }

    public Task SaveAsync(Order order, CancellationToken cancellationToken) =>
        _dbContext.SaveChangesAsync(cancellationToken);

    /// <summary>
    /// The store's refusal of a second row with the same key. SQL Server reports it as a unique/primary key
    /// violation (2627/2601); the EF in-memory provider rejects the insert with an ArgumentException
    /// ("An item with the same key has already been added").
    /// </summary>
    private static bool IsDuplicateKey(Exception ex, DbContext dbContext) => ex switch
    {
        DbUpdateException { InnerException: SqlException { Number: 2627 or 2601 } } => true,
        ArgumentException when dbContext.Database.IsInMemory() => true,
        _ => false
    };
}
