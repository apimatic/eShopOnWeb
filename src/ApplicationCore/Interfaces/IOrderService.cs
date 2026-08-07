using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderService
{
    Task CreateOrderAsync(int basketId, Address shippingAddress);

    /// <summary>
    /// Places an order directly from catalog item ids and quantities (used by the API, which has no
    /// server-side basket). Prices are taken from the catalog, the order reuses the standard
    /// Order/OrderItem model, and it is created awaiting payment. Returns the created order.
    /// </summary>
    Task<Order> CreateOrderAsync(string buyerId, IReadOnlyCollection<OrderItemInput> items,
        Address shippingAddress, CancellationToken cancellationToken = default);
}

/// <summary>A catalog item and quantity requested when placing an order via the API.</summary>
public record OrderItemInput(int CatalogItemId, int Quantity);
