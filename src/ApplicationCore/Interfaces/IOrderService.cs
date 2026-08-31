using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Invoicing;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderService
{
    Task CreateOrderAsync(int basketId, Address shippingAddress);

    /// <summary>
    /// Places an order directly from catalog items and quantities (no basket required) and returns it.
    /// Reuses the same Order/OrderItem model as basket checkout; each line's price is snapshotted from
    /// the catalog.
    /// </summary>
    Task<Order> CreateOrderAsync(string buyerId, IReadOnlyCollection<CatalogItemQuantity> items, Address shippingAddress);
}
