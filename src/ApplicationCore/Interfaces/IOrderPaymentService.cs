using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record OrderLineItemRequest(int CatalogItemId, int Quantity);

/// <summary>
/// Owns the order-payment lifecycle: place → authorize (pay) → fulfil (capture) → refund, or
/// cancel before fulfilment. Every method that acts on an existing order scoped to a shopper
/// returns null when the order does not exist or does not belong to that shopper, so callers
/// can map that directly to a 404 without distinguishing the two (never leak whether another
/// shopper's order id exists).
/// </summary>
public interface IOrderPaymentService
{
    Task<Order> PlaceOrderAsync(string buyerId, Address shipToAddress, IReadOnlyList<OrderLineItemRequest> items, CancellationToken ct);

    Task<(Order Order, AuthorizePaymentOutcome Outcome)?> PayAsync(int orderId, string buyerId, PaymentSourceRequest paymentSource, CancellationToken ct);

    /// <summary>Operator action — not buyer-scoped.</summary>
    Task<Order?> FulfilAsync(int orderId, CancellationToken ct);

    /// <summary>Operator action — not buyer-scoped.</summary>
    Task<Order?> CancelAsync(int orderId, CancellationToken ct);

    Task<(Order Order, Refund Refund)?> RefundAsync(int orderId, string buyerId, decimal? amount, string idempotencyKey, CancellationToken ct);

    Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken ct);
}
