using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Notifications;

/// <summary>An order paired with the notifications sent about it and where each one got to.</summary>
public class OrderWithNotifications
{
    public OrderWithNotifications(Order order, IReadOnlyList<OrderNotification> notifications)
    {
        Order = order;
        Notifications = notifications;
    }

    public Order Order { get; }
    public IReadOnlyList<OrderNotification> Notifications { get; }
}
