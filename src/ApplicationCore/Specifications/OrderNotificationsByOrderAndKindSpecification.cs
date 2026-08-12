using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderNotificationsByOrderAndKindSpecification : Specification<OrderNotification>
{
    public OrderNotificationsByOrderAndKindSpecification(int orderId, NotificationKind kind)
    {
        Query.Where(n => n.OrderId == orderId && n.Kind == kind)
            .OrderBy(n => n.CreatedAt);
    }
}
