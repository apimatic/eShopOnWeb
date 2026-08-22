using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderNotificationByIdSpec : Specification<OrderNotification>, ISingleResultSpecification<OrderNotification>
{
    public OrderNotificationByIdSpec(int notificationId)
    {
        Query.Where(n => n.Id == notificationId);
    }
}
