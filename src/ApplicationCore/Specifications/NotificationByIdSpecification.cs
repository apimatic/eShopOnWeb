using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class NotificationByIdSpecification : Specification<OrderNotification>
{
    public NotificationByIdSpecification(int notificationId)
    {
        Query.Where(n => n.Id == notificationId);
    }
}
