using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

/// <summary>Notifications of a given kind for an order (e.g. the delivery follow-up messages).</summary>
public class OrderNotificationsByOrderAndTypeSpecification : Specification<OrderNotification>
{
    public OrderNotificationsByOrderAndTypeSpecification(int orderId, NotificationType type)
    {
        Query.Where(n => n.OrderId == orderId && n.Type == type);
    }
}
