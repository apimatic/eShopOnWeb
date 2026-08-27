using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class NotificationByIdempotencyKeySpecification : Specification<OrderNotification>
{
    public NotificationByIdempotencyKeySpecification(int resendOfNotificationId, string idempotencyKey)
    {
        Query.Where(n => n.ResendOfNotificationId == resendOfNotificationId
            && n.IdempotencyKey == idempotencyKey);
    }
}
