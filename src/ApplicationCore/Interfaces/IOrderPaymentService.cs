using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderPaymentService
{
    Task<Order> PayWithCardAsync(string buyerId, int orderId, CardPaymentInput card, CancellationToken ct);
    Task<Order> PayWithSavedCardAsync(string buyerId, int orderId, int paymentMethodId, CancellationToken ct);
    Task<Order> FulfilAsync(int orderId, CancellationToken ct);
    Task<Order> CancelAsync(int orderId, CancellationToken ct);
    Task<OrderRefund> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken ct);
    Task<IReadOnlyList<Order>> ListMyOrdersAsync(string buyerId, CancellationToken ct);
    Task<Order> GetMyOrderAsync(string buyerId, int orderId, CancellationToken ct);
}
