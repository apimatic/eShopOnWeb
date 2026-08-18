using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderService
{
    Task CreateOrderAsync(int basketId, Address shippingAddress);

    /// <summary>
    /// Places an order directly from catalog item ids and quantities (used by the API, which has no
    /// server-side basket), reusing the existing order/order-item model. Returns the created order.
    /// </summary>
    Task<Order> CreateOrderAsync(string buyerId, IReadOnlyCollection<NewOrderItem> items, Address shippingAddress);
}
