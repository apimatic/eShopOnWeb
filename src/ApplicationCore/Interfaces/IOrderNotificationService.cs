using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record PlaceOrderItem(int CatalogItemId, int Quantity);

public record PlaceOrderResult(Order Order, IReadOnlyList<OrderNotification> Notifications);

public record OrderWithNotifications(Order Order, IReadOnlyList<OrderNotification> Notifications);

public interface IOrderNotificationService
{
    Task<PlaceOrderResult> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<PlaceOrderItem> items,
        Address? shipToAddress,
        CancellationToken cancellationToken = default);

    Task<OrderWithNotifications> DispatchAsync(int orderId, CancellationToken cancellationToken = default);

    Task<OrderWithNotifications> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderWithNotifications>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderNotification>> GetOrderNotificationsAsync(
        string buyerId,
        int orderId,
        CancellationToken cancellationToken = default);
}
