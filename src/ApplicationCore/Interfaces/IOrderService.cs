using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A requested catalog item and how many of it, used when placing an order directly from catalog items.</summary>
public record OrderLineRequest(int CatalogItemId, int Quantity);

public interface IOrderService
{
    Task CreateOrderAsync(int basketId, Address shippingAddress);

    /// <summary>
    /// Places an order for the given buyer directly from catalog items (no basket), reusing the app's
    /// existing order/order-item model. Each line's price is snapshotted from the catalog. Returns the
    /// created order (with its assigned id).
    /// </summary>
    Task<Order> CreateOrderAsync(string buyerId, IReadOnlyCollection<OrderLineRequest> lines, Address shippingAddress);
}
