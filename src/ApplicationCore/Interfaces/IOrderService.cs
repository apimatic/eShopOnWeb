using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderService
{
    Task CreateOrderAsync(int basketId, Address shippingAddress);

    /// <summary>
    /// Places an order for <paramref name="buyerId"/> directly from catalog items, priced from the catalog.
    /// Lines for the same catalog item are merged. Throws <see cref="Exceptions.CatalogItemsNotFoundException"/>
    /// when an item does not exist.
    /// </summary>
    Task<Order> CreateOrderAsync(string buyerId, IReadOnlyCollection<OrderLineRequest> lines, Address? shippingAddress);
}

public sealed record OrderLineRequest(int CatalogItemId, int Quantity);
