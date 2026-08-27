using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderService
{
    Task CreateOrderAsync(int basketId, Address shippingAddress);

    /// <summary>Places an order directly from catalog item ids and quantities, priced from the catalog.</summary>
    Task<Order> CreateOrderFromItemsAsync(string buyerId, Address shippingAddress, IReadOnlyList<OrderItemRequest> items, CancellationToken ct = default);

    /// <summary>Marks an order dispatched. Throws NotFoundException / OrderStateException.</summary>
    Task<Order> DispatchOrderAsync(int orderId, CancellationToken ct = default);

    /// <summary>Cancels an order. Throws NotFoundException / OrderStateException.</summary>
    Task<Order> CancelOrderAsync(int orderId, CancellationToken ct = default);
}
