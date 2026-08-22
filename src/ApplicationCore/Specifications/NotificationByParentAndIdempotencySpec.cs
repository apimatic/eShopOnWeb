using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class NotificationByParentAndIdempotencySpec : Specification<OrderNotification>
{
    public NotificationByParentAndIdempotencySpec(int parentNotificationId, string idempotencyKey)
    {
        Query.Where(n => n.ParentNotificationId == parentNotificationId && n.IdempotencyKey == idempotencyKey);
    }
}
