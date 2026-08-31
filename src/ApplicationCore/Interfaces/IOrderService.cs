using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A catalog item and how many of it to order.</summary>
public record CatalogItemQuantity(int CatalogItemId, int Quantity);

public interface IOrderService
{
    Task CreateOrderAsync(int basketId, Address shippingAddress);

    /// <summary>
    /// Create an order directly from catalog item ids and quantities (no basket), reusing the app's existing
    /// order/order-item model. Returns the persisted order so its identifier can be handed back to the caller.
    /// </summary>
    Task<Order> CreateOrderAsync(string buyerId, IEnumerable<CatalogItemQuantity> items, Address shippingAddress);
}
