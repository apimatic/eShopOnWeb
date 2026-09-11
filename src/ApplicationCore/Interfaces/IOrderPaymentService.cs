using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A catalog line requested when placing an order.</summary>
public record OrderLineRequest(int CatalogItemId, int Quantity);

/// <summary>
/// Orchestrates the money movement around an order: place, authorize (hold), fulfil (capture),
/// cancel (void) and refund. Each action is a separate operation, idempotent in effect.
/// </summary>
public interface IOrderPaymentService
{
    /// <summary>Places an order for the shopper from catalog items. The order starts awaiting payment.</summary>
    Task<Order> CreateOrderAsync(string buyerId, IReadOnlyCollection<OrderLineRequest> lines, CancellationToken ct = default);

    /// <summary>Authorizes (holds) the order total, paying with one-off card details or a saved card.</summary>
    Task<Order> AuthorizeOrderAsync(string buyerId, int orderId, GatewayCardDetails? card, int? savedPaymentMethodId, CancellationToken ct = default);

    /// <summary>Operator action: fulfils the order, capturing the held funds (renewing a stale hold if needed).</summary>
    Task<Order> FulfilOrderAsync(int orderId, CancellationToken ct = default);

    /// <summary>Operator action: cancels the order before fulfilment, releasing any held funds.</summary>
    Task<Order> CancelOrderAsync(int orderId, CancellationToken ct = default);

    /// <summary>Refunds a fulfilled order, fully or partially, under a caller-supplied idempotency key.</summary>
    Task<(Order Order, OrderRefund Refund)> RefundOrderAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken ct = default);

    /// <summary>The shopper's own orders with their payment state.</summary>
    Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken ct = default);

    /// <summary>Loads a single order (with payment) scoped to its owner, or null if not found / not owned.</summary>
    Task<Order?> GetOrderForBuyerAsync(string buyerId, int orderId, CancellationToken ct = default);
}
