using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A catalog item and the quantity of it being ordered.</summary>
public record OrderItemRequest(int CatalogItemId, int Quantity);

public interface IOrderService
{
    Task CreateOrderAsync(int basketId, Address shippingAddress);

    /// <summary>
    /// Creates an order directly from catalog item ids and quantities for a given buyer,
    /// reusing the existing order/order-item model. The unit price is snapshotted from the
    /// catalog at order time, mirroring how the basket-based flow snapshots prices.
    /// Returns the created order (with its assigned id).
    /// </summary>
    Task<Order> CreateOrderAsync(string buyerId, IEnumerable<OrderItemRequest> items, Address shippingAddress);
}
