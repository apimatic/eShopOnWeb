using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderService
{
    Task CreateOrderAsync(int basketId, Address shippingAddress);

    Task<Order> CreateOrderFromCatalogItemsAsync(string buyerId, IReadOnlyList<CatalogQuantity> items, Address shippingAddress);
}

public readonly record struct CatalogQuantity(int CatalogItemId, int Quantity);
