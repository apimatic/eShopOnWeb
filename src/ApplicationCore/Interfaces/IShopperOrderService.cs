using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record CatalogOrderItemRequest(int CatalogItemId, int Quantity);

public class ShopperOrderSummary
{
    public required Order Order { get; init; }
    public required IReadOnlyList<OrderNotification> Notifications { get; init; }
}

public interface IShopperOrderService
{
    Task<Order> PlaceAsync(string buyerId, IReadOnlyList<CatalogOrderItemRequest> items, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ShopperOrderSummary>> ListMineAsync(string buyerId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OrderNotification>> ListNotificationsAsync(int orderId, string buyerId, bool isAdministrator, CancellationToken cancellationToken = default);
}
