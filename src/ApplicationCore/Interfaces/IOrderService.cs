using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderService
{
    Task CreateOrderAsync(int basketId, Address shippingAddress);

    /// <summary>
    /// Places an order directly from catalog item ids and quantities (no basket),
    /// reusing the existing order/order-item model. Returns the created order.
    /// </summary>
    Task<Order> CreateOrderFromItemsAsync(string buyerId, Address shippingAddress,
        IReadOnlyDictionary<int, int> itemQuantities, CancellationToken ct = default);
}
