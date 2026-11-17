using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using OrderEntity = Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate.Order;

namespace Microsoft.eShopWeb.PublicApi.Services;

public static class OrderQueries
{
    public static Task<OrderEntity?> LoadOrderAsync(
        CatalogContext db,
        int orderId,
        CancellationToken cancellationToken) =>
        db.Orders
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.ItemOrdered)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);

    public static Task<OrderPayment?> LoadPaymentAsync(
        CatalogContext db,
        int orderId,
        CancellationToken cancellationToken) =>
        db.OrderPayments.FirstOrDefaultAsync(p => p.OrderId == orderId, cancellationToken);

    public static Task<List<PaymentRefund>> LoadRefundsAsync(
        CatalogContext db,
        int orderPaymentId,
        CancellationToken cancellationToken) =>
        db.PaymentRefunds
            .Where(r => r.OrderPaymentId == orderPaymentId)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(cancellationToken);
}
