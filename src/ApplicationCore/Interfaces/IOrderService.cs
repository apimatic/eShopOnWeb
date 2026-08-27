using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IOrderService
{
    Task CreateOrderAsync(int basketId, Address shippingAddress);

    /// <summary>
    /// Creates an order directly from catalog items at their current catalog prices.
    /// The order starts in <see cref="OrderStatus.AwaitingPayment"/>.
    /// </summary>
    Task<Order> CreateOrderFromItemsAsync(string buyerId, Address shippingAddress,
        IReadOnlyDictionary<int, int> catalogItemQuantities, CancellationToken cancellationToken = default);
}
