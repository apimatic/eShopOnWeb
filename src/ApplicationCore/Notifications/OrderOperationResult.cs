using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Notifications;

/// <summary>
/// The result of an order lifecycle action (place / dispatch / cancel): the order it acted on,
/// plus the notifications the action produced or that are attached to the order.
/// </summary>
public class OrderOperationResult
{
    public OrderOperationResult(Order order, IReadOnlyList<OrderNotification> notifications)
    {
        Order = order;
        Notifications = notifications;
    }

    public Order Order { get; }
    public IReadOnlyList<OrderNotification> Notifications { get; }
}
