using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A line requested when placing an order directly (catalog item + quantity).</summary>
public record OrderItemRequest(int CatalogItemId, int Quantity);

public interface IOrderService
{
    Task CreateOrderAsync(int basketId, Address shippingAddress);

    /// <summary>
    /// Places an order directly for a buyer from catalog item ids and quantities, reusing the app's
    /// existing Order/OrderItem model. Unit prices come from the catalog, not the caller.
    /// </summary>
    Task<Order> CreateOrderAsync(string buyerId, IReadOnlyCollection<OrderItemRequest> items,
        Address shippingAddress, CancellationToken cancellationToken = default);
}
