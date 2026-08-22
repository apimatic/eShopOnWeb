using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public sealed record CatalogOrderLine(int CatalogItemId, int Quantity);

public interface IShopperOrderService
{
    Task<Order> PlaceAsync(string buyerId, IReadOnlyList<CatalogOrderLine> lines, CancellationToken cancellationToken);
    Task<IReadOnlyList<Order>> ListMineAsync(string buyerId, CancellationToken cancellationToken);
    Task<Order> DispatchAsync(int orderId, CancellationToken cancellationToken);
    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken);
    Task<Order?> GetByIdForBuyerAsync(int orderId, string buyerId, CancellationToken cancellationToken);
}
