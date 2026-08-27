using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Specifications;

public class ResendByIdempotencyKeySpecification : Specification<OrderNotification>
{
    public ResendByIdempotencyKeySpecification(int notificationId, string idempotencyKey)
    {
        Query.Where(n => n.ResendOfNotificationId == notificationId && n.IdempotencyKey == idempotencyKey);
    }
}
