using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A requested line: a catalog item and how many of it.</summary>
public record OrderItemRequest(int CatalogItemId, int Quantity);

public interface IOrderService
{
    Task CreateOrderAsync(int basketId, Address shippingAddress);

    /// <summary>
    /// Place an order directly from catalog item ids + quantities (no basket), pricing each line
    /// from the current catalog price. Returns the persisted <see cref="Order"/> including its id.
    /// </summary>
    Task<Order> CreateOrderFromItemsAsync(string buyerId, IEnumerable<OrderItemRequest> items, Address shippingAddress);
}
