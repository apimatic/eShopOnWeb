using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record CatalogOrderItemRequest(int CatalogItemId, int Quantity);

public record ShopperOrderDetails(
    Order Order,
    IReadOnlyList<OrderNotification> Notifications);

public interface IShopperOrderService
{
    Task<ShopperOrderDetails> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<CatalogOrderItemRequest> items,
        Address? shipToAddress,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ShopperOrderDetails>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);

    Task<ShopperOrderDetails?> GetOrderNotificationsAsync(
        string buyerId,
        int orderId,
        CancellationToken cancellationToken = default);
}

public interface IOperatorOrderService
{
    Task<ShopperOrderDetails> DispatchAsync(int orderId, CancellationToken cancellationToken = default);
    Task<ShopperOrderDetails> CancelAsync(int orderId, CancellationToken cancellationToken = default);
}
