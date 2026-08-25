using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Services;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderService
{
    Task CreateOrderAsync(int basketId, Address shippingAddress);

    /// <summary>Places an order directly from catalog item ids/quantities (no basket involved).</summary>
    Task<Order> CreateOrderFromItemsAsync(string buyerId, IReadOnlyList<CatalogItemQuantity> items);
}
