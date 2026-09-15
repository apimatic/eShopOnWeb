using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public readonly record struct OrderCatalogItem(int CatalogItemId, int Quantity);

public interface IOrderService
{
    Task CreateOrderAsync(int basketId, Address shippingAddress);

    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderCatalogItem> items, Address? shippingAddress);
}
