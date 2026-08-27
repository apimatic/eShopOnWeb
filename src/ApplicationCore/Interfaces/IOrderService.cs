using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderService
{
    Task CreateOrderAsync(int basketId, Address shippingAddress);

    /// <summary>
    /// Places an order directly from catalog item ids and quantities, priced from the catalog.
    /// The order starts in <see cref="OrderStatus.PendingPayment"/>.
    /// </summary>
    Task<Order> CreateOrderAsync(string buyerId, Address shippingAddress,
        IReadOnlyDictionary<int, int> items, CancellationToken cancellationToken = default);
}
