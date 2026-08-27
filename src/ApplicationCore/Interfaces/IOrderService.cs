using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderService
{
    Task CreateOrderAsync(int basketId, Address shippingAddress);

    /// <summary>
    /// Places an order directly from catalog item ids and quantities, reusing the existing
    /// order/order-item model. Returns the created order.
    /// </summary>
    Task<Order> CreateOrderFromItemsAsync(string buyerId, Address shippingAddress, IReadOnlyList<OrderItemRequest> items);
}
