using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record OrderItemRequest(int CatalogItemId, int Quantity);

public interface IOrderService
{
    Task CreateOrderAsync(int basketId, Address shippingAddress);

    /// <summary>Place an order directly from catalog item ids and quantities.</summary>
    Task<Order> CreateOrderAsync(string buyerId, Address shippingAddress, IReadOnlyList<OrderItemRequest> items, CancellationToken cancellationToken = default);

    /// <summary>Mark an order dispatched (operator action).</summary>
    Task<Order> DispatchOrderAsync(int orderId, CancellationToken cancellationToken = default);

    /// <summary>Cancel an order (operator action).</summary>
    Task<Order> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default);
}
