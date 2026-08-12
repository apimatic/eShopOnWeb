using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A single requested line of an order placed through the API.</summary>
public record OrderLineRequest(int CatalogItemId, int Quantity);

/// <summary>
/// Places and moves orders through the API, reusing the existing Order/OrderItem model, and
/// drives the matching shopper notifications as each transition happens. A messaging failure never
/// fails the underlying order operation.
/// </summary>
public interface IStoreOrderService
{
    /// <summary>Place an order for the shopper from catalog item ids + quantities, then notify them it was placed.</summary>
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineRequest> lines, CancellationToken cancellationToken = default);

    /// <summary>Operator marks an order dispatched, notifies the shopper, and queues the delivery follow-up. Null if not found.</summary>
    Task<Order?> DispatchOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Operator cancels an order, notifies the shopper, and calls off any not-yet-sent follow-up. Null if not found.</summary>
    Task<Order?> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>The caller's own orders.</summary>
    Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);

    /// <summary>One of the caller's own orders, or null if it is not found among the caller's orders.</summary>
    Task<Order?> GetOrderForBuyerAsync(int orderId, string buyerId, CancellationToken cancellationToken = default);
}
