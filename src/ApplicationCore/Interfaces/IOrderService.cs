using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderService
{
    Task CreateOrderAsync(int basketId, Address shippingAddress);

    /// <summary>
    /// Places an order directly from catalog item ids and quantities (reusing the existing Order /
    /// OrderItem / CatalogItemOrdered model) on behalf of <paramref name="buyerId"/>. Returns the created
    /// order, including its assigned identifier.
    /// </summary>
    Task<Order> CreateOrderAsync(string buyerId, IEnumerable<OrderItemRequest> items, Address shippingAddress);
}

/// <summary>A requested line item when placing an order from the API: a catalog item and how many.</summary>
public record OrderItemRequest(int CatalogItemId, int Quantity);
