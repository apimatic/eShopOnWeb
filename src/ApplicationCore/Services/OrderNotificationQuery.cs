using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderNotificationQuery : IOrderNotificationQuery
{
    private readonly IRepository<Order> _orders;
    private readonly IRepository<OrderNotification> _notifications;
    private readonly OrderNotificationSender _notificationSender;

    public OrderNotificationQuery(
        IRepository<Order> orders,
        IRepository<OrderNotification> notifications,
        OrderNotificationSender notificationSender)
    {
        _orders = orders;
        _notifications = notifications;
        _notificationSender = notificationSender;
    }

    public async Task<IReadOnlyList<OrderNotification>> ListForOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.GetByIdAsync(orderId, cancellationToken);
        if (order is null || order.BuyerId != buyerId)
        {
            throw new OrderNotificationException(404, "Order not found.");
        }

        var notifications = await _notifications.ListAsync(new OrderNotificationsByOrderIdSpec(orderId), cancellationToken);
        foreach (var notification in notifications)
        {
            await _notificationSender.RefreshFromProviderAsync(notification, cancellationToken);
        }

        return notifications;
    }
}
