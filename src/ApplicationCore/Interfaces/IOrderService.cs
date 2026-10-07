using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderService
{
    Task CreateOrderAsync(int basketId, Address shippingAddress);

    /// <summary>
    /// Places an order for <paramref name="buyerId"/> directly from catalog items, priced at the current
    /// catalog price. Lines for the same catalog item are merged.
    /// </summary>
    /// <exception cref="Exceptions.CatalogItemsNotFoundException">A line names a catalog item that does not exist.</exception>
    Task<Order> CreateOrderAsync(string buyerId, IReadOnlyCollection<OrderLineRequest> lines, Address shippingAddress,
        CancellationToken cancellationToken = default);
}

public sealed record OrderLineRequest(int CatalogItemId, int Quantity);
