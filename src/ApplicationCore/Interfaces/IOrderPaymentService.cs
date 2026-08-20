using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderPaymentService
{
    Task<Order> AuthorizePaymentAsync(
        int orderId,
        string buyerId,
        PayPalCardDetails? card,
        int? savedPaymentMethodId,
        CancellationToken cancellationToken = default);

    Task<Order> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default);

    Task<Order> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);

    Task<(Order Order, OrderRefund Refund)> RefundOrderAsync(
        int orderId,
        string buyerId,
        string idempotencyKey,
        decimal? amount,
        CancellationToken cancellationToken = default);
}
