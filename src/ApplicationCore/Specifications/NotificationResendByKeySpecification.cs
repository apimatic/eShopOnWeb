using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class NotificationResendByKeySpecification : Specification<OrderNotification>
{
    public NotificationResendByKeySpecification(int sourceNotificationId, string idempotencyKey)
    {
        Query.Where(n =>
            n.SourceNotificationId == sourceNotificationId &&
            n.IdempotencyKey == idempotencyKey);
    }
}
