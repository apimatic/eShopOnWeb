using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record OrderLineRequest(int CatalogItemId, int Quantity);

public interface IPublicOrderService
{
    Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> items, CancellationToken cancellationToken = default);

    Task DispatchAsync(int orderId, CancellationToken cancellationToken = default);

    Task CancelAsync(int orderId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Order>> ListOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);

    Task<Order?> GetOrderAsync(int orderId, CancellationToken cancellationToken = default);
}
