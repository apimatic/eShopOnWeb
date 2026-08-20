using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record OrderLineRequest(int CatalogItemId, int Quantity);

public interface IShopperOrderService
{
    Task<Order> PlaceAsync(string buyerId, IReadOnlyList<OrderLineRequest> items, CancellationToken cancellationToken);
    Task<Order> DispatchAsync(int orderId, CancellationToken cancellationToken);
    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Order>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken);
    Task<Order?> GetForBuyerAsync(string buyerId, int orderId, CancellationToken cancellationToken);
}
