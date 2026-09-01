using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Orchestrates the order payment lifecycle: place, pay (authorize), fulfil (capture),
/// cancel (void) and refund. Payment operations are idempotent in effect — repeating one
/// returns the current state instead of charging again.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>Places an order from catalog items at current catalog prices. Starts awaiting payment.</summary>
    Task<Order> CreateOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> items, Address? shipToAddress,
        CancellationToken ct = default);

    /// <summary>Authorizes the order total via PayPal, with an inline card or one of the caller's saved cards.</summary>
    Task<Order> PayOrderAsync(string buyerId, int orderId, CardDetails? card, int? savedCardId,
        CancellationToken ct = default);

    /// <summary>Operator: fulfils the order, capturing the held money. Renews a stale authorization when possible.</summary>
    Task<Order> FulfilOrderAsync(int orderId, CancellationToken ct = default);

    /// <summary>Operator: cancels before fulfilment, releasing the shopper's held funds.</summary>
    Task<Order> CancelOrderAsync(int orderId, CancellationToken ct = default);

    /// <summary>Operator: refunds the captured payment in full (amount null) or in part, under a caller idempotency key.</summary>
    Task<RefundOutcome> RefundOrderAsync(int orderId, decimal? amount, string idempotencyKey,
        CancellationToken ct = default);

    /// <summary>The caller's own orders with their payment state.</summary>
    Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId, CancellationToken ct = default);
}

public record OrderLineRequest(int CatalogItemId, int Quantity);

public record RefundOutcome(Order Order, OrderRefund Refund, bool Replayed);
