using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class NotificationsForOrderSpecification : Specification<OrderNotification>
{
    public NotificationsForOrderSpecification(int orderId)
    {
        Query.Where(n => n.OrderId == orderId)
            .OrderBy(n => n.CreatedAt);
    }
}
