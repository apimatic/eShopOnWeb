using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderService
{
    Task CreateOrderAsync(int basketId, Address shippingAddress);

    /// <summary>Places an order directly from catalog item ids/quantities (no basket involved).</summary>
    Task<Order> CreateOrderFromCatalogItemsAsync(string buyerId, Address shippingAddress,
        IReadOnlyList<OrderItemQuantity> items);

    Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId);

    Task<Order?> GetOrderForBuyerAsync(string buyerId, int orderId);
}
