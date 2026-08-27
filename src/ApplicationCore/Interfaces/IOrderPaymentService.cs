using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record OrderItemLine(int CatalogItemId, int Quantity);

/// <summary>
/// Orchestrates the money movement for orders: authorize at checkout, capture at
/// fulfilment, release on cancel, refund on return.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>Places an order from catalog items at current catalog prices. The order starts awaiting payment.</summary>
    Task<Order> CreateOrderAsync(string buyerId, IReadOnlyList<OrderItemLine> items, Address shipToAddress, CancellationToken cancellationToken = default);

    /// <summary>Authorizes the order total with either full card details or one of the buyer's saved cards. Idempotent per order.</summary>
    Task<Order> PayOrderAsync(string buyerId, int orderId, GatewayCard? card, int? savedCardId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: captures the authorized funds, renewing a stale authorization when possible.</summary>
    Task<Order> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Operator action: cancels before fulfilment, releasing the shopper's held funds.</summary>
    Task<Order> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Refunds a fulfilled order, in full (amount null) or in part. Idempotent per idempotency key.</summary>
    Task<(Order Order, PaymentRefund Refund)> RefundOrderAsync(string buyerId, bool isAdmin, int orderId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default);
}
