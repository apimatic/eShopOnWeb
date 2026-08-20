using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record CatalogOrderItemRequest(int CatalogItemId, int Quantity);

public interface IShopperOrderService
{
    Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<CatalogOrderItemRequest> items,
        CancellationToken cancellationToken = default);

    Task<Order> DispatchAsync(int orderId, CancellationToken cancellationToken = default);

    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Order>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken = default);

    Task<Order?> GetByIdForCallerAsync(int orderId, string buyerId, bool isAdministrator, CancellationToken cancellationToken = default);
}
