using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderService
{
    Task CreateOrderAsync(int basketId, Address shippingAddress);

    /// <summary>
    /// Places an order directly from catalog item ids and quantities (prices come from the catalog),
    /// reusing the existing order/order-item model. Returns the persisted order.
    /// </summary>
    Task<Order> CreateOrderAsync(string buyerId, Address shippingAddress, IReadOnlyCollection<(int CatalogItemId, int Units)> items);
}
