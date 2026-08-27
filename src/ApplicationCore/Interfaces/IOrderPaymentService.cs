using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record OrderItemRequest(int CatalogItemId, int Quantity);

/// <summary>
/// Orchestrates the order payment lifecycle: authorize at checkout, capture at
/// fulfilment, void on cancel, refund after fulfilment.
/// </summary>
public interface IOrderPaymentService
{
    Task<Order> CreateOrderAsync(string buyerId, IReadOnlyList<OrderItemRequest> items, CancellationToken ct);

    Task<Order> PayOrderAsync(string buyerId, int orderId, CardDetails? card, int? savedCardId, CancellationToken ct);

    Task<Order> FulfilOrderAsync(int orderId, CancellationToken ct);

    Task<Order> CancelOrderAsync(int orderId, CancellationToken ct);

    Task<PaymentRefund> RefundOrderAsync(int orderId, decimal? amount, string idempotencyKey, CancellationToken ct);
}
