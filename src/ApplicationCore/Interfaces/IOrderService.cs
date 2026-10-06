using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderService
{
    Task CreateOrderAsync(int basketId, Address shippingAddress);

    /// <summary>
    /// Places an order for <paramref name="buyerId"/> straight from catalog items, priced from the catalog.
    /// Throws <see cref="Exceptions.OrderValidationException"/> when the lines cannot be ordered.
    /// </summary>
    Task<Order> CreateOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines, Address shippingAddress);
}
