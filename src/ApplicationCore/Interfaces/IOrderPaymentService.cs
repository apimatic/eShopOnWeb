using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderPaymentService
{
    Task<Order> PayAsync(
        int orderId,
        string buyerId,
        CardDetails? card,
        int? paymentMethodId,
        CancellationToken cancellationToken);

    Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken);

    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken);

    Task<OrderRefund> RefundAsync(
        int orderId,
        string buyerId,
        string idempotencyKey,
        decimal? amount,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Order>> ListBuyerOrdersAsync(string buyerId, CancellationToken cancellationToken);

    Task<Order?> GetBuyerOrderAsync(int orderId, string buyerId, CancellationToken cancellationToken);
}
