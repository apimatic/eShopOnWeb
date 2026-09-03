using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class OrderNotificationByIdSpecification : Specification<OrderNotification>
{
    public OrderNotificationByIdSpecification(int notificationId)
    {
        Query.Where(n => n.Id == notificationId);
    }
}
