using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.PayPal;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A single requested line of a new order.</summary>
public record OrderLineRequest(int CatalogItemId, int Quantity);

/// <summary>
/// Orchestrates the payment lifecycle of an order against PayPal: place, authorize (hold),
/// fulfil (capture), cancel (void) and refund. Shopper-scoped operations take the caller's
/// buyer id and act only on that shopper's orders.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>Place an order from catalog items for the shopper. The order starts awaiting payment.</summary>
    Task<Order> CreateOrderAsync(string buyerId, IReadOnlyCollection<OrderLineRequest> lines, Address? shipToAddress);

    /// <summary>Authorize (hold) the order total using either raw card details or one of the
    /// shopper's saved cards. Idempotent: an already-authorized order is returned unchanged.</summary>
    Task<Order> AuthorizeAsync(int orderId, string buyerId, CardDetails? card, int? savedPaymentMethodId);

    /// <summary>Operator action: capture the held funds at fulfilment, renewing a stale hold if
    /// needed. Idempotent: an already-captured order is returned unchanged.</summary>
    Task<Order> FulfilAsync(int orderId);

    /// <summary>Operator action: cancel before fulfilment, releasing the held funds.</summary>
    Task<Order> CancelAsync(int orderId);

    /// <summary>Refund a captured order in full or in part under a caller-supplied idempotency key.
    /// Allowed for the owning shopper or an administrator.</summary>
    Task<(Order Order, string RefundId)> RefundAsync(int orderId, string buyerId, bool isAdministrator, decimal? amount, string idempotencyKey);

    /// <summary>The shopper's orders with their payment state.</summary>
    Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId);
}
