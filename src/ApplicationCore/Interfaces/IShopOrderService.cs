using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IShopOrderService
{
    Task<Order> PlaceAsync(string buyerId, IReadOnlyList<ShopOrderLine> lines, CancellationToken cancellationToken);

    Task<Order> DispatchAsync(int orderId, CancellationToken cancellationToken);

    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Order>> ListForBuyerAsync(string buyerId, CancellationToken cancellationToken);

    Task<Order?> GetByIdAsync(int orderId, CancellationToken cancellationToken);
}

public sealed record ShopOrderLine(int CatalogItemId, int Quantity);
