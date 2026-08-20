using System.Linq;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class NotificationContentRedactionByNotificationIdSpecification : Specification<NotificationContentRedaction>, ISingleResultSpecification<NotificationContentRedaction>
{
    public NotificationContentRedactionByNotificationIdSpecification(int notificationId)
    {
        Query.Where(r => r.NotificationId == notificationId);
    }

    public NotificationContentRedactionByNotificationIdSpecification(int[] notificationIds)
    {
        Query.Where(r => notificationIds.Contains(r.NotificationId));
    }
}
