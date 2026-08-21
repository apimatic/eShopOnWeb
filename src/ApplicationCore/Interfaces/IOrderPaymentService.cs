using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderPaymentService
{
    Task<Order> AuthorizeAsync(
        int orderId,
        string buyerId,
        CardPaymentDetails? card,
        int? paymentMethodId,
        CancellationToken ct);

    Task<Order> FulfilAsync(int orderId, CancellationToken ct);

    Task<Order> CancelAsync(int orderId, CancellationToken ct);

    Task<OrderRefund> RefundAsync(
        int orderId,
        string buyerId,
        string idempotencyKey,
        decimal? amount,
        CancellationToken ct);
}
