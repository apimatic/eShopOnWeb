using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IShopperOrderService
{
    Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<OrderLineRequest> items,
        Address shippingAddress,
        CancellationToken cancellationToken = default);

    Task<Order> DispatchAsync(int orderId, CancellationToken cancellationToken = default);

    Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Order>> ListBuyerOrdersAsync(string buyerId, CancellationToken cancellationToken = default);

    Task<Order?> GetBuyerOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderNotification>?> ListOrderNotificationsAsync(
        string buyerId,
        int orderId,
        bool isAdministrator,
        CancellationToken cancellationToken = default);

    Task RefreshNotificationStatusesAsync(
        IEnumerable<OrderNotification> notifications,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrderNotification>> ListBuyerNotificationsAsync(
        string buyerId,
        CancellationToken cancellationToken = default);
}

public record OrderLineRequest(int CatalogItemId, int Quantity);
