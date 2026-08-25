using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderService
{
    Task CreateOrderAsync(int basketId, Address shippingAddress);

    /// <summary>
    /// Places an order directly from catalog item ids/quantities (no basket involved). The
    /// order starts in <see cref="Entities.OrderAggregate.OrderStatus.AwaitingPayment"/>.
    /// </summary>
    Task<Order> CreateOrderFromCatalogItemsAsync(string buyerId, IReadOnlyList<(int CatalogItemId, int Quantity)> items, Address shipToAddress);
}
