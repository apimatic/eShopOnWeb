using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record OrderItemRequest(int CatalogItemId, int Quantity);

/// <summary>
/// Places orders directly from catalog items (no basket involved) and pays for them via PayPal.
/// Additive to the existing basket-driven <see cref="IOrderService"/> checkout flow.
/// </summary>
public interface IOrderCheckoutService
{
    Task<Order> PlaceOrderAsync(string buyerId, Address shipToAddress, IReadOnlyList<OrderItemRequest> items,
        CancellationToken ct = default);

    /// <summary>Authorizes (holds) the order total using either one-off card details or a saved card. Idempotent.</summary>
    Task<Order> PayAsync(string buyerId, int orderId, CardDetails? card, int? paymentMethodId,
        CancellationToken ct = default);

    Task<Order> GetOrderForBuyerAsync(string buyerId, int orderId, CancellationToken ct = default);

    Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken ct = default);
}
