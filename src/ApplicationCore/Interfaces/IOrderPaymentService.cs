using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>An order line requested by catalog item id + quantity (prices are always taken from the catalog, never the caller).</summary>
public record OrderItemQuantity(int CatalogItemId, int Quantity);

/// <summary>
/// Orchestrates the pay-for-an-order flow: placing an order, authorizing (holding) payment, capturing it
/// at fulfilment, cancelling before fulfilment, and refunding after.
/// </summary>
public interface IOrderPaymentService
{
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderItemQuantity> items, Address shipToAddress);

    /// <summary>Authorizes the order total with PayPal. Returns null if the order doesn't exist or isn't owned by <paramref name="buyerId"/>.</summary>
    Task<Order?> AuthorizePaymentAsync(int orderId, string buyerId, CardDetails? card, int? savedPaymentMethodId);

    /// <summary>Captures a previously authorized order. Returns null if the order doesn't exist.</summary>
    Task<Order?> FulfilOrderAsync(int orderId);

    /// <summary>Releases a held-but-not-yet-captured order. Returns null if the order doesn't exist.</summary>
    Task<Order?> CancelOrderAsync(int orderId);

    /// <summary>Refunds a fulfilled order's captured payment, in full or in part. Returns null if the order doesn't exist or isn't owned by <paramref name="buyerId"/>.</summary>
    Task<(Order Order, OrderRefund Refund)?> RefundOrderAsync(int orderId, string buyerId, decimal? amount, string idempotencyKey);

    Task<Order?> GetOrderForBuyerAsync(int orderId, string buyerId);

    Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId);
}
